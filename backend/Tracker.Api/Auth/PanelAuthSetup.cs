using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Tracker.Api.Data;

namespace Tracker.Api.Auth;

public static class PanelAuthSetup
{
    public const string LoginRateLimit = "panel-login";
    public const string CsrfHeader = "X-Requested-With";

    public static void AddPanelAuth(this WebApplicationBuilder builder)
    {
        var s = builder.Services;
        s.Configure<AuthOptions>(builder.Configuration.GetSection("Auth"));
        s.AddMemoryCache();
        s.AddHttpContextAccessor();
        s.AddSingleton(TimeProvider.System);
        s.AddSingleton<CookieNames>();
        s.AddSingleton<SessionStore>();
        s.AddSingleton<LoginThrottle>();
        s.AddScoped<AuditLog>();
        s.AddScoped<AuthService>();

        s.AddAuthentication(SessionAuthHandler.SchemeName)
            .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, SessionAuthHandler>(SessionAuthHandler.SchemeName, null);

        static bool NoPendingChange(AuthorizationHandlerContext c) => !c.User.HasClaim(SessionAuthHandler.MustChangeClaim, "1");
        s.AddAuthorizationBuilder()
            .SetDefaultPolicy(new AuthorizationPolicyBuilder(SessionAuthHandler.SchemeName).RequireAuthenticatedUser().RequireAssertion(NoPendingChange).Build())
            .AddPolicy(Policies.AnySession, p => p.AddAuthenticationSchemes(SessionAuthHandler.SchemeName).RequireAuthenticatedUser())
            .AddPolicy(Policies.Admin, p => p.AddAuthenticationSchemes(SessionAuthHandler.SchemeName).RequireAuthenticatedUser().RequireAssertion(NoPendingChange)
                .RequireRole(nameof(AdminRole.Admin), nameof(AdminRole.Owner)))
            .AddPolicy(Policies.Owner, p => p.AddAuthenticationSchemes(SessionAuthHandler.SchemeName).RequireAuthenticatedUser().RequireAssertion(NoPendingChange)
                .RequireRole(nameof(AdminRole.Owner)));
    }

    /// <summary>Login requests per minute per IP (Auth:LoginRequestsPerMinute, default 10), on top of the per-account lockout.</summary>
    public static void AddLoginRateLimit(this Microsoft.AspNetCore.RateLimiting.RateLimiterOptions r, IConfiguration config)
    {
        var limit = config.GetValue("Auth:LoginRequestsPerMinute", 10);
        r.AddPolicy(LoginRateLimit, ctx => RateLimitPartition.GetFixedWindowLimiter("login:" + ctx.Connection.RemoteIpAddress,
            _ => new FixedWindowRateLimiterOptions { PermitLimit = limit, Window = TimeSpan.FromMinutes(1) }));
    }

    /// <summary>
    /// For every /api call: optional IP allow-list, CSRF guard (state-changing calls need a custom header, which a
    /// cross-site form or image cannot send), and no caching of panel data.
    /// </summary>
    public static void UsePanelGuards(this WebApplication app)
    {
        var allowed = app.Configuration.GetSection("Auth:AllowedIps").Get<string[]>() ?? [];
        app.Use(async (ctx, next) =>
        {
            if (ctx.Request.Path.StartsWithSegments("/api"))
            {
                if (allowed.Length > 0 && !allowed.Any(rule => IpRange.Matches(ctx.Connection.RemoteIpAddress, rule)))
                {
                    ctx.Response.StatusCode = 404;
                    return;
                }
                if (!HttpMethods.IsGet(ctx.Request.Method) && !HttpMethods.IsHead(ctx.Request.Method) && !HttpMethods.IsOptions(ctx.Request.Method)
                    && ctx.Request.Headers[CsrfHeader] != "fetch")
                {
                    ctx.Response.StatusCode = 400;
                    await ctx.Response.WriteAsJsonAsync(new { message = "درخواست نامعتبر است." });
                    return;
                }
                ctx.Response.Headers.CacheControl = "no-store";
            }
            await next();
        });
    }

