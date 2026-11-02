namespace Rahgir.Collector;

public sealed class CollectorOptions
{
    public List<ProjectOptions> Projects { get; set; } = new();
    public List<DashboardUser> DashboardUsers { get; set; } = new();

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

    /// <summary>Set when the Collector runs behind IIS ARR / nginx / Cloudflare and should read X-Forwarded-For.</summary>
    public bool TrustForwardedHeaders { get; set; }
    public string[] KnownProxies { get; set; } = Array.Empty<string>();

    /// <summary>Zero the last part of visitor IPs before storing.</summary>
    public bool AnonymizeIp { get; set; }

    /// <summary>Optional: only these IPs/CIDRs may open the dashboard (empty = any).</summary>
    public string[] DashboardAllowedIps { get; set; } = Array.Empty<string>();

    public string[] SensitiveKeys { get; set; } = Array.Empty<string>();
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
}

public sealed class DashboardUser
{
    public string Username { get; set; } = "";
    /// <summary>Generate with: dotnet Rahgir.Collector.dll hash-password</summary>
    public string PasswordHash { get; set; } = "";
    /// <summary>Project keys this user can see; "*" = all.</summary>
    public string[] Projects { get; set; } = { "*" };
    /// <summary>Admins can delete a user's data (right to be forgotten).</summary>
    public bool IsAdmin { get; set; }

    public bool CanSee(string project) => Projects.Contains("*") || Projects.Contains(project, StringComparer.Ordinal);
}
