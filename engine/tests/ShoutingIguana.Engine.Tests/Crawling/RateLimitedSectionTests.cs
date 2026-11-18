using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Fetching;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Tests.Fetching;
using ShoutingIguana.Engine.Tests.Support;

namespace ShoutingIguana.Engine.Tests.Crawling;

/// <summary>
/// What a crawl does with the pages a site keeps refusing (429 Too Many Requests), and what it says of them: when a
/// limited section's last try comes, and, of the pages left unchecked, which were asked and refused, how often, and which
/// never had their turn. On loopback shops and the fake clock, which the 20 minutes a limited section may go on after the
/// rest of the crawl pass on in moments.
/// </summary>
public sealed class RateLimitedSectionTests : IAsyncLifetime
{
    private static readonly TimeSpan SLACK = TimeSpan.FromSeconds(10);

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
    /// The real shop's case: its product pages asked for a minute's pause, and the gap between tries doubled to 16
    /// minutes, past the 20 minutes the section may go on after the rest of the crawl. The try that would have come after
    /// them comes as they end instead, so their last minutes aren't spent waiting for nothing, and progress never
    /// promises a try past them.
    /// </summary>
    /// <remarks>
    /// The fake clock is pumped while the engine works in real time, so under load it runs on between the engine deciding
    /// something and the test seeing it: a report or a request comes late on it, never early. Each bound is set so that
    /// only lateness it can't come from would break it: when the rest was over lies between the last report before the
    /// section was left alone and the first after; the latest try promised is the time running out; and the site was asked
    /// no earlier than that.
    /// </remarks>
    [Fact]
    public async Task ASectionsLastTryComesAsItsTimeRunsOutAndProgressNeverPromisesOneAfter()
    {
        var run = await CrawlAShopWhoseProductsAreAlwaysRefusedAsync(products: 1);

        Assert.Equal(CrawlStatus.Completed, run.Summary.Status);
        var restOverAfter = run.Observer.LastAtBefore(progress => progress.Phase == CrawlPhase.Retrying);
        var restOverBy = run.Observer.FirstAt(progress => progress.Phase == CrawlPhase.Retrying);
        var promised = run.Observer.Promises().ToList();
        Assert.NotEmpty(promised);
        var latest = promised.MaxBy(promise => promise.NextTry);

        Assert.True(latest.NextTry <= restOverBy + SectionTrickle.GIVE_UP_AFTER + SLACK,
            $"At {latest.At - restOverAfter} after the rest of the crawl, the next try was promised at {latest.NextTry - restOverAfter}.");
        Assert.True(latest.NextTry >= restOverAfter + SectionTrickle.GIVE_UP_AFTER - SLACK,
            $"The latest try promised was at {latest.NextTry - restOverAfter} after the rest of the crawl, before its time ran out.");
        Assert.True(run.Asked["/products/1"].Max() >= latest.NextTry - SLACK,
            $"The last try came {run.Asked["/products/1"].Max() - restOverAfter} after the rest of the crawl, before the one promised at {latest.NextTry - restOverAfter}.");
    }

    /// <summary>
    /// Paused time is no part of the 20 minutes a limited section may go on once the rest of the crawl is over: a crawl
    /// paused for half an hour then, and resumed, still gives the section its 20 minutes of crawling, its last try as they
    /// end, not at once on resuming.
    /// </summary>
    [Fact]
    public async Task PausedTimeIsNoPartOfASectionsTwentyMinutes()
    {
        var pausedFor = TimeSpan.FromMinutes(30);
        var run = await CrawlAShopWhoseProductsAreAlwaysRefusedAsync(products: 1, pauseOnceTheRestIsOver: pausedFor);

        Assert.Equal(CrawlStatus.Completed, run.Summary.Status);
        var restOverBy = run.Observer.FirstAt(progress => progress.Phase == CrawlPhase.Retrying);
        var lastTry = run.Asked["/products/1"].Max();
        Assert.True(lastTry >= restOverBy + pausedFor + SectionTrickle.GIVE_UP_AFTER - SLACK,
            $"The last try came {lastTry - restOverBy} after the rest of the crawl, of which {pausedFor} was paused.");
    }

