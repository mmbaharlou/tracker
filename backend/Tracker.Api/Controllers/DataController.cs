using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tracker.Api.Auth;
using Tracker.Api.Dashboard;
using Tracker.Api.Management;

namespace Tracker.Api.Controllers;

/// <summary>Reports and timelines for one project. Every action checks that the signed-in user may see that project.</summary>
[ApiController]
[Route("api/data")]
[Authorize]
public sealed class DataController(DashboardQueries db, Registry registry, AuditLog audit) : ControllerBase
{
    ObjectResult? Deny(string project)
        => registry.ProjectIncludingDisabled(project) is null ? this.Fail(404, "پروژه پیدا نشد.") : this.DenyProject(project);

    [HttpGet("overview")]
    public async Task<IActionResult> Overview(string project, DateTime? from, DateTime? to, int? tz)
    {
        if (Deny(project) is { } d) return d;
        var t = to ?? DateTime.UtcNow;
        var f = from ?? t.AddDays(-30);
        if ((t - f).TotalDays > 400) f = t.AddDays(-400);
        return Ok(await db.OverviewAsync(project, f, t, Math.Clamp(tz ?? 210, -720, 840)));
    }

    [HttpGet("events")]
    public async Task<IActionResult> Events(string project, string? type, string? userId, string? sessionId, string? q, DateTime? before, int? take)
    {
        if (Deny(project) is { } d) return d;
        return Ok(await db.EventsAsync(project, Clean(type), Clean(userId), Clean(sessionId), q, before, Math.Clamp(take ?? 100, 1, 500)));
    }

    [HttpGet("users")]
    public async Task<IActionResult> Users(string project, string? q, int? page)
    {
        if (Deny(project) is { } d) return d;
        return Ok(await db.UsersAsync(project, q, Math.Max(1, page ?? 1), 50));
    }

    [HttpGet("user")]
    public async Task<IActionResult> UserDetail(string project, string id, DateTime? before)
    {
        if (Deny(project) is { } d) return d;
        return Ok(await db.UserAsync(project, id, before, 300));
    }

    [HttpGet("visitor")]
    public async Task<IActionResult> Visitor(string project, string id)
    {
        if (Deny(project) is { } d) return d;
        return Ok(await db.VisitorAsync(project, id, 300));
    }

    [HttpGet("session")]
    public async Task<IActionResult> Session(string project, string id)
    {
        if (Deny(project) is { } d) return d;
        return Ok(await db.SessionAsync(project, id));
    }

    /// <summary>Right to be forgotten: deletes every event, alias and profile of one end user. Admins only.</summary>
    [HttpPost("user/forget")]
    [Authorize(Policy = Policies.Admin)]
    public async Task<IActionResult> Forget(string project, string id)
    {
        if (Deny(project) is { } d) return d;
        await db.ForgetUserAsync(project, id);
        await audit.WriteAsync("data.forget-user", $"{project}/{id}");
        return Ok();
    }

    static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
