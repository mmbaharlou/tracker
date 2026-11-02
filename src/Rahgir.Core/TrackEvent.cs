using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rahgir.Core;

/// <summary>Well-known event types. Any other string is allowed for custom events.</summary>
public static class EventTypes
{
    public const string PageView = "pageview";
    public const string PageLeave = "pageleave";
    public const string Click = "click";
    public const string Submit = "submit";
    public const string Change = "change";
    public const string Api = "api";
    public const string Custom = "custom";
    public const string Identify = "identify";
}

/// <summary>One tracked user action. Shared wire format between SDKs and the Collector.</summary>
public sealed class TrackEvent
{
    [JsonPropertyName("id")] public Guid Id { get; set; } = Guid.NewGuid();
    [JsonPropertyName("type")] public string Type { get; set; } = EventTypes.Custom;
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("ts")] public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("userId")] public string? UserId { get; set; }
    [JsonPropertyName("anonId")] public string? AnonymousId { get; set; }
    [JsonPropertyName("sessionId")] public string? SessionId { get; set; }

    [JsonPropertyName("url")] public string? Url { get; set; }
    [JsonPropertyName("path")] public string? Path { get; set; }
    [JsonPropertyName("referrer")] public string? Referrer { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }

    /// <summary>API events: route template, e.g. "api/orders/{id}".</summary>
    [JsonPropertyName("route")] public string? Route { get; set; }
    [JsonPropertyName("method")] public string? Method { get; set; }
    [JsonPropertyName("status")] public int? StatusCode { get; set; }

    [JsonPropertyName("ip")] public string? Ip { get; set; }
    [JsonPropertyName("ua")] public string? UserAgent { get; set; }
    [JsonPropertyName("screen")] public string? Screen { get; set; }
    [JsonPropertyName("lang")] public string? Language { get; set; }

    /// <summary>Free-form properties. Masked before storage.</summary>
    [JsonPropertyName("props")] public Dictionary<string, JsonElement>? Properties { get; set; }
}

public sealed class EventBatch
{
    [JsonPropertyName("events")] public List<TrackEvent> Events { get; set; } = new();
    /// <summary>"web" or "server". Set by the SDK, enforced by the Collector.</summary>
    [JsonPropertyName("source")] public string? Source { get; set; }
    [JsonPropertyName("sdk")] public string? Sdk { get; set; }
}

public static class RahgirHeaders
{
    public const string Project = "X-Rahgir-Project";
    public const string Timestamp = "X-Rahgir-Timestamp";
    public const string Signature = "X-Rahgir-Signature";
    public const string Session = "X-Rahgir-Session";
    public const string Anonymous = "X-Rahgir-Anon";
}

public static class RahgirJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
