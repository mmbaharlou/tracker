using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Rahgir.Core;

namespace Rahgir.AspNetCore;

/// <summary>
/// Records "who called which endpoint, when, with what result". All the work after the request
/// is a few property reads and one non-blocking enqueue.
/// </summary>
internal sealed class RahgirMiddleware
{
    private readonly RequestDelegate _next;
    private readonly RahgirOptions _options;
    private readonly EffectiveSettings _settings;
    private readonly EventQueue _queue;
    private readonly Masker _masker;
    private readonly ILogger<RahgirMiddleware> _logger;

    public RahgirMiddleware(RequestDelegate next, RahgirOptions options, EffectiveSettings settings, EventQueue queue, Masker masker, ILogger<RahgirMiddleware> logger)
    {
        _next = next; _options = options; _settings = settings; _queue = queue; _masker = masker; _logger = logger;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        if (!ShouldConsider(ctx))
        {
            await _next(ctx);
            return;
        }

        string? body = null;
        if (_settings.CaptureRequestBody) body = await TryReadBodyAsync(ctx);

        try
        {
            await _next(ctx);
        }
        catch
        {
            TryRecord(ctx, body, failed: true);
            throw;
        }
        TryRecord(ctx, body, failed: false);
    }

    private bool ShouldConsider(HttpContext ctx)
    {
        if (!_settings.Enabled || !_settings.TrackApiCalls) return false;
        var req = ctx.Request;
        if (_options.IgnorePreflight && (HttpMethods.IsOptions(req.Method) || HttpMethods.IsHead(req.Method))) return false;
        var path = req.Path.Value ?? "/";
        foreach (var p in _settings.IgnorePathPrefixes)
            if (path.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return false;
        var include = _settings.IncludePathPrefixes;
        if (include.Length > 0)
        {
            var ok = false;
            foreach (var p in include)
                if (path.StartsWith(p, StringComparison.OrdinalIgnoreCase)) { ok = true; break; }
            if (!ok) return false;
        }
        var rate = _settings.SampleRate;
        if (rate < 1.0 && Random.Shared.NextDouble() >= rate) return false;
        return true;
    }

    private void TryRecord(HttpContext ctx, string? body, bool failed)
    {
        try
        {
            var endpoint = ctx.GetEndpoint();
            if (endpoint?.Metadata.GetMetadata<NoTrackAttribute>() != null) return;

            var e = new TrackEvent
            {
                Type = EventTypes.Api,
                Name = endpoint?.Metadata.GetMetadata<TrackAsAttribute>()?.Name,
                Route = (endpoint as RouteEndpoint)?.RoutePattern.RawText,
                StatusCode = failed ? 500 : ctx.Response.StatusCode,
            };
            RequestInfo.Fill(e, ctx, _options);

            Dictionary<string, JsonElement>? props = null;
            if (_settings.CaptureQueryString && ctx.Request.QueryString.HasValue)
            {
                props ??= new();
                var q = new Dictionary<string, string>();
                foreach (var (k, v) in ctx.Request.Query)
                    q[k] = _masker.IsSensitiveKey(k) ? Masker.Mask : _masker.MaskText(v.ToString());
                props["query"] = JsonSerializer.SerializeToElement(q);
            }
            if (body != null)
            {
                props ??= new();
                props["body"] = JsonSerializer.SerializeToElement(body);
            }
            if (endpoint is RouteEndpoint)
            {
                // Route values such as {id} tell you *which* order/product the user touched.
                var rv = ctx.Request.RouteValues;
                var values = new Dictionary<string, string?>();
                foreach (var (k, v) in rv)
                {
                    if (k is "controller" or "action" or "area" or "page") continue;
                    values[k] = _masker.IsSensitiveKey(k) ? Masker.Mask : RequestInfo.Truncate(v?.ToString(), 128);
                }
                if (values.Count > 0) { props ??= new(); props["routeValues"] = JsonSerializer.SerializeToElement(values); }
            }
            e.Properties = props;
            _queue.TryEnqueue(e);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Rahgir: failed to record request");
        }
    }

    private async Task<string?> TryReadBodyAsync(HttpContext ctx)
    {
        try
        {
            var req = ctx.Request;
            if (req.ContentLength is null or 0 || req.ContentLength > _options.MaxBodyBytes) return null;
            if (req.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) != true) return null;
            req.EnableBuffering();
            var buffer = new byte[(int)req.ContentLength.Value];
            var read = 0;
            while (read < buffer.Length)
            {
                var n = await req.Body.ReadAsync(buffer.AsMemory(read), ctx.RequestAborted);
                if (n == 0) break;
                read += n;
            }
            req.Body.Position = 0;
            return _masker.MaskJson(Encoding.UTF8.GetString(buffer, 0, read));
        }
        catch
        {
            try { ctx.Request.Body.Position = 0; } catch { /* ignore */ }
            return null;
        }
    }
}
