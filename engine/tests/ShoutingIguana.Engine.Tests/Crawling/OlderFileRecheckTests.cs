using Microsoft.Extensions.Logging.Abstractions;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Tests.Support;

namespace ShoutingIguana.Engine.Tests.Crawling;

/// <summary>
/// A crawl file an earlier engine made, from the first file of its schema (<see cref="OlderCrawlFiles"/>): every kind of
/// recheck, and a continue, bring it up to this engine's crawl-time schema as they open it, and then run as on a file this
/// engine made. A real one of 17 November failed every robots.txt and sitemaps recheck part-way (it had no
/// <c>sitemap_urls.lastmod</c>).
/// </summary>
[Collection(RecheckCrawlsCollection.NAME)]
public sealed class OlderFileRecheckTests(RecheckCrawls crawls, RecheckKindsTests.HttpsCrawl https) : IClassFixture<RecheckKindsTests.HttpsCrawl>
{
    [Fact]
    public async Task AUrlsRecheckRunsOnAnOlderFile()
    {
        var path = await OlderCopyAsync(crawls.Copy(crawls.Then));

        var (run, recheck) = await RecheckCrawls.RecheckAsync(path, new RecheckAsk("title-too-long"));

        Assert.Equal((CrawlStatus.Completed, RecheckStatus.Completed), (run.Status, recheck.Status));
        Assert.Equal((1, 1, 1), (recheck.Result!.Fixed, recheck.Result.Still, recheck.Result.NotChecked));
        await AssertUpToDateAsync(path);

        // The recheck read no sitemap: the lastmod column it added is empty, not "no lastmod anywhere".
        AssertLastmodsNotRun(path);
    }

    [Fact]
    public async Task ASitemapsRecheckRunsOnAnOlderFile()
    {
        var path = await OlderCopyAsync(crawls.Copy(crawls.Then));

        var (run, recheck) = await RecheckCrawls.RecheckAsync(path, new RecheckAsk("sitemap-error"));

        Assert.Equal((CrawlStatus.Completed, RecheckStatus.Completed, 1, 1, 0), (run.Status, recheck.Status, recheck.Judged, recheck.Result!.Fixed, recheck.Result.Still));
        Assert.True(TestCrawls.Count(path, "SELECT COUNT(*) FROM sitemap_urls WHERE lastmod IS NOT NULL") > 0);
        await AssertUpToDateAsync(path);

        // It read every sitemap again, each entry with its lastmod: the check runs.
        Assert.DoesNotContain(IssueReader.ReadSkippedChecks(path), skip => skip.CheckId == "sitemap-lastmod-invalid");
    }

    [Fact]
    public async Task AUrlsAndSitemapsRecheckRunsOnAnOlderFile()
    {
        var path = await OlderCopyAsync(crawls.Copy(crawls.Then));
        var before = (int)IssueReader.ReadCounts(path)["not-in-sitemap"];

        var (run, recheck) = await RecheckCrawls.RecheckAsync(path, new RecheckAsk("not-in-sitemap"));

        Assert.Equal((CrawlStatus.Completed, before, 1, before - 1), (run.Status, recheck.Judged, recheck.Result!.Fixed, recheck.Result.Still));
        await AssertUpToDateAsync(path);
    }

    [Fact]
    public async Task ARobotsTxtRecheckRunsOnAnOlderFile()
    {
        var path = await OlderCopyAsync(https.Copy());

        var (run, recheck) = await RecheckCrawls.RecheckAsync(path, new RecheckAsk("robots-txt-unreachable-other-origin"));

        Assert.Equal((CrawlStatus.Completed, 1, 1, 0), (run.Status, recheck.Judged, recheck.Result!.Fixed, recheck.Result.Still));
        await AssertUpToDateAsync(path);
    }

    [Fact]
    public async Task ASiteVersionsRecheckRunsOnAnOlderFile()
    {
        var path = await OlderCopyAsync(https.Copy());

        var (run, recheck) = await RecheckCrawls.RecheckAsync(path, new RecheckAsk("duplicate-site-version"));

        Assert.Equal((CrawlStatus.Completed, 1, 1, 0), (run.Status, recheck.Judged, recheck.Result!.Fixed, recheck.Result.Still));
        await AssertUpToDateAsync(path);
    }

    /// <summary>The robots.txt and sitemaps recheck the real file failed, on a file that never had rechecks' tables either.</summary>
    [Fact]
    public async Task ARobotsTxtAndSitemapsRecheckRunsOnAnOlderFile()
    {
        var path = await OlderCopyAsync(crawls.Copy(crawls.Then));
        var plan = RecheckPlanReader.Plan(path, new RecheckAsk("sitemap-error"), new AnalysisSettings());

        var (run, recheck) = await RecheckCrawls.RecheckAsync(path, plan with { Kinds = [RecheckKind.RobotsTxt, RecheckKind.Sitemaps] });

        Assert.Equal((CrawlStatus.Completed, RecheckStatus.Completed, 1), (run.Status, recheck.Status, recheck.Result!.Fixed));
        await AssertUpToDateAsync(path);
    }

    /// <summary>A crawl an earlier engine stopped, continued by this one.</summary>
    [Fact]
    public async Task AContinueRunsOnAnOlderFile()
    {
        var path = await OlderCopyAsync(crawls.Copy(crawls.Then));

        var run = await TestCrawls.RunAsync(crawls.Options, path, resume: true);

        Assert.Equal(CrawlStatus.Completed, run.Status);
        await AssertUpToDateAsync(path);

        // Its sitemaps were read before: continuing read none again, so their entries' lastmods weren't collected.
        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path);
        AssertLastmodsNotRun(path);
    }

    /// <summary>The lastmod check wasn't run, for the reason an earlier version's crawl gives, rather than finding nothing.</summary>
    private static void AssertLastmodsNotRun(string path)
    {
        var skipped = Assert.Single(IssueReader.ReadSkippedChecks(path), skip => skip.CheckId == "sitemap-lastmod-invalid");
        Assert.Contains("earlier version", skipped.Text, StringComparison.Ordinal);
        // Said in the analysis's notes: on its own, or among the checks an earlier version left out.
        Assert.Contains(IssueReader.ReadNotes(path), note => note.Contains("earlier version", StringComparison.Ordinal) &&
                                                             (note.StartsWith("Sitemap lastmod invalid or in the future wasn't checked", StringComparison.Ordinal) ||
                                                              note.Contains("checks weren't run: this crawl was made by an earlier version", StringComparison.Ordinal)));
    }

    private static async Task<string> OlderCopyAsync(string path)
    {
        await OlderCrawlFiles.DowngradeAsync(path);
        Assert.Empty(OlderCrawlFiles.Columns(path).Intersect(await AddedSinceAsync()));
        return path;
    }

    /// <summary>The file has every crawl-time table and column a new crawl's file has.</summary>
    private static async Task AssertUpToDateAsync(string path) =>
        Assert.Empty((await OlderCrawlFiles.NewCrawlColumnsAsync()).Except(OlderCrawlFiles.Columns(path)));

    private static async Task<SortedSet<string>> AddedSinceAsync()
    {
        using var files = new CrawlFiles();
        var first = files.NewPath();
        OlderCrawlFiles.Create(first, OlderCrawlFiles.SCHEMA_2);
        var added = await OlderCrawlFiles.NewCrawlColumnsAsync();
        added.ExceptWith(OlderCrawlFiles.Columns(first));
        return added;
    }
}
