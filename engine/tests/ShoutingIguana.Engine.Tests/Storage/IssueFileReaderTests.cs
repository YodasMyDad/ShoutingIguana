using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Tests.Support;
using static ShoutingIguana.Engine.Tests.Support.GridQueries;

namespace ShoutingIguana.Engine.Tests.Storage;

/// <summary>
/// The files the checks of the site's robots.txt and sitemaps are about, as the analysis stores them (<c>issue_files</c>),
/// so the views can name the file instead of the home page the issue is reported on (step 3b's "can I fix it from this?").
/// </summary>
[Collection(GridCrawlCollection.NAME)]
public sealed class IssueFileReaderTests(GridCrawl crawl)
{
    [Fact]
    public void EachFileCheckThatFoundSomethingNamesItsFilesWithTheSitemapsIds()
    {
        var read = IssueFileReader.Read(crawl.Path)!;
        Assert.Equal(IssueReader.ReadAnalysis(crawl.Path)!.AnalyzedAt, read.AnalyzedAt);
        var site = crawl.Server.BaseUri.GetLeftPart(UriPartial.Authority);

        // The synthetic site's sitemap faults: one that isn't there, an empty one, and dates in the future.
        Assert.Equal([$"{site}/sitemaps/missing.xml"], read.Checks["sitemap-error"].Select(file => file.Url));
        Assert.Equal([$"{site}/sitemaps/empty.xml"], read.Checks["sitemap-empty"].Select(file => file.Url));
        Assert.Equal([$"{site}/sitemaps/pages-0.xml"], read.Checks["sitemap-lastmod-invalid"].Select(file => file.Url));

        // Each sitemap with the id the sitemaps list gives it, so a view can open it there.
        var sitemaps = SitemapListReader.Read(crawl.Path).Sitemaps.ToDictionary(sitemap => sitemap.Url, sitemap => (long)sitemap.Id, StringComparer.Ordinal);
        Assert.All(read.Checks.Values.SelectMany(files => files), file => Assert.Equal(sitemaps[file.Url], file.SitemapId));

        // Only checks of the site's files, and only those that found something.
        Assert.All(read.Checks.Keys, check => Assert.NotNull(IssueCatalog.Find(check)!.About));
        var flagged = crawl.Reference.Issues.SelectMany(entry => entry.Value).Select(issue => issue.Issue).ToHashSet(StringComparer.Ordinal);
        Assert.All(read.Checks.Keys, check => Assert.Contains(check, flagged));
    }

    [Fact]
    public void AnAnalysisFromBeforeFilesWereStoredNamesNone()
    {
        var copy = crawl.Copy();
        Change(copy, $"UPDATE analysis SET analysis_version = {CrawlSchema.ISSUE_FILES_SINCE - 1}");
        Assert.Empty(IssueFileReader.Read(copy)!.Checks);

        // An engine from before the table: none either, rather than an error.
        var older = crawl.Copy();
        Change(older, "DROP TABLE issue_files");
        Assert.Empty(IssueFileReader.Read(older)!.Checks);
    }
}
