using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Fetching;
using ShoutingIguana.Engine.Tests.Fetching;
using ShoutingIguana.Engine.Tests.Support;

namespace ShoutingIguana.Engine.Tests.Crawling;

/// <summary>
/// What a crawl does with the pages a site keeps refusing (429 Too Many Requests): when a limited section's last try
/// comes. On loopback shops and the fake clock, which the 20 minutes a limited section may go on after the rest of the
/// crawl pass on in moments.
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
    [Fact]
    public async Task ASectionsLastTryComesAsItsTimeRunsOutAndProgressNeverPromisesOneAfter()
    {
        var run = await CrawlAShopWhoseProductsAreAlwaysRefusedAsync(products: 1);

        Assert.Equal(CrawlStatus.Completed, run.Summary.Status);
        var restOver = run.Observer.FirstAt(progress => progress.Phase == CrawlPhase.Retrying);
        var timeRunsOut = restOver + SectionTrickle.GIVE_UP_AFTER;
        var lastTry = run.Asked["/products/1"].Max();
        Assert.InRange(lastTry, timeRunsOut - SLACK, timeRunsOut + SLACK);

        var promised = run.Observer.Promises().ToList();
        Assert.NotEmpty(promised);
        Assert.All(promised, promise => Assert.True(promise.NextTry <= timeRunsOut + SLACK,
            $"At {promise.At - restOver} after the rest of the crawl, the next try was promised at {promise.NextTry - restOver}."));
    }

    /// <summary>
    /// A shop whose product pages, linked from its home page, answer every request with 429 and a minute's Retry-After,
    /// read by one worker, so the first product page limits the section before any other is asked.
    /// </summary>
    private async Task<ShopRun> CrawlAShopWhoseProductsAreAlwaysRefusedAsync(int products)
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

        /// <summary>When each report said a limited section's next try would come, and when it said so.</summary>
        public IEnumerable<(DateTimeOffset At, DateTimeOffset NextTry)> Promises() =>
            from report in _progress
            from wait in report.Progress.Waits
            where wait is { Reason: CrawlWaitReason.SectionLimited, Duration: not null }
            select (report.At, report.At + wait.Duration!.Value);
    }
}
