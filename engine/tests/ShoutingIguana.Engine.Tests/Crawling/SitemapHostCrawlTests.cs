using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Tests.Fetching;

namespace ShoutingIguana.Engine.Tests.Crawling;

/// <summary>A crawl whose sitemaps list URLs on hosts they don't live on, analysed end to end.</summary>
public sealed class SitemapHostCrawlTests : IAsyncLifetime
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"si-test-{Guid.NewGuid():N}.sicrawl");
    private LoopbackServer _server = null!;

    public async Task InitializeAsync() => _server = await LoopbackServer.StartAsync(RespondAsync);

    public async Task DisposeAsync()
    {
        await _server.DisposeAsync();
        foreach (var path in new[] { _path, _path + "-wal", _path + "-shm" })
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// The site's own sitemap lists one of its pages and a page on its www twin, the same site but another host. A
    /// sitemap on the partner's host, which the site's robots.txt declares, lists one of the site's pages (allowed:
    /// the site declared it) and one of the partner's (not one of the site's URLs at all).
    /// </summary>
    [Fact]
    public async Task EntriesBelongOnTheirSitemapsHostOrTheSiteThatDeclaredIt()
    {
        var options = new CrawlOptions { StartUrl = _server.BaseUri.AbsoluteUri, MaxRetries = 0 };
        await using (var job = new CrawlEngine(NullLoggerFactory.Instance).Start(new CrawlRequest(options, _path)))
        {
            Assert.Equal(CrawlStatus.Completed, (await job.Completion.WaitAsync(TimeSpan.FromMinutes(1))).Status);
        }

        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(_path);

        Assert.Equal(
            new[] { Twin("/b"), $"{_server.OtherBaseUri}d" }.Order(StringComparer.Ordinal),
            IssueReader.ReadIssues(_path, IssueCatalog.SITEMAP_URL_OTHER_HOST.Id, 10).Select(row => row.Url).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// The site lives on www, and the bare domain typed as the start URL redirects everything there, robots.txt included.
    /// However its sitemap is declared (relative, on the bare host, on www, or not at all), its www pages are the site's
    /// own. The loopback server answers for both hosts as the crawl's proxy, so no DNS is needed.
    /// </summary>
    [Theory]
    [InlineData("/sitemap.xml")]
    [InlineData("http://example.com/sitemap.xml")]
    [InlineData("http://www.example.com/sitemap.xml")]
    [InlineData(null)]
    public async Task ASiteThatRedirectsToWwwOwnsItsWwwEntries(string? sitemapLine)
    {
        const string WWW = "http://www.example.com";
        await using var site = await LoopbackServer.StartAsync(context =>
        {
            var path = context.Request.Path.Value ?? "/";
            if (context.Request.Host.Host != "www.example.com")
            {
                context.Response.StatusCode = 301;
                context.Response.Headers.Location = WWW + path;
                return Task.CompletedTask;
            }

            return path switch
            {
                // Without a Sitemap line, the crawler guesses /sitemap.xml on the start host.
                "/robots.txt" => Text(context, sitemapLine is null ? "User-agent: *\nAllow: /" : $"User-agent: *\nAllow: /\nSitemap: {sitemapLine}"),
                "/sitemap.xml" => Xml(context, Sitemap($"{WWW}/", $"{WWW}/a")),
                "/" => Html(context, "<a href=\"/a\">A</a>"),
                "/a" => Html(context, "<p>A page</p>"),
                _ => Status(context, 404)
            };
        });

        var options = new CrawlOptions { StartUrl = "http://example.com/", ProxyUrl = site.BaseUri.AbsoluteUri, MaxRetries = 0 };
        await using (var job = new CrawlEngine(NullLoggerFactory.Instance).Start(new CrawlRequest(options, _path)))
        {
            Assert.Equal(CrawlStatus.Completed, (await job.Completion.WaitAsync(TimeSpan.FromMinutes(1))).Status);
        }

        var analysis = await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(_path);

        // Both www pages were crawled and the sitemap listing them was read, and neither is on another host.
        var stats = CrawlStatsReader.Read(_path);
        Assert.Equal(2, stats.HtmlPageCount);
        Assert.Equal(2, stats.SitemapUrlCount);
        Assert.Equal(0, analysis.Issues.Single(issue => issue.Issue == IssueCatalog.SITEMAP_URL_OTHER_HOST).Count);

        // www, where the start URL leads, is the main version, and the bare domain redirects there. The https:// versions,
        // dead through this proxy, are no problem of the site's.
        Assert.Equal(0, analysis.Issues.Single(issue => issue.Issue == IssueCatalog.DUPLICATE_SITE_VERSION).Count);
        Assert.DoesNotContain(IssueReader.ReadAll(_path), row => row.Issue is "internal-unreachable" or "robots-txt-unreachable-other-origin");
    }

    private string Twin(string path) => $"http://www.127.0.0.1:{_server.BaseUri.Port}{path}";

    private Task RespondAsync(HttpContext context)
    {
        var path = context.Request.Path.Value;
        if (context.Connection.LocalPort == _server.OtherBaseUri.Port)
        {
            return path switch
            {
                "/cross.xml" => Xml(context, Sitemap($"{_server.BaseUri}c", $"{_server.OtherBaseUri}d")),
                "/d" => Html(context, "<p>A partner page</p>"),
                _ => Status(context, 404)
            };
        }

        return path switch
        {
            "/robots.txt" => Text(context, $"User-agent: *\nAllow: /\nSitemap: {_server.BaseUri}sitemap.xml\nSitemap: {_server.OtherBaseUri}cross.xml"),
            "/sitemap.xml" => Xml(context, Sitemap($"{_server.BaseUri}a", Twin("/b"))),
            "/" => Html(context, "<a href=\"/a\">A</a> <a href=\"/c\">C</a>"),
            "/a" or "/c" => Html(context, "<p>A page</p>"),
            _ => Status(context, 404)
        };
    }

    private static string Sitemap(params string[] urls) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?><urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">" +
        string.Concat(urls.Select(url => $"<url><loc>{url}</loc></url>")) + "</urlset>";

    private static Task Html(HttpContext context, string body)
    {
        context.Response.ContentType = "text/html; charset=utf-8";
        return context.Response.WriteAsync($"<!doctype html><html lang=\"en\"><head><title>Test page</title></head><body>{body}</body></html>");
    }

    private static Task Xml(HttpContext context, string body)
    {
        context.Response.ContentType = "application/xml";
        return context.Response.WriteAsync(body);
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
