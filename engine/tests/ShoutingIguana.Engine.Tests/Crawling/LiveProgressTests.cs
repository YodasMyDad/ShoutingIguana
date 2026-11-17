using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Fetching;
using ShoutingIguana.Engine.Robots;
using ShoutingIguana.Engine.Tests.Fetching;
using ShoutingIguana.Engine.Tests.Support;
using ShoutingIguana.TestSite;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.Engine.Tests.Crawling;

/// <summary>
/// What a crawl says it is doing while it runs, and what holds it up: robots.txt before the first fetch, the sitemaps
/// before the URLs only they list, a crawl-delay, a site asking it to slow down or not answering, and robots.txt's
/// cool-down before the last tries. Read from the job's progress as a UI would, on small loopback sites; and each report's
/// numbers agree with each other, read as fast as they can be through a crawl of thousands of pages.
/// </summary>
public sealed class LiveProgressTests : IAsyncLifetime
{
    private static readonly TimeSpan WAIT = TimeSpan.FromMinutes(1);

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

    /// <summary>robots.txt and the sitemap are held back, so each phase lasts until the test lets it end.</summary>
    [Fact]
    public async Task ThePhasesComeInOrderFromRobotsTxtToFinishing()
    {
        var robots = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sitemap = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        LoopbackServer? server = null;
        server = _server = await LoopbackServer.StartAsync(async context =>
        {
            switch (context.Request.Path.Value)
            {
                case "/robots.txt":
                    await robots.Task;
                    await Text(context, $"User-agent: *\nDisallow:\nSitemap: {server!.BaseUri}sitemap.xml");
                    break;
                case "/sitemap.xml":
                    await sitemap.Task;
                    context.Response.ContentType = "application/xml";
                    await context.Response.WriteAsync(
                        $"<?xml version=\"1.0\"?><urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\"><url><loc>{server!.BaseUri}listed</loc></url></urlset>");
                    break;
                case "/":
                    await Html(context, "<a href=\"/about\">About</a>");
                    break;
                case "/about" or "/listed":
                    await Html(context, "<p>A page</p>");
                    break;
                default:
                    context.Response.StatusCode = 404;
                    break;
            }
        });

        List<CrawlPhase> seen = [];
        await using var job = Start(Options(server));
        var reading = await PhaseAsync(job, CrawlPhase.ReadingRobotsTxt, seen);
        Assert.Contains(reading.Waits, wait => wait is { Reason: CrawlWaitReason.RobotsTxt, Host: "127.0.0.1", Urls: >= 1 });

        // The linked pages are fetched, then nothing is left but what the sitemap, still being read, lists.
        robots.SetResult();
        var sitemaps = await PhaseAsync(job, CrawlPhase.ReadingSitemaps, seen);
        Assert.Equal(0, sitemaps.Queued);
        Assert.DoesNotContain(sitemaps.Waits, wait => wait.Reason == CrawlWaitReason.RobotsTxt);

        sitemap.SetResult();
        var summary = await job.Completion.WaitAsync(WAIT);
        Assert.Equal(CrawlStatus.Completed, summary.Status);
        Assert.Equal(CrawlPhase.Finishing, summary.Progress.Phase);
        seen.Add(summary.Progress.Phase);

        // Each phase in turn, never one going back to an earlier.
        List<CrawlPhase> order = [CrawlPhase.ReadingRobotsTxt, CrawlPhase.ReadingSitemaps, CrawlPhase.Finishing];
        Assert.Equal(order, seen.Where(order.Contains).Distinct());
        Assert.True(seen.IndexOf(CrawlPhase.ReadingSitemaps) > seen.LastIndexOf(CrawlPhase.ReadingRobotsTxt), string.Join(", ", seen));
    }

    [Fact]
    public async Task ACrawlDelayIsSaidForTheSiteItSlowsDown()
    {
        _server = await LoopbackServer.StartAsync(context => context.Request.Path.Value switch
        {
            "/robots.txt" => Text(context, "User-agent: *\nCrawl-delay: 3\nDisallow:"),
            _ => Html(context, "<a href=\"/a\">A</a> <a href=\"/b\">B</a>")
        });

        await using var job = Start(Options(_server) with { RespectCrawlDelay = true });
        var progress = await ProgressAsync(job, progress => progress.Waits.Any(wait => wait.Reason == CrawlWaitReason.CrawlDelay));

        var delay = Assert.Single(progress.Waits, wait => wait.Reason == CrawlWaitReason.CrawlDelay);
        Assert.Equal(new CrawlWait(CrawlWaitReason.CrawlDelay, "127.0.0.1", Duration: TimeSpan.FromSeconds(3)), delay);
        Assert.Empty(progress.MoreWaits);

        // The site's gate still spaces its requests, but once the crawl is finishing nothing is held up any more.
        var final = (await job.StopAsync()).Progress;
        Assert.Equal(CrawlPhase.Finishing, final.Phase);
        Assert.Empty(final.Waits);
    }

