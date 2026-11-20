using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Export;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Tests.Support;

namespace ShoutingIguana.Engine.Tests.Analysis;

/// <summary>
/// A crawl file whose sitemap says it listed URLs and holds none of them: a recheck by the engine of 18 November deleted a
/// sitemap's entries, failed, and left its row with the count. Analysed as it stood, every page read as missing from the
/// sitemaps (18 became 257), two real findings went and the score rose. Such a file is now said for what it is: the checks
/// that compare pages with what the sitemaps list aren't run, each saying to crawl again; no page is placed in or out of a
/// sitemap; and the sitemap's row says its list is lost, with no counts.
/// </summary>
[Collection(RecheckCrawlsCollection.NAME)]
public sealed class LostSitemapListTests(RecheckCrawls crawls)
{
    /// <summary>The checks that read what the sitemaps list.</summary>
    private static readonly string[] LIST_CHECKS =
    [
        "not-in-sitemap", "orphan-page", "sitemap-non-indexable", "sitemap-url-other-host", "sitemap-url-repeated", "sitemap-lastmod-invalid", "canonical-unlinked",
        "hreflang-unlinked"
    ];

    [Fact]
    public async Task TheChecksOfWhatTheSitemapsListArentRunOnAFileThatLostAList()
    {
        var path = crawls.Copy(crawls.Then);
        var before = IssueReader.ReadCounts(path);
        Assert.True(before["not-in-sitemap"] > 0 && before["sitemap-non-indexable"] > 0 && before["orphan-page"] > 0);
        var lost = LoseTheLargestList(path);

        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path);

        var counts = IssueReader.ReadCounts(path);
        var skipped = IssueReader.ReadSkippedChecks(path).ToDictionary(skip => skip.CheckId);
        foreach (var check in LIST_CHECKS)
        {
            Assert.False(counts.ContainsKey(check), $"{check} reported {counts.GetValueOrDefault(check)}");
            Assert.Equal(SkippedCheckReasons.SITEMAP_LIST_LOST, skipped[check].Reason);
            Assert.Contains(lost.Url, skipped[check].Text, StringComparison.Ordinal);
            Assert.Contains("Crawl the site again", skipped[check].Text, StringComparison.Ordinal);
        }

        // What the files themselves say is still judged: the index's missing file is still an error.
        Assert.Equal(before["sitemap-error"], counts["sitemap-error"]);
        Assert.Single(IssueReader.ReadNotes(path), note => note.Contains(lost.Url, StringComparison.Ordinal) && note.Contains("Crawl the site again", StringComparison.Ordinal));

        // No URL is said to be in no sitemap: one a sitemap still lists is in it, the rest aren't known.
        Assert.Equal(0, TestCrawls.Count(path, "SELECT COUNT(*) FROM url_summary WHERE in_sitemap = 0"));
        Assert.True(TestCrawls.Count(path, "SELECT COUNT(*) FROM url_summary WHERE in_sitemap = 1") > 0);
    }

    [Fact]
    public void TheSitemapsListSaysTheListIsLostAndGivesNoCounts()
    {
        var path = crawls.Copy(crawls.Then);
        var lost = LoseTheLargestList(path);

        var list = SitemapListReader.Read(path);
        var sitemap = list.Sitemaps.Single(listed => listed.Url == lost.Url);
        var subtree = SitemapListWords.SubtreeOf(list.Sitemaps, sitemap.Id, list.Discovery);

        Assert.Equal(SitemapState.ListLost, sitemap.State);
        // The screen's own words for its status cell (sitemapWords.ts), so the exported file reads as the screen does.
        Assert.Equal($"Read, but its {lost.Entries:N0} URLs are lost", SitemapListWords.State(sitemap, crawling: false, subtree));
        Assert.Equal("Read, but its 1 URL is lost", SitemapListWords.State(sitemap with { Entries = 1 }, crawling: false, subtree));
        Assert.Null(SitemapListWords.Counts(sitemap, subtree));

        // The others are as they were, and the index over it no longer knows what it lists in all.
        Assert.Contains(list.Sitemaps, listed => listed is { State: SitemapState.Read, Urls.Listed: > 0 });
        var index = list.Sitemaps.Single(listed => listed.Id == sitemap.ParentId);
        Assert.Null(SitemapListWords.Counts(index, SitemapListWords.SubtreeOf(list.Sitemaps, index.Id, list.Discovery)));
    }

    /// <summary>A file with every list in place has no such sitemap, and its checks run.</summary>
    [Fact]
    public void AFileWithItsListsIsAsItWas()
    {
        Assert.DoesNotContain(SitemapListReader.Read(crawls.Then).Sitemaps, sitemap => sitemap.State == SitemapState.ListLost);
        Assert.DoesNotContain(IssueReader.ReadSkippedChecks(crawls.Then), skip => skip.Reason == SkippedCheckReasons.SITEMAP_LIST_LOST);
    }

    /// <summary>Deletes the entries of the sitemap that lists the most, as the failed recheck did, leaving its row's count.</summary>
    private static (string Url, int Entries) LoseTheLargestList(string path)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, url, url_count FROM sitemaps ORDER BY url_count DESC LIMIT 1";
        int id;
        (string Url, int Entries) lost;
        using (var reader = command.ExecuteReader())
        {
            Assert.True(reader.Read());
            (id, lost) = (reader.GetInt32(0), (reader.GetString(1), reader.GetInt32(2)));
        }

        Assert.True(lost.Entries > 0);
        command.CommandText = $"DELETE FROM sitemap_urls WHERE sitemap_id = {id}";
        Assert.True(command.ExecuteNonQuery() > 0);
        return lost;
    }
}
