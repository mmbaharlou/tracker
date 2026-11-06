using System.Net;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Tracker.Api;
using Tracker.Api.Auth;
using Tracker.Api.Controllers;
using Tracker.Api.Dashboard;
using Tracker.Api.Data;
using Tracker.Api.Ingestion;
using Tracker.Api.Management;
using Tracker.Api.Storage;
using Tracker.Core;

// CLI: create-owner <username> | reset-password <username> | hash-password | new-secret
if (args.Length > 0 && args[0] == "new-secret")
{
    Console.WriteLine(Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant());
    return 0;
}
if (args.Length > 0 && args[0] is "create-owner" or "reset-password" or "hash-password")
{
    var cli = WebApplication.CreateBuilder(args[2..]);
    cli.Configuration.AddJsonFile("appsettings.Local.json", optional: true);
    var cs = new[] { cli.Configuration.GetConnectionString("Default"), cli.Configuration.GetConnectionString("TrackerWriter") }.FirstOrDefault(x => !string.IsNullOrEmpty(x))
             ?? throw new InvalidOperationException("ConnectionStrings:Default is missing.");
    return await PanelAuthSetup.RunCliAsync(args, () => new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(cs, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", AppDbContext.Schema)).Options)) ?? 1;
}

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;
config.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
// Tracker 1.x used ConnectionStrings:TrackerWriter / TrackerReader. They still work, but the main one now also applies the
// EF Core migrations on startup, so it needs to own the schema (db_owner, or run once with such a login).
if (string.IsNullOrEmpty(config.GetConnectionString("Default")) && config.GetConnectionString("TrackerWriter") is { Length: > 0 } legacyWriter)
    config["ConnectionStrings:Default"] = legacyWriter;
if (string.IsNullOrEmpty(config.GetConnectionString("Reader")) && config.GetConnectionString("TrackerReader") is { Length: > 0 } legacyReader)
    config["ConnectionStrings:Reader"] = legacyReader;
builder.WebHost.ConfigureKestrel(k => k.AddServerHeader = false);

var options = new CollectorOptions();
config.GetSection("Tracker").Bind(options);
ValidateOptions(options, builder.Environment);
builder.Services.AddSingleton(options);

builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlServer(config.GetConnectionString("Default"),
    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", AppDbContext.Schema)));
builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    // Report rows come from Dapper as dictionaries: camelCase their column names like normal properties.
    o.JsonSerializerOptions.DictionaryKeyPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
});
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.DictionaryKeyPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);

builder.Services.AddSingleton(new Masker(options.SensitiveKeys));
builder.Services.AddSingleton<EventValidator>();
builder.Services.AddSingleton<IngestQueue>();
builder.Services.AddSingleton<IEventStore, SqlServerEventStore>();
builder.Services.AddSingleton<DashboardQueries>();
builder.Services.AddSingleton<IProjectStore, EfProjectStore>();
builder.Services.AddSingleton<Registry>();
builder.Services.AddSingleton<IProjectCatalog>(sp => sp.GetRequiredService<Registry>());
builder.Services.AddHostedService<RegistryRefresher>();
builder.Services.AddHostedService<EventWriter>();
builder.Services.AddHostedService<RetentionService>();
builder.AddPanelAuth();

// Project server secrets are stored encrypted with these keys. Keep the folder private and backed up.
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("Tracker")
    .PersistKeysToFileSystem(new DirectoryInfo(options.DataProtectionKeysPath ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "keys")));
if (OperatingSystem.IsWindows()) dataProtection.ProtectKeysWithDpapi(protectToLocalMachine: true);

// The panel (Next.js) and IIS sit in front of this service on the same machine: trust X-Forwarded-For from them only.
builder.Services.Configure<ForwardedHeadersOptions>(f =>
{
    f.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    f.ForwardLimit = 2;
    foreach (var p in options.KnownProxies)
        if (IPAddress.TryParse(p, out var ip)) f.KnownProxies.Add(ip);
});

