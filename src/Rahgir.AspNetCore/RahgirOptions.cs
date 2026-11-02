namespace Rahgir.AspNetCore;

public sealed class RahgirOptions
{
    /// <summary>Master switch. When false nothing is captured and nothing is sent.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Project key registered in the Collector (e.g. "shop-api").</summary>
    public string ProjectKey { get; set; } = "";

    /// <summary>Server secret of the project. Used to HMAC-sign every batch. Keep it out of source control.</summary>
    public string ServerSecret { get; set; } = "";

    /// <summary>Base URL of the Collector, e.g. https://track.mydomain.ir</summary>
    public string CollectorUrl { get; set; } = "";

    /// <summary>Fraction of API calls to record, 0..1. Custom Track() events are always recorded.</summary>
    public double SampleRate { get; set; } = 1.0;

    public int QueueCapacity { get; set; } = 10_000;
    public int BatchSize { get; set; } = 500;
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(2);
    public TimeSpan HttpTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public int MaxRetries { get; set; } = 3;

    /// <summary>Record API calls automatically. Disable to only send events from Track().</summary>
    public bool TrackApiCalls { get; set; } = true;

    /// <summary>Only paths with this prefix are tracked as API calls. Empty = every path.</summary>
    public string[] IncludePathPrefixes { get; set; } = Array.Empty<string>();

    /// <summary>Paths never tracked (prefix match, case-insensitive).</summary>
    public string[] IgnorePathPrefixes { get; set; } = { "/health", "/healthz", "/swagger", "/favicon", "/_framework", "/metrics" };

    /// <summary>Skip OPTIONS / HEAD requests.</summary>
    public bool IgnorePreflight { get; set; } = true;

    /// <summary>Store the JSON request body (masked). Off by default. Costs a buffered read on tracked requests.</summary>
    public bool CaptureRequestBody { get; set; }
    public int MaxBodyBytes { get; set; } = 4096;

    /// <summary>Store the query string (masked).</summary>
    public bool CaptureQueryString { get; set; } = true;

    public bool CaptureIp { get; set; } = true;
    /// <summary>Zero the last octet (IPv4) / last 80 bits (IPv6).</summary>
    public bool AnonymizeIp { get; set; }

    /// <summary>Claim types checked, in order, to find the user id.</summary>
    public string[] UserIdClaimTypes { get; set; } =
    {
        "sub", System.Security.Claims.ClaimTypes.NameIdentifier, "userId", "user_id", "uid", "id",
    };

    /// <summary>Follow the settings set for this project in the central dashboard (on/off, sampling, paths...).</summary>
    public bool UseRemoteSettings { get; set; } = true;
    public TimeSpan RemoteSettingsInterval { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Read-only key of this project, used by MapRahgirInsights to show tracking data inside this app.</summary>
    public string ReadKey { get; set; } = "";

    /// <summary>Extra keys to mask in bodies / properties (on top of password, token, card, ...).</summary>
    public string[] SensitiveKeys { get; set; } = Array.Empty<string>();
}
