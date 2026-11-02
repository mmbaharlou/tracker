using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rahgir.Core;

namespace Rahgir.AspNetCore;

/// <summary>Background worker: drains the queue in batches, gzips, signs and sends them to the Collector.</summary>
internal sealed class BatchSender : BackgroundService
{
    internal const string HttpClientName = "Rahgir";
    private readonly EventQueue _queue;
    private readonly RahgirOptions _options;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<BatchSender> _logger;
    private DateTimeOffset _lastWarning = DateTimeOffset.MinValue;

    public BatchSender(EventQueue queue, RahgirOptions options, IHttpClientFactory httpFactory, ILogger<BatchSender> logger)
    {
        _queue = queue; _options = options; _httpFactory = httpFactory; _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled) return;
        if (string.IsNullOrWhiteSpace(_options.CollectorUrl) || string.IsNullOrWhiteSpace(_options.ProjectKey) || string.IsNullOrWhiteSpace(_options.ServerSecret))
        {
            _logger.LogWarning("Rahgir is enabled but CollectorUrl, ProjectKey or ServerSecret is missing. Tracking is disabled.");
            return;
        }

        var batch = new List<TrackEvent>(_options.BatchSize);
        var reader = _queue.Reader;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var flushTimer = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                flushTimer.CancelAfter(_options.FlushInterval);
                try
                {
                    while (batch.Count < _options.BatchSize && await reader.WaitToReadAsync(flushTimer.Token))
                        while (batch.Count < _options.BatchSize && reader.TryRead(out var e)) batch.Add(e);
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested) { /* flush interval */ }

                if (batch.Count > 0)
                {
                    await SendAsync(batch, stoppingToken);
                    batch.Clear();
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                Warn(ex, "Rahgir: unexpected sender error");
                batch.Clear();
            }
        }

        // Graceful shutdown: one last best-effort flush.
        while (reader.TryRead(out var e) && batch.Count < _options.BatchSize * 4) batch.Add(e);
        if (batch.Count > 0)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await SendAsync(batch, cts.Token); } catch { /* ignore */ }
        }
    }

    internal async Task SendAsync(List<TrackEvent> events, CancellationToken ct)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new EventBatch { Events = events, Source = "server", Sdk = "rahgir-dotnet/1.0" }, RahgirJson.Options);
        byte[] body;
        using (var ms = new MemoryStream())
        {
            using (var gz = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true)) gz.Write(payload);
            body = ms.ToArray();
        }

        var url = _options.CollectorUrl.TrimEnd('/') + "/v1/server/batch";
        for (var attempt = 0; attempt <= _options.MaxRetries; attempt++)
        {
            try
            {
                var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = new ByteArrayContent(body) };
                req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                req.Content.Headers.ContentEncoding.Add("gzip");
                req.Headers.Add(RahgirHeaders.Project, _options.ProjectKey);
                req.Headers.Add(RahgirHeaders.Timestamp, ts.ToString(System.Globalization.CultureInfo.InvariantCulture));
                // Signature is over the bytes actually sent (gzipped).
                req.Headers.Add(RahgirHeaders.Signature, BatchSigner.Sign(_options.ServerSecret, ts, body));

                using var res = await _httpFactory.CreateClient(HttpClientName).SendAsync(req, ct);
                if (res.IsSuccessStatusCode) return;
                if ((int)res.StatusCode is >= 400 and < 500 and not 429)
                {
                    Warn(null, $"Rahgir: collector rejected batch with {(int)res.StatusCode}. Check ProjectKey/ServerSecret.");
                    return;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) when (attempt == _options.MaxRetries) { Warn(ex, "Rahgir: collector unreachable, batch dropped"); return; }
            catch { /* retry */ }

            await Task.Delay(TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt)), ct);
        }
        Warn(null, "Rahgir: batch dropped after retries");
    }

    private void Warn(Exception? ex, string message)
    {
        // At most one warning per minute so a down collector never floods the app's logs.
        if (DateTimeOffset.UtcNow - _lastWarning < TimeSpan.FromMinutes(1)) return;
        _lastWarning = DateTimeOffset.UtcNow;
        _logger.LogWarning(ex, "{Message} (dropped so far: {Dropped})", message, _queue.Dropped);
    }
}