    /// <summary>On the fake clock, which stands still while the test reads how long the site asked it to wait.</summary>
    [Fact]
    public async Task ASiteThatAsksTheCrawlerToWaitIsSaidToHoldItBack()
    {
        _server = await LoopbackServer.StartAsync(context =>
        {
            if (context.Request.Path.Value == "/robots.txt")
            {
                return Text(context, "User-agent: *\nDisallow:");
            }

            context.Response.StatusCode = 429;
            context.Response.Headers.RetryAfter = "30";
            return Task.CompletedTask;
        });
        var time = new FakeTimeProvider();

        await using var job = Start(Options(_server), time);

        // The clock moves on as the crawl's slow start spaces its first requests, a second at most.
        var progress = await ProgressAsync(job, progress => progress.Waits.Any(wait => wait.Reason == CrawlWaitReason.SectionLimited), time);

        // The 429 is the root pages' (the home page's), so it is that section's wait, at the Retry-After it asked for.
        var held = Assert.Single(progress.Waits, wait => wait.Reason == CrawlWaitReason.SectionLimited);
        Assert.Equal("127.0.0.1", held.Host);
        Assert.Equal("/", held.Section);
        Assert.Equal(1, held.Urls);
        Assert.Equal(TimeSpan.FromSeconds(30), held.Duration);
        await job.StopAsync();
    }

    /// <summary>The home page answers, then nothing does: on the fake clock, the outage is said and how long it has lasted.</summary>
    [Fact]
    public async Task ASiteThatStopsAnsweringIsSaidToBeDown()
    {
        var links = string.Concat(Enumerable.Range(0, 20).Select(page => $"<a href=\"/p{page}\">Page {page}</a> "));
        var isUp = true;
        _server = await LoopbackServer.StartAsync(context =>
        {
            if (!Volatile.Read(ref isUp))
            {
                context.Abort();
                return Task.CompletedTask;
            }

            if (context.Request.Path.Value == "/robots.txt")
            {
                context.Response.StatusCode = 404;
                return Task.CompletedTask;
            }

            Volatile.Write(ref isUp, false);
            return Html(context, links);
        });
        var time = new FakeTimeProvider();

        await using var job = Start(Options(_server) with { Concurrency = 2 }, time);
        await FakeClock.PumpUntilAsync(time, () => job.Progress.Waits.Any(wait => wait.Reason == CrawlWaitReason.SiteDown));
        var progress = job.Progress;

        var down = Assert.Single(progress.Waits, wait => wait.Reason == CrawlWaitReason.SiteDown);
        Assert.Equal(_server.BaseUri.GetLeftPart(UriPartial.Authority), down.Host);
        Assert.InRange(down.Duration!.Value, TimeSpan.Zero, RetryingFetcher.GIVE_UP_AFTER);
        await job.StopAsync();
    }

    /// <summary>
    /// robots.txt fails at first, so the start URL waits for its cool-down to be tried again: the crawl is retrying, and
    /// says how long until robots.txt is asked again.
    /// </summary>
    [Fact]
    public async Task TheLastTriesWaitForRobotsTxtsCoolDownAndSaySo()
    {
        var robotsRequests = 0;
        _server = await LoopbackServer.StartAsync(context =>
        {
            if (context.Request.Path.Value == "/robots.txt")
            {
                if (Interlocked.Increment(ref robotsRequests) == 1)
                {
                    context.Response.StatusCode = 503;
                    return Task.CompletedTask;
                }

                return Text(context, "User-agent: *\nDisallow:");
            }

            return Html(context, "<p>Home</p>");
        });
        var time = new FakeTimeProvider();

        await using var job = Start(Options(_server) with { MaxRetries = 0, DiscoverSitemaps = false }, time);
        await FakeClock.PumpUntilAsync(time, () => job.Progress is { Phase: CrawlPhase.Retrying } progress &&
                                                   progress.Waits.Any(wait => wait.Reason == CrawlWaitReason.RobotsTxtCoolDown));
        var coolDown = Assert.Single(job.Progress.Waits, wait => wait.Reason == CrawlWaitReason.RobotsTxtCoolDown);
        Assert.InRange(coolDown.Duration!.Value, TimeSpan.FromTicks(1), RobotsCache.RETRY_AFTER);

        var summary = await FakeClock.PumpAsync(time, job.Completion);
        Assert.Equal(CrawlStatus.Completed, summary.Status);
        Assert.Equal(CrawlPhase.Finishing, summary.Progress.Phase);
        Assert.DoesNotContain(summary.Progress.Waits, wait => wait.Reason == CrawlWaitReason.RobotsTxtCoolDown);
    }

