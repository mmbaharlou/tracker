using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Tracker.Api.Auth;
using Tracker.Api.Data;

namespace Tracker.Api.Controllers;

public sealed record ProjectRef(string Key, string Name, bool IsEnabled);

/// <summary>Every project the panel knows about (for access lists and the project switcher).</summary>
public interface IProjectCatalog
{
    Task<IReadOnlyList<ProjectRef>> ListAsync(CancellationToken ct = default);
}

[ApiController]
[Route("api/auth")]
public sealed class AuthController(AuthService auth, SessionStore sessions, CookieNames cookies, AppDbContext db, AuditLog audit, IProjectCatalog catalog)
    : ControllerBase
{
    public sealed record LoginDto(string? Username, string? Password, bool Remember = false);
    public sealed record ChangePasswordDto(string? CurrentPassword, string? NewPassword);

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(PanelAuthSetup.LoginRateLimit)]
    public async Task<IActionResult> Login(LoginDto dto)
    {
        var r = await auth.LoginAsync(dto.Username, dto.Password, dto.Remember, this.ClientIp() ?? "?", Request.Headers.UserAgent.ToString());
        if (!r.Ok) return this.Fail(r.Status, r.Message!);
        Response.Cookies.Append(cookies.Session, r.Token!, cookies.Options(HttpContext, dto.Remember ? r.ExpiresAt : null));
        return Ok(new { mustChangePassword = r.User!.MustChangePassword });
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout()
    {
        if (HttpContext.PanelUser() is { } me)
        {
            await sessions.RevokeAsync(db, me.Id, onlySession: me.SessionId);
            await audit.WriteAsync("logout");
        }
        Response.Cookies.Delete(cookies.Session, cookies.Options(HttpContext, null));
        return Ok();
    }

    [HttpGet("me")]
    [Authorize(Policy = Policies.AnySession)]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var me = HttpContext.PanelUser()!;
        var all = await catalog.ListAsync(ct);
        var projects = all.Where(p => me.CanSee(p.Key)).OrderBy(p => p.Name).ToList();
        var user = await db.AdminUsers.AsNoTracking().FirstAsync(u => u.Id == me.Id, ct);
        return Ok(new
        {
            me.Id, me.Username, me.DisplayName, Role = me.Role.ToString(), me.MustChangePassword,
            user.LastLoginAt, user.PasswordChangedAt, projects,
        });
    }

    [HttpPost("change-password")]
    [Authorize(Policy = Policies.AnySession)]
    public async Task<IActionResult> ChangePassword(ChangePasswordDto dto)
    {
        var err = await auth.ChangePasswordAsync(HttpContext.PanelUser()!, dto.CurrentPassword, dto.NewPassword);
        return err is null ? Ok(new { message = "رمز عبور عوض شد و بقیه‌ی دستگاه‌ها از حساب خارج شدند." }) : this.Fail(400, err);
    }

    [HttpGet("sessions")]
    [Authorize(Policy = Policies.AnySession)]
    public async Task<IActionResult> Sessions(CancellationToken ct)
    {
        var me = HttpContext.PanelUser()!;
        var now = DateTime.UtcNow;
        var list = await db.AdminSessions.AsNoTracking()
            .Where(s => s.UserId == me.Id && s.RevokedAt == null && s.ExpiresAt > now)
            .OrderByDescending(s => s.LastSeenAt)
            .Select(s => new { s.Id, s.CreatedAt, s.LastSeenAt, s.ExpiresAt, s.Ip, s.UserAgent, s.Persistent, Current = s.Id == me.SessionId })
            .ToListAsync(ct);
        return Ok(list);
    }

    [HttpDelete("sessions/{id:guid}")]
    [Authorize(Policy = Policies.AnySession)]
    public async Task<IActionResult> RevokeSession(Guid id)
    {
        var me = HttpContext.PanelUser()!;
        await sessions.RevokeAsync(db, me.Id, onlySession: id);
        await audit.WriteAsync("session.revoke", id.ToString());
        if (id == me.SessionId) Response.Cookies.Delete(cookies.Session, cookies.Options(HttpContext, null));
        return Ok();
    }

    [HttpPost("sessions/revoke-others")]
    [Authorize(Policy = Policies.AnySession)]
    public async Task<IActionResult> RevokeOthers()
    {
        var me = HttpContext.PanelUser()!;
        await sessions.RevokeAsync(db, me.Id, exceptSession: me.SessionId);
        await audit.WriteAsync("session.revoke-others");
        return Ok();
    }
}
