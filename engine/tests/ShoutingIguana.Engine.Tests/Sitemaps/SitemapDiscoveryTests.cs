using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Sitemaps;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Tests.Crawling;
using ShoutingIguana.Engine.Tests.Fetching;
using ShoutingIguana.Engine.Tests.Support;

namespace ShoutingIguana.Engine.Tests.Sitemaps;

/// <summary>Crawls of a small loopback site whose robots.txt and sitemaps are awkward in the ways real ones are.</summary>
public sealed class SitemapDiscoveryTests : IAsyncLifetime
{
    private const string URLSET = "<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">";

    /// <summary>Sends a sitemap's first URL, then nothing more, without closing.</summary>
    private const string STALLING_SITEMAP = "/stalls.xml";

    private readonly List<string> _databases = [];
    private readonly Dictionary<string, (string ContentType, string Body)> _files = new(StringComparer.Ordinal);
    private readonly TaskCompletionSource _stalled = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _robotsFailures;
    private LoopbackServer _server = null!;

    public async Task InitializeAsync() => _server = await LoopbackServer.StartAsync(RespondAsync);

    public async Task DisposeAsync()
    {
        await _server.DisposeAsync();
        foreach (var path in _databases.SelectMany(path => new[] { path, path + "-wal", path + "-shm" }))
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task UrlsBeforeAParseErrorAreKeptAndCounted()
    {
        Serve("/robots.txt", "text/plain", $"User-agent: *\nDisallow:\nSitemap: {_server.Url("/sitemap.xml")}");
        Serve("/sitemap.xml", "application/xml", $"{URLSET}{Loc("/a")}{Loc("/b")}{Loc("/c")}<url><loc>{_server.Url("/d")}?x=1&y=2</loc></url></urlset>");

        var path = await CrawlAsync();

        var sitemap = Assert.Single(Sitemaps(path));
        Assert.Equal(3, sitemap.UrlCount);
        Assert.NotNull(sitemap.Error);
        Assert.Equal(3, Count(path, "SELECT COUNT(*) FROM sitemap_urls"));
    }

    [Fact]
    public async Task XmlServedAsTextIsStillRead()
    {
        Serve("/robots.txt", "text/plain", $"User-agent: *\nDisallow:\nSitemap: {_server.Url("/sitemap.xml")}");
        Serve("/sitemap.xml", "text/plain; charset=utf-8", $"<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n{URLSET}\n{Loc("/a")}\n{Loc("/b")}\n</urlset>");

        var path = await CrawlAsync();

        var sitemap = Assert.Single(Sitemaps(path));
        Assert.Equal(2, sitemap.UrlCount);
        Assert.Null(sitemap.Error);
        Assert.Equal(2, Count(path, "SELECT COUNT(*) FROM sitemap_urls"));
    }

    [Fact]
    public async Task ARelativeSitemapLineIsResolvedAgainstRobotsTxt()
    {
        Serve("/robots.txt", "text/plain", "User-agent: *\nDisallow:\nSitemap: /maps/site.xml");
        Serve("/maps/site.xml", "application/xml", $"{URLSET}{Loc("/a")}{Loc("/b")}</urlset>");
        Serve("/sitemap.xml", "application/xml", $"{URLSET}{Loc("/c")}</urlset>");

        var path = await CrawlAsync();

        var sitemap = Assert.Single(Sitemaps(path));
        Assert.Equal(_server.Url("/maps/site.xml").AbsoluteUri, sitemap.Url);
        Assert.Equal(2, sitemap.UrlCount);
        Assert.False(sitemap.IsProbe);
    }

    [Fact]
    public async Task OnlyUnusableSitemapLinesStillLeadToTheProbe()
    {
        Serve("/robots.txt", "text/plain", "User-agent: *\nDisallow:\nSitemap: ftp://files.example/sitemap.xml");
        Serve("/sitemap.xml", "application/xml", $"{URLSET}{Loc("/a")}{Loc("/b")}</urlset>");

        var path = await CrawlAsync();

        var sitemap = Assert.Single(Sitemaps(path));
        Assert.Equal(_server.Url("/sitemap.xml").AbsoluteUri, sitemap.Url);
        Assert.True(sitemap.IsProbe);
        Assert.Equal(2, sitemap.UrlCount);
    }

    /// <summary>robots.txt fails once, then names the sitemap: it is read once robots.txt can be, and nothing is guessed.</summary>
    [Fact]
    public async Task TheSitemapsOfARobotsTxtThatFailedAtFirstAreRead()
    {
        _robotsFailures = 1;
        Serve("/robots.txt", "text/plain", "User-agent: *\nDisallow:\nSitemap: /real-sitemap.xml");
        Serve("/real-sitemap.xml", "application/xml", $"{URLSET}{Loc("/a")}{Loc("/b")}</urlset>");
        var time = new FakeTimeProvider();
        var path = NewDatabasePath();

        await using var job = new CrawlEngine(NullLoggerFactory.Instance, time).Start(
            new CrawlRequest(new CrawlOptions { StartUrl = _server.BaseUri.AbsoluteUri, MaxRetries = 0 }, path));
        var summary = await FakeClock.PumpAsync(time, job.Completion);

        Assert.Equal(CrawlStatus.Completed, summary.Status);
        var sitemap = Assert.Single(Sitemaps(path));
        Assert.Equal(_server.Url("/real-sitemap.xml").AbsoluteUri, sitemap.Url);
        Assert.False(sitemap.IsProbe);
        Assert.Equal(2, sitemap.UrlCount);
    }

    [Fact]
    public async Task ASitemapNestedTooDeepIsRecordedAsNotRead()
    {
        // Each index lists the next; the last is one level deeper than the crawler follows.
        Serve("/robots.txt", "text/plain", $"User-agent: *\nDisallow:\nSitemap: {_server.Url("/index-0.xml")}");
        for (var level = 0; level <= SitemapDiscoverer.MAX_NESTING; level++)
        {
            Serve($"/index-{level}.xml", "application/xml",
                $"<sitemapindex xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\"><sitemap><loc>{_server.Url($"/index-{level + 1}.xml")}</loc></sitemap></sitemapindex>");
        }

        var observer = new RecordingObserver();
        var path = await CrawlAsync(observer);

        var sitemaps = Sitemaps(path);
        Assert.Equal(SitemapDiscoverer.MAX_NESTING + 2, sitemaps.Count);
        var notRead = sitemaps[^1];
        Assert.Equal(_server.Url($"/index-{SitemapDiscoverer.MAX_NESTING + 1}.xml").AbsoluteUri, notRead.Url);
        Assert.Equal($"{SitemapDiscoverer.NOT_READ_ERROR}: sitemap indexes nested more than 3 deep aren't followed", notRead.Error);
        Assert.Null(notRead.StatusCode);
        Assert.False(notRead.IsProbe);
        Assert.Equal(1, observer.Count("sitemaps-not-read"));
    }

    [Fact]
    public async Task SitemapsPastTheLimitAreRecordedAsNotRead()
    {
        // An index of more sitemaps than the crawler reads: it and all but the last of them are read.
        Serve("/robots.txt", "text/plain", $"User-agent: *\nDisallow:\nSitemap: {_server.Url("/index.xml")}");
        Serve("/index.xml", "application/xml", $"<sitemapindex xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">{string.Concat(
            Enumerable.Range(0, SitemapDiscoverer.MAX_SITEMAPS).Select(child => $"<sitemap><loc>{_server.Url($"/part-{child}.xml")}</loc></sitemap>"))}</sitemapindex>");
        for (var child = 0; child < SitemapDiscoverer.MAX_SITEMAPS; child++)
        {
            Serve($"/part-{child}.xml", "application/xml", $"{URLSET}</urlset>");
        }

        var observer = new RecordingObserver();
        var path = await CrawlAsync(observer);

        var sitemaps = Sitemaps(path);
        Assert.Equal(SitemapDiscoverer.MAX_SITEMAPS + 1, sitemaps.Count);
        var notRead = sitemaps[^1];
        Assert.Equal(_server.Url($"/part-{SitemapDiscoverer.MAX_SITEMAPS - 1}.xml").AbsoluteUri, notRead.Url);
        Assert.Equal($"{SitemapDiscoverer.NOT_READ_ERROR}: the crawler reads at most 1,000 sitemaps", notRead.Error);
        Assert.Null(notRead.StatusCode);
        Assert.False(notRead.IsProbe);
        Assert.Equal(1, observer.Count("sitemaps-not-read"));
    }

    /// <remarks>
    /// The stalling crawls aren't disposed: a read that ignored its token would hold the stop for good. Disposing the
    /// server ends them either way.
    /// </remarks>
    [Fact]
    public async Task ASitemapThatStallsTimesOutAndKeepsWhatItListed()
    {
        var time = new FakeTimeProvider();
        var path = NewDatabasePath();

        var job = new CrawlEngine(NullLoggerFactory.Instance, time).Start(new CrawlRequest(StallingOptions(), path));
        var summary = await FakeClock.PumpAsync(time, job.Completion, realLimit: TimeSpan.FromSeconds(30));

        Assert.Equal(CrawlStatus.Completed, summary.Status);
        var sitemap = Assert.Single(Sitemaps(path));
        Assert.Equal("Timed out", sitemap.Error);
        Assert.Equal(1, sitemap.UrlCount);
        Assert.Equal(1, Count(path, "SELECT COUNT(*) FROM sitemap_urls"));
    }

    [Fact]
    public async Task StoppingWhileASitemapStallsEndsTheCrawl()
    {
        var job = new CrawlEngine(NullLoggerFactory.Instance).Start(new CrawlRequest(StallingOptions(), NewDatabasePath()));
        await _stalled.Task.WaitAsync(TimeSpan.FromSeconds(30));

        var summary = await job.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(CrawlStatus.Stopped, summary.Status);
    }

    private CrawlOptions StallingOptions() => new()
    {
        StartUrl = _server.BaseUri.AbsoluteUri,
        MaxRetries = 0,
        ExtraSitemaps = [_server.Url(STALLING_SITEMAP).AbsoluteUri]
    };

    private string Loc(string path) => $"<url><loc>{_server.Url(path)}</loc></url>";

    private void Serve(string path, string contentType, string body) => _files[path] = (contentType, body);

    private string NewDatabasePath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"si-test-{Guid.NewGuid():N}.sicrawl");
        _databases.Add(path);
        return path;
    }

