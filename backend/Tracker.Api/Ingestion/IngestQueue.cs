using System.Threading.Channels;
using Tracker.Api.Storage;

namespace Tracker.Api.Ingestion;

public sealed class IngestQueue
{
    private readonly Channel<StoredEvent> _channel;
    public IngestQueue(CollectorOptions o)
        => _channel = Channel.CreateBounded<StoredEvent>(new BoundedChannelOptions(Math.Max(1000, o.QueueCapacity))
        { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });

    /// <summary>Returns false when the queue is full (the caller decides whether to report 503).</summary>
    public bool TryEnqueueAll(IReadOnlyList<StoredEvent> events)
    {
        var ok = true;
        foreach (var e in events) ok &= _channel.Writer.TryWrite(e);
        return ok;
    }

    public int Count => _channel.Reader.Count;
    public ChannelReader<StoredEvent> Reader => _channel.Reader;
}
