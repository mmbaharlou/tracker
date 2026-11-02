using System.Threading.Channels;
using Tracker.Core;

namespace Tracker.AspNetCore;

/// <summary>
/// Bounded in-memory queue. Enqueue never blocks and never throws: when full, the event is dropped
/// so the application is never slowed down by tracking.
/// </summary>
public sealed class EventQueue
{
    private readonly Channel<TrackEvent> _channel;
    private long _dropped;

    public EventQueue(TrackerOptions options)
    {
        _channel = Channel.CreateBounded<TrackEvent>(new BoundedChannelOptions(Math.Max(100, options.QueueCapacity))
        {
            FullMode = BoundedChannelFullMode.Wait, // with Wait, TryWrite returns false when full (never blocks)
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
    }

    public bool TryEnqueue(TrackEvent e)
    {
        if (_channel.Writer.TryWrite(e)) return true;
        Interlocked.Increment(ref _dropped);
        return false;
    }

    public long Dropped => Interlocked.Read(ref _dropped);
    public int Count => _channel.Reader.CanCount ? _channel.Reader.Count : -1;
    internal ChannelReader<TrackEvent> Reader => _channel.Reader;
    internal void Complete() => _channel.Writer.TryComplete();
}
