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

namespace ShoutingIguana.Engine.Tests.Crawling;

/// <summary>
/// What a crawl says it is doing while it runs, and what holds it up: robots.txt before the first fetch, the sitemaps
/// before the URLs only they list, a crawl-delay, a site asking it to slow down or not answering, and robots.txt's
/// cool-down before the last tries. Read from the job's progress as a UI would, on small loopback sites.
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
        Assert.Contains(reading.Waits, wait => wait is { Reason: CrawlWaitReason.RobotsTxt, Urls: >= 1 });

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
        await job.StopAsync();
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
        var progress = await ProgressAsync(job, progress => progress.Waits.Any(wait => wait.Reason == CrawlWaitReason.HeldBack));

        var held = Assert.Single(progress.Waits, wait => wait.Reason == CrawlWaitReason.HeldBack);
        Assert.Equal("127.0.0.1", held.Host);
        Assert.InRange(held.Duration!.Value, TimeSpan.FromSeconds(25), TimeSpan.FromSeconds(30));
        Assert.Contains(progress.Waits, wait => wait is { Reason: CrawlWaitReason.Backoff, Host: "127.0.0.1" } && wait.Duration > TimeSpan.Zero);
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

    private static async Task<CrawlProgress> ProgressAsync(CrawlJob job, Func<CrawlProgress, bool> condition)
    {
        var deadline = DateTime.UtcNow + WAIT;
        while (true)
        {
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
}
