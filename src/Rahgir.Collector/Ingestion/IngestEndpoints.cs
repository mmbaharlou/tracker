using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Primitives;
using Rahgir.Core;

namespace Rahgir.Collector.Ingestion;

public static class IngestEndpoints
{
    public const string WebPolicy = "rahgir-web";
    public const string ServerPolicy = "rahgir-server";

    public static void MapIngest(this WebApplication app)
    {
        // Browser SDK. Body is JSON sent as text/plain (sendBeacon / no-cors fetch), project key in ?k=
        app.MapMethods("/v1/batch", new[] { "OPTIONS" }, (HttpContext ctx, CollectorOptions o) =>
        {
            var project = o.Projects.FirstOrDefault(p => p.Key == ctx.Request.Query["k"].ToString());
            if (project != null) ApplyCors(ctx, project);
            return Results.NoContent();
        }).RequireRateLimiting(WebPolicy);

        app.MapPost("/v1/batch", async (HttpContext ctx, CollectorOptions o, EventValidator validator, IngestQueue queue) =>
        {
            var key = ctx.Request.Query["k"].ToString();
            var project = o.Projects.FirstOrDefault(p => p.Key == key);
            if (project == null) return Results.StatusCode(403);
            if (!OriginAllowed(ctx, project)) return Results.StatusCode(403);
            ApplyCors(ctx, project);

            var bytes = await ReadLimitedAsync(ctx.Request.Body, o.MaxWebBodyBytes, ctx.RequestAborted);
            if (bytes == null) return Results.StatusCode(413);
            var batch = Parse(bytes);
            if (batch == null) return Results.BadRequest();

            var events = validator.Validate(batch, project.Key, "web", ctx.Connection.RemoteIpAddress, ctx.Request.Headers.UserAgent.ToString());
            queue.TryEnqueueAll(events); // browsers don't retry; a full queue just drops
            return Results.Accepted();
        }).RequireRateLimiting(WebPolicy).DisableAntiforgery();

        // Backend SDK. gzip body, HMAC-signed.
        app.MapPost("/v1/server/batch", async (HttpContext ctx, CollectorOptions o, EventValidator validator, IngestQueue queue) =>
        {
            var key = ctx.Request.Headers[RahgirHeaders.Project].ToString();
            var project = o.Projects.FirstOrDefault(p => p.Key == key);
            if (project == null || string.IsNullOrEmpty(project.ServerSecret)) return Results.StatusCode(403);

            var raw = await ReadLimitedAsync(ctx.Request.Body, o.MaxServerBodyBytes, ctx.RequestAborted);
            if (raw == null) return Results.StatusCode(413);

            if (!BatchSigner.Verify(project.ServerSecret, ctx.Request.Headers[RahgirHeaders.Timestamp], ctx.Request.Headers[RahgirHeaders.Signature], raw, DateTimeOffset.UtcNow))
                return Results.StatusCode(401);

            byte[]? json = raw;
            if (ctx.Request.Headers.ContentEncoding.ToString().Contains("gzip", StringComparison.OrdinalIgnoreCase))
                json = await GunzipLimitedAsync(raw, o.MaxServerBodyBytes * 20, ctx.RequestAborted);
            if (json == null) return Results.StatusCode(413);

            var batch = Parse(json);
            if (batch == null) return Results.BadRequest();
            var events = validator.Validate(batch, project.Key, "server", null, null);
            return queue.TryEnqueueAll(events) ? Results.Accepted() : Results.StatusCode(503);
        }).RequireRateLimiting(ServerPolicy).DisableAntiforgery();

        app.MapGet("/healthz", (IngestQueue q) => Results.Ok(new { status = "ok", queued = q.Count }));
    }

    internal static bool OriginAllowed(HttpContext ctx, ProjectOptions project)
    {
        if (project.AllowedOrigins.Length == 0) return true;
        var origin = ctx.Request.Headers.Origin.ToString();
        if (string.IsNullOrEmpty(origin))
        {
            // Some browsers omit Origin on same-origin beacons; fall back to Referer.
            var referer = ctx.Request.Headers.Referer.ToString();
            if (Uri.TryCreate(referer, UriKind.Absolute, out var r)) origin = r.GetLeftPart(UriPartial.Authority);
        }
        return project.AllowedOrigins.Any(a => string.Equals(a.TrimEnd('/'), origin, StringComparison.OrdinalIgnoreCase));
    }

    private static void ApplyCors(HttpContext ctx, ProjectOptions project)
    {
        var origin = ctx.Request.Headers.Origin.ToString();
        if (string.IsNullOrEmpty(origin) || !OriginAllowed(ctx, project)) return;
        ctx.Response.Headers.AccessControlAllowOrigin = project.AllowedOrigins.Length == 0 ? "*" : origin;
        ctx.Response.Headers.AccessControlAllowMethods = "POST, OPTIONS";
        ctx.Response.Headers.AccessControlAllowHeaders = "Content-Type";
        ctx.Response.Headers.AccessControlMaxAge = "86400";
        ctx.Response.Headers.Vary = new StringValues("Origin");
    }

    private static EventBatch? Parse(byte[] json)
    {
        try { return JsonSerializer.Deserialize<EventBatch>(json, RahgirJson.Options); }
        catch (JsonException) { return null; }
    }

    internal static async Task<byte[]?> ReadLimitedAsync(Stream body, int max, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[16 * 1024];
        int n;
        while ((n = await body.ReadAsync(buffer, ct)) > 0)
        {
            if (ms.Length + n > max) return null;
            ms.Write(buffer, 0, n);
        }
        return ms.ToArray();
    }

    /// <summary>Decompress with an output cap (zip-bomb guard).</summary>
    internal static async Task<byte[]?> GunzipLimitedAsync(byte[] data, int max, CancellationToken ct)
    {
        try
        {
            await using var gz = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
            return await ReadLimitedAsync(gz, max, ct);
        }
        catch (InvalidDataException) { return null; }
    }
}
