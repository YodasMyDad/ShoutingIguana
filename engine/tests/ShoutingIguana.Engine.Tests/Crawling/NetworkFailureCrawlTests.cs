using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Fetching;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Robots;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Tests.Fetching;
using ShoutingIguana.Engine.Tests.Robots;

namespace ShoutingIguana.Engine.Tests.Crawling;

/// <summary>Whole crawls and analyses of small loopback sites whose robots.txt or links fail.</summary>
public sealed class NetworkFailureCrawlTests : IAsyncLifetime
{
    private static readonly TimeSpan CRAWL_TIMEOUT = TimeSpan.FromMinutes(1);

    private readonly List<string> _databases = [];
    private LoopbackServer? _server;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_server is not null)
        {
            await _server.DisposeAsync();
        }

        foreach (var path in _databases.SelectMany(path => new[] { path, path + "-wal", path + "-shm" }))
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ALinkToASiteThatDoesNotAnswerIsABrokenExternalLink()
    {
        var dead = RobotsCacheHarness.DeadSite();
        _server = await LoopbackServer.StartAsync(context => context.Request.Path.Value switch
        {
            "/" => Html(context, $"<a href=\"{dead}gone\">Partner</a> <a href=\"/private/x\">Private</a>"),
            "/robots.txt" => Text(context, "User-agent: *\nDisallow: /private/"),
            _ => Status(context, 404)
        });
        var observer = new RecordingObserver();
        var path = NewDatabasePath();

        var summary = await CrawlAsync(path, observer);

        Assert.Equal(CrawlStatus.Completed, summary.Status);
        Assert.Equal(FetchOutcome.NetworkError, Outcome(path, $"{dead}gone"));
        Assert.Equal(0, observer.Count("robots-unreachable"));

        // A rule's refusal is stored without an error.
        Assert.Equal(FetchOutcome.RobotsDisallowed, Outcome(path, $"{_server.BaseUri}private/x"));
        Assert.Null(Error(path, $"{_server.BaseUri}private/x"));

        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path);
        Assert.Equal(1, IssueReader.ReadCounts(path).GetValueOrDefault(IssueCatalog.BROKEN_EXTERNAL_LINKS.Id));
        Assert.Null(Assert.Single(IssueReader.ReadIssues(path, IssueCatalog.BLOCKED_BY_ROBOTS.Id, 10)).Detail);
    }

    [Fact]
    public async Task UrlsRefusedBecauseRobotsTxtIsUnreachableSayWhy()
    {
        _server = await LoopbackServer.StartAsync(context => context.Request.Path.Value == "/robots.txt"
            ? Status(context, 503)
            : Html(context, "<p>Home</p>"));
        var observer = new RecordingObserver();
        var path = NewDatabasePath();

        var summary = await CrawlAsync(path, observer);

        Assert.Equal(CrawlStatus.Completed, summary.Status);
        Assert.Equal(FetchOutcome.RobotsDisallowed, Outcome(path, _server.BaseUri.AbsoluteUri));
        Assert.Equal($"{RobotsTxt.UNREACHABLE_ERROR}: HTTP 503", Error(path, _server.BaseUri.AbsoluteUri));
        Assert.Equal(1, observer.Count("robots-unreachable"));

        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path);
        var issues = IssueReader.ReadCounts(path);
        Assert.Equal(1, issues.GetValueOrDefault(IssueCatalog.ROBOTS_TXT_UNREACHABLE.Id));
        Assert.Equal(1, issues.GetValueOrDefault(IssueCatalog.BLOCKED_BY_ROBOTS.Id));

        // The blocked URL says it wasn't a robots.txt rule.
        var blocked = Assert.Single(IssueReader.ReadIssues(path, IssueCatalog.BLOCKED_BY_ROBOTS.Id, 10));
        Assert.Equal($"{RobotsTxt.UNREACHABLE_ERROR}: HTTP 503", blocked.Detail);
    }

    [Fact]
    public async Task UrlsNotCheckedBecauseTheirSiteStoppedAnsweringAreNotCalledBroken()
    {
        _server = await LoopbackServer.StartAsync(context =>
        {
            if (context.Connection.LocalPort == _server!.OtherBaseUri.Port)
            {
                // The partner site has gone: no answer to anything.
                context.Abort();
                return Task.CompletedTask;
            }

            return context.Request.Path.Value == "/"
                ? Html(context, string.Concat(Enumerable.Range(0, 30).Select(page => $"<a href=\"{_server.OtherBaseUri}p{page}\">Partner {page}</a> ")))
                : Status(context, 404);
        });
        var path = NewDatabasePath();

        await CrawlAsync(path, new RecordingObserver());
        var analysis = await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path);

        // The URLs it asked for before the breaker tripped are broken links. The rest weren't asked for, and say so.
        var notChecked = Convert.ToInt32(Scalar(path, $"SELECT COUNT(*) FROM responses WHERE error LIKE '{RetryingFetcher.NOT_CHECKED_ERROR}%'"), CultureInfo.InvariantCulture);
        var failed = Convert.ToInt32(Scalar(path, $"""
            SELECT COUNT(*) FROM responses r JOIN urls u ON u.id = r.url_id
            WHERE u.is_internal = 0 AND r.outcome = {(int)FetchOutcome.NetworkError}
            """), CultureInfo.InvariantCulture) - notChecked;
        Assert.True(notChecked > 0);
        Assert.Equal(30, failed + notChecked);
        Assert.StartsWith($"{failed} broken URL", Assert.Single(IssueReader.ReadIssues(path, IssueCatalog.BROKEN_EXTERNAL_LINKS.Id, 10)).Detail);
        Assert.Contains(analysis.Notes, note => note.StartsWith($"{notChecked} URLs weren't checked", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ASiteWithoutRobotsTxtIsCrawledInFullAndToldSo()
    {
        _server = await LoopbackServer.StartAsync(context => context.Request.Path.Value switch
        {
            "/" => Html(context, "<a href=\"/private/x\">Anything goes</a>"),
            "/private/x" => Html(context, "<p>Crawled, with no robots.txt to say otherwise.</p>"),
            _ => Status(context, 404)
        });
        var path = NewDatabasePath();

        var summary = await CrawlAsync(path, new RecordingObserver());

        Assert.Equal(CrawlStatus.Completed, summary.Status);
        Assert.Equal(FetchOutcome.Success, Outcome(path, $"{_server.BaseUri}private/x"));
        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path);
        Assert.Equal(_server.BaseUri.AbsoluteUri, Assert.Single(IssueReader.ReadIssues(path, IssueCatalog.ROBOTS_TXT_MISSING.Id, 10)).Url);
        Assert.Equal(0, IssueReader.ReadCounts(path).GetValueOrDefault(IssueCatalog.ROBOTS_TXT_UNREACHABLE.Id));
    }

    [Fact]
    public async Task APageThatGotNoResponseIsTriedOnceMoreAtTheEnd()
    {
        var flakyRequests = 0;
        _server = await LoopbackServer.StartAsync(context =>
        {
            if (context.Request.Path == "/")
            {
                return Html(context, "<a href=\"/flaky\">Flaky</a>");
            }

            if (context.Request.Path != "/flaky")
            {
                return Status(context, 404);
            }

            if (Interlocked.Increment(ref flakyRequests) == 1)
            {
                context.Abort();
                return Task.CompletedTask;
            }

            return Html(context, "<p>Back again</p>");
        });
        var path = NewDatabasePath();

        var summary = await CrawlAsync(path, new RecordingObserver());

        Assert.Equal(CrawlStatus.Completed, summary.Status);
        Assert.Equal(FetchOutcome.Success, Outcome(path, $"{_server.BaseUri}flaky"));
        Assert.Equal(2, flakyRequests);
    }

    [Fact]
    public async Task TheTryAtTheEndIsASingleAttempt()
    {
        var deadRequests = 0;
        _server = await LoopbackServer.StartAsync(context =>
        {
            if (context.Request.Path != "/dead")
            {
                return Html(context, "<a href=\"/dead\">Dead</a>");
            }

            Interlocked.Increment(ref deadRequests);
            context.Abort();
            return Task.CompletedTask;
        });
        var path = NewDatabasePath();

        await CrawlAsync(path, new RecordingObserver(), maxRetries: 1);

        // Two attempts in the crawl, one at the end.
        Assert.Equal(FetchOutcome.NetworkError, Outcome(path, $"{_server.BaseUri}dead"));
        Assert.Equal(3, deadRequests);
    }

    [Fact]
    public async Task ARobotsTxtThatFailedIsReadAgainAtTheEnd()
    {
        var robotsRequests = 0;
        _server = await LoopbackServer.StartAsync(context => context.Request.Path.Value switch
        {
            "/robots.txt" when Interlocked.Increment(ref robotsRequests) == 1 => Status(context, 503),
            "/robots.txt" => Text(context, "User-agent: *\nDisallow:"),
            "/" => Html(context, "<a href=\"/about\">About</a>"),
            "/about" => Html(context, "<p>About us</p>"),
            _ => Status(context, 404)
        });
        var path = NewDatabasePath();

        var summary = await CrawlAsync(path, new RecordingObserver());

        Assert.Equal(CrawlStatus.Completed, summary.Status);
        Assert.Equal(FetchOutcome.Success, Outcome(path, _server.BaseUri.AbsoluteUri));
        Assert.Equal(FetchOutcome.Success, Outcome(path, $"{_server.BaseUri}about"));
        Assert.Equal(2, robotsRequests);
    }

    [Fact]
    public async Task AStartUrlThatRedirectsToAnotherSiteSaysSo()
    {
        // A site that moved to a new domain: the start URL is all there is to crawl, which looks broken unless it's said.
        const string MOVED_TO = "http://moved.invalid/";
        _server = await LoopbackServer.StartAsync(context =>
        {
            if (context.Request.Path.Value == "/robots.txt")
            {
                return Status(context, 404);
            }

            context.Response.Headers.Location = MOVED_TO;
            return Status(context, 301);
        });
        var observer = new RecordingObserver();
        var path = NewDatabasePath();

        var summary = await CrawlAsync(path, observer);

        Assert.Equal(CrawlStatus.Completed, summary.Status);
        Assert.Equal(FetchOutcome.Redirect, Outcome(path, _server.BaseUri.AbsoluteUri));
        var notice = Assert.Single(observer.Notices, notice => notice.Code == "start-redirects-away");
        Assert.Equal(CrawlNoticeLevel.Warning, notice.Level);
        Assert.Contains(MOVED_TO, notice.Message);
    }

    /// <summary>
    /// The home page answers, then the site goes down for good. On the fake clock, so the 1-minute pauses between
    /// tries and the 10 minutes before giving up pass in a couple of seconds.
    /// </summary>
    [Fact]
    public async Task ASiteThatStaysDownStopsTheCrawlAndCanBeResumed()
    {
        var links = string.Concat(Enumerable.Range(0, 60).Select(page => $"<a href=\"/p{page}\">Page {page}</a> "));
        _server = await LoopbackServer.StartAsync(context =>
        {
            switch (context.Request.Path.Value)
            {
                case "/":
                    return Html(context, links);
                case "/robots.txt":
                    return Status(context, 404);
                default:
                    context.Abort();
                    return Task.CompletedTask;
            }
        });
        var time = new FakeTimeProvider();
        var observer = new RecordingObserver();
        var path = NewDatabasePath();
        var options = new CrawlOptions { StartUrl = _server.BaseUri.AbsoluteUri, MaxRetries = 0, DiscoverSitemaps = false, Concurrency = 2 };

        await using var job = new CrawlEngine(NullLoggerFactory.Instance, time).Start(new CrawlRequest(options, path), observer);
        var started = time.GetUtcNow();
        var deadline = DateTime.UtcNow + CRAWL_TIMEOUT;
        while (!job.Completion.IsCompleted && DateTime.UtcNow < deadline)
        {
            time.Advance(TimeSpan.FromSeconds(5));
            await Task.Delay(1);
        }

        var summary = await job.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(CrawlStatus.Stopped, summary.Status);
        Assert.Equal(1, observer.Count("site-down"));
        Assert.InRange(time.GetUtcNow() - started, RetryingFetcher.GIVE_UP_AFTER, RetryingFetcher.GIVE_UP_AFTER + TimeSpan.FromMinutes(2));

        // Nothing it couldn't reach was written off as broken: it's all still to do, for when the site is back.
        Assert.Equal(0L, Scalar(path, $"""
            SELECT COUNT(*) FROM responses r JOIN urls u ON u.id = r.url_id
            WHERE u.is_internal = 1 AND r.outcome IN ({(int)FetchOutcome.Timeout}, {(int)FetchOutcome.NetworkError})
            """));
        Assert.Equal(60L, Scalar(path, "SELECT COUNT(*) FROM urls WHERE is_internal = 1 AND state IN (0, 1)"));
    }

    private async Task<CrawlSummary> CrawlAsync(string path, ICrawlObserver observer, int maxRetries = 0)
    {
        var options = new CrawlOptions { StartUrl = _server!.BaseUri.AbsoluteUri, MaxRetries = maxRetries, DiscoverSitemaps = false };
        await using var job = new CrawlEngine(NullLoggerFactory.Instance).Start(new CrawlRequest(options, path), observer);
        return await job.Completion.WaitAsync(CRAWL_TIMEOUT);
    }

    private string NewDatabasePath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"si-test-{Guid.NewGuid():N}.sicrawl");
        _databases.Add(path);
        return path;
    }

    private static FetchOutcome Outcome(string path, string url) =>
        (FetchOutcome)Convert.ToInt32(Scalar(path, $"SELECT r.outcome FROM responses r JOIN urls u ON u.id = r.url_id WHERE u.url = '{url}'"),
            CultureInfo.InvariantCulture);

    private static string? Error(string path, string url) =>
        Scalar(path, $"SELECT r.error FROM responses r JOIN urls u ON u.id = r.url_id WHERE u.url = '{url}'") as string;

    private static object? Scalar(string path, string sql)
    {
        using var connection = CrawlDatabase.OpenReader(path);
        return CrawlDatabase.Scalar(connection, sql);
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

    private static Task Status(HttpContext context, int status)
    {
        context.Response.StatusCode = status;
        return Task.CompletedTask;
    }
}
