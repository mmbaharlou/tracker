using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Tracker.Api.Data;

namespace Tracker.Api.Auth;

public sealed class AuthOptions
{
    /// <summary>Cookie name. In production the "__Host-" prefix is added (HTTPS only, this host only, path /).</summary>
    public string CookieName { get; set; } = "tk_session";
    /// <summary>A normal session ends after this much inactivity.</summary>
    public int IdleMinutes { get; set; } = 8 * 60;
    /// <summary>A normal session ends this long after login, however active.</summary>
    public int MaxSessionHours { get; set; } = 24;
    /// <summary>"Remember me": absolute lifetime and inactivity limit.</summary>
    public int RememberDays { get; set; } = 30;
    public int RememberIdleDays { get; set; } = 7;
    /// <summary>Failed logins before an account is locked; each further round doubles the lock.</summary>
    public int MaxFailedLogins { get; set; } = 5;
    public int LockMinutes { get; set; } = 15;
    /// <summary>Failed logins from one IP in 15 minutes before that IP is blocked from logging in.</summary>
    public int MaxFailuresPerIp { get; set; } = 20;
    public int MaxSessionsPerUser { get; set; } = 20;
    /// <summary>Optional allow-list for the panel (IPs or CIDRs). Empty = any.</summary>
    public string[] AllowedIps { get; set; } = [];
}

/// <summary>Who is calling, resolved from the session cookie once per request.</summary>
public sealed record PanelUser(int Id, string Username, string? DisplayName, AdminRole Role, bool AllProjects, IReadOnlySet<string> ProjectKeys,
    bool MustChangePassword, Guid SessionId)
{
    public bool SeesAllProjects => Role >= AdminRole.Admin || AllProjects;
    public bool CanSee(string? projectKey) => projectKey is { Length: > 0 } && (SeesAllProjects || ProjectKeys.Contains(projectKey));
    public bool IsAdmin => Role >= AdminRole.Admin;
    public bool IsOwner => Role == AdminRole.Owner;
}

public static class PanelUserExtensions
{
    const string Key = "panel.user";
    public static PanelUser? PanelUser(this HttpContext ctx) => ctx.Items.TryGetValue(Key, out var u) ? u as PanelUser : null;
    internal static void SetPanelUser(this HttpContext ctx, PanelUser u) => ctx.Items[Key] = u;
}

/// <summary>Creates, validates and revokes sessions. Validation is cached for a few seconds; revocation takes effect immediately.</summary>
public sealed class SessionStore(IServiceScopeFactory scopes, IMemoryCache cache, IOptions<AuthOptions> options, TimeProvider clock)
{
    static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(20);
    static readonly TimeSpan TouchEvery = TimeSpan.FromMinutes(2);
    readonly ConcurrentDictionary<int, DateTime> _revokedAfter = new();
    AuthOptions O => options.Value;

    sealed record Cached(PanelUser User, DateTime ExpiresAt, DateTime IdleLimit, DateTime LastSeen, DateTime CachedAt);