    /// <summary>
    /// A linked page gets no response, so it's held back for one more try when nothing else is left, and the URLs only the
    /// sitemap lists wait for that: the retrying is that try, and they're crawled after it, as crawling again.
    /// </summary>
    [Fact]
    public async Task WhatOnlyTheSitemapListsIsCrawledAfterTheLastTriesNotAsOneOfThem()
    {
        CrawlJob? job = null;
        ConcurrentQueue<(string Path, CrawlPhase Phase)> requested = new();
        LoopbackServer? server = null;
        server = _server = await LoopbackServer.StartAsync(async context =>
        {
            var path = context.Request.Path.Value!;
            if (Volatile.Read(ref job) is { } running && (path == "/dead" || path.StartsWith("/listed", StringComparison.Ordinal)))
            {
                requested.Enqueue((path, running.Progress.Phase));
            }

            switch (path)
            {
                case "/robots.txt":
                    await Text(context, $"User-agent: *\nDisallow:\nSitemap: {server!.BaseUri}sitemap.xml");
                    break;
                case "/sitemap.xml":
                    await Sitemap(context, Enumerable.Range(0, 20).Select(page => $"{server!.BaseUri}listed{page}"));
                    break;
                case "/":
                    await Html(context, "<a href=\"/a\">A</a> <a href=\"/dead\">Dead</a>");
                    break;
                case "/dead":
                    context.Abort();
                    break;
                default:
                    await Html(context, "<p>A page</p>");
                    break;
            }
        });

        await using (var started = Start(Options(server) with { Concurrency = 2, MaxRetries = 0 }))
        {
            Volatile.Write(ref job, started);
            Assert.Equal(CrawlStatus.Completed, (await started.Completion.WaitAsync(WAIT)).Status);
        }

        Assert.Equal(CrawlPhase.Retrying, requested.Last(request => request.Path == "/dead").Phase);
        var listed = requested.Where(request => request.Path.StartsWith("/listed", StringComparison.Ordinal)).ToList();
        Assert.Equal(20, listed.Count);
        Assert.All(listed, request => Assert.Equal(CrawlPhase.Crawling, request.Phase));
    }

    /// <summary>
    /// robots.txt answers 503 at the start, with sitemaps on: the start URL is refused and held back, and the sitemaps wait
    /// for robots.txt's cool-down, which the crawl says it waits for. Then the start URL's one more try is the retrying,
    /// and the rest of the site, what it links and what the sitemap lists, is crawling. On the fake clock.
    /// </summary>
    [Fact]
    public async Task ARobotsTxtUnreadableAtTheStartIsWaitedOutSayingSoAndOnlyTheStartUrlIsRetried()
    {
        var robotsRequests = 0;
        CrawlJob? job = null;
        ConcurrentQueue<(string Path, CrawlPhase Phase)> requested = new();
        LoopbackServer? server = null;
        server = _server = await LoopbackServer.StartAsync(async context =>
        {
            var path = context.Request.Path.Value!;
            if (Volatile.Read(ref job) is { } running && path is "/" or "/a" or "/b" or "/listed")
            {
                requested.Enqueue((path, running.Progress.Phase));
            }

            switch (path)
            {
                case "/robots.txt" when Interlocked.Increment(ref robotsRequests) == 1:
                    context.Response.StatusCode = 503;
                    break;
                case "/robots.txt":
                    await Text(context, $"User-agent: *\nDisallow:\nSitemap: {server!.BaseUri}sitemap.xml");
                    break;
                case "/sitemap.xml":
                    await Sitemap(context, [$"{server!.BaseUri}listed"]);
                    break;
                case "/":
                    await Html(context, "<a href=\"/a\">A</a> <a href=\"/b\">B</a>");
                    break;
                default:
                    await Html(context, "<p>A page</p>");
                    break;
            }
        });
        var time = new FakeTimeProvider();

        await using (var started = Start(Options(server) with { Concurrency = 2, MaxRetries = 0 }, time))
        {
            Volatile.Write(ref job, started);
            await FakeClock.PumpUntilAsync(time, () => started.Progress is { Phase: CrawlPhase.ReadingSitemaps } progress &&
                                                       progress.Waits.Any(wait => wait.Reason == CrawlWaitReason.RobotsTxtCoolDown));
            var coolDown = Assert.Single(started.Progress.Waits, wait => wait.Reason == CrawlWaitReason.RobotsTxtCoolDown);
            Assert.Equal(1, coolDown.Urls);
            Assert.InRange(coolDown.Duration!.Value, TimeSpan.FromTicks(1), RobotsCache.RETRY_AFTER);

            Assert.Equal(CrawlStatus.Completed, (await FakeClock.PumpAsync(time, started.Completion)).Status);
        }

        Assert.Equal(CrawlPhase.Retrying, Assert.Single(requested, request => request.Path == "/").Phase);
        Assert.Equal(["/a", "/b", "/listed"], requested.Where(request => request.Path != "/").Select(request => request.Path).Order());
        Assert.All(requested.Where(request => request.Path != "/"), request => Assert.Equal(CrawlPhase.Crawling, request.Phase));
    }

