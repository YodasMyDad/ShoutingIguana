using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Sidecar.Api.Models;
using ShoutingIguana.Sidecar.Events;
using ShoutingIguana.Sidecar.Events.Models;
using ShoutingIguana.Sidecar.Models;

namespace ShoutingIguana.Sidecar.Tests;

/// <summary>A slow event stream gets the latest progress of each crawl, never loses another event, and is cut off when too far behind.</summary>
public sealed class EventClientTests
{
    [Fact]
    public void ProgressIsMergedPerCrawlWhileTheClientIsBehind()
    {
        var client = new EventClient(limit: 100);

        client.EnqueueProgress("a", Progress("a", 1));
        client.EnqueueProgress("b", Progress("b", 1));
        client.EnqueueProgress("a", Progress("a", 2));
        client.EnqueueProgress("a", Progress("a", 3));

        Assert.Equal(["a 3", "b 1"], client.TakeAll()!.Select(sent => sent.Data));
        Assert.Empty(client.TakeAll()!);
    }

    [Fact]
    public void ACrawlsProgressStaysInOrderWithItsOtherEvents()
    {
        var client = new EventClient(limit: 100);

        client.EnqueueProgress("a", Progress("a", 1));
        client.Enqueue(new SidecarEvent("crawl", "a stopping", "a"));
        client.EnqueueProgress("a", Progress("a", 2));
        client.EnqueueProgress("a", Progress("a", 3));
        client.Enqueue(new SidecarEvent("library", "{}"));
        client.EnqueueProgress("a", Progress("a", 4));
        client.Enqueue(new SidecarEvent("alert", "a blocked", "a"));

        // Progress after "stopping" never jumps ahead of it; an event about no crawl doesn't hold progress back.
        Assert.Equal(["a 1", "a stopping", "a 4", "{}", "a blocked"], client.TakeAll()!.Select(sent => sent.Data));
    }

    [Fact]
    public void OtherEventsAreNeverDroppedButAClientTooFarBehindIsDisconnected()
    {
        var client = new EventClient(limit: 5);
        for (var alert = 0; alert < 4; alert++)
        {
            client.Enqueue(new SidecarEvent("alert", $"alert {alert}", "a"));
        }

        Assert.Equal(4, client.TakeAll()!.Count);

        for (var alert = 0; alert < 5; alert++)
        {
            client.Enqueue(new SidecarEvent("alert", $"alert {alert}", "a"));
        }

        Assert.Null(client.TakeAll());
        client.EnqueueProgress("a", Progress("a", 1));
        Assert.Null(client.TakeAll());
    }

    [Fact]
    public async Task WaitingEndsWhenAnEventArrivesOrTheKeepAliveIsDue()
    {
        var client = new EventClient(limit: 10);
        Assert.False(await client.WaitAsync(TimeSpan.FromMilliseconds(50), CancellationToken.None));

        var waiting = client.WaitAsync(TimeSpan.FromMinutes(1), CancellationToken.None);
        client.Enqueue(new SidecarEvent("library", "{}"));
        Assert.True(await waiting.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    /// <summary>
    /// An analysis's progress is never merged, so a stream hears each phase start and end; one that connects during it
    /// hears how far it has got, and once it has ended, nothing.
    /// </summary>
    [Fact]
    public void AnAnalysisIsHeardPhaseByPhaseAndFromWhereItIsByAStreamThatConnectsLate()
    {
        var hub = new EventHub(new SidecarOptions { DataDirectory = "data", StateDirectory = "state" });
        var early = hub.Connect();
        hub.PublishAnalysis(new AnalysisEventDto("a", AnalysisPhase.Reading, 0, 100));
        hub.PublishAnalysis(new AnalysisEventDto("a", AnalysisPhase.Reading, 100, 100));
        hub.PublishAnalysis(new AnalysisEventDto("a", AnalysisPhase.PageRules, 3, 60));
        Assert.Equal(["hello", "analysis", "analysis", "analysis"], early.TakeAll()!.Select(sent => sent.Name));

        var during = hub.Connect().TakeAll()!;
        Assert.Equal(["hello", "analysis"], during.Select(sent => sent.Name));
        Assert.Equal("""{"crawlId":"a","phase":"pageRules","done":3,"total":60}""", during[1].Data);

        hub.PublishCrawl(new CrawlEventDto("a", CrawlListStatus.Completed), crawlEnded: true);
        Assert.Equal(["hello"], hub.Connect().TakeAll()!.Select(sent => sent.Name));
    }

    private static SidecarEvent Progress(string crawlId, int fetched) => new("progress", $"{crawlId} {fetched}", crawlId);
}
