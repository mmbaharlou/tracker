using System.Net;
using System.Text.Json;
using Tracker.Core;
using Tracker.Collector.Storage;

namespace Tracker.Collector.Ingestion;

/// <summary>Turns untrusted SDK input into safe, bounded, masked rows.</summary>
public sealed class EventValidator
{
    private static readonly HashSet<string> WebTypes = new(StringComparer.Ordinal)
    {
        EventTypes.PageView, EventTypes.PageLeave, EventTypes.Click, EventTypes.Submit, EventTypes.Change,
        EventTypes.Custom, EventTypes.Identify,
    };
    private const int MaxPropertiesChars = 16_000;

    private readonly Masker _masker;
    private readonly CollectorOptions _options;

    public EventValidator(Masker masker, CollectorOptions options) { _masker = masker; _options = options; }

    public List<StoredEvent> Validate(EventBatch batch, string projectKey, string source, IPAddress? requestIp, string? requestUa)
    {
        var now = DateTime.UtcNow;
        var list = new List<StoredEvent>(Math.Min(batch.Events.Count, _options.MaxEventsPerBatch));
        foreach (var e in batch.Events.Take(_options.MaxEventsPerBatch))
        {
            if (e == null) continue;
            var type = Token(e.Type, 32)?.ToLowerInvariant();
            if (type == null) continue;
            // Browsers may not claim to be server API calls.
            if (source == "web" && !WebTypes.Contains(type)) continue;

            var ts = e.Timestamp.UtcDateTime;
            if (ts == default || Math.Abs((ts - now).TotalHours) > 24) ts = now;

            // Browser events: IP and UA come from the request itself, never from the payload.
            string? ip = source == "web" ? requestIp?.ToString() : Ip(e.Ip);
            if (ip != null && _options.AnonymizeIp && IPAddress.TryParse(ip, out var parsed)) ip = Anonymize(parsed).ToString();
            var ua = source == "web" ? requestUa : e.UserAgent;
            var parsedUa = UserAgentParser.Parse(ua);

            string? props = null;
            var masked = _masker.MaskProperties(e.Properties);
            if (masked != null && masked.Count > 0)
            {
                props = JsonSerializer.Serialize(masked);
                if (props.Length > MaxPropertiesChars) props = JsonSerializer.Serialize(new { truncated = true });
            }

            list.Add(new StoredEvent
            {
                Id = e.Id == Guid.Empty ? Guid.NewGuid() : e.Id,
                ProjectKey = projectKey,
                Source = source,
                Type = type,
                Name = Text(e.Name, 200),
                Ts = ts,
                ReceivedAt = now,
                UserId = Text(e.UserId, 128),
                AnonymousId = Token(e.AnonymousId, 64),
                SessionId = Token(e.SessionId, 64),
                Url = Url(e.Url),
                Path = Text(e.Path, 512),
                Referrer = Url(e.Referrer),
                Title = Text(e.Title, 256),
                Route = Text(e.Route, 256),
                Method = Token(e.Method, 10)?.ToUpperInvariant(),
                StatusCode = e.StatusCode is >= 100 and <= 599 ? (short)e.StatusCode : null,
                Ip = ip,
                Browser = parsedUa.Browser,
                Os = parsedUa.Os,
                Device = parsedUa.Device,
                Screen = Token(e.Screen?.Replace("x", "_"), 16)?.Replace("_", "x"),
                Language = Token(e.Language, 16),
                Properties = props,
            });
        }
        return list;
    }

    private string? Text(string? v, int max)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        v = v.Trim();
        if (v.Length > max) v = v[..max];
        // Strip control characters.
        if (v.Any(char.IsControl)) v = new string(v.Where(c => !char.IsControl(c)).ToArray());
        return _masker.MaskText(v);
    }

    /// <summary>Ids / enums: letters, digits, - _ . only.</summary>
    private static string? Token(string? v, int max)
    {
        if (string.IsNullOrEmpty(v) || v.Length > max) return null;
        foreach (var c in v)
            if (!(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '$')) return null;
        return v;
    }

    private string? Url(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        if (!Uri.TryCreate(v, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https")) return null;
        // Mask sensitive query parameters (?token=..., ?password=...).
        var query = uri.Query.TrimStart('?');
        if (query.Length > 0)
        {
            var parts = query.Split('&').Select(p =>
            {
                var i = p.IndexOf('=');
                var key = Uri.UnescapeDataString(i < 0 ? p : p[..i]);
                return i >= 0 && _masker.IsSensitiveKey(key) ? p[..i] + "=" + Masker.Mask : p;
            });
            v = uri.GetLeftPart(UriPartial.Path) + "?" + string.Join('&', parts) + uri.Fragment;
        }
        return Text(v, 1024);
    }

    private static string? Ip(string? v) => IPAddress.TryParse(v, out var ip) ? ip.ToString() : null;

    private static IPAddress Anonymize(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        var b = ip.GetAddressBytes();
        if (b.Length == 4) b[3] = 0; else for (var i = 6; i < b.Length; i++) b[i] = 0;
        return new IPAddress(b);
    }
}
