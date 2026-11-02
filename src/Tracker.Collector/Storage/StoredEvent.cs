namespace Tracker.Collector.Storage;

/// <summary>A validated, enriched event ready to be stored.</summary>
public sealed class StoredEvent
{
    public Guid Id { get; init; }
    public string ProjectKey { get; init; } = "";
    public string Source { get; init; } = "";
    public string Type { get; init; } = "";
    public string? Name { get; init; }
    public DateTime Ts { get; init; }
    public DateTime ReceivedAt { get; init; }
    public string? UserId { get; init; }
    public string? AnonymousId { get; init; }
    public string? SessionId { get; init; }
    public string? Url { get; init; }
    public string? Path { get; init; }
    public string? Referrer { get; init; }
    public string? Title { get; init; }
    public string? Route { get; init; }
    public string? Method { get; init; }
    public short? StatusCode { get; init; }
    public string? Ip { get; init; }
    public string? Browser { get; init; }
    public string? Os { get; init; }
    public string? Device { get; init; }
    public string? Screen { get; init; }
    public string? Language { get; init; }
    public string? Properties { get; init; }
}

/// <summary>Storage abstraction. Implement it to store events somewhere else (ClickHouse, files, a queue...).</summary>
public interface IEventStore
{
    Task WriteAsync(IReadOnlyList<StoredEvent> events, CancellationToken ct);
    Task<int> PurgeAsync(int olderThanDays, CancellationToken ct);
}
