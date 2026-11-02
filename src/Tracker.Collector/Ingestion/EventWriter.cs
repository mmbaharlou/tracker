using Tracker.Collector.Storage;

namespace Tracker.Collector.Ingestion;

/// <summary>Drains the ingest queue into the store in large batches.</summary>
public sealed class EventWriter : BackgroundService
{
    private readonly IngestQueue _queue;
    private readonly IEventStore _store;
    private readonly CollectorOptions _options;
    private readonly ILogger<EventWriter> _logger;

    public EventWriter(IngestQueue queue, IEventStore store, CollectorOptions options, ILogger<EventWriter> logger)
    { _queue = queue; _store = store; _options = options; _logger = logger; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<StoredEvent>(_options.WriteBatchSize);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var timer = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                timer.CancelAfter(TimeSpan.FromSeconds(1));
                try
                {
                    while (batch.Count < _options.WriteBatchSize && await _queue.Reader.WaitToReadAsync(timer.Token))
                        while (batch.Count < _options.WriteBatchSize && _queue.Reader.TryRead(out var e)) batch.Add(e);
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested) { }

                if (batch.Count > 0) { await WriteWithRetryAsync(batch, stoppingToken); batch.Clear(); }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Tracker writer loop failed"); batch.Clear(); }
        }

        while (_queue.Reader.TryRead(out var e)) batch.Add(e);
        if (batch.Count > 0)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await _store.WriteAsync(batch, cts.Token); } catch (Exception ex) { _logger.LogError(ex, "Final flush failed, {Count} events lost", batch.Count); }
        }
    }

    private async Task WriteWithRetryAsync(List<StoredEvent> batch, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try { await _store.WriteAsync(batch, ct); return; }
            catch (Exception ex) when (attempt < 4 && !ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Tracker: write failed (attempt {Attempt}), retrying", attempt);
                await Task.Delay(TimeSpan.FromSeconds(attempt * 2), ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Tracker: dropping {Count} events after retries", batch.Count);
                return;
            }
        }
    }
}

public sealed class RetentionService : BackgroundService
{
    private readonly IEventStore _store;
    private readonly CollectorOptions _options;
    private readonly ILogger<RetentionService> _logger;
    public RetentionService(IEventStore store, CollectorOptions options, ILogger<RetentionService> logger)
    { _store = store; _options = options; _logger = logger; }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (_options.RetentionDays <= 0) return;
        try { await Task.Delay(TimeSpan.FromMinutes(5), ct); } catch { return; }
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var n = await _store.PurgeAsync(_options.RetentionDays, ct);
                if (n > 0) _logger.LogInformation("Tracker retention removed {Count} events", n);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested) { _logger.LogWarning(ex, "Tracker retention failed"); }
            try { await Task.Delay(TimeSpan.FromHours(6), ct); } catch { return; }
        }
    }
}
