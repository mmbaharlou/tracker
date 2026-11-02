using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Tracker.Collector.Security;
using Tracker.Collector.Management;

namespace Tracker.Collector.Dashboard;

public static class DashboardEndpoints
{
    public const string LoginPolicy = "tracker-login";
    public const string CsrfHeader = "X-Tracker-Csrf";

    public static void MapDashboardApi(this WebApplication app)
    {
        var api = app.MapGroup("/api/dash");

        api.MapPost("/login", async (HttpContext ctx, LoginRequest req, Registry o, LoginThrottle throttle) =>
        {
            var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "?";
            var username = (req.Username ?? "").Trim();
            if (username.Length is 0 or > 64 || (req.Password ?? "").Length is 0 or > 256) return Results.BadRequest();
            if (throttle.IsLocked(username, ip)) return Results.StatusCode(429);

            var user = o.User(username);
            // Always run a hash check so response time does not reveal whether the username exists.
            var ok = PasswordHasher.Verify(req.Password!, user?.PasswordHash ?? PasswordHasher.Dummy) && user != null;
            if (!ok)
            {
                throttle.Fail(username, ip);
                return Results.Unauthorized();
            }
            throttle.Success(username, ip);

            var identity = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, user!.Username),
                new Claim(ClaimTypes.Role, user.IsAdmin ? "admin" : "viewer"),
            }, CookieAuthenticationDefaults.AuthenticationScheme);
            await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
            return Results.Ok(new { user.Username, user.IsAdmin });
        }).RequireRateLimiting(LoginPolicy);

        api.MapPost("/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Ok();
        });

        var secured = api.MapGroup("").RequireAuthorization();

        secured.MapGet("/me", (ClaimsPrincipal p, Registry o) =>
        {
            var u = FindUser(p, o);
            if (u == null) return Results.Unauthorized();
            var projects = o.Projects.Where(x => u.CanSee(x.Key)).OrderBy(x => x.Name).Select(x => new { x.Key, x.Name, x.IsEnabled });
            return Results.Ok(new { u.Username, u.IsAdmin, projects });
        });

        secured.MapGet("/overview", async (ClaimsPrincipal p, Registry o, DashboardQueries db, string project, DateTime? from, DateTime? to, int? tz) =>
        {
            if (!CanSee(p, o, project)) return Results.Forbid();
            var t = to ?? DateTime.UtcNow;
            var f = from ?? t.AddDays(-30);
            if ((t - f).TotalDays > 400) f = t.AddDays(-400);
            return Results.Ok(await db.OverviewAsync(project, f, t, Math.Clamp(tz ?? 210, -720, 840)));
        });

        secured.MapGet("/events", async (ClaimsPrincipal p, Registry o, DashboardQueries db, string project,
            string? type, string? userId, string? sessionId, string? q, DateTime? before, int? take) =>
        {
            if (!CanSee(p, o, project)) return Results.Forbid();
            return Results.Ok(await db.EventsAsync(project, Empty(type), Empty(userId), Empty(sessionId), q, before, Math.Clamp(take ?? 100, 1, 500)));
        });

        secured.MapGet("/users", async (ClaimsPrincipal p, Registry o, DashboardQueries db, string project, string? q, int? page) =>
        {
            if (!CanSee(p, o, project)) return Results.Forbid();
            return Results.Ok(await db.UsersAsync(project, q, Math.Max(1, page ?? 1), 50));
        });

        secured.MapGet("/user", async (ClaimsPrincipal p, Registry o, DashboardQueries db, string project, string id, DateTime? before) =>
        {
            if (!CanSee(p, o, project)) return Results.Forbid();
            return Results.Ok(await db.UserAsync(project, id, before, 300));
        });

        secured.MapGet("/visitor", async (ClaimsPrincipal p, Registry o, DashboardQueries db, string project, string id) =>
        {
            if (!CanSee(p, o, project)) return Results.Forbid();
            return Results.Ok(await db.VisitorAsync(project, id, 300));
        });

        secured.MapGet("/session", async (ClaimsPrincipal p, Registry o, DashboardQueries db, string project, string id) =>
        {
            if (!CanSee(p, o, project)) return Results.Forbid();
            return Results.Ok(await db.SessionAsync(project, id));
        });

        secured.MapPost("/user/forget", async (ClaimsPrincipal p, Registry o, DashboardQueries db, string project, string id) =>
        {
            if (!CanSee(p, o, project) || FindUser(p, o)?.IsAdmin != true) return Results.Forbid();
            await db.ForgetUserAsync(project, id);
            return Results.Ok();
        });
    }

    public sealed record LoginRequest(string? Username, string? Password);

    internal static UserInfo? FindUser(ClaimsPrincipal p, Registry o) => o.User(p.Identity?.Name);

    internal static bool CanSee(ClaimsPrincipal p, Registry o, string project)
        => o.ProjectIncludingDisabled(project) != null && FindUser(p, o)?.CanSee(project) == true;

    private static string? Empty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>
    /// CSRF guard for state-changing dashboard calls: a custom header cannot be sent cross-site without a CORS
    /// preflight, and the dashboard API never answers preflights. Combined with SameSite=Strict cookies.
    /// </summary>
    public static IApplicationBuilder UseDashboardCsrfGuard(this IApplicationBuilder app)
        => app.Use(async (ctx, next) =>
        {
            if (ctx.Request.Path.StartsWithSegments("/api/dash") && !HttpMethods.IsGet(ctx.Request.Method)
                && ctx.Request.Headers[CsrfHeader] != "1")
            {
                ctx.Response.StatusCode = 400;
                return;
            }
            await next();
        });
}
