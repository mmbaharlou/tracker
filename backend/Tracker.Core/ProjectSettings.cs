using System.Text.Json.Serialization;

namespace Tracker.Core;

/// <summary>
/// Settings that the central Collector pushes to every install of a project. Changing them in the
/// dashboard takes effect in all installs within about a minute, without redeploying.
/// Null means "keep the install's local setting".
/// </summary>
public sealed class ProjectSettings
{
    /// <summary>Master switch for every install of the project.</summary>
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;

    /// <summary>Fraction of API calls recorded by the backend SDK (0..1).</summary>
    [JsonPropertyName("sampleRate")] public double? SampleRate { get; set; }

    /// <summary>Fraction of browser visitors tracked (0..1). Decided once per visitor.</summary>
    [JsonPropertyName("webSampleRate")] public double? WebSampleRate { get; set; }

    [JsonPropertyName("trackApiCalls")] public bool? TrackApiCalls { get; set; }
    [JsonPropertyName("autocapture")] public bool? Autocapture { get; set; }
    [JsonPropertyName("captureRequestBody")] public bool? CaptureRequestBody { get; set; }
    [JsonPropertyName("captureQueryString")] public bool? CaptureQueryString { get; set; }
    [JsonPropertyName("ignorePathPrefixes")] public string[]? IgnorePathPrefixes { get; set; }
    [JsonPropertyName("includePathPrefixes")] public string[]? IncludePathPrefixes { get; set; }
}

/// <summary>What the browser is allowed to know. No secrets, no server-only switches.</summary>
public sealed class WebConfig
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    [JsonPropertyName("sampleRate")] public double SampleRate { get; set; } = 1;
    [JsonPropertyName("autocapture")] public bool? Autocapture { get; set; }
}
