using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tracker.Api.Data;

namespace Tracker.Api.Auth;

/// <summary>Counts failed logins per IP (in memory) so one address cannot try passwords across many accounts.</summary>
public sealed class LoginThrottle(IOptions<AuthOptions> options, TimeProvider clock)
{
    static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
    readonly ConcurrentDictionary<string, (DateTime Start, int Fails)> _ip = new();

    public bool IsBlocked(string ip)
        => _ip.TryGetValue(ip, out var v) && clock.GetUtcNow().UtcDateTime - v.Start < Window && v.Fails >= options.Value.MaxFailuresPerIp;

    public void Fail(string ip)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        _ip.AddOrUpdate(ip, _ => (now, 1), (_, v) => now - v.Start >= Window ? (now, 1) : (v.Start, v.Fails + 1));
        if (_ip.Count > 50_000) foreach (var k in _ip.Where(x => now - x.Value.Start >= Window).Select(x => x.Key).ToList()) _ip.TryRemove(k, out _);
    }
}

public sealed class AuditLog(AppDbContext db, IHttpContextAccessor http, ILogger<AuditLog> log)
{
    public async Task WriteAsync(string action, string? target = null, string? detail = null, int? userId = null, string? username = null)
    {
        var ctx = http.HttpContext;
        var me = ctx?.PanelUser();
        try
        {
            db.AuditLog.Add(new AuditEntry
            {
                Action = action, Target = Cut(target, 200), Detail = Cut(detail, 1000),
                UserId = userId ?? me?.Id, Username = Cut(username ?? me?.Username, 64), Ip = ctx?.Connection.RemoteIpAddress?.ToString(),
            });
            await db.SaveChangesAsync();
        }
        catch (Exception ex) { log.LogWarning(ex, "audit write failed: {Action}", action); }
    }

    static string? Cut(string? s, int n) => s is { Length: > 0 } && s.Length > n ? s[..n] : s;
}

public sealed record LoginResult(bool Ok, int Status, string? Message, string? Token = null, DateTime? ExpiresAt = null, AdminUser? User = null);

public sealed class AuthService(AppDbContext db, SessionStore sessions, LoginThrottle throttle, AuditLog audit, IOptions<AuthOptions> options, TimeProvider clock)
{
    const string Generic = "نام کاربری یا رمز عبور اشتباه است.";

    public async Task<LoginResult> LoginAsync(string? username, string? password, bool remember, string ip, string? userAgent)
    {
        username = (username ?? "").Trim();
        password ??= "";
        if (username.Length is 0 or > 64 || password.Length is 0 or > PasswordPolicy.MaxLength) return new(false, 400, Generic);
        if (throttle.IsBlocked(ip))
        {
            await audit.WriteAsync("login.ip-blocked", username, null, null, username);
            return new(false, 429, "تلاش‌های ناموفق زیادی از این شبکه انجام شده. ۱۵ دقیقه بعد دوباره امتحان کنید.");
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var user = await db.AdminUsers.FirstOrDefaultAsync(u => u.Username == username);
        // Always verify a hash so the response time does not tell whether the username exists.
        var passwordOk = PasswordHasher.Verify(password, user?.PasswordHash ?? PasswordHasher.Dummy) && user is not null;

        if (user is not null && user.LockedUntil > now)
        {
            throttle.Fail(ip);
            await audit.WriteAsync("login.locked", username, null, user.Id, user.Username);
            var minutes = Math.Max(1, (int)Math.Ceiling((user.LockedUntil.Value - now).TotalMinutes));
            return new(false, 423, $"این حساب به‌دلیل تلاش‌های ناموفق تا {minutes} دقیقه‌ی دیگر قفل است.");
        }
        if (!passwordOk || user is null)
        {
            throttle.Fail(ip);
            if (user is not null)
            {
                user.FailedLogins++;
                var o = options.Value;
                if (user.FailedLogins % o.MaxFailedLogins == 0)
                {
                    var rounds = user.FailedLogins / o.MaxFailedLogins;
                    user.LockedUntil = now.AddMinutes(Math.Min(o.LockMinutes * Math.Pow(2, rounds - 1), 24 * 60));
                }
                await db.SaveChangesAsync();
            }
            await audit.WriteAsync("login.failed", username, user is null ? "unknown user" : $"attempt {user.FailedLogins}", user?.Id, username);
            await Task.Delay(Random.Shared.Next(200, 500));
            return new(false, 401, Generic);
        }
        if (!user.IsActive)
        {
            await audit.WriteAsync("login.disabled", username, null, user.Id, user.Username);
            return new(false, 403, "این حساب غیرفعال شده است.");
        }

        user.FailedLogins = 0;
        user.LockedUntil = null;
        user.LastLoginAt = now;
        user.LastLoginIp = ip;
        if (PasswordHasher.NeedsRehash(user.PasswordHash)) user.PasswordHash = PasswordHasher.Hash(password);
        await db.SaveChangesAsync();
        var (token, expires) = await sessions.CreateAsync(db, user, remember, ip, userAgent);
        await audit.WriteAsync("login.ok", username, remember ? "remember" : null, user.Id, user.Username);
        return new(true, 200, null, token, expires, user);
    }

    public async Task<string?> ChangePasswordAsync(PanelUser me, string? current, string? next)
    {
        var user = await db.AdminUsers.FindAsync(me.Id);
        if (user is null) return "حساب پیدا نشد.";
        if (!PasswordHasher.Verify(current ?? "", user.PasswordHash))
        {
            await audit.WriteAsync("password.change-failed", user.Username);
            return "رمز عبور فعلی اشتباه است.";
        }
        if (PasswordPolicy.Check(next, user.Username) is { } err) return err;
        if (PasswordHasher.Verify(next!, user.PasswordHash)) return "رمز جدید باید با رمز فعلی فرق داشته باشد.";
        user.PasswordHash = PasswordHasher.Hash(next!);
        user.PasswordChangedAt = clock.GetUtcNow().UtcDateTime;
        user.MustChangePassword = false;
        await db.SaveChangesAsync();
        // Every other device is signed out; this one stays.
        await sessions.RevokeAsync(db, user.Id, exceptSession: me.SessionId);
        await audit.WriteAsync("password.changed", user.Username);
        return null;
    }
}
