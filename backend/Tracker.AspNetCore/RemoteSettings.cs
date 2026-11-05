using System.Net.Http.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tracker.Core;

namespace Tracker.AspNetCore;

/// <summary>
/// Local options merged with the settings pushed from the central Collector.
/// The central value wins when set; the master switch is off if either side is off.
/// </summary>
public sealed class EffectiveSettings
{
    private readonly TrackerOptions _o;
    private volatile ProjectSettings? _remote;

    public EffectiveSettings(TrackerOptions options) => _o = options;

    public ProjectSettings? Remote => _remote;
    internal void Apply(ProjectSettings? remote) => _remote = remote;

    public bool Enabled => _o.Enabled && (_remote?.Enabled ?? true);
    public bool TrackApiCalls => _remote?.TrackApiCalls ?? _o.TrackApiCalls;
    public double SampleRate => _remote?.SampleRate ?? _o.SampleRate;
    public bool CaptureRequestBody => _remote?.CaptureRequestBody ?? _o.CaptureRequestBody;
    public bool CaptureQueryString => _remote?.CaptureQueryString ?? _o.CaptureQueryString;
    /// <summary>Local and central ignore lists both apply.</summary>
    public IEnumerable<string> IgnorePathPrefixes => _remote?.IgnorePathPrefixes is { } r ? _o.IgnorePathPrefixes.Concat(r) : _o.IgnorePathPrefixes;
    public string[] IncludePathPrefixes => _remote?.IncludePathPrefixes is { Length: > 0 } r ? r : _o.IncludePathPrefixes;
}

/// <summary>Polls the Collector for this project's central settings (signed request, every minute by default).</summary>
internal sealed class RemoteSettingsPoller : BackgroundService
{
    private readonly TrackerOptions _o;
    private readonly EffectiveSettings _settings;
    private readonly IHttpClientFactory _http;
    private readonly ILogger<RemoteSettingsPoller> _logger;

    public RemoteSettingsPoller(TrackerOptions o, EffectiveSettings settings, IHttpClientFactory http, ILogger<RemoteSettingsPoller> logger)
    { _o = o; _settings = settings; _http = http; _logger = logger; }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!_o.Enabled || !_o.UseRemoteSettings || string.IsNullOrWhiteSpace(_o.CollectorUrl) || string.IsNullOrWhiteSpace(_o.ServerSecret)) return;
        var failures = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                _settings.Apply(await FetchAsync(ct));
                failures = 0;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                // Keep the last known settings; log once in a while only.
                if (failures++ % 30 == 0) _logger.LogWarning(ex, "Tracker: could not refresh central settings");
            }
            try { await Task.Delay(_o.RemoteSettingsInterval, ct); } catch (OperationCanceledException) { return; }
        }
    }

    internal async Task<ProjectSettings?> FetchAsync(CancellationToken ct)
    {
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var req = new HttpRequestMessage(HttpMethod.Get, _o.CollectorUrl.TrimEnd('/') + "/v1/server/config");
        req.Headers.Add(TrackerHeaders.Project, _o.ProjectKey);
        req.Headers.Add(TrackerHeaders.Timestamp, ts.ToString(System.Globalization.CultureInfo.InvariantCulture));
        req.Headers.Add(TrackerHeaders.Signature, BatchSigner.Sign(_o.ServerSecret, ts, ReadOnlySpan<byte>.Empty));
        using var res = await _http.CreateClient(BatchSender.HttpClientName).SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<ProjectSettings>(TrackerJson.Options, ct);
    }
}
