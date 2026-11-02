using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Tracker.Core;

namespace Tracker.AspNetCore;

public interface ITracker
{
    /// <summary>Record a business event, e.g. Track("OrderPaid", new { orderId, amount }).
    /// User and session are taken from the current request when available.</summary>
    void Track(string name, object? properties = null, string? userId = null);

    /// <summary>Attach traits (name, plan, ...) to a user profile.</summary>
    void Identify(string userId, object? traits = null);
}

internal sealed class DefaultTracker : ITracker
{
    private readonly EventQueue _queue;
    private readonly IHttpContextAccessor _http;
    private readonly TrackerOptions _options;
    private readonly EffectiveSettings _settings;
    private readonly Masker _masker;

    public DefaultTracker(EventQueue queue, IHttpContextAccessor http, TrackerOptions options, EffectiveSettings settings, Masker masker)
    {
        _queue = queue; _http = http; _options = options; _settings = settings; _masker = masker;
    }

    public void Track(string name, object? properties = null, string? userId = null)
        => Enqueue(EventTypes.Custom, name, properties, userId);

    public void Identify(string userId, object? traits = null)
        => Enqueue(EventTypes.Identify, null, traits, userId);

    private void Enqueue(string type, string? name, object? props, string? userId)
    {
        if (!_settings.Enabled) return;
        try
        {
            var e = new TrackEvent { Type = type, Name = name, Properties = _masker.MaskProperties(ToDictionary(props)) };
            var ctx = _http.HttpContext;
            if (ctx != null) RequestInfo.Fill(e, ctx, _options);
            if (!string.IsNullOrEmpty(userId)) e.UserId = userId;
            _queue.TryEnqueue(e);
        }
        catch
        {
            // Tracking must never break the caller.
        }
    }

    internal static Dictionary<string, JsonElement>? ToDictionary(object? props)
    {
        if (props == null) return null;
        var element = JsonSerializer.SerializeToElement(props, TrackerJson.Options);
        if (element.ValueKind != JsonValueKind.Object)
            return new Dictionary<string, JsonElement> { ["value"] = element };
        var dict = new Dictionary<string, JsonElement>();
        foreach (var p in element.EnumerateObject()) dict[p.Name] = p.Value.Clone();
        return dict;
    }
}
