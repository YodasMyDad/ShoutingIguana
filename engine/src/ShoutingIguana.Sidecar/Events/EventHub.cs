using System.Text.Json;
using ShoutingIguana.Sidecar.Api.Models;
using ShoutingIguana.Sidecar.Events.Models;
using ShoutingIguana.Sidecar.Models;
using ShoutingIguana.Sidecar.Serialization;

namespace ShoutingIguana.Sidecar.Events;

/// <summary>
/// Sends every event to every connected stream. Each event is serialised once. The latest progress of each live crawl,
/// and of each running analysis, is kept, so a stream that connects (or reconnects) starts with a <c>hello</c> naming the
/// live crawls and the ones being analysed, then their progress and the analyses', and is up to date at once. Nothing is
/// replayed beyond that.
/// </summary>
internal sealed class EventHub(SidecarOptions options)
{
    private const string LIBRARY_DATA = "{}";

    private readonly Lock _sync = new();
    private readonly List<EventClient> _clients = [];
    private readonly Dictionary<string, SidecarEvent> _latestProgress = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SidecarEvent> _latestAnalysis = new(StringComparer.Ordinal);

    // The latest recheck event of each crawl rechecked here (the fix loop), until the crawl ends.
    private readonly Dictionary<string, SidecarEvent> _latestRecheck = new(StringComparer.Ordinal);

    // The exports running, each with its latest event, in the order they started.
    private readonly Dictionary<string, SidecarEvent> _latestExport = new(StringComparer.Ordinal);
    private readonly List<string> _exports = [];

    // The crawls whose last crawl event said analysing, in the order they started, from then until their crawl ends.
    private readonly List<string> _analyzing = [];

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
            var hello = new HelloEventDto([.. _latestProgress.Keys], [.. _analyzing], [.. _exports]);
            client.Enqueue(new SidecarEvent("hello", JsonSerializer.Serialize(hello, SidecarJsonContext.Default.HelloEventDto)));
            foreach (var (crawlId, progress) in _latestProgress)
            {
                client.EnqueueProgress(crawlId, progress);
            }

            foreach (var analysis in _latestAnalysis.Values)
            {
                client.Enqueue(analysis);
            }

            foreach (var recheck in _latestRecheck.Values)
            {
                client.Enqueue(recheck);
            }

            foreach (var export in _exports)
            {
                client.Enqueue(_latestExport[export]);
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

    /// <summary>
    /// A running analysis's progress. The latest of each is kept for streams that connect later. Unlike a crawl's, it is
    /// never merged: an analysis reports a few times a second, and each phase's start and end are worth hearing.
    /// </summary>
    public void PublishAnalysis(AnalysisEventDto analysis)
    {
        var sidecarEvent = new SidecarEvent("analysis", JsonSerializer.Serialize(analysis, SidecarJsonContext.Default.AnalysisEventDto), analysis.CrawlId);
        lock (_sync)
        {
            _latestAnalysis[analysis.CrawlId] = sidecarEvent;
            Broadcast(sidecarEvent);
        }
    }

    /// <param name="crawlEnded">
    /// The crawl, or its analysis, is no longer live here, so a stream that connects from now on doesn't hear of it. A crawl
    /// that goes on to be analysed is announced as analysing next, which a stream that connects then hears of in its hello.
    /// </param>
    public void PublishCrawl(CrawlEventDto crawl, bool crawlEnded)
    {
        var sidecarEvent = new SidecarEvent("crawl", JsonSerializer.Serialize(crawl, SidecarJsonContext.Default.CrawlEventDto), crawl.CrawlId);
        lock (_sync)
        {
            if (crawlEnded)
            {
                _latestProgress.Remove(crawl.CrawlId);
                _latestAnalysis.Remove(crawl.CrawlId);
                _latestRecheck.Remove(crawl.CrawlId);
                _analyzing.Remove(crawl.CrawlId);
            }
            else if (crawl.Status == CrawlListStatus.Analyzing)
            {
                if (!_analyzing.Contains(crawl.CrawlId))
                {
                    _analyzing.Add(crawl.CrawlId);
                }
            }
            else
            {
                // An analysis that ended with the crawl still live (a recheck's analysis first, before its run) is no
                // longer one a stream that connects should hear of.
                _latestAnalysis.Remove(crawl.CrawlId);
                _analyzing.Remove(crawl.CrawlId);
            }

            Broadcast(sidecarEvent);
        }
    }

    /// <summary>
    /// An export as it stands: started, further on, or ended. Never merged. The latest of each running export is kept for
    /// streams that connect later; one that has ended is forgotten here, and asked for with its id.
    /// </summary>
    public void PublishExport(ExportDto export, bool ended)
    {
        var sidecarEvent = new SidecarEvent("export", JsonSerializer.Serialize(export, SidecarJsonContext.Default.ExportDto), export.CrawlId);
        lock (_sync)
        {
            if (ended)
            {
                _latestExport.Remove(export.ExportId);
                _exports.Remove(export.ExportId);
            }
            else
            {
                if (!_latestExport.ContainsKey(export.ExportId))
                {
                    _exports.Add(export.ExportId);
                }

                _latestExport[export.ExportId] = sidecarEvent;
            }

            Broadcast(sidecarEvent);
        }
    }

    /// <summary>
    /// A recheck's own progress, as it starts, as it goes and as it ends, and once settled (the fix loop). Never merged or
    /// dropped. The latest of each is kept for streams that connect later, until the crawl is no longer live here.
    /// </summary>
    public void PublishRecheck(RecheckEventDto recheck)
    {
        var sidecarEvent = new SidecarEvent("recheck", JsonSerializer.Serialize(recheck, SidecarJsonContext.Default.RecheckEventDto), recheck.CrawlId);
        lock (_sync)
        {
            _latestRecheck[recheck.CrawlId] = sidecarEvent;
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
