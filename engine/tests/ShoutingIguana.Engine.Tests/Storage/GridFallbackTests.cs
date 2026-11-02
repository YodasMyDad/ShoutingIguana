using Microsoft.Extensions.Logging.Abstractions;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Tests.Support;
using ShoutingIguana.TestSite;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.Engine.Tests.Storage;

/// <summary>
/// The grids while there's no URL summary that describes the crawl: during a crawl, read live from its own tables, with
/// nothing counted kept; after a stopped crawl is analysed and then continued, from its own tables again, until it's
/// analysed again. Either way the columns that need the summary say so, and sorting or filtering on one is refused.
/// </summary>
public sealed class GridFallbackTests : IDisposable
{
    private const string HTML = "text/html; charset=utf-8";

    private readonly CrawlFiles _files = new();

    [Fact]
    public async Task DuringACrawlTheGridsReadItLive()
    {
        await using var site = await TestWebHost.StartAsync((_, _) => (path, _) => path switch
        {
            "/" => Page("Home", """<a href="/a">A</a> <a href="/b">B</a> <a href="/held">Held</a>"""),
            "/a" => Page("A", """<a href="/c">C</a>"""),
            "/b" or "/c" or "/held" => Page(path[1..].ToUpperInvariant(), "<p>A page.</p>"),
            _ => new SyntheticResponse(404, HTML, "<p>Not found</p>", null, null)
        });
        site.Hold("/held");
        var path = _files.NewPath();
        await using var job = TestCrawls.Start(new CrawlOptions { StartUrl = site.BaseUri.AbsoluteUri, Concurrency = 2 }, path);
        await TestCrawls.WaitUntilAsync(path, "SELECT COUNT(*) FROM responses r JOIN urls u ON u.id = r.url_id WHERE u.url LIKE '%/c'");

        // The crawl is waiting on /held: the grid reads what it has so far, and keeps no counts, which change as it goes.
        var cache = new ReadCache();
        var page = UrlGridReader.Query(path, new UrlQuery { Columns = ["url", "statusCode", "title", "inlinks"], Sort = [new UrlSort("url")] }, cache);
        Assert.Equal(new SummaryState(false, SummaryUnavailableReason.Crawling), page.Summary);
        Assert.Equal(["id", "url", "statusCode", "title"], page.Columns);
        Assert.Equal(["inlinks"], page.Unavailable);
        Assert.Contains(page.Rows, row => (string?)row.Values[1] == site.BaseUri + "c" && row.Values[2] is 200L && (string?)row.Values[3] == "C");
        var held = Assert.Single(page.Rows, row => (string?)row.Values[1] == site.BaseUri + "held");
        Assert.Null(held.Values[2]);
        Assert.Equal(page.Rows.Count, page.Total);
        Assert.InRange(page.Total, 5, TestCrawls.Count(path, "SELECT COUNT(*) FROM urls"));
        Assert.Equal(0, cache.Count);

        var refused = Assert.Throws<NeedsAnalysisException>(() => UrlGridReader.Query(path, new UrlQuery { Sort = [new UrlSort("inlinks", true)] }));
        Assert.Equal(SummaryUnavailableReason.Crawling, refused.Reason);
        Assert.Equal(["inlinks"], refused.Columns);

        var facets = UrlGridReader.Facets(path, new UrlFacetQuery(), cache);
        Assert.Equal(["inSitemap", "severity"], facets.Unavailable);
        Assert.Contains(facets.Facets["statusCode"], bucket => bucket.Value is null);
        Assert.Equal(0, cache.Count);

        var home = page.Rows.Single(row => (string?)row.Values[1] == site.BaseUri.AbsoluteUri).Id;
        var inspected = UrlInspectorReader.Read(path, home)!;
        Assert.Equal(IndexabilityJudge.Crawl, inspected.Indexability.JudgedBy);
        Assert.True(inspected.Indexability.Indexable);
        Assert.Null(inspected.Links);
        Assert.Null(inspected.Issues);
        Assert.Equal(3, UrlInspectorReader.ReadLinks(path, home, LinkDirection.Outlinks, new LinkQuery { Kinds = [LinkKind.Hyperlink] })!.Total);

        site.Release("/held");
        Assert.Equal(CrawlStatus.Completed, (await job.Completion.WaitAsync(TestCrawls.TIMEOUT)).Status);
        Assert.Equal(SummaryUnavailableReason.NotAnalyzed, UrlGridReader.Query(path, new UrlQuery()).Summary.Reason);
        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path);
        var analysed = UrlGridReader.Query(path, new UrlQuery { Columns = ["url", "inlinks"], Sort = [new UrlSort("inlinks", true)] });
        Assert.Equal(SummaryState.Present, analysed.Summary);
        Assert.Empty(analysed.Unavailable);
    }

    /// <summary>A stopped crawl analysed, then continued: its summary describes what it was, so until it's analysed again, none.</summary>
    [Fact]
    public async Task AfterAResumeTheGridsReadTheCrawlUntilItsAnalysedAgain()
    {
        await using var site = await SyntheticSiteServer.StartAsync(new SyntheticSiteOptions { PageCount = 60 });
        var path = _files.NewPath();
        await using (var job = TestCrawls.Start(new CrawlOptions { StartUrl = site.BaseUri.AbsoluteUri, Concurrency = 2, MaxRequestsPerSecond = 50 }, path))
        {
            var deadline = DateTime.UtcNow + TestCrawls.TIMEOUT;
            while (job.Progress.Fetched < 20 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(5);
            }

            Assert.Equal(CrawlStatus.Stopped, (await job.StopAsync()).Status);
        }

        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path);
        var stopped = UrlGridReader.Query(path, new UrlQuery { Columns = ["url", "statusCode", "inlinks"], Sort = [new UrlSort("inlinks", true)], Limit = 1_000 });
        Assert.Equal(SummaryState.Present, stopped.Summary);

        Assert.Equal(CrawlStatus.Completed, (await TestCrawls.RunAsync(CrawlDatabase.ReadOptions(path) with { MaxRequestsPerSecond = 0 }, path, resume: true)).Status);
        var cache = new ReadCache();
        var resumed = UrlGridReader.Query(path, new UrlQuery { Columns = ["url", "statusCode", "inlinks", "issues"], Limit = 1_000 }, cache);
        Assert.Equal(new SummaryState(false, SummaryUnavailableReason.CrawlChanged), resumed.Summary);
        Assert.Equal(["inlinks", "issues"], resumed.Unavailable);
        Assert.Equal(TestCrawls.Count(path, "SELECT COUNT(*) FROM urls"), resumed.Total);
        Assert.True(resumed.Rows.Count(row => row.Values[2] is not null) > stopped.Rows.Count(row => row.Values[2] is not null));
        Assert.Equal(TestCrawls.Count(path, "SELECT COUNT(*) FROM responses WHERE status_code = 200"), resumed.Rows.Count(row => row.Values[2] is 200L));
        Assert.True(cache.Count > 0);

        var refused = Assert.Throws<NeedsAnalysisException>(() => UrlGridReader.Query(path, new UrlQuery { Filters = [new UrlFilter("inSitemap", FilterOperator.Is) { Value = FilterValue.Of(true) }] }));
        Assert.Equal(SummaryUnavailableReason.CrawlChanged, refused.Reason);

        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path);
        var analysed = UrlGridReader.Query(path, new UrlQuery { Columns = ["url", "inlinks"] }, cache);
        Assert.Equal(SummaryState.Present, analysed.Summary);
        Assert.Equal(resumed.Total, analysed.Total);
    }

    public void Dispose() => _files.Dispose();

    private static SyntheticResponse Page(string title, string body) => new(200, HTML,
        $"""<!DOCTYPE html><html lang="en"><head><title>{title}</title></head><body><h1>{title}</h1>{body}</body></html>""", null, null);
}
