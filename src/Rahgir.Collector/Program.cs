using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.FileProviders;
using Rahgir.Collector;
using Rahgir.Collector.Dashboard;
using Rahgir.Collector.Ingestion;
using Rahgir.Collector.Management;
using Microsoft.AspNetCore.DataProtection;
using Rahgir.Collector.Security;
using Rahgir.Collector.Storage;
using Rahgir.Core;

// CLI helper: dotnet Rahgir.Collector.dll hash-password
if (args.Length > 0 && args[0] == "hash-password")
{
    Console.Write("Password: ");
    var pw = ReadPassword();
    if (pw.Length < 10) { Console.Error.WriteLine("Use at least 10 characters."); return 1; }
    Console.WriteLine(PasswordHasher.Hash(pw));
    return 0;
}
if (args.Length > 0 && args[0] == "new-secret")
{
    Console.WriteLine(Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant());
    return 0;
}

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
builder.WebHost.ConfigureKestrel(k => k.AddServerHeader = false);

var options = new CollectorOptions();
builder.Configuration.GetSection("Rahgir").Bind(options);
ValidateOptions(options, builder.Environment);

builder.Services.AddSingleton(options);
builder.Services.AddSingleton(new Masker(options.SensitiveKeys));
builder.Services.AddSingleton<EventValidator>();
builder.Services.AddSingleton<IngestQueue>();
builder.Services.AddSingleton<IEventStore, SqlServerEventStore>();
builder.Services.AddSingleton<DashboardQueries>();
builder.Services.AddSingleton<LoginThrottle>();
builder.Services.AddSingleton<DashboardCsp>();
builder.Services.AddSingleton<IAdminStore, SqlAdminStore>();
builder.Services.AddSingleton<Registry>();
builder.Services.AddHostedService<RegistryRefresher>();
builder.Services.AddHostedService<EventWriter>();

// Project server secrets are stored encrypted with these keys. Keep the folder private and backed up.
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("Rahgir")
    .PersistKeysToFileSystem(new DirectoryInfo(options.DataProtectionKeysPath ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "keys")));
if (OperatingSystem.IsWindows()) dataProtection.ProtectKeysWithDpapi(protectToLocalMachine: true);
builder.Services.AddHostedService<RetentionService>();

if (options.TrustForwardedHeaders)
{
    builder.Services.Configure<ForwardedHeadersOptions>(f =>
    {
        f.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        f.ForwardLimit = 1;
        foreach (var p in options.KnownProxies)
            if (IPAddress.TryParse(p, out var ip)) f.KnownProxies.Add(ip);
    });
}

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(c =>
    {
        c.Cookie.Name = "rahgir_auth";
        c.Cookie.HttpOnly = true;
        c.Cookie.SameSite = SameSiteMode.Strict;
        c.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        c.ExpireTimeSpan = TimeSpan.FromHours(8);
        c.SlidingExpiration = true;
        // API: answer 401/403 instead of redirecting to a login page.
        c.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
        c.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
    });
builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(r =>
{
    r.RejectionStatusCode = 429;
    r.AddPolicy(IngestEndpoints.WebPolicy, ctx => RateLimitPartition.GetFixedWindowLimiter(
        "ip:" + ctx.Connection.RemoteIpAddress, _ => new FixedWindowRateLimiterOptions
        { PermitLimit = options.WebRateLimitPerMinute, Window = TimeSpan.FromMinutes(1) }));
    r.AddPolicy(IngestEndpoints.ServerPolicy, ctx => RateLimitPartition.GetFixedWindowLimiter(
        "p:" + ctx.Request.Headers[RahgirHeaders.Project], _ => new FixedWindowRateLimiterOptions
        { PermitLimit = options.ServerRateLimitPerMinute, Window = TimeSpan.FromMinutes(1) }));
    r.AddPolicy(ConfigAndReadEndpoints.ReadPolicy, ctx => RateLimitPartition.GetFixedWindowLimiter(
        "read:" + ctx.Request.Headers.Authorization.ToString().GetHashCode(), _ => new FixedWindowRateLimiterOptions
        { PermitLimit = options.ReadRateLimitPerMinute, Window = TimeSpan.FromMinutes(1) }));
    r.AddPolicy(DashboardEndpoints.LoginPolicy, ctx => RateLimitPartition.GetFixedWindowLimiter(
        "login:" + ctx.Connection.RemoteIpAddress, _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
});

builder.Services.Configure<Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions>(k => k.Limits.MaxRequestBodySize = options.MaxServerBodyBytes + 1024);
builder.Services.Configure<Microsoft.AspNetCore.Builder.IISServerOptions>(k => k.MaxRequestBodySize = options.MaxServerBodyBytes + 1024);

var app = builder.Build();

if (options.TrustForwardedHeaders) app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment()) app.UseHsts();

app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h.XContentTypeOptions = "nosniff";
    h.XFrameOptions = "DENY";
    h["Referrer-Policy"] = "strict-origin-when-cross-origin";
    h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    h["Cross-Origin-Opener-Policy"] = "same-origin";
    await next();
});

