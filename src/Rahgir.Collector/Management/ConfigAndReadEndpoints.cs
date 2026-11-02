using Rahgir.Collector.Dashboard;
using Rahgir.Collector.Ingestion;
using Rahgir.Core;

namespace Rahgir.Collector.Management;

public static class ConfigAndReadEndpoints
{
    public const string ReadPolicy = "rahgir-read";

    public static void MapConfigAndRead(this WebApplication app)
    {
        // Browser SDK: public, non-secret switches only.
        app.MapGet("/v1/config", (HttpContext ctx, Registry r, string k) =>
        {
            var p = r.Project(k);
            if (p == null) return Results.StatusCode(403);
            var origin = ctx.Request.Headers.Origin.ToString();
            var opts = p.AsOptions();
            if (!string.IsNullOrEmpty(origin) && IngestEndpoints.OriginAllowed(ctx, opts))
            {
                ctx.Response.Headers.AccessControlAllowOrigin = opts.AllowedOrigins.Length == 0 ? "*" : origin;
                ctx.Response.Headers.Vary = "Origin";
            }
            ctx.Response.Headers.CacheControl = "public, max-age=120";
            return Results.Ok(new WebConfig
            {
                Enabled = p.Settings.Enabled,
                SampleRate = Math.Clamp(p.Settings.WebSampleRate ?? 1, 0, 1),
                Autocapture = p.Settings.Autocapture,
            });
        }).RequireRateLimiting(IngestEndpoints.WebPolicy);

        // Backend SDK: signed request (HMAC over timestamp + empty body), full settings.
        app.MapGet("/v1/server/config", (HttpContext ctx, Registry r) =>
        {
            var p = r.Project(ctx.Request.Headers[RahgirHeaders.Project].ToString());
            if (p == null || string.IsNullOrEmpty(p.ServerSecret)) return Results.StatusCode(403);
            if (!BatchSigner.Verify(p.ServerSecret, ctx.Request.Headers[RahgirHeaders.Timestamp], ctx.Request.Headers[RahgirHeaders.Signature],
                    ReadOnlySpan<byte>.Empty, DateTimeOffset.UtcNow))
                return Results.StatusCode(401);
            ctx.Response.Headers.CacheControl = "no-store";
            return Results.Ok(p.Settings);
        }).RequireRateLimiting(IngestEndpoints.ServerPolicy);

        // Read-only API for one project, authenticated by its read key ("Authorization: Bearer rk_...").
        // Meant to be called server-to-server, e.g. by the project's own backend to show its data in its admin panel.
        var read = app.MapGroup("/v1/read").RequireRateLimiting(ReadPolicy);

        read.MapGet("/overview", async (HttpContext ctx, Registry r, DashboardQueries db, DateTime? from, DateTime? to, int? tz) =>
        {
            if (Auth(ctx, r) is not { } p) return Results.Unauthorized();
            var t = to ?? DateTime.UtcNow; var f = from ?? t.AddDays(-30);
            if ((t - f).TotalDays > 400) f = t.AddDays(-400);
            return Results.Ok(await db.OverviewAsync(p.Key, f, t, Math.Clamp(tz ?? 210, -720, 840)));
        });
        read.MapGet("/events", async (HttpContext ctx, Registry r, DashboardQueries db, string? type, string? userId, string? sessionId, string? q, DateTime? before, int? take) =>
        {
            if (Auth(ctx, r) is not { } p) return Results.Unauthorized();
            return Results.Ok(await db.EventsAsync(p.Key, Empty(type), Empty(userId), Empty(sessionId), q, before, Math.Clamp(take ?? 100, 1, 500)));
        });
        read.MapGet("/users", async (HttpContext ctx, Registry r, DashboardQueries db, string? q, int? page) =>
        {
            if (Auth(ctx, r) is not { } p) return Results.Unauthorized();
            return Results.Ok(await db.UsersAsync(p.Key, q, Math.Max(1, page ?? 1), 50));
        });
        read.MapGet("/user", async (HttpContext ctx, Registry r, DashboardQueries db, string id, DateTime? before) =>
        {
            if (Auth(ctx, r) is not { } p) return Results.Unauthorized();
            return Results.Ok(await db.UserAsync(p.Key, id, before, 300));
        });
        read.MapGet("/session", async (HttpContext ctx, Registry r, DashboardQueries db, string id) =>
        {
            if (Auth(ctx, r) is not { } p) return Results.Unauthorized();
            return Results.Ok(await db.SessionAsync(p.Key, id));
        });
    }

    private static ProjectInfo? Auth(HttpContext ctx, Registry r)
    {
        var h = ctx.Request.Headers.Authorization.ToString();
        ctx.Response.Headers.CacheControl = "no-store";
        return h.StartsWith("Bearer ", StringComparison.Ordinal) ? r.ProjectByReadKey(h[7..].Trim()) : null;
    }

    private static string? Empty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