    /// <summary>
    /// One worker reads the shop: its first product page is refused, which limits the section, so the others wait their
    /// turn there and the first is asked again and again. None of the others is ever asked: each is stored as never asked,
    /// told apart from the one refused, and the notice says which is which, and how often the refused one was asked.
    /// </summary>
    [Fact]
    public async Task PagesASectionNeverReachedAreStoredAndSaidApartFromThoseRefused()
    {
        var run = await CrawlAShopWhoseProductsAreAlwaysRefusedAsync(products: 5);

        Assert.Equal(CrawlStatus.Completed, run.Summary.Status);
        Assert.Equal(["/products/1"], run.Asked.Keys);
        var refusals = run.Asked["/products/1"].Count;
        Assert.True(refusals >= 4, $"{refusals} refusals");

        var errors = TestCrawls.Rows(run.Path, "SELECT u.url, r.error FROM responses r JOIN urls u ON u.id = r.url_id WHERE u.url LIKE '%/products/%'")
            .Select(row => row.Split(" | ")).ToDictionary(row => new Uri(row[0]).AbsolutePath, row => row[1]);
        Assert.Equal($"{RetryingFetcher.RATE_LIMITED_ERROR} (429 Too Many Requests)", errors["/products/1"]);
        Assert.True(RateLimitedErrors.WasRefused(errors["/products/1"]));
        Assert.False(RateLimitedErrors.WasNeverAsked(errors["/products/1"]));
        foreach (var product in Enumerable.Range(2, 4).Select(product => $"/products/{product}"))
        {
            Assert.Equal(RetryingFetcher.NEVER_ASKED_ERROR, errors[product]);
            Assert.True(RateLimitedErrors.WasNeverAsked(errors[product]));
            Assert.True(RateLimitedErrors.IsRateLimited(errors[product]));
            Assert.False(RateLimitedErrors.WasRefused(errors[product]));
        }

        Assert.Equal(5, CrawlStatsReader.Read(run.Path).NotCheckedCount);

        var notice = Assert.Single(run.Observer.Notices, notice => notice.Code == "rate-limited-unchecked");
        Assert.Equal(5, notice.Data!.Count);
        Assert.Equal(
            "5 URLs weren't checked because 127.0.0.1 kept asking the crawler to slow down (429 Too Many Requests). " +
            $"1 was asked for and refused {refusals} times. " +
            "4 never had their turn to be asked: the crawl reads /products/ one page at a time, as the site limits it, and the 20 minutes it gives such pages once the rest of the site is done ran out first. " +
            "Check the site again later, or at the Polite speed.",
            notice.Message);
    }

    /// <summary>
    /// 429s from three sections are the host's limit: its pages that weren't in the first two limited sections are held
    /// back to the end of the crawl and asked three more times there. The notice says how often they were asked, as the
    /// site's own count of its refusals has it.
    /// </summary>
    [Fact]
    public async Task TheNoticeSaysHowOftenThePagesHeldToTheEndWereRefused()
    {
        ConcurrentDictionary<string, int> asked = new();
        _server = await LoopbackServer.StartAsync(context =>
        {
            var path = context.Request.Path.Value!;
            switch (path)
            {
                case "/robots.txt":
                    return Text(context, "User-agent: *\nDisallow:");
                case "/":
                    return Html(context, "<a href=\"/a/1\">A</a> <a href=\"/b/1\">B</a> <a href=\"/c/1\">C</a> <a href=\"/d/1\">D</a>");
                case "/a/1" or "/b/1" or "/c/1" or "/d/1":
                    // The first two let a page through once asked again; the last two never do.
                    if (asked.AddOrUpdate(path, 1, static (_, count) => count + 1) > 1 && path is "/a/1" or "/b/1")
                    {
                        return Html(context, "<p>Through</p>");
                    }

                    context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                    context.Response.Headers.RetryAfter = "1";
                    return Task.CompletedTask;
                default:
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return Task.CompletedTask;
            }
        });

        var time = new FakeTimeProvider();
        var observer = new Recorder(time);
        CrawlSummary summary;
        var path = _files.NewPath();
        await using (var job = new CrawlEngine(NullLoggerFactory.Instance, time).Start(new CrawlRequest(Options(_server), path), observer))
        {
            summary = await FakeClock.PumpAsync(time, job.Completion, TimeSpan.FromSeconds(1), realLimit: TimeSpan.FromMinutes(2));
        }

        Assert.Equal(CrawlStatus.Completed, summary.Status);
        Assert.Equal(4, asked["/c/1"]);
        Assert.Equal(4, asked["/d/1"]);
        var notice = Assert.Single(observer.Notices, notice => notice.Code == "rate-limited-unchecked");
        Assert.Equal(2, notice.Data!.Count);
        Assert.Equal(
            "2 URLs weren't checked because 127.0.0.1 kept asking the crawler to slow down (429 Too Many Requests). " +
            "2 were asked for and refused 4 times each. Check the site again later, or at the Polite speed.",
            notice.Message);
    }