    /// <summary>
    /// First run: when no panel user exists and Bootstrap:OwnerUsername / Bootstrap:OwnerPassword are set, creates that owner
    /// with "must change password". Remove the bootstrap values afterwards.
    /// </summary>
    public static async Task BootstrapOwnerAsync(AppDbContext db, IConfiguration config, ILogger log)
    {
        if (await db.AdminUsers.AnyAsync()) return;
        var username = config["Bootstrap:OwnerUsername"]?.Trim();
        var password = config["Bootstrap:OwnerPassword"];
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            log.LogWarning("No panel user exists. Create one with: dotnet <app>.dll create-owner <username>  (or set Bootstrap:OwnerUsername and Bootstrap:OwnerPassword)");
            return;
        }
        if (PasswordPolicy.Check(password, username) is { } err) { log.LogError("Bootstrap owner not created: {Error}", err); return; }
        db.AdminUsers.Add(new AdminUser { Username = username, PasswordHash = PasswordHasher.Hash(password), Role = AdminRole.Owner, MustChangePassword = true });
        db.AuditLog.Add(new AuditEntry { Action = "user.bootstrap", Target = username });
        await db.SaveChangesAsync();
        log.LogWarning("Created owner '{User}' from Bootstrap settings. Sign in, change the password, and remove Bootstrap:OwnerPassword from configuration.", username);
    }

    /// <summary>CLI: create-owner &lt;username&gt; (password read from the console) or reset-password &lt;username&gt;.</summary>
    public static async Task<int?> RunCliAsync(string[] args, Func<AppDbContext> createDb)
    {
        if (args.Length == 0 || args[0] is not ("create-owner" or "reset-password" or "hash-password")) return null;
        if (args[0] == "hash-password") { Console.Write("Password: "); Console.WriteLine(PasswordHasher.Hash(ReadPassword())); return 0; }
        if (args.Length < 2) { Console.Error.WriteLine($"Usage: {args[0]} <username>"); return 1; }
        var username = args[1].Trim();
        Console.Write("New password: ");
        var pw = ReadPassword();
        if (PasswordPolicy.Check(pw, username) is { } err) { Console.Error.WriteLine(err); return 1; }
        await using var db = createDb();
        await db.Database.MigrateAsync();
        var user = await db.AdminUsers.FirstOrDefaultAsync(u => u.Username == username);
        if (args[0] == "create-owner")
        {
            if (user is not null) { Console.Error.WriteLine("This username already exists. Use reset-password."); return 1; }
            db.AdminUsers.Add(new AdminUser { Username = username, PasswordHash = PasswordHasher.Hash(pw), Role = AdminRole.Owner });
            db.AuditLog.Add(new AuditEntry { Action = "user.create-cli", Target = username });
        }
        else
        {
            if (user is null) { Console.Error.WriteLine("No such user."); return 1; }
            user.PasswordHash = PasswordHasher.Hash(pw);
            user.PasswordChangedAt = DateTime.UtcNow;
            user.FailedLogins = 0; user.LockedUntil = null; user.IsActive = true;
            await db.AdminSessions.Where(s => s.UserId == user.Id && s.RevokedAt == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTime.UtcNow));
            db.AuditLog.Add(new AuditEntry { Action = "password.reset-cli", Target = username });
        }
        await db.SaveChangesAsync();
        Console.WriteLine("Done.");
        return 0;
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
}

public static class IpRange
{
    /// <summary>Matches "1.2.3.4" or a CIDR such as "10.0.0.0/8".</summary>
    public static bool Matches(IPAddress? ip, string rule)
    {
        if (ip is null) return false;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        var parts = rule.Trim().Split('/');
        if (!IPAddress.TryParse(parts[0], out var net)) return false;
        if (net.IsIPv4MappedToIPv6) net = net.MapToIPv4();
        if (parts.Length == 1) return ip.Equals(net);
        if (!int.TryParse(parts[1], out var bits) || ip.AddressFamily != net.AddressFamily) return false;
        var a = ip.GetAddressBytes(); var b = net.GetAddressBytes();
        if (bits < 0 || bits > a.Length * 8) return false;
        for (var i = 0; i < a.Length && bits > 0; i++, bits -= 8)
        {
            var mask = bits >= 8 ? 0xFF : (byte)(0xFF << (8 - bits));
            if ((a[i] & mask) != (b[i] & mask)) return false;
        }
        return true;
    }
}

public sealed record LegacyUser(string Username, string PasswordHash, bool IsAdmin, string[] Projects);

public static class LegacyImport
{
    /// <summary>
    /// Upgrade path: when the panel has no users yet, accounts from the previous version (same PBKDF2 hash format) are imported.
    /// The first admin becomes the owner; "*" means every project.
    /// </summary>
    public static async Task<int> ImportUsersAsync(AppDbContext db, IEnumerable<LegacyUser> legacy, ILogger log)
    {
        if (await db.AdminUsers.AnyAsync()) return 0;
        var list = legacy.Where(u => !string.IsNullOrWhiteSpace(u.Username) && u.PasswordHash.StartsWith("pbkdf2$", StringComparison.Ordinal))
            .GroupBy(u => u.Username.Trim(), StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
        if (list.Count == 0) return 0;
        var owner = list.FirstOrDefault(u => u.IsAdmin) ?? list[0];
        foreach (var u in list)
        {
            var all = u.Projects.Length == 0 || u.Projects.Contains("*");
            var user = new AdminUser
            {
                Username = u.Username.Trim(), PasswordHash = u.PasswordHash,
                Role = u == owner ? AdminRole.Owner : u.IsAdmin ? AdminRole.Admin : AdminRole.Viewer, AllProjects = all,
            };
            if (!all) foreach (var p in u.Projects.Distinct()) user.Projects.Add(new AdminUserProject { ProjectKey = p });
            db.AdminUsers.Add(user);
            db.AuditLog.Add(new AuditEntry { Action = "user.import", Target = user.Username, Detail = $"role={user.Role}" });
        }
        await db.SaveChangesAsync();
        log.LogWarning("Imported {Count} panel user(s) from the previous version; '{Owner}' is the owner.", list.Count, owner.Username);
        return list.Count;
    }
}
