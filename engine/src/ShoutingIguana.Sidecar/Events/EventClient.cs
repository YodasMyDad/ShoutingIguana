using System.Threading.Channels;
using ShoutingIguana.Sidecar.Events.Models;

namespace ShoutingIguana.Sidecar.Events;

/// <summary>
/// One event stream's queue. A crawl's progress waiting to be sent is replaced by newer progress, so a client that falls
/// behind gets the latest numbers rather than every one; other events are never dropped. A crawl's progress stays in
/// order with its other events. A client that falls <c>limit</c> events behind is disconnected, and reconnects.
/// </summary>
internal sealed class EventClient(int limit)
{
    private readonly Lock _sync = new();
    private readonly Queue<QueuedEvent> _queue = new();

    // Progress not yet sent, by crawl, which newer progress replaces in place.
    private readonly Dictionary<string, QueuedEvent> _pendingProgress = new(StringComparer.Ordinal);
    private readonly Channel<bool> _signal = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
    private bool _overflowed;

    public void Enqueue(SidecarEvent sidecarEvent)
    {
        lock (_sync)
        {
            if (_overflowed)
            {
                return;
            }

            // Progress that comes after this event must be sent after it.
            if (sidecarEvent.CrawlId is { } crawlId)
            {
                _pendingProgress.Remove(crawlId);
            }

            Add(new QueuedEvent(sidecarEvent));
        }
    }

    public void EnqueueProgress(string crawlId, SidecarEvent progress)
    {
        lock (_sync)
        {
            if (_overflowed)
            {
                return;
            }

            if (_pendingProgress.TryGetValue(crawlId, out var pending))
            {
                pending.Event = progress;
                return;
            }

            var queued = new QueuedEvent(progress);
            _pendingProgress[crawlId] = queued;
            Add(queued);
        }
    }

    /// <summary>Everything queued, in order; null once the client has fallen too far behind and must be disconnected.</summary>
    public IReadOnlyList<SidecarEvent>? TakeAll()
    {
        lock (_sync)
        {
            if (_overflowed)
            {
                return null;
            }

            List<SidecarEvent> events = [.. _queue.Select(queued => queued.Event)];
            _queue.Clear();
            _pendingProgress.Clear();
            return events;
        }
    }

    /// <summary>True when something was queued (or the client must go), false when <paramref name="timeout"/> passed first.</summary>
    public async Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (_signal.Reader.TryRead(out _))
        {
            return true;
        }

        using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        wait.CancelAfter(timeout);
        try
        {
            await _signal.Reader.WaitToReadAsync(wait.Token).ConfigureAwait(false);
            _signal.Reader.TryRead(out _);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private void Add(QueuedEvent queued)
    {
        _queue.Enqueue(queued);
        if (_queue.Count >= limit)
        {
            _overflowed = true;
            _queue.Clear();
            _pendingProgress.Clear();
        }

        _signal.Writer.TryWrite(true);
    }

    private sealed class QueuedEvent(SidecarEvent sidecarEvent)
    {
        public SidecarEvent Event { get; set; } = sidecarEvent;
    }
}
