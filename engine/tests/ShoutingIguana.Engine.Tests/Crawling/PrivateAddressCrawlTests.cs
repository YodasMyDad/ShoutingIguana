using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Fetching;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Tests.Fetching;
using ShoutingIguana.Engine.Tests.Support;

namespace ShoutingIguana.Engine.Tests.Crawling;

/// <summary>
/// A public site that links to the user's own machine and network. The loopback server answers as the crawl's proxy for
/// example.com, a public name, and notes every request it is sent, whichever host it is for.
/// </summary>
public sealed class PrivateAddressCrawlTests : IAsyncLifetime
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"si-test-{Guid.NewGuid():N}.sicrawl");
    private readonly ConcurrentQueue<string> _asked = new();
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

    [Fact]
    public async Task APublicSitesLinksToPrivateAddressesAreNeitherAskedNorJudged()
    {
        var port = _server.BaseUri.Port;
        string[] privateUrls = [$"http://127.0.0.1:{port}/secret", $"http://localhost:{port}/admin", $"http://[::1]:{port}/v6", "http://10.255.255.1/router"];
        var options = new CrawlOptions { StartUrl = "http://example.com/", ProxyUrl = _server.BaseUri.AbsoluteUri, MaxRetries = 0, DiscoverSitemaps = false };

        await using (var job = new CrawlEngine(NullLoggerFactory.Instance).Start(new CrawlRequest(options, _path)))
        {
            Assert.Equal(CrawlStatus.Completed, (await job.Completion.WaitAsync(TimeSpan.FromMinutes(1))).Status);
        }

        var analysis = await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(_path);

        // The site was crawled; nothing on the private addresses was asked, not even robots.txt.
        Assert.Contains("example.com/public", _asked);
        Assert.DoesNotContain(_asked, request => !request.StartsWith("example.com", StringComparison.Ordinal) && !request.StartsWith("www.example.com", StringComparison.Ordinal));
        foreach (var url in privateUrls)
        {
            Assert.Equal([PrivateNetworkGuard.ERROR], TestCrawls.Rows(_path,
                $"SELECT r.error FROM responses r JOIN urls u ON u.id = r.url_id WHERE u.url = '{new Uri(url).AbsoluteUri}'"));
        }

        // Unknown, not broken: no check judges them, and the analysis says why. Linking to them at all is a fault of the
        // page's, which says so.
        var rows = IssueReader.ReadAll(_path).ToList();
        Assert.DoesNotContain(rows, row => row.Issue != IssueCatalog.LINKS_TO_LOCALHOST.Id &&
                                           privateUrls.Any(url => row.Url == new Uri(url).AbsoluteUri || row.RelatedUrl == new Uri(url).AbsoluteUri));
        Assert.Equal(("http://example.com/", $"4 links, such as {new Uri(privateUrls[0]).AbsoluteUri}"),
            rows.Where(row => row.Issue == IssueCatalog.LINKS_TO_LOCALHOST.Id).Select(row => (row.Url, row.Detail)).Single());
        Assert.Contains("4 URLs weren't checked because they are on private network addresses (such as localhost or 192.168.x.x), which a crawl of a public site doesn't ask, so links to them aren't judged.",
            analysis.Notes);
        Assert.DoesNotContain(analysis.Notes, note => note.Contains("stopped answering", StringComparison.Ordinal));
    }

    private Task RespondAsync(HttpContext context)
    {
        var host = context.Request.Host.Value ?? string.Empty;
        var path = context.Request.Path.Value ?? "/";
        _asked.Enqueue(host + path);
        if (host != "example.com")
        {
            return Html(context, "<p>Not the site</p>");
        }

        var port = _server.BaseUri.Port;
        return path switch
        {
            "/robots.txt" => Status(context, 404),
            "/" => Html(context, $"""
                <a href="/public">Public</a>
                <a href="http://127.0.0.1:{port}/secret">Router</a>
                <a href="http://localhost:{port}/admin">Admin</a>
                <a href="http://[::1]:{port}/v6">Loopback</a>
                <a href="http://10.255.255.1/router">Intranet</a>
                """),
            "/public" => Html(context, "<p>A page</p>"),
            _ => Status(context, 404)
        };
    }

    private static Task Html(HttpContext context, string body)
    {
        context.Response.ContentType = "text/html; charset=utf-8";
        return context.Response.WriteAsync($"<!doctype html><html lang=\"en\"><head><title>A page</title></head><body>{body}</body></html>");
    }

    private static Task Status(HttpContext context, int status)
    {
        context.Response.StatusCode = status;
        return Task.CompletedTask;
    }
}
