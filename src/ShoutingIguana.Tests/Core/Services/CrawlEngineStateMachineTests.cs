using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using ShoutingIguana.Core.Services;
using Xunit;

namespace ShoutingIguana.Tests.Core.Services;

/// <summary>
/// Focused tests on the <see cref="CrawlEngine"/> state flags. A full crawl
/// harness (pause-under-load, checkpoint round-trip, transaction rollback)
/// needs an in-memory SQLite fixture with ~12 mocked repositories and a fake
/// Playwright page, which is a Wave 5 follow-up. These tests lock in the
/// idempotent no-op behaviour of the pause/resume surface.
/// </summary>
public sealed class CrawlEngineStateMachineTests
{
    [Fact]
    public void FreshEngineIsNotCrawlingAndNotPaused()
    {
        var engine = BuildEngine();

        Assert.False(engine.IsCrawling);
        Assert.False(engine.IsPaused);
    }

    [Fact]
    public async Task PauseOnIdleEngineIsNoOp()
    {
        var engine = BuildEngine();

        await engine.PauseCrawlAsync();

        // Pause is a no-op when not crawling — state must not flip
        Assert.False(engine.IsPaused);
        Assert.False(engine.IsCrawling);
    }

    [Fact]
    public async Task ResumeOnIdleEngineIsNoOp()
    {
        var engine = BuildEngine();

        await engine.ResumeCrawlAsync();

        Assert.False(engine.IsPaused);
        Assert.False(engine.IsCrawling);
    }

    [Fact]
    public async Task StopOnIdleEngineIsNoOp()
    {
        var engine = BuildEngine();

        await engine.StopCrawlAsync();

        Assert.False(engine.IsCrawling);
    }

    [Fact(Skip = "Wave 5 follow-up: needs in-memory SQLite + mocked repository/Playwright fixture to inject failure between SaveUrlAsync and SaveRedirectChainAsync and assert no orphan Url row remains after rollback.")]
    public void TransactionRollbackLeavesNoOrphanUrlRow()
    {
        // See docs/audit.md Wave 1 item 4 for the transactional crawl-write design.
    }

    [Fact(Skip = "Wave 5 follow-up: requires running a real crawl (Playwright + seeded project DB) to exercise the pause/resume worker-flow gating end-to-end.")]
    public void PauseThenResumeUnderLoadRestoresWorkerFlow()
    {
    }

    [Fact(Skip = "Wave 5 follow-up: checkpoint round-trip test needs a populated CrawlQueue and CrawlCheckpoint repository pair.")]
    public void CheckpointAndResumeRoundTripsQueueState()
    {
    }

    private static CrawlEngine BuildEngine()
    {
        var logger = new Mock<ILogger<CrawlEngine>>();
        var serviceProvider = new Mock<IServiceProvider>();
        var robots = new Mock<IRobotsService>();
        var linkExtractor = new Mock<ILinkExtractor>();
        var playwright = new Mock<IPlaywrightService>();
        var httpClientFactory = new Mock<IHttpClientFactory>();
        var hostThrottleLogger = new Mock<ILogger<HostThrottle>>();
        var hostThrottle = new HostThrottle(hostThrottleLogger.Object);

        return new CrawlEngine(
            logger.Object,
            serviceProvider.Object,
            robots.Object,
            linkExtractor.Object,
            playwright.Object,
            httpClientFactory.Object,
            hostThrottle);
    }
}
