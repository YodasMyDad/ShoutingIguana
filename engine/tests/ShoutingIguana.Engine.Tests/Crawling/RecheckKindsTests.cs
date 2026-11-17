using Microsoft.Extensions.Logging.Abstractions;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Tests.Support;
using ShoutingIguana.TestSite;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.Engine.Tests.Crawling;

/// <summary>
/// The rechecks that read again what isn't a URL of the check's (stage E4 of the fix loop): every sitemap, the robots.txt
/// of the origins a check names, and the site's other versions, each on what revision 1 of the synthetic site mends.
/// </summary>
[Collection(RecheckCrawlsCollection.NAME)]
public sealed class RecheckKindsTests(RecheckCrawls crawls, RecheckKindsTests.HttpsCrawl https) : IClassFixture<RecheckKindsTests.HttpsCrawl>
{
    /// <summary>
    /// Revision 1 drops the missing file from the sitemap index: reading the sitemaps again finds every file there, and
    /// the file nothing lists any more goes, so the sitemap error is fixed. Nothing else is fetched.
    /// </summary>
    [Fact]
    public async Task ASitemapErrorIsRecheckedByReadingTheSitemapsAgain()
    {
        var path = crawls.Copy(crawls.Then);
        var missing = $"{crawls.Server.BaseUri}sitemaps/missing.xml";
        Assert.Equal(1, TestCrawls.Count(path, $"SELECT COUNT(*) FROM sitemaps WHERE url = '{missing}'"));

        var (run, recheck) = await RecheckCrawls.RecheckAsync(path, new RecheckAsk("sitemap-error"));

        Assert.Equal((CrawlStatus.Completed, RecheckStatus.Completed, 1, 1, 0), (run.Status, recheck.Status, recheck.Judged, recheck.Result!.Fixed, recheck.Result.Still));
        Assert.Equal(0, TestCrawls.Count(path, $"SELECT COUNT(*) FROM sitemaps WHERE url = '{missing}'"));
        Assert.Empty(IssueReader.ReadIssues(path, "sitemap-error", 10));
        Assert.Equal(0, recheck.Fetched);
    }

    /// <summary>
    /// Revision 1 lists the first category, and a new page, in its extra sitemap. Rechecking the categories not in a sitemap
    /// reads them and the sitemaps again: the first is fixed and the rest still aren't listed. The new page a sitemap now
    /// lists is recorded, as found by the recheck, and not fetched.
    /// </summary>
    [Fact]
    public async Task NotInSitemapIsRecheckedWithTheSitemapsReadAgain()
    {
        var path = crawls.Copy(crawls.Then);
        var before = (int)IssueReader.ReadCounts(path)["not-in-sitemap"];
        Assert.True(before >= SyntheticSite.CATEGORY_COUNT);

        var (_, recheck) = await RecheckCrawls.RecheckAsync(path, new RecheckAsk("not-in-sitemap"));

        Assert.Equal((before, 1, before - 1, 0), (recheck.Judged, recheck.Result!.Fixed, recheck.Result.Still, recheck.Result.NotChecked));
        var first = $"{crawls.Server.BaseUri}c/0";
        Assert.Equal(1, TestCrawls.Count(path, $"SELECT COUNT(*) FROM sitemap_urls su JOIN urls u ON u.id = su.url_id WHERE u.url = '{first}'"));
        Assert.Equal($"{(int)UrlState.Skipped} | {(int)SkipReason.Recheck} | 0", Assert.Single(TestCrawls.Rows(path, $"""
            SELECT u.state, u.skip_reason, (SELECT COUNT(*) FROM responses r WHERE r.url_id = u.id) FROM urls u
            WHERE u.url = '{crawls.Server.BaseUri}plants/title-long-new'
            """)));
    }

    /// <summary>Over HTTPS, revision 1's plain-HTTP side answers robots.txt: reading it again fixes the robots.txt another origin of the site couldn't serve.</summary>
    [Fact]
    public async Task RobotsTxtIsRecheckedByReadingItAgain()
    {
        var path = https.Copy();
        var plain = "http" + https.Server.BaseUri.GetLeftPart(UriPartial.Authority)["https".Length..];
        Assert.Contains(plain, RecheckPlanReader.Plan(path, new RecheckAsk("robots-txt-unreachable-other-origin"), new AnalysisSettings()).RobotsOrigins);

        var (_, recheck) = await RecheckCrawls.RecheckAsync(path, new RecheckAsk("robots-txt-unreachable-other-origin"));

        Assert.Equal((1, 1, 0), (recheck.Judged, recheck.Result!.Fixed, recheck.Result.Still));
        Assert.Equal(200, TestCrawls.Count(path, $"SELECT status_code FROM robots WHERE origin = '{plain}'"));
    }

    /// <summary>Over HTTPS, revision 1's plain-HTTP home page redirects to the HTTPS one: asking the site's other versions again fixes the duplicate.</summary>
    [Fact]
    public async Task ADuplicateSiteVersionIsRecheckedByAskingTheVersionsAgain()
    {
        var path = https.Copy();

        var (_, recheck) = await RecheckCrawls.RecheckAsync(path, new RecheckAsk("duplicate-site-version"));

        Assert.Equal((1, 1, 0), (recheck.Judged, recheck.Result!.Fixed, recheck.Result.Still));
        Assert.Empty(IssueReader.ReadIssues(path, "duplicate-site-version", 10));
        Assert.True(recheck.Fetched >= 1);
    }

    /// <summary>The HTTPS synthetic site crawled and analysed at revision 0, then switched to revision 1.</summary>
    public sealed class HttpsCrawl : IAsyncLifetime
    {
        private readonly CrawlFiles _files = new();
        private string _then = string.Empty;

        public SyntheticSiteServer Server { get; private set; } = null!;

        public async Task InitializeAsync()
        {
            Server = await SyntheticSiteServer.StartAsync(new SyntheticSiteOptions { PageCount = 120, Https = true });
            _then = _files.NewPath();
            var options = new CrawlOptions { StartUrl = Server.BaseUri.AbsoluteUri, Concurrency = 8, TrustedCertificate = Server.Certificate };
            Assert.Equal(CrawlStatus.Completed, (await TestCrawls.RunAsync(options, _then)).Status);
            await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(_then);
            Server.Revision = 1;
        }

        public string Copy()
        {
            var copy = _files.NewPath();
            File.Copy(_then, copy);
            return copy;
        }

        public async Task DisposeAsync()
        {
            await Server.DisposeAsync();
            _files.Dispose();
        }
    }
}
