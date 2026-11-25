using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Tests.Fetching;
using ShoutingIguana.Engine.Tests.Support;

namespace ShoutingIguana.Engine.Tests.Crawling;

/// <summary>
/// A site that starts refusing the crawler after some of its pages were read (a firewall's challenge, not a 429): the crawl
/// slows down by itself, asks the refused pages again, and records them blocked only when they are still refused at the
/// slowest pace. On loopback sites and the fake clock, so the waits pass in moments.
/// </summary>
public sealed class RefusalRetreatTests : IAsyncLifetime
{
    private const int PAGES = 10;

    /// <summary>Pages a site lets through before it starts refusing.</summary>
    private const int LET_THROUGH = 3;

    private readonly CrawlFiles _files = new();
    private LoopbackServer? _server;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_server is not null)
        {
            await _server.DisposeAsync();
        }

        _files.Dispose();
    }

    /// <summary>
    /// After three pages the site refuses anything asked within three seconds of the page before: the crawl slows down,
    /// says so, and asks the refused pages again until they get through. None is recorded blocked, and nothing says the
    /// site is blocking.
    /// </summary>
    [Fact]
    public async Task ASiteThatStartsRefusingIsReadMoreSlowlyUntilItsPagesGetThrough()
    {
        var gap = TimeSpan.FromSeconds(3);
        // The three after the first three are refused whatever the wait, so the crawl has a run of refusals to go on.
        var run = await CrawlAsync((path, before, sinceLast) => before < LET_THROUGH || (before >= 2 * LET_THROUGH && sinceLast >= gap));

        Assert.Equal(CrawlStatus.Completed, run.Summary.Status);
        Assert.Equal(0, TestCrawls.Count(run.Path, $"SELECT COUNT(*) FROM responses WHERE outcome = {(int)FetchOutcome.Blocked}"));
        Assert.Equal(PAGES, TestCrawls.Count(run.Path, "SELECT COUNT(*) FROM responses r JOIN urls u ON u.id = r.url_id WHERE u.url LIKE '%/p/%' AND r.status_code = 200"));
        Assert.Contains(run.Asked.Values, times => times > 1);

        var slowdown = Assert.Single(run.Notices, notice => notice.Code == "blocked-slowdown");
        Assert.Equal(CrawlNoticeLevel.Info, slowdown.Level);
        Assert.Equal(RefusalPace.REFUSALS_TO_SLOW, slowdown.Data!.Count);
        Assert.Equal(RefusalPace.START_INTERVAL, slowdown.Data.Duration);
        Assert.Equal("Cloudflare challenge", slowdown.Data.Reason);
        Assert.StartsWith("127.0.0.1 started refusing the crawler (Cloudflare challenge) after ", slowdown.Message);
        Assert.DoesNotContain(run.Notices, notice => notice.Code is "blocked" or "mostly-forbidden");
    }

    /// <summary>
    /// After three pages the site refuses everything: the crawl slows down to one page every 30 seconds, asks each refused
    /// page again a bounded number of times, gives the site up after refusals in a row at that pace, and records them
    /// blocked, in a bounded time. The alert says it slowed down and asked again, and doesn't tell the person to go slower.
    /// </summary>
    [Fact]
    public async Task ASiteThatNeverLetsThroughAgainIsGivenUpAtTheSlowestPaceInABoundedTime()
    {
        var run = await CrawlAsync((path, before, sinceLast) => before < LET_THROUGH);

        Assert.Equal(CrawlStatus.Completed, run.Summary.Status);
        Assert.Equal(PAGES - LET_THROUGH, TestCrawls.Count(run.Path, $"SELECT COUNT(*) FROM responses WHERE outcome = {(int)FetchOutcome.Blocked}"));
        Assert.All(run.Asked.Values, times => Assert.InRange(times, 1, 1 + RefusalRetreat.MAX_RETRIES));

        var slowdown = Assert.Single(run.Notices, notice => notice.Code == "blocked-slowdown");
        var blocked = Assert.Single(run.Notices, notice => notice.Code == "blocked");
        Assert.Equal(RefusalPace.MAX_INTERVAL, blocked.Data!.Duration);
        Assert.InRange(blocked.Data.Retried ?? 0, 1, PAGES - LET_THROUGH);
        Assert.Equal("off", blocked.Data.Tried);
        Assert.Contains("The crawl had slowed down by itself to one page every 30 seconds and asked ", blocked.Message);
        Assert.DoesNotContain("slower crawl rate", blocked.Message);

        // From slowing down to giving up: one page every 1, 2, 4, 8, 16 and 30 seconds, then three more at 30 seconds.
        var tookFromSlowdown = run.EndedAt - slowdown.At;
        Assert.True(tookFromSlowdown < TimeSpan.FromMinutes(5), $"It took {tookFromSlowdown} after slowing down.");
        Assert.True(tookFromSlowdown > TimeSpan.FromSeconds(100), $"It gave up {tookFromSlowdown} after slowing down, sooner than the waits allow.");
    }

    /// <summary>One page always refused among pages that get through is that page's own answer: asked once, recorded blocked, nothing slowed.</summary>
    [Fact]
    public async Task OneRefusedPageAmongPagesThatGetThroughChangesNothing()
    {
        var run = await CrawlAsync((path, before, sinceLast) => path != "/p/5");

        Assert.Equal(CrawlStatus.Completed, run.Summary.Status);
        Assert.Equal(1, run.Asked["/p/5"]);
        Assert.Equal(1, TestCrawls.Count(run.Path, $"SELECT COUNT(*) FROM responses WHERE outcome = {(int)FetchOutcome.Blocked}"));
        Assert.DoesNotContain(run.Notices, notice => notice.Code == "blocked-slowdown");
        var blocked = Assert.Single(run.Notices, notice => notice.Code == "blocked");
        Assert.Null(blocked.Data!.Retried);
        Assert.DoesNotContain("slowed down by itself", blocked.Message);
    }

    /// <summary>A site that refuses from its first page goes the way it always has: nothing slows down, nothing is asked twice.</summary>
    [Fact]
    public async Task ASiteThatRefusesFromItsFirstPageIsNotSlowedDownFor()
    {
        var run = await CrawlAsync((path, before, sinceLast) => false, refuseHome: true);

        Assert.Equal(CrawlStatus.Completed, run.Summary.Status);
        Assert.Equal(1, run.Asked["/"]);
        Assert.DoesNotContain(run.Notices, notice => notice.Code == "blocked-slowdown");
        var blocked = Assert.Single(run.Notices, notice => notice.Code == "blocked");
        Assert.Null(blocked.Data!.Retried);
        Assert.Null(blocked.Data.Duration);
    }

    [Fact]
    public void APaceSlowsOnARunOfRefusalsBacksOffEasesAndGivesUp()
    {
        var pace = new RefusalPace("example.com");
        Assert.False(pace.Refused(pace.Generation));
        pace.GotThrough();
        Assert.False(pace.Refused(pace.Generation));
        Assert.False(pace.Refused(pace.Generation));
        pace.GotThrough();
        Assert.False(pace.IsSlow);

        var before = pace.Generation;
        Assert.False(pace.Refused(pace.Generation));
        Assert.False(pace.Refused(pace.Generation));
        Assert.True(pace.Refused(pace.Generation));
        Assert.True(pace.IsSlow);
        Assert.Equal(RefusalPace.START_INTERVAL, pace.Interval);

        // A page asked before it slowed down says nothing of the slower pace.
        Assert.False(pace.Refused(before));
        Assert.Equal(RefusalPace.START_INTERVAL, pace.Interval);

        pace.Refused(pace.Generation);
        Assert.Equal(RefusalPace.START_INTERVAL * 2, pace.Interval);
        for (var page = 0; page < RefusalPace.THROUGH_TO_EASE; page++)
        {
            pace.GotThrough();
        }

        Assert.Equal(RefusalPace.START_INTERVAL, pace.Interval);
        for (var page = 0; page < RefusalPace.THROUGH_TO_EASE; page++)
        {
            pace.GotThrough();
        }

        Assert.False(pace.IsSlow);

        // Slowing down again starts slower, and it backs off to the slowest pace, where refusals in a row give it up.
        pace.Refused(pace.Generation);
        pace.Refused(pace.Generation);
        pace.Refused(pace.Generation);
        Assert.Equal(RefusalPace.START_INTERVAL * 2, pace.Interval);
        while (pace.Interval < RefusalPace.MAX_INTERVAL)
        {
            pace.Refused(pace.Generation);
        }

        Assert.Equal(RefusalPace.MAX_INTERVAL, pace.Slowest);
        for (var refusal = 0; refusal < RefusalPace.GIVE_UP_REFUSALS; refusal++)
        {
            Assert.False(pace.GaveUp);
            pace.Refused(pace.Generation);
        }

        Assert.True(pace.GaveUp);
        Assert.False(pace.IsSlow);
    }

    /// <summary>
    /// A site whose home page links to <see cref="PAGES"/> pages, each let through when <paramref name="letThrough"/> says
    /// (given its path, how many times those pages were asked before, and the time since the last was asked), or refused
    /// with a Cloudflare challenge.
    /// </summary>
    private async Task<Run> CrawlAsync(Func<string, int, TimeSpan, bool> letThrough, bool refuseHome = false)
    {
        var time = new FakeTimeProvider();
        ConcurrentDictionary<string, int> asked = new();
        var sync = new Lock();
        var last = DateTimeOffset.MinValue;
        var pagesAsked = 0;
        var links = string.Concat(Enumerable.Range(1, PAGES).Select(page => $"<a href=\"/p/{page}\">Page {page}</a> "));
        _server = await LoopbackServer.StartAsync(context =>
        {
            var path = context.Request.Path.Value!;
            if (path == "/robots.txt")
            {
                context.Response.ContentType = "text/plain";
                return context.Response.WriteAsync("User-agent: *\nDisallow:");
            }

            if (path != "/" && !path.StartsWith("/p/", StringComparison.Ordinal))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return Task.CompletedTask;
            }

            bool through;
            lock (sync)
            {
                var now = time.GetUtcNow();
                through = path == "/" ? !refuseHome : letThrough(path, pagesAsked, now - last);
                asked.AddOrUpdate(path, 1, static (_, count) => count + 1);
                if (path != "/")
                {
                    last = now;
                    pagesAsked++;
                }
            }

            if (!through)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.Headers["cf-mitigated"] = "challenge";
                context.Response.Headers.Server = "cloudflare";
                context.Response.ContentType = "text/html";
                return context.Response.WriteAsync("<!doctype html><title>Just a moment...</title>");
            }

            context.Response.ContentType = "text/html; charset=utf-8";
            return context.Response.WriteAsync(
                $"<!doctype html><html lang=\"en\"><head><title>Page {path}</title></head><body>{(path == "/" ? links : "<p>Read</p>")}</body></html>");
        });

        var observer = new Recorder(time);
        var file = _files.NewPath();
        var options = new CrawlOptions { StartUrl = _server.BaseUri.AbsoluteUri, Concurrency = 4, MaxRetries = 0, BrowserFallback = false };
        CrawlSummary summary;
        await using (var job = new CrawlEngine(NullLoggerFactory.Instance, time).Start(new CrawlRequest(options, file), observer))
        {
            summary = await FakeClock.PumpAsync(time, job.Completion, TimeSpan.FromMilliseconds(100), realLimit: TimeSpan.FromMinutes(3));
        }

        // Only the site's own: the home page's other versions are probes, which never count.
        var pages = asked.Where(page => page.Key == "/" || page.Key.StartsWith("/p/", StringComparison.Ordinal)).ToDictionary(page => page.Key, page => page.Value);
        return new Run(file, summary, observer.Notices, pages, time.GetUtcNow());
    }

    private sealed record Run(string Path, CrawlSummary Summary, IReadOnlyList<CrawlNotice> Notices, Dictionary<string, int> Asked, DateTimeOffset EndedAt);

    /// <summary>Keeps the notices, each stamped with when on the fake clock it came.</summary>
    private sealed class Recorder(TimeProvider time) : ICrawlObserver
    {
        private readonly ConcurrentQueue<CrawlNotice> _notices = new();

        public IReadOnlyList<CrawlNotice> Notices => [.. _notices];

        public void OnProgress(CrawlProgress progress)
        {
        }

        public void OnNotice(CrawlNotice notice) => _notices.Enqueue(notice with { At = time.GetUtcNow() });
    }
}