    /// <summary>
    /// Another site's robots.txt holds up the status check of a link to it while a page of the site is being fetched: the
    /// crawl is crawling, not reading robots.txt, and says the check waits, without naming a host of the site.
    /// </summary>
    [Fact]
    public async Task AnotherSitesRobotsTxtHoldingUpACheckIsNotWhatTheCrawlIsDoing()
    {
        var otherRobotsTxt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var slowPage = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var slowPageAsked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        LoopbackServer? server = null;
        server = _server = await LoopbackServer.StartAsync(async context =>
        {
            var isOtherSite = context.Connection.LocalPort == server!.OtherBaseUri.Port;
            switch (context.Request.Path.Value)
            {
                case "/robots.txt":
                    if (isOtherSite)
                    {
                        await otherRobotsTxt.Task;
                    }

                    await Text(context, "User-agent: *\nDisallow:");
                    break;
                case "/":
                    await Html(context, $"<a href=\"{server.OtherBaseUri}elsewhere\">Elsewhere</a> <a href=\"/slow\">Slow</a>");
                    break;
                case "/slow":
                    slowPageAsked.TrySetResult();
                    await slowPage.Task;
                    await Html(context, "<p>Slow</p>");
                    break;
                default:
                    await Html(context, "<p>A page</p>");
                    break;
            }
        });

        await using var job = Start(Options(server) with { DiscoverSitemaps = false, MaxRetries = 0 });
        await slowPageAsked.Task.WaitAsync(WAIT);
        var progress = await ProgressAsync(job, progress => progress.Waits.Any(wait => wait.Reason == CrawlWaitReason.OtherSitesRobotsTxt));

        Assert.Equal(CrawlPhase.Crawling, progress.Phase);
        Assert.Equal(new CrawlWait(CrawlWaitReason.OtherSitesRobotsTxt, Urls: 1),
            Assert.Single(progress.Waits, wait => wait.Reason == CrawlWaitReason.OtherSitesRobotsTxt));

        otherRobotsTxt.SetResult();
        slowPage.SetResult();
        Assert.Equal(CrawlStatus.Completed, (await job.Completion.WaitAsync(WAIT)).Status);
    }

    /// <summary>
    /// Progress read as fast as it can be, not every half second, through a crawl of thousands of pages: each report's
    /// numbers are of one moment, so they always agree with each other, however the writer's batches fall.
    /// </summary>
    [Fact]
    public async Task EveryReportsNumbersAgreeWithEachOther()
    {
        await using var site = await SyntheticSiteServer.StartAsync(new SyntheticSiteOptions { PageCount = 4_000 });
        await using var job = new CrawlEngine(NullLoggerFactory.Instance).Start(new CrawlRequest(
            new CrawlOptions { StartUrl = site.BaseUri.AbsoluteUri, Concurrency = 16 }, _files.NewPath()));
        long reports = 0;
        ConcurrentQueue<string> disagreements = new();
        var reader = Task.Run(() =>
        {
            while (!job.Completion.IsCompleted)
            {
                reports++;
                if (Disagreement(job.Progress) is { } disagreement && disagreements.Count < 10)
                {
                    disagreements.Enqueue(disagreement);
                }
            }
        });

        var summary = await job.Completion.WaitAsync(TimeSpan.FromMinutes(3));
        await reader;

        Assert.Equal(CrawlStatus.Completed, summary.Status);
        Assert.True(reports > 1_000, $"Only {reports} reports read.");
        Assert.Empty(disagreements);
        Assert.Null(Disagreement(summary.Progress));
    }

