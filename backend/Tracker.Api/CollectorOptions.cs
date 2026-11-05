namespace Tracker.Api;

public sealed class CollectorOptions
{
    public List<ProjectOptions> Projects { get; set; } = new();

    /// <summary>Delete events older than this many days. 0 = keep forever.</summary>
    public int RetentionDays { get; set; } = 180;

    public int QueueCapacity { get; set; } = 100_000;
    public int WriteBatchSize { get; set; } = 5_000;

    public int MaxWebBodyBytes { get; set; } = 256 * 1024;
    public int MaxServerBodyBytes { get; set; } = 2 * 1024 * 1024;
    public int MaxEventsPerBatch { get; set; } = 1_000;

    /// <summary>Requests per minute per IP on the browser endpoint.</summary>
    public int WebRateLimitPerMinute { get; set; } = 300;
    /// <summary>Requests per minute per project on the server endpoint.</summary>
    public int ServerRateLimitPerMinute { get; set; } = 3_000;

    /// <summary>Extra reverse proxies (besides this machine) allowed to set X-Forwarded-For.</summary>
    public string[] KnownProxies { get; set; } = Array.Empty<string>();

    /// <summary>Zero the last part of visitor IPs before storing.</summary>
    public bool AnonymizeIp { get; set; }

    public string[] SensitiveKeys { get; set; } = Array.Empty<string>();

    /// <summary>Folder for ASP.NET Data Protection keys (encrypts project secrets in the database). Back it up.</summary>
    public string? DataProtectionKeysPath { get; set; }

    /// <summary>Requests per minute per read key on /v1/read.</summary>
    public int ReadRateLimitPerMinute { get; set; } = 600;
}

public sealed class ProjectOptions
{
    /// <summary>Public id, used by both SDKs, e.g. "shop".</summary>
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Secret shared with the backend SDK only. Never put it in frontend code.</summary>
    public string ServerSecret { get; set; } = "";
    /// <summary>Browser origins allowed to send events, e.g. "https://shop.ir". Empty = any (not recommended).</summary>
    public string[] AllowedOrigins { get; set; } = Array.Empty<string>();
    /// <summary>Optional read-only key for the /v1/read API (e.g. to show data inside the project's own admin panel).</summary>
    public string ReadKey { get; set; } = "";
    /// <summary>Settings pushed to every install of this project.</summary>
    public Tracker.Core.ProjectSettings? Settings { get; set; }
}
