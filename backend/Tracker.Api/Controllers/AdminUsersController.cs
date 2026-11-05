using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tracker.Api.Auth;
using Tracker.Api.Data;

namespace Tracker.Api.Controllers;

/// <summary>
/// Panel accounts. Admins manage viewers; only owners create or change admins and owners.
/// There is always at least one active owner.
/// </summary>
[ApiController]
[Route("api/admin/users")]
[Authorize(Policy = Policies.Admin)]
public sealed partial class AdminUsersController(AppDbContext db, SessionStore sessions, AuditLog audit, IProjectCatalog catalog) : ControllerBase
{
    public sealed record UserInput(string? Username, string? DisplayName, string? Password, AdminRole? Role, bool? AllProjects, string[]? Projects, bool? IsActive);

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var users = await db.AdminUsers.AsNoTracking().Include(u => u.Projects).OrderBy(u => u.Username).ToListAsync(ct);
        var sessionCounts = await db.AdminSessions.AsNoTracking().Where(s => s.RevokedAt == null && s.ExpiresAt > now)
            .GroupBy(s => s.UserId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return Ok(new
        {
            users = users.Select(u => new
            {
                u.Id, u.Username, u.DisplayName, Role = u.Role.ToString(), u.AllProjects, Projects = u.Projects.Select(p => p.ProjectKey).Order(),
                u.IsActive, u.MustChangePassword, Locked = u.LockedUntil > now, u.LockedUntil, u.FailedLogins, u.LastLoginAt, u.LastLoginIp,
                u.CreatedAt, u.PasswordChangedAt, ActiveSessions = sessionCounts.GetValueOrDefault(u.Id),
            }),
            projects = await catalog.ListAsync(ct),
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create(UserInput input, CancellationToken ct)
    {
        var me = this.Me();
        var username = input.Username?.Trim() ?? "";
        if (!UsernameRegex().IsMatch(username)) return this.Fail(400, "نام کاربری ۳ تا ۶۴ کاراکتر: حروف انگلیسی، عدد، نقطه، خط تیره یا زیرخط.");
        var role = input.Role ?? AdminRole.Viewer;
        if (role >= AdminRole.Admin && !me.IsOwner) return this.Fail(403, "فقط مالک می‌تواند مدیر یا مالک بسازد.");
        if (await db.AdminUsers.AnyAsync(u => u.Username == username, ct)) return this.Fail(400, "این نام کاربری وجود دارد.");
        if (PasswordPolicy.Check(input.Password, username) is { } err) return this.Fail(400, err);

        var user = new AdminUser
        {
            Username = username, DisplayName = Clean(input.DisplayName), PasswordHash = PasswordHasher.Hash(input.Password!), Role = role,
            AllProjects = input.AllProjects ?? false, MustChangePassword = true, IsActive = input.IsActive ?? true,
        };
        await SetProjectsAsync(user, input.Projects, ct);
        db.AdminUsers.Add(user);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("user.create", username, $"role={role}");
        return Ok(new { user.Id });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, UserInput input, CancellationToken ct)
    {
        var me = this.Me();
        var user = await db.AdminUsers.Include(u => u.Projects).FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return NotFound();
        if (CannotManage(me, user)) return this.Fail(403, "فقط مالک می‌تواند حساب مدیرها و مالک‌ها را تغییر دهد.");
        var role = input.Role ?? user.Role;
        if (role >= AdminRole.Admin && !me.IsOwner) return this.Fail(403, "فقط مالک می‌تواند نقش مدیر یا مالک بدهد.");
        var active = input.IsActive ?? user.IsActive;
        if (user.Id == me.Id && (role != user.Role || !active)) return this.Fail(400, "نقش یا وضعیت حساب خودتان را نمی‌توانید عوض کنید.");
        if (user.Role == AdminRole.Owner && (role != AdminRole.Owner || !active) && !await OtherActiveOwnerAsync(user.Id, ct))
            return this.Fail(400, "حداقل یک مالک فعال باید بماند.");

        var changes = new List<string>();
        if (role != user.Role) changes.Add($"role {user.Role}->{role}");
        if (active != user.IsActive) changes.Add(active ? "enabled" : "disabled");
        user.Role = role;
        user.IsActive = active;
        user.DisplayName = Clean(input.DisplayName);
        user.AllProjects = input.AllProjects ?? user.AllProjects;
        await SetProjectsAsync(user, input.Projects, ct);
        await db.SaveChangesAsync(ct);
        if (!active) await sessions.RevokeAsync(db, user.Id); else sessions.Invalidate(user.Id);
        await audit.WriteAsync("user.update", user.Username, string.Join(", ", changes));
        return Ok();
    }

    /// <summary>Sets a temporary password: the user must change it at next sign-in, and all their sessions end.</summary>
    [HttpPost("{id:int}/reset-password")]
    public async Task<IActionResult> ResetPassword(int id, UserInput input, CancellationToken ct)
    {
        var me = this.Me();
        var user = await db.AdminUsers.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return NotFound();
        if (CannotManage(me, user)) return this.Fail(403, "فقط مالک می‌تواند رمز مدیرها و مالک‌ها را عوض کند.");
        if (user.Id == me.Id) return this.Fail(400, "رمز خودتان را از صفحه‌ی «حساب من» عوض کنید.");
        if (PasswordPolicy.Check(input.Password, user.Username) is { } err) return this.Fail(400, err);
        user.PasswordHash = PasswordHasher.Hash(input.Password!);
        user.PasswordChangedAt = DateTime.UtcNow;
        user.MustChangePassword = true;
        user.FailedLogins = 0;
        user.LockedUntil = null;
        await db.SaveChangesAsync(ct);
        await sessions.RevokeAsync(db, user.Id);
        await audit.WriteAsync("user.reset-password", user.Username);
        return Ok();
    }

    [HttpPost("{id:int}/unlock")]
    public async Task<IActionResult> Unlock(int id, CancellationToken ct)
    {
        var user = await db.AdminUsers.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return NotFound();
        if (CannotManage(this.Me(), user)) return this.Fail(403, "دسترسی ندارید.");
        user.FailedLogins = 0;
        user.LockedUntil = null;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("user.unlock", user.Username);
        return Ok();
    }

    [HttpPost("{id:int}/revoke-sessions")]
    public async Task<IActionResult> RevokeSessions(int id, CancellationToken ct)
    {
        var user = await db.AdminUsers.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return NotFound();
        if (CannotManage(this.Me(), user)) return this.Fail(403, "دسترسی ندارید.");
        await sessions.RevokeAsync(db, user.Id);
        await audit.WriteAsync("user.revoke-sessions", user.Username);
        return Ok();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var me = this.Me();
        var user = await db.AdminUsers.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return NotFound();
        if (user.Id == me.Id) return this.Fail(400, "نمی‌توانید حساب خودتان را حذف کنید.");
        if (CannotManage(me, user)) return this.Fail(403, "فقط مالک می‌تواند مدیرها و مالک‌ها را حذف کند.");
        if (user.Role == AdminRole.Owner && !await OtherActiveOwnerAsync(user.Id, ct)) return this.Fail(400, "حداقل یک مالک فعال باید بماند.");
        db.AdminUsers.Remove(user);
        await db.SaveChangesAsync(ct);
        sessions.Invalidate(user.Id);
        await audit.WriteAsync("user.delete", user.Username);
        return Ok();
    }

    static bool CannotManage(PanelUser me, AdminUser target) => target.Role >= AdminRole.Admin && !me.IsOwner && target.Id != me.Id;

    Task<bool> OtherActiveOwnerAsync(int exceptId, CancellationToken ct)
        => db.AdminUsers.AnyAsync(u => u.Id != exceptId && u.Role == AdminRole.Owner && u.IsActive, ct);

    async Task SetProjectsAsync(AdminUser user, string[]? keys, CancellationToken ct)
    {
        if (keys is null) return;
        var known = (await catalog.ListAsync(ct)).Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
        var wanted = keys.Select(k => k.Trim()).Where(known.Contains).Distinct().ToHashSet();
        user.Projects.RemoveAll(p => !wanted.Contains(p.ProjectKey));
        foreach (var k in wanted.Where(k => user.Projects.All(p => p.ProjectKey != k)))
            user.Projects.Add(new AdminUserProject { UserId = user.Id, ProjectKey = k });
    }

    static string? Clean(string? s) => s?.Trim() is { Length: > 0 } t ? t[..Math.Min(t.Length, 100)] : null;

    [GeneratedRegex(@"^[A-Za-z0-9._-]{3,64}$")] private static partial Regex UsernameRegex();
}

[ApiController]
[Route("api/admin/audit")]
[Authorize(Policy = Policies.Admin)]
public sealed class AuditController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(string? q, string? action, long? before, int take = 100, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 500);
        var query = db.AuditLog.AsNoTracking().AsQueryable();
        if (before is { } b) query = query.Where(x => x.Id < b);
        if (!string.IsNullOrWhiteSpace(action)) query = query.Where(x => x.Action.StartsWith(action));
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(x => (x.Username ?? "").Contains(q) || (x.Target ?? "").Contains(q) || (x.Ip ?? "").Contains(q));
        return Ok(await query.OrderByDescending(x => x.Id).Take(take).ToListAsync(ct));
    }
}