    /// <summary>How a report's numbers disagree with each other, or null when they don't.</summary>
    private static string? Disagreement(CrawlProgress progress)
    {
        var classes = progress.StatusClasses;
        var noResponse = progress.NoResponse;
        long Codes(int from, int to) => progress.StatusCodes.Where(code => code.Key >= from && code.Key < to).Sum(code => code.Value);
        var classesSum = classes.Success + classes.Redirect + classes.ClientError + classes.ServerError + classes.OtherStatus + classes.NoResponse + classes.Blocked;
        if (classesSum != progress.Fetched)
        {
            return $"The classes add up to {classesSum}, fetched is {progress.Fetched}: {classes}";
        }

        var reasonsSum = noResponse.RobotsDisallowed + noResponse.Timeout + noResponse.NetworkError + noResponse.NotChecked + noResponse.ProcessingError;
        if (reasonsSum != classes.NoResponse)
        {
            return $"The reasons for no response add up to {reasonsSum}, not {classes.NoResponse}: {noResponse}";
        }

        if (Codes(200, 300) != classes.Success || Codes(300, 400) != classes.Redirect || Codes(400, 500) != classes.ClientError ||
            Codes(500, 600) != classes.ServerError)
        {
            return $"The codes don't add up to their classes: {classes}, {string.Join(", ", progress.StatusCodes)}";
        }

        if (noResponse.RobotsDisallowed != progress.RobotsDisallowed)
        {
            return $"robots.txt refused {noResponse.RobotsDisallowed}, or {progress.RobotsDisallowed}";
        }

        return progress.InternalFetched > progress.InternalFound
            ? $"{progress.InternalFetched} of the site's URLs fetched, of {progress.InternalFound} found"
            : null;
    }

    private static CrawlOptions Options(LoopbackServer server) => new() { StartUrl = server.BaseUri.AbsoluteUri, Concurrency = 4 };

    private CrawlJob Start(CrawlOptions options, TimeProvider? time = null) =>
        new CrawlEngine(NullLoggerFactory.Instance, time ?? TimeProvider.System).Start(new CrawlRequest(options, _files.NewPath()));

    /// <summary>Polls the job's progress, as a UI would, until it is in <paramref name="phase"/>, noting each phase seen on the way.</summary>
    private static async Task<CrawlProgress> PhaseAsync(CrawlJob job, CrawlPhase phase, List<CrawlPhase> seen) =>
        await ProgressAsync(job, progress =>
        {
            if (seen.Count == 0 || seen[^1] != progress.Phase)
            {
                seen.Add(progress.Phase);
            }

            return progress.Phase == phase;
        });

    /// <param name="pump">A fake clock to move on 50 ms at each look, for a crawl whose requests are spaced on it.</param>
    private static async Task<CrawlProgress> ProgressAsync(CrawlJob job, Func<CrawlProgress, bool> condition, FakeTimeProvider? pump = null)
    {
        var deadline = DateTime.UtcNow + WAIT;
        while (true)
        {
            pump?.Advance(TimeSpan.FromMilliseconds(50));
            var progress = job.Progress;
            if (condition(progress))
            {
                return progress;
            }

            if (DateTime.UtcNow > deadline || job.Completion.IsCompleted)
            {
                throw new TimeoutException($"Never seen: phase {progress.Phase}, waits {string.Join(", ", progress.Waits)}, status {progress.Status}.");
            }

            await Task.Delay(5);
        }
    }

    private static Task Html(HttpContext context, string body)
    {
        context.Response.ContentType = "text/html; charset=utf-8";
        return context.Response.WriteAsync($"<!doctype html><html lang=\"en\"><head><title>Test page</title></head><body>{body}</body></html>");
    }

    private static Task Text(HttpContext context, string body)
    {
        context.Response.ContentType = "text/plain";
        return context.Response.WriteAsync(body);
    }

    private static Task Sitemap(HttpContext context, IEnumerable<string> urls)
    {
        context.Response.ContentType = "application/xml";
        return context.Response.WriteAsync("<?xml version=\"1.0\"?><urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">" +
                                           string.Concat(urls.Select(url => $"<url><loc>{url}</loc></url>")) + "</urlset>");
    }
}