    /// <summary>
    /// A shop whose product pages, linked from its home page, answer every request with 429 and a minute's Retry-After,
    /// read by one worker, so the first product page limits the section before any other is asked.
    /// </summary>
    /// <param name="pauseOnceTheRestIsOver">Pauses the crawl for this long, on the fake clock, as only the section is left.</param>
    private async Task<ShopRun> CrawlAShopWhoseProductsAreAlwaysRefusedAsync(int products, TimeSpan? pauseOnceTheRestIsOver = null)
    {
        var time = new FakeTimeProvider();
        ConcurrentDictionary<string, ConcurrentQueue<DateTimeOffset>> asked = new();
        var links = string.Concat(Enumerable.Range(1, products).Select(product => $"<a href=\"/products/{product}\">Product {product}</a> "));
        _server = await LoopbackServer.StartAsync(context =>
        {
            var path = context.Request.Path.Value!;
            if (path.StartsWith("/products/", StringComparison.Ordinal))
            {
                asked.GetOrAdd(path, _ => new ConcurrentQueue<DateTimeOffset>()).Enqueue(time.GetUtcNow());
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.Response.Headers.RetryAfter = "60";
                return Task.CompletedTask;
            }

            switch (path)
            {
                case "/robots.txt":
                    return Text(context, "User-agent: *\nDisallow:");
                case "/":
                    return Html(context, links);
                default:
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return Task.CompletedTask;
            }
        });

        var observer = new Recorder(time);
        var path = _files.NewPath();
        CrawlSummary summary;
        await using (var job = new CrawlEngine(NullLoggerFactory.Instance, time).Start(new CrawlRequest(Options(_server), path), observer))
        {
            if (pauseOnceTheRestIsOver is { } paused)
            {
                await FakeClock.PumpUntilAsync(time, () => job.Progress.Phase == CrawlPhase.Retrying);
                job.Pause();
                await FakeClock.PumpUntilAsync(time, () => job.IsPaused);

                // The run's clock stops as the session takes the pause in, a moment on: the first minute in seconds, so the
                // fake clock doesn't run a minute past that moment in one step.
                for (var waited = TimeSpan.Zero; waited < paused; waited += waited < TimeSpan.FromMinutes(1) ? TimeSpan.FromSeconds(1) : TimeSpan.FromMinutes(1))
                {
                    var step = waited < TimeSpan.FromMinutes(1) ? TimeSpan.FromSeconds(1) : TimeSpan.FromMinutes(1);
                    time.Advance(step);
                    await Task.Delay(waited < TimeSpan.FromMinutes(1) ? 1 : 5);
                }

                job.Resume();
            }

            summary = await FakeClock.PumpAsync(time, job.Completion, TimeSpan.FromSeconds(1), realLimit: TimeSpan.FromMinutes(3));
        }

        return new ShopRun(path, summary, observer, asked);
    }

    private static CrawlOptions Options(LoopbackServer server) => new() { StartUrl = server.BaseUri.AbsoluteUri, Concurrency = 1, MaxRetries = 0 };

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

    private sealed record ShopRun(string Path, CrawlSummary Summary, Recorder Observer, ConcurrentDictionary<string, ConcurrentQueue<DateTimeOffset>> Asked);

    /// <summary>Keeps the notices, and each progress report with when on the clock it came.</summary>
    private sealed class Recorder(TimeProvider time) : ICrawlObserver
    {
        private readonly ConcurrentQueue<(CrawlProgress Progress, DateTimeOffset At)> _progress = new();
        private readonly ConcurrentQueue<CrawlNotice> _notices = new();

        public IReadOnlyList<CrawlNotice> Notices => [.. _notices];

        public void OnProgress(CrawlProgress progress) => _progress.Enqueue((progress, time.GetUtcNow()));

        public void OnNotice(CrawlNotice notice) => _notices.Enqueue(notice);

        public DateTimeOffset FirstAt(Func<CrawlProgress, bool> condition) => _progress.First(report => condition(report.Progress)).At;

        /// <summary>When the last report before the first that meets <paramref name="condition"/> came.</summary>
        public DateTimeOffset LastAtBefore(Func<CrawlProgress, bool> condition) =>
            _progress.TakeWhile(report => !condition(report.Progress)).Last().At;

        /// <summary>When each report said a limited section's next try would come, and when it said so.</summary>
        public IEnumerable<(DateTimeOffset At, DateTimeOffset NextTry)> Promises() =>
            from report in _progress
            from wait in report.Progress.Waits
            where wait is { Reason: CrawlWaitReason.SectionLimited, Duration: not null }
            select (report.At, report.At + wait.Duration!.Value);
    }
}