builder.Services.AddRateLimiter(r =>
{
    r.RejectionStatusCode = 429;
    r.AddPolicy(IngestEndpoints.WebPolicy, ctx => RateLimitPartition.GetFixedWindowLimiter(
        "ip:" + ctx.Connection.RemoteIpAddress, _ => new FixedWindowRateLimiterOptions
        { PermitLimit = options.WebRateLimitPerMinute, Window = TimeSpan.FromMinutes(1) }));
    r.AddPolicy(IngestEndpoints.ServerPolicy, ctx => RateLimitPartition.GetFixedWindowLimiter(
        "p:" + ctx.Request.Headers[TrackerHeaders.Project], _ => new FixedWindowRateLimiterOptions
        { PermitLimit = options.ServerRateLimitPerMinute, Window = TimeSpan.FromMinutes(1) }));
    r.AddPolicy(ConfigAndReadEndpoints.ReadPolicy, ctx => RateLimitPartition.GetFixedWindowLimiter(
        "read:" + ctx.Request.Headers.Authorization.ToString().GetHashCode(), _ => new FixedWindowRateLimiterOptions
        { PermitLimit = options.ReadRateLimitPerMinute, Window = TimeSpan.FromMinutes(1) }));
    r.AddLoginRateLimit(config);
});

builder.Services.Configure<Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions>(k => k.Limits.MaxRequestBodySize = options.MaxServerBodyBytes + 1024);
builder.Services.Configure<Microsoft.AspNetCore.Builder.IISServerOptions>(k => k.MaxRequestBodySize = options.MaxServerBodyBytes + 1024);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    // Tests run on SQLite, which cannot apply the SQL Server migrations.
    if (db.Database.IsSqlServer()) await db.Database.MigrateAsync(); else await db.Database.EnsureCreatedAsync();
    await ImportLegacyUsersAsync(db, config, app.Logger);
    await PanelAuthSetup.BootstrapOwnerAsync(db, config, app.Logger);
}
await app.Services.GetRequiredService<Registry>().RefreshAsync();

app.UseForwardedHeaders();
app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h.XContentTypeOptions = "nosniff";
    h.XFrameOptions = "DENY";
    h["Referrer-Policy"] = "strict-origin-when-cross-origin";
    await next();
});
app.UseRateLimiter();
app.UsePanelGuards();
app.UseAuthentication();
app.UseAuthorization();

app.MapIngest();
app.MapConfigAndRead();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();
return 0;

static void ValidateOptions(CollectorOptions o, IWebHostEnvironment env)
{
    foreach (var p in o.Projects)
    {
        if (string.IsNullOrWhiteSpace(p.Key)) throw new InvalidOperationException("Every project needs a Key.");
        if (!string.IsNullOrEmpty(p.ServerSecret) && p.ServerSecret.Length < 32)
            throw new InvalidOperationException($"Project '{p.Key}': ServerSecret must be at least 32 characters (use: dotnet Tracker.Api.dll new-secret).");
        if (p.AllowedOrigins.Length == 0 && !env.IsDevelopment())
            Console.WriteLine($"WARNING: project '{p.Key}' accepts browser events from any origin. Set AllowedOrigins.");
    }
}

// Tracker 1.x users (tracker.DashboardUsers table and Tracker:DashboardUsers in appsettings) keep their passwords.
static async Task ImportLegacyUsersAsync(AppDbContext db, IConfiguration config, ILogger log)
{
    if (await db.AdminUsers.AnyAsync()) return;
    var legacy = new List<LegacyUser>();
    foreach (var u in config.GetSection("Tracker:DashboardUsers").GetChildren())
        legacy.Add(new LegacyUser(u["Username"] ?? "", u["PasswordHash"] ?? "", u.GetValue<bool>("IsAdmin"), u.GetSection("Projects").Get<string[]>() ?? ["*"]));
    if (db.Database.IsSqlServer())
    {
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync();
        try
        {
            var rows = await Dapper.SqlMapper.QueryAsync<(string Username, string PasswordHash, string Projects, bool IsAdmin)>(conn,
                "IF OBJECT_ID('tracker.DashboardUsers') IS NOT NULL SELECT Username, PasswordHash, Projects, IsAdmin FROM tracker.DashboardUsers ORDER BY CreatedAt");
            foreach (var r in rows)
                legacy.Add(new LegacyUser(r.Username, r.PasswordHash, r.IsAdmin, System.Text.Json.JsonSerializer.Deserialize<string[]>(r.Projects) ?? ["*"]));
        }
        finally { await conn.CloseAsync(); }
    }
    await LegacyImport.ImportUsersAsync(db, legacy, log);
}

public partial class Program { }