// Optional IP allow-list for everything except the ingestion endpoints.
if (options.DashboardAllowedIps.Length > 0)
{
    app.Use(async (ctx, next) =>
    {
        var path = ctx.Request.Path;
        var isIngest = path.StartsWithSegments("/v1") || path.StartsWithSegments("/healthz");
        if (!isIngest && !options.DashboardAllowedIps.Any(rule => IpRange.Matches(ctx.Connection.RemoteIpAddress, rule)))
        {
            ctx.Response.StatusCode = 404;
            return;
        }
        await next();
    });
}

var csp = app.Services.GetRequiredService<DashboardCsp>();
var staticOptions = new StaticFileOptions
{
    OnPrepareResponse = s =>
    {
        if (s.File.PhysicalPath != null && s.File.Name.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            s.Context.Response.Headers.ContentSecurityPolicy = csp.For(s.File.PhysicalPath);
            s.Context.Response.Headers.CacheControl = "no-cache";
        }
    },
};
app.UseDefaultFiles();
app.UseStaticFiles(staticOptions);

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseDashboardCsrfGuard();

app.MapIngest();
app.MapDashboardApi();
app.MapAdmin();
app.MapConfigAndRead();
app.MapFallback(async ctx =>
{
    // Unknown /api or /v1 paths are 404; everything else serves the dashboard shell.
    var p = ctx.Request.Path;
    var index = Path.Combine(app.Environment.WebRootPath ?? "", "index.html");
    if (p.StartsWithSegments("/api") || p.StartsWithSegments("/v1") || !File.Exists(index)) { ctx.Response.StatusCode = 404; return; }
    ctx.Response.ContentType = "text/html; charset=utf-8";
    ctx.Response.Headers.ContentSecurityPolicy = csp.For(index);
    await ctx.Response.SendFileAsync(index);
});

app.Run();
return 0;

static void ValidateOptions(CollectorOptions o, IWebHostEnvironment env)
{
    foreach (var p in o.Projects)
    {
        if (string.IsNullOrWhiteSpace(p.Key)) throw new InvalidOperationException("Every project needs a Key.");
        if (!string.IsNullOrEmpty(p.ServerSecret) && p.ServerSecret.Length < 32)
            throw new InvalidOperationException($"Project '{p.Key}': ServerSecret must be at least 32 characters (use: dotnet Rahgir.Collector.dll new-secret).");
        if (p.AllowedOrigins.Length == 0 && !env.IsDevelopment())
            Console.WriteLine($"WARNING: project '{p.Key}' accepts browser events from any origin. Set AllowedOrigins.");
    }
    foreach (var u in o.DashboardUsers)
        if (!u.PasswordHash.StartsWith("pbkdf2$"))
            throw new InvalidOperationException($"Dashboard user '{u.Username}': PasswordHash must be generated with 'hash-password'.");
}

static string ReadPassword()
{
    if (Console.IsInputRedirected) return Console.ReadLine() ?? "";
    var pw = new System.Text.StringBuilder();
    ConsoleKeyInfo k;
    while ((k = Console.ReadKey(true)).Key != ConsoleKey.Enter)
    {
        if (k.Key == ConsoleKey.Backspace && pw.Length > 0) pw.Length--;
        else if (!char.IsControl(k.KeyChar)) pw.Append(k.KeyChar);
    }
    Console.WriteLine();
    return pw.ToString();
}

public partial class Program { }
