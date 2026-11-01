using System.Text.Json;
using ShoutingIguana.Sidecar.Api.Models;
using ShoutingIguana.Sidecar.Events.Models;
using ShoutingIguana.Sidecar.Models;
using ShoutingIguana.Sidecar.Serialization;

namespace ShoutingIguana.Sidecar.Events;

/// <summary>
/// Sends every event to every connected stream. Each event is serialised once. The latest progress of each live crawl
/// is kept, so a stream that connects (or reconnects) starts with a <c>hello</c> naming the live crawls, then their
/// progress, and is up to date at once. Nothing is replayed beyond that.
/// </summary>
internal sealed class EventHub(SidecarOptions options)
{
    private const string LIBRARY_DATA = "{}";

    private readonly Lock _sync = new();
    private readonly List<EventClient> _clients = [];
    private readonly Dictionary<string, SidecarEvent> _latestProgress = new(StringComparer.Ordinal);

    public int ClientCount
    {
        get
        {
            lock (_sync)
            {
                return _clients.Count;
            }
        }
    }

    public EventClient Connect()
    {
        var client = new EventClient(options.MaxQueuedEvents);
        lock (_sync)
        {
            var hello = new HelloEventDto([.. _latestProgress.Keys]);
            client.Enqueue(new SidecarEvent("hello", JsonSerializer.Serialize(hello, SidecarJsonContext.Default.HelloEventDto)));
            foreach (var (crawlId, progress) in _latestProgress)
            {
                client.EnqueueProgress(crawlId, progress);
            }

            _clients.Add(client);
        }

        return client;
    }

    public void Disconnect(EventClient client)
    {
        lock (_sync)
        {
            _clients.Remove(client);
        }
    }

    public void PublishProgress(ProgressEventDto progress)
    {
        var sidecarEvent = new SidecarEvent("progress", JsonSerializer.Serialize(progress, SidecarJsonContext.Default.ProgressEventDto), progress.CrawlId);
        lock (_sync)
        {
            _latestProgress[progress.CrawlId] = sidecarEvent;
            foreach (var client in _clients)
            {
                client.EnqueueProgress(progress.CrawlId, sidecarEvent);
            }
        }
    }

    /// <param name="crawlEnded">The crawl is no longer live here, so a stream that connects from now on doesn't hear of it.</param>
    public void PublishCrawl(CrawlEventDto crawl, bool crawlEnded)
    {
        var sidecarEvent = new SidecarEvent("crawl", JsonSerializer.Serialize(crawl, SidecarJsonContext.Default.CrawlEventDto), crawl.CrawlId);
        lock (_sync)
        {
            if (crawlEnded)
            {
                _latestProgress.Remove(crawl.CrawlId);
            }

            Broadcast(sidecarEvent);
        }
    }

    public void PublishAlert(AlertDto alert)
    {
        var sidecarEvent = new SidecarEvent("alert", JsonSerializer.Serialize(alert, SidecarJsonContext.Default.AlertDto), alert.CrawlId);
        lock (_sync)
        {
            Broadcast(sidecarEvent);
        }
    }

    /// <summary>The library changed: the UI fetches the list again.</summary>
    public void PublishLibrary()
    {
        lock (_sync)
        {
            Broadcast(new SidecarEvent("library", LIBRARY_DATA));
        }
    }

    private void Broadcast(SidecarEvent sidecarEvent)
    {
        foreach (var client in _clients)
        {
            client.Enqueue(sidecarEvent);
        }
    }
}