    public static byte[] Hash(string token) => SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));

    public async Task<(string Token, DateTime ExpiresAt)> CreateAsync(AppDbContext db, AdminUser user, bool remember, string? ip, string? userAgent)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var token = Base64Url(RandomNumberGenerator.GetBytes(32));
        var session = new AdminSession
        {
            UserId = user.Id, TokenHash = Hash(token), CreatedAt = now, LastSeenAt = now, Persistent = remember,
            ExpiresAt = remember ? now.AddDays(O.RememberDays) : now.AddHours(O.MaxSessionHours),
            Ip = ip, UserAgent = userAgent is { Length: > 300 } ? userAgent[..300] : userAgent,
        };
        db.AdminSessions.Add(session);
        await db.SaveChangesAsync();

        // Keep the newest sessions only.
        var active = await db.AdminSessions.Where(s => s.UserId == user.Id && s.RevokedAt == null && s.ExpiresAt > now)
            .OrderByDescending(s => s.LastSeenAt).Select(s => s.Id).ToListAsync();
        if (active.Count > O.MaxSessionsPerUser)
        {
            var old = active.Skip(O.MaxSessionsPerUser).ToList();
            await db.AdminSessions.Where(s => old.Contains(s.Id)).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now));
        }
        return (token, session.ExpiresAt);
    }

    public async Task<PanelUser?> ValidateAsync(string token)
    {
        if (token.Length is < 20 or > 100) return null;
        var hash = Hash(token);
        var key = "sess:" + Convert.ToHexString(hash);
        var now = clock.GetUtcNow().UtcDateTime;
        if (cache.TryGetValue(key, out Cached? c) && c is not null)
        {
            if (_revokedAfter.TryGetValue(c.User.Id, out var ra) && c.CachedAt <= ra) cache.Remove(key);
            else if (now < c.ExpiresAt && now < c.IdleLimit && now - c.LastSeen < TouchEvery) return c.User;
        }

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var s = await db.AdminSessions.Include(x => x.User).ThenInclude(u => u!.Projects).FirstOrDefaultAsync(x => x.TokenHash == hash);
        if (s?.User is not { IsActive: true } u || s.RevokedAt != null || now >= s.ExpiresAt) { cache.Remove(key); return null; }
        var idle = s.Persistent ? TimeSpan.FromDays(O.RememberIdleDays) : TimeSpan.FromMinutes(O.IdleMinutes);
        if (now - s.LastSeenAt > idle)
        {
            s.RevokedAt = now;
            await db.SaveChangesAsync();
            cache.Remove(key);
            return null;
        }
        if (now - s.LastSeenAt >= TouchEvery)
        {
            s.LastSeenAt = now;
            await db.SaveChangesAsync();
        }
        var user = new PanelUser(u.Id, u.Username, u.DisplayName, u.Role, u.AllProjects,
            u.Projects.Select(p => p.ProjectKey).ToHashSet(StringComparer.Ordinal), u.MustChangePassword, s.Id);
        cache.Set(key, new Cached(user, s.ExpiresAt, s.LastSeenAt + idle, s.LastSeenAt, now), CacheFor);
        return user;
    }

    /// <summary>Call after anything that changes a user's sessions, role, projects or status, so cached validations are dropped.</summary>
    public void Invalidate(int userId) => _revokedAfter[userId] = clock.GetUtcNow().UtcDateTime;

    public async Task RevokeAsync(AppDbContext db, int userId, Guid? onlySession = null, Guid? exceptSession = null)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        await db.AdminSessions.Where(s => s.UserId == userId && s.RevokedAt == null
                && (onlySession == null || s.Id == onlySession) && (exceptSession == null || s.Id != exceptSession))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now));
        Invalidate(userId);
    }

    static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public sealed class SessionAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> o, ILoggerFactory logger, UrlEncoder encoder,
    SessionStore store, CookieNames cookies) : AuthenticationHandler<AuthenticationSchemeOptions>(o, logger, encoder)
{
    public const string SchemeName = "PanelSession";
    public const string MustChangeClaim = "must_change_password";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = Request.Cookies[cookies.Session];
        if (string.IsNullOrEmpty(token)) return AuthenticateResult.NoResult();
        var user = await store.ValidateAsync(token);
        if (user is null)
        {
            Response.Cookies.Delete(cookies.Session, cookies.Options(Context, null));
            return AuthenticateResult.NoResult();
        }
        Context.SetPanelUser(user);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Role, user.Role.ToString()),
            new("sid", user.SessionId.ToString()),
        };
        if (user.MustChangePassword) claims.Add(new(MustChangeClaim, "1"));
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)), SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 401;
        return Response.WriteAsJsonAsync(new { message = "برای ادامه وارد شوید.", code = "unauthorized" });
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 403;
        var mustChange = Context.PanelUser()?.MustChangePassword == true;
        return Response.WriteAsJsonAsync(mustChange
            ? new { message = "ابتدا رمز عبور خود را عوض کنید.", code = "password_change_required" }
            : new { message = "به این بخش دسترسی ندارید.", code = "forbidden" });
    }
}

/// <summary>Cookie names and options, so the login endpoint and the handler agree.</summary>
public sealed class CookieNames(IOptions<AuthOptions> options, IHostEnvironment env)
{
    public bool Secure { get; } = !env.IsDevelopment();
    public string Session => Secure ? "__Host-" + options.Value.CookieName : options.Value.CookieName;

    public CookieOptions Options(HttpContext ctx, DateTime? expires) => new()
    {
        HttpOnly = true,
        Secure = Secure || ctx.Request.IsHttps,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        Expires = expires,
        IsEssential = true,
    };
}

public static class Policies
{
    /// <summary>Signed in, even with a pending password change (only for /api/auth endpoints).</summary>
    public const string AnySession = "any-session";
    public const string Admin = "admin";
    public const string Owner = "owner";
}