    private async Task<string> CrawlAsync(ICrawlObserver? observer = null)
    {
        var path = NewDatabasePath();
        var options = new CrawlOptions { StartUrl = _server.BaseUri.AbsoluteUri, MaxRetries = 0 };
        await using var job = new CrawlEngine(NullLoggerFactory.Instance).Start(new CrawlRequest(options, path), observer);
        Assert.Equal(CrawlStatus.Completed, (await job.Completion.WaitAsync(TimeSpan.FromMinutes(1))).Status);
        return path;
    }

    private static IReadOnlyList<StoredSitemap> Sitemaps(string path)
    {
        using var reader = AnalysisReader.Open(path);
        return reader.ReadSitemaps();
    }

    private static long Count(string path, string sql)
    {
        using var connection = CrawlDatabase.OpenReader(path);
        return Convert.ToInt64(CrawlDatabase.Scalar(connection, sql), CultureInfo.InvariantCulture);
    }

    private Task RespondAsync(HttpContext context)
    {
        if (context.Request.Path == STALLING_SITEMAP)
        {
            return StallAsync(context);
        }

        if (context.Request.Path == "/robots.txt" && Interlocked.Decrement(ref _robotsFailures) >= 0)
        {
            context.Response.StatusCode = 503;
            return Task.CompletedTask;
        }

        if (_files.TryGetValue(context.Request.Path.Value ?? "/", out var file))
        {
            context.Response.ContentType = file.ContentType;
            return context.Response.WriteAsync(file.Body);
        }

        context.Response.ContentType = "text/html; charset=utf-8";
        return context.Response.WriteAsync("<!doctype html><html lang=\"en\"><head><title>Page</title></head><body><p>Page</p></body></html>");
    }

    private async Task StallAsync(HttpContext context)
    {
        context.Response.ContentType = "application/xml";
        await context.Response.WriteAsync($"{URLSET}{Loc("/a")}");
        await context.Response.Body.FlushAsync();
        _stalled.TrySetResult();
        try
        {
            await Task.Delay(Timeout.Infinite, context.RequestAborted);
        }
        catch (OperationCanceledException)
        {
            // The crawler gave up on it.
        }
    }
}
