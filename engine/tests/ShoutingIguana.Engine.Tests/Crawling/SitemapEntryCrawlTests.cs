using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Tests.Support;
using ShoutingIguana.TestSite;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.Engine.Tests.Crawling;

/// <summary>
/// What step 3b's sitemap checks need from the crawl (batch 4 of docs/v2-new-checks.md), against the synthetic site: each
/// entry's lastmod and each file's size stored, robots.txt without a Sitemap: line, and a crawl from before lastmods.
/// </summary>
public sealed class SitemapEntryCrawlTests : IAsyncLifetime
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"si-test-{Guid.NewGuid():N}.sicrawl");

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        foreach (var path in new[] { _path, _path + "-wal", _path + "-shm" })
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task TheCrawlKeepsEachEntrysLastmodAndEachFilesSize()
    {
        await using var server = await SyntheticSiteServer.StartAsync(new SyntheticSiteOptions { PageCount = 40 });
        await TestCrawls.RunAsync(new CrawlOptions { StartUrl = server.BaseUri.AbsoluteUri, Concurrency = 8 }, _path);

        // The thin plant, in pages-0.xml with a lastmod in the year 3000 and in the extra sitemap without one.
        Assert.Equal(["/sitemaps/extra.xml | null", "/sitemaps/pages-0.xml | 32503680000"], TestCrawls.Rows(_path, """
            SELECT substr(s.url, instr(s.url, '/sitemaps/')) || ' | ' || coalesce(su.lastmod, 'null')
            FROM sitemap_urls su JOIN sitemaps s ON s.id = su.sitemap_id JOIN urls u ON u.id = su.url_id
            WHERE u.url LIKE '%/plants/thin' ORDER BY s.url
            """));

        // Each file's size as read: the empty one is a bare urlset; one that wasn't there has none.
        var empty = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\"></urlset>";
        Assert.Equal([$"0 | 0 | {empty.Length}"], TestCrawls.Rows(_path, "SELECT url_count || ' | ' || listed_sitemaps || ' | ' || size_bytes FROM sitemaps WHERE url LIKE '%/empty.xml'"));
        Assert.Equal(["null"], TestCrawls.Rows(_path, "SELECT coalesce(size_bytes, 'null') FROM sitemaps WHERE url LIKE '%/missing.xml'"));
        Assert.True(TestCrawls.Count(_path, "SELECT size_bytes FROM sitemaps WHERE url LIKE '%/pages-0.xml'") > 1_000);

        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(_path);
        Assert.Equal(["sitemap-empty", "sitemap-lastmod-invalid"],
            TestCrawls.Rows(_path, "SELECT i.issue FROM issues i JOIN urls u ON u.id = i.url_id WHERE i.issue IN ('sitemap-empty', 'sitemap-lastmod-invalid') ORDER BY i.issue"));
        // The first wrong entry and what it says, and the sitemaps listing a URL twice (the fix-it pass).
        var site = server.BaseUri.AbsoluteUri;
        Assert.Equal([$"1 entry: 1 in the future ({site}sitemaps/pages-0.xml): {site}plants/thin, lastmod 3000-01-01"],
            TestCrawls.Rows(_path, "SELECT detail FROM issues WHERE issue = 'sitemap-lastmod-invalid'"));
        Assert.Equal([$"In 2 sitemaps: {site}sitemaps/pages-0.xml and {site}sitemaps/extra.xml"],
            TestCrawls.Rows(_path, "SELECT detail FROM issues WHERE issue = 'sitemap-url-repeated'"));
        Assert.Equal(0, TestCrawls.Count(_path, "SELECT COUNT(*) FROM issues WHERE issue = 'robots-txt-no-sitemap'"));
    }

    [Fact]
    public async Task RobotsTxtThatDoesntNameTheSitemapTheCrawlFoundAnywayIsFlagged()
    {
        await using var server = await SyntheticSiteServer.StartAsync(new SyntheticSiteOptions { PageCount = 40, SitemapInRobots = false });
        await TestCrawls.RunAsync(new CrawlOptions { StartUrl = server.BaseUri.AbsoluteUri, Concurrency = 8 }, _path);
        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(_path);

        var origin = server.BaseUri.GetLeftPart(UriPartial.Authority);
        Assert.Equal([$"{origin}/ | {origin}/robots.txt has no Sitemap: line; the sitemap was found at {origin}/sitemap.xml"],
            TestCrawls.Rows(_path, "SELECT u.url || ' | ' || i.detail FROM issues i JOIN urls u ON u.id = i.url_id WHERE i.issue = 'robots-txt-no-sitemap'"));
    }

    [Fact]
    public async Task ACrawlFromBeforeLastmodsSkipsTheLastmodCheck()
    {
        await using var server = await SyntheticSiteServer.StartAsync(new SyntheticSiteOptions { PageCount = 40 });
        await TestCrawls.RunAsync(new CrawlOptions { StartUrl = server.BaseUri.AbsoluteUri, Concurrency = 8 }, _path);
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _path, Pooling = false }.ToString()))
        {
            connection.Open();
            using var drop = connection.CreateCommand();
            drop.CommandText = "ALTER TABLE sitemap_urls DROP COLUMN lastmod";
            drop.ExecuteNonQuery();
        }

        var analysis = await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(_path);

        Assert.Equal(SkippedCheckReasons.EARLIER_VERSION, analysis.Skipped.Single(skip => skip.CheckId == IssueCatalog.SITEMAP_LASTMOD_INVALID.Id).Reason);
        Assert.Contains("Sitemap lastmod invalid or in the future wasn't checked: this crawl was made by an earlier version, which didn't record what the check needs. Crawl again to check it.",
            analysis.Notes);
        Assert.Equal(0, TestCrawls.Count(_path, "SELECT COUNT(*) FROM issues WHERE issue = 'sitemap-lastmod-invalid'"));
        Assert.Equal(1, TestCrawls.Count(_path, "SELECT COUNT(*) FROM issues WHERE issue = 'sitemap-url-repeated'"));
    }
}
