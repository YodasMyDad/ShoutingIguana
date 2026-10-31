using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
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
    }

    private async Task<CrawlSummary> CrawlAsync(string path, ICrawlObserver observer)
    {
        var options = new CrawlOptions { StartUrl = _server!.BaseUri.AbsoluteUri, MaxRetries = 0, DiscoverSitemaps = false };
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
