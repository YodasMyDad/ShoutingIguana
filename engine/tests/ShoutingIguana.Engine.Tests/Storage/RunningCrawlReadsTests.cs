using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Tests.Support;
using ShoutingIguana.TestSite;
using ShoutingIguana.TestSite.Models;
using static ShoutingIguana.Engine.Tests.Support.GridQueries;

namespace ShoutingIguana.Engine.Tests.Storage;

/// <summary>
/// The links grid, robots.txt and the sitemaps read through a cache while a crawl runs and grows, as the sidecar reads
/// them. What a read counts is kept briefly, and each read's snapshot is later than the last, so an answer could mix one
/// moment's count with another's rows: here each answer is of one moment (its count, total, rows, facets and cursor agree
/// with each other), whatever the crawl stored between the reads that counted for it, and no read fails.
/// </summary>
[Collection(GridCrawlCollection.NAME)]
public sealed class RunningCrawlReadsTests(GridCrawl crawl) : IDisposable
{
    private static readonly TimeSpan PART = TimeSpan.FromMilliseconds(1_200);

    /// <summary>Until what's kept of a running crawl has aged out, from <see cref="PART"/> after it was read.</summary>
    private static readonly TimeSpan REST = ReadCache.LIVE_FOR - PART + TimeSpan.FromMilliseconds(200);

    private const int READERS = 4;
    private const int LIMIT = 50;

    /// <summary>A limit on the write-ahead log that a crawl of a few hundred pages passes after every few it stores.</summary>
    private const long FOLDED_OFTEN_BYTES = 64 * 1024;

    /// <summary>
    /// A limit a crawl of a few thousand pages passes many times over, and the most its log may reach with it: the limit
    /// and the batch that takes it past (a batch of 2,000 commands of a crawl this fast writes up to 7 MB of log), doubled.
    /// </summary>
    private const long SMALL_LOG_BYTES = 2 * 1024 * 1024;
    private const long LARGEST_SMALL_LOG_BYTES = 16 * 1024 * 1024;

    /// <summary>
    /// How long a reader in relay holds its read at least, and at most: for longer than a fold waits by itself for a
    /// reader it can't see (a quarter of a second), so that the log is only ever folded because new reads are held while
    /// these end; and, waiting for the other to begin a read, for far longer than that takes unless new reads are held.
    /// </summary>
    private static readonly TimeSpan RELAY_LEAST = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan RELAY_HOLD = TimeSpan.FromMilliseconds(700);

    private static readonly string[] SHOWN = ["kind", "to.id"];

    /// <summary>Filter sets of every kind the grid reads its own way: on the link, on either end, on text, and a search.</summary>
    private static readonly (string Name, UrlFilter[] Filters, string? Search)[] SETS =
    [
        ("every link", [], null),
        ("hyperlinks", [Names("kind", FilterOperator.In, "hyperlink")], null),
        ("in the navigation", [Names("position", FilterOperator.In, "navigation")], null),
        ("followed", [Boolean("followed", true)], null),
        ("text contains", [Text("text", FilterOperator.Contains, "product")], null),
        ("to other sites", [Boolean("to.isInternal", false)], null),
        ("to working URLs", [Names("to.statusClass", FilterOperator.In, "2xx")], null),
        ("to URLs not fetched", [Boolean("to.fetched", false)], null),
        ("to URLs like", [Text("to.url", FilterOperator.Contains, "/p/1")], null),
        ("from depth 1", [Number("from.depth", FilterOperator.Eq, 1)], null),
        ("search", [], "about")
    ];

    private static readonly UrlSort[] TARGET_SORTS = [new("to.url"), new("to.statusCode", true), new("to.depth")];

    /// <summary>
    /// The filter sets that go by a link alone, each as SQL over the links: what a link is never changes once it's
    /// stored, so an answer given while the crawl ran can be held to the finished file afterwards.
    /// </summary>
    private static readonly Dictionary<string, string> TRUTHS = new(StringComparer.Ordinal)
    {
        ["every link"] = "1",
        ["hyperlinks"] = $"kind = {(int)LinkKind.Hyperlink}",
        ["in the navigation"] = $"position = {(int)LinkPosition.Navigation}",
        ["text contains"] = "instr(lower(COALESCE(text, '')), 'product') > 0"
    };

    private readonly CrawlFiles _files = new();

    public void Dispose() => _files.Dispose();

    /// <summary>A copy of the crawl that reads as running: its row says so, and someone holds its lock.</summary>
    private (string Path, IDisposable Lock) Running()
    {
        var running = crawl.Copy();
        Change(running, $"UPDATE crawl SET status = {(int)CrawlStatus.Running}, updated_at = updated_at + 1");
        return (running, CrawlFileLock.Acquire(running));
    }

    /// <summary>
    /// The facets' facts of each URL and a filter set's tally were kept under clocks of their own. A tally counted after
    /// the crawl found more URLs had targets the facts kept a moment earlier didn't reach, and the facets failed.
    /// </summary>
    [Fact]
    public void TheFacetsOfARunningCrawlSurviveItsGrowth()
    {
        var (running, held) = Running();
        using (held)
        {
            var cache = new ReadCache();

            // Now: a page counts the crawl's URLs.
            Assert.Equal(SummaryUnavailableReason.Crawling, LinkGridReader.Query(running, new UrlQuery { Filters = [Boolean("to.isInternal", false)] }, cache).Summary.Reason);
            Thread.Sleep(PART);

            // A moment later: the facets keep each URL's facts, as many as were counted then.
            LinkGridReader.Facets(running, new UrlFacetQuery(), cache);

            // The crawl goes on: more URLs, and links to them.
            var before = TestCrawls.Count(running, "SELECT MAX(id) FROM urls");
            Change(running, """
                INSERT INTO urls (url, is_internal, fetch_mode, depth, state, skip_reason, source)
                SELECT 'https://review.example/new/' || id, 0, 0, 1, 0, 0, 1 FROM urls ORDER BY id LIMIT 200
                """);
            Change(running, "INSERT INTO links (from_id, to_id, kind, flags, position, text, fragment) SELECT 1, id, 0, 0, 0, 'new', NULL FROM urls WHERE id > $before", ("$before", before));
            Thread.Sleep(REST);

            // The URLs are counted again, and the facts with them.
            var facets = LinkGridReader.Facets(running, new UrlFacetQuery { Filters = [Names("kind", FilterOperator.In, "hyperlink")] }, cache);
            Assert.Equal(facets.Matching, facets.Facets["to.isInternal"].Sum(bucket => bucket.Count));
        }
    }

    /// <summary>
    /// The number of links and their highest id were kept under clocks of their own too. Once the count was the newer of
    /// the two, the grid looked for the newest links past the end of the ids it knew: a page of no rows, with every link
    /// matching.
    /// </summary>
    [Fact]
    public void TheNewestLinksOfARunningCrawlAreAPageOfRows()
    {
        var (running, held) = Running();
        using (held)
        {
            var cache = new ReadCache();
            UrlQuery newest = new() { Sort = [new UrlSort("id", true)], Limit = LIMIT };

            // Now: the facets count the links.
            LinkGridReader.Facets(running, new UrlFacetQuery(), cache);
            Thread.Sleep(PART);

            // A moment later: a page asks for their highest id.
            Assert.Equal(LIMIT, LinkGridReader.Query(running, newest, cache).Rows.Count);

            // The crawl goes on: 500 more links.
            Change(running, "INSERT INTO links (from_id, to_id, kind, flags, position, text, fragment) SELECT from_id, to_id, kind, flags, position, text, fragment FROM links ORDER BY rowid LIMIT 500");
            Thread.Sleep(REST);

            // The links are counted again, with their highest id.
            var page = LinkGridReader.Query(running, newest, cache);
            Assert.Equal(TestCrawls.Count(running, "SELECT COUNT(*) FROM links"), page.Matching);
            Assert.Equal(LIMIT, page.Rows.Count);
            Assert.Equal(TestCrawls.Count(running, "SELECT MAX(rowid) FROM links"), page.Rows[0].Id);
        }
    }

    /// <summary>
    /// Within the moment a running crawl's reads answer for, the crawl's growth isn't in any answer: a read that counts
    /// something new for the moment (another filter set, the facets, a sort) counts the links the moment had, though its
    /// own snapshot has more, so it agrees with what the reads before it said of the same moment.
    /// </summary>
    [Fact]
    public void WhatALaterReadCountsIsOfTheMomentTheEarlierReadsAnsweredFor()
    {
        UrlFilter[] hyperlinks = [Names("kind", FilterOperator.In, "hyperlink")];
        UrlQuery byTarget = new() { Filters = hyperlinks, Sort = [new UrlSort("to.url", true)], Columns = ["to.url"], Limit = LIMIT };
        const string LATE = "https://review.example/late";
        for (var attempt = 0; ; attempt++)
        {
            var (running, held) = Running();
            using (held)
            {
                var cache = new ReadCache();
                var links = TestCrawls.Count(running, "SELECT COUNT(*) FROM links");
                var expected = TestCrawls.Count(running, "SELECT COUNT(*) FROM links WHERE kind = 0");
                var moment = Stopwatch.StartNew();
                var first = LinkGridReader.Query(running, new UrlQuery { Limit = LIMIT }, cache);

                // The crawl goes on: every link is stored again as a hyperlink, and one more to a new URL.
                Change(running, $"INSERT INTO urls (url, is_internal, fetch_mode, depth, state, skip_reason, source) VALUES ('{LATE}', 0, 0, 1, 0, 0, 1)");
                Change(running, """
                    INSERT INTO links (from_id, to_id, kind, flags, position, text, fragment) SELECT from_id, to_id, 0, 0, 0, 'late', NULL FROM links ORDER BY rowid;
                    INSERT INTO links (from_id, to_id, kind, flags, position, text, fragment) VALUES (1, (SELECT MAX(id) FROM urls), 0, 0, 0, 'late', NULL)
                    """);

                // Read at once: a filter set, the facets and a sort the moment hasn't counted yet.
                var filtered = LinkGridReader.Query(running, new UrlQuery { Filters = hyperlinks, Sort = [new UrlSort("id", true)], Limit = LIMIT }, cache);
                var facets = LinkGridReader.Facets(running, new UrlFacetQuery(), cache);
                var sorted = LinkGridReader.Query(running, byTarget, cache);
                var walked = Walk(running, byTarget with { Limit = 1_000 }, cache);

                // And a page that starts at the last link of the target with the most: read from the far end of its links.
                var mostLinked = TestCrawls.Rows(running, $"SELECT rowid FROM links WHERE kind = 0 AND rowid <= {links} AND to_id = (SELECT to_id FROM links WHERE kind = 0 AND rowid <= {links} GROUP BY 1 ORDER BY COUNT(*) DESC LIMIT 1)")
                    .Select(long.Parse).Order().ToList();
                var fromTheEnd = LinkGridReader.Query(running, byTarget with { Offset = Math.Max(0, walked.IndexOf(mostLinked[0])) + mostLinked.Count - 1, Limit = 1 }, cache);
                if (moment.Elapsed > ReadCache.LIVE_FOR - TimeSpan.FromMilliseconds(300))
                {
                    // Too slow a machine just now: the reads may have been of two moments. Again, from the start.
                    Assert.True(attempt < 5, "The reads never fitted in one moment.");
                    continue;
                }

                // Everything counted is of the links the first read counted: none is a link stored since, to the new URL
                // or to any other.
                Assert.Equal((links, links), (first.Matching, first.Total));
                Assert.Equal((expected, links, LIMIT), (filtered.Matching, filtered.Total, filtered.Rows.Count));
                Assert.All(filtered.Rows, row => Assert.InRange(row.Id, 1, links));
                Assert.Equal((links, links), (facets.Matching, facets.Total));
                Assert.All(LinkGridReader.FACETS, facet => Assert.Equal(links, facets.Facets[facet].Sum(bucket => bucket.Count)));
                Assert.Equal((expected, LIMIT), (sorted.Matching, sorted.Rows.Count));
                Assert.DoesNotContain(sorted.Rows, row => (string?)row.Values[1] == LATE);
                Assert.Equal(expected, walked.Count);
                Assert.All(walked, id => Assert.InRange(id, 1, links));
                Assert.InRange(mostLinked.Count, 100, links);
                Assert.Equal(mostLinked[^1], Assert.Single(fromTheEnd.Rows).Id);

                // Once the moment has passed, the crawl as it now is: the new URL sorts first, with its link.
                Thread.Sleep(ReadCache.LIVE_FOR + TimeSpan.FromMilliseconds(200));
                var later = LinkGridReader.Query(running, byTarget, cache);
                Assert.Equal((expected + links + 1, 2 * links + 1), (later.Matching, later.Total));
                Assert.Equal((LATE, 2 * links + 1), (later.Rows[0].Values[1], later.Rows[0].Id));
                Assert.Equal(2 * links + 1, LinkGridReader.Facets(running, new UrlFacetQuery(), cache).Facets["kind"].Sum(bucket => bucket.Count));
                return;
            }
        }
    }

    /// <summary>
    /// The read that takes a moment may not get as far as counting anything (here its filter needs the analysis, and is
    /// refused): the moment is kept all the same, with the links it had. The total the next read gives is the moment's,
    /// not the links of its own later snapshot counted because nobody had yet: every link still matches no filter.
    /// </summary>
    [Fact]
    public void TheTotalIsTheMomentsWhenTheReadThatTookItNeverAnswered()
    {
        var (running, held) = Running();
        using (held)
        {
            var cache = new ReadCache();
            var links = TestCrawls.Count(running, "SELECT COUNT(*) FROM links");

            // A read that takes the moment and is then refused: a running crawl has no URL summary to filter on.
            Assert.Throws<NeedsAnalysisException>(() => LinkGridReader.Query(running, new UrlQuery { Filters = [Number("to.inlinks", FilterOperator.Gt, 5)], Limit = LIMIT }, cache));

            // The crawl goes on: 500 more links.
            Change(running, "INSERT INTO links (from_id, to_id, kind, flags, position, text, fragment) SELECT from_id, to_id, kind, flags, position, text, fragment FROM links ORDER BY rowid LIMIT 500");
            var page = LinkGridReader.Query(running, new UrlQuery { Limit = LIMIT }, cache);
            Assert.Equal((links, links, LIMIT), (page.Matching, page.Total, page.Rows.Count));
            var filtered = LinkGridReader.Query(running, new UrlQuery { Filters = [Names("kind", FilterOperator.In, "hyperlink")], Limit = LIMIT }, cache);
            Assert.Equal(links, filtered.Total);
            Assert.Equal(links, LinkGridReader.Facets(running, new UrlFacetQuery(), cache).Total);
        }
    }

    /// <summary>
    /// A read keeps to its own snapshot. One that began before the read that took the moment can't see all of the moment's
    /// links: it answers for the crawl as its own snapshot has it, and the moment kept stays the later read's.
    /// </summary>
    [Fact]
    public void AReadThatBeganBeforeTheMomentAnswersForItsOwnSnapshot()
    {
        var (running, held) = Running();
        using (held)
        {
            // As a running crawl's file is: its readers and its writer don't wait for each other.
            Change(running, "PRAGMA journal_mode = WAL");
            var cache = new ReadCache();
            var links = TestCrawls.Count(running, "SELECT COUNT(*) FROM links");
            UrlQuery newest = new() { Sort = [new UrlSort("id", true)], Limit = LIMIT };
            var (older, newer) = GridReadContext.Read(running, cache, CancellationToken.None, context =>
            {
                // This read has its snapshot. The crawl goes on, and a read that begins now answers for it as it now is.
                Change(running, "INSERT INTO links (from_id, to_id, kind, flags, position, text, fragment) SELECT from_id, to_id, kind, flags, position, text, fragment FROM links ORDER BY rowid LIMIT 500");
                var later = LinkGridReader.Query(running, newest, cache);
                return (LinkGridReader.Query(context, newest), later);
            });
            Assert.Equal((links + 500, links + 500, LIMIT, links + 500), (newer.Matching, newer.Total, newer.Rows.Count, newer.Rows[0].Id));
            Assert.Equal((links, links, LIMIT, links), (older.Matching, older.Total, older.Rows.Count, older.Rows[0].Id));
            Assert.Equal(links + 500, LinkGridReader.Query(running, newest, cache).Total);
        }
    }

    /// <summary>
    /// A crawl only adds links, but for a page it fetches again, whose links it stores again. A read that finds links gone
    /// that its moment counted starts over on a moment of its own, and answers for the crawl as it now is: a full page, a
    /// count that's the rows there are, facets that add up to it, and each target's links as they now are. Each kind of
    /// read finds it for itself.
    /// </summary>
    [Theory]
    [InlineData("page")]
    [InlineData("facets")]
    [InlineData("sorted")]
    public void AReadThatFindsItsMomentsLinksGoneStartsOver(string read)
    {
        UrlQuery newest = new() { Sort = [new UrlSort("id", true)], Limit = LIMIT };
        UrlQuery byTarget = new() { Sort = [new UrlSort("to.url")], Columns = ["to.id"], Limit = 1_000 };
        var (running, held) = Running();
        using (held)
        {
            // The moment: the links counted; for the sorted read, their tally and the sort's order kept too. The sort's
            // first target is asked of another cache, so the facets count the moment's links for themselves.
            var cache = new ReadCache();
            var before = LinkGridReader.Query(running, newest, cache);
            var sortedBefore = LinkGridReader.Query(running, byTarget, read == "sorted" ? cache : new ReadCache());
            Assert.Equal((SummaryUnavailableReason.Crawling, 1_000), (before.Summary.Reason, sortedBefore.Rows.Count));

            // A page is fetched again: its links go, and are stored again after every other link. Among them are the newest
            // links there were; with them, every other link to the sort's first target.
            var page = TestCrawls.Count(running, "SELECT from_id FROM links ORDER BY rowid DESC LIMIT 1");
            var early = Convert.ToInt64(sortedBefore.Rows[0].Values[1], CultureInfo.InvariantCulture);
            var again = $"from_id = {page} OR (to_id = {early} AND rowid % 2 = 0)";
            Assert.InRange(TestCrawls.Count(running, $"SELECT COUNT(*) FROM links WHERE {again}"), 5, before.Total - 1);
            Change(running, $"""
                BEGIN;
                INSERT INTO links (from_id, to_id, kind, flags, position, text, fragment) SELECT from_id, to_id, kind, flags, position, text, fragment FROM links WHERE {again} ORDER BY rowid;
                DELETE FROM links WHERE ({again}) AND rowid <= {before.Total};
                COMMIT
                """);
            var (links, highest) = (TestCrawls.Count(running, "SELECT COUNT(*) FROM links"), TestCrawls.Count(running, "SELECT MAX(rowid) FROM links"));
            Assert.Equal(before.Total, links);
            Assert.True(highest > links);

            // At once, within the moment the reads before answered for.
            if (read == "page")
            {
                var after = LinkGridReader.Query(running, newest, cache);
                Assert.Equal((links, links, LIMIT), (after.Matching, after.Total, after.Rows.Count));
                Assert.Equal(TestCrawls.Rows(running, $"SELECT rowid FROM links ORDER BY rowid DESC LIMIT {LIMIT}").Select(long.Parse).OrderDescending(), after.Rows.Select(row => row.Id));
            }
            else if (read == "facets")
            {
                var facets = LinkGridReader.Facets(running, new UrlFacetQuery(), cache);
                Assert.Equal((links, links), (facets.Matching, facets.Total));
                Assert.All(LinkGridReader.FACETS, facet => Assert.Equal(links, facets.Facets[facet].Sum(bucket => bucket.Count)));
            }
            else
            {
                // Sorted by the target, each target's links are those it has now, in crawl order: the first target's every
                // link, those stored again among them.
                var sorted = LinkGridReader.Query(running, byTarget, cache);
                Assert.Equal((links, 1_000), (sorted.Matching, sorted.Rows.Count));
                var first = sorted.Rows.TakeWhile(row => Convert.ToInt64(row.Values[1], CultureInfo.InvariantCulture) == early).Select(row => row.Id).ToList();
                Assert.Equal(TestCrawls.Rows(running, $"SELECT rowid FROM links WHERE to_id = {early}").Select(long.Parse).Order().Take(1_000), first);
                Assert.Contains(first, id => id > before.Total);
            }
        }
    }

    /// <summary>
    /// An origin's answer and its blocked URLs were kept apart, the origins' list under one clock and each origin's file
    /// under another: the file's lines then accounted for URLs the origin didn't yet say it blocked. They're read and kept
    /// as one.
    /// </summary>
    [Fact]
    public void AnOriginsRobotsTxtIsOfOneMomentOfARunningCrawl()
    {
        var (running, held) = Running();
        using (held)
        {
            var cache = new ReadCache();
            var origin = RobotsReader.ReadOrigins(running, cache: cache).Origins[0];
            Assert.True(origin.Blocked > 0);

            // The crawl goes on: robots.txt keeps it from more of the origin's URLs.
            Change(running, $"UPDATE responses SET outcome = {(int)FetchOutcome.RobotsDisallowed}, error = NULL WHERE url_id IN (SELECT id FROM urls WHERE url LIKE $prefix || '/p/1%')", ("$prefix", origin.Origin));
            var blocked = TestCrawls.Count(running, $"SELECT COUNT(*) FROM responses r JOIN urls u ON u.id = r.url_id WHERE r.outcome = {(int)FetchOutcome.RobotsDisallowed} AND u.url LIKE '{origin.Origin}/%'");
            Assert.True(blocked > origin.Blocked);

            // The origin's file is read now, and says of the origin what it found now, whatever the list kept says.
            var file = RobotsReader.ReadFile(running, origin.Origin, cache)!;
            Assert.Equal(blocked, file.Origin.Blocked);
            Assert.Equal(file.Origin.Blocked, file.Lines.Sum(line => line.Blocked ?? 0) + file.Unmatched.Unreachable + file.Unmatched.Unknown);
            Assert.Equal((origin.State, origin.StatusCode, origin.IsInternal, origin.Bytes, origin.FetchedAt), (file.Origin.State, file.Origin.StatusCode, file.Origin.IsInternal, file.Origin.Bytes, file.Origin.FetchedAt));
            var urls = RobotsReader.ReadBlocked(running, origin.Origin, cache: cache)!;
            Assert.Equal((blocked, blocked), (urls.Total, urls.Matching));
        }
    }

    /// <summary>
    /// A sitemap's URL grid counted the URLs the sitemap lists twice, as its total and as the rows matching no filter, each
    /// kept under its own clock: while the crawl read the sitemap they could differ. They're one count.
    /// </summary>
    [Fact]
    public void ASitemapsUrlsAreCountedOnceWhileACrawlRuns()
    {
        var (running, held) = Running();
        using (held)
        {
            var cache = new ReadCache();
            var sitemap = (int)TestCrawls.Count(running, "SELECT sitemap_id FROM sitemap_urls GROUP BY 1 ORDER BY COUNT(*) DESC LIMIT 1");
            var listed = TestCrawls.Count(running, $"SELECT COUNT(*) FROM sitemap_urls WHERE sitemap_id = {sitemap}");

            // Now: a filtered page counts the sitemap's URLs for its total.
            var pages = UrlGridReader.QuerySitemap(running, sitemap, new UrlQuery { Filters = [Names("type", FilterOperator.In, "page")], Limit = 1 }, cache)!;
            Assert.Equal((SummaryUnavailableReason.Crawling, listed), (pages.Summary.Reason, pages.Total));
            Thread.Sleep(PART);

            // A moment later: a page with no filter counts its rows.
            Assert.Equal((listed, listed), Counts(UrlGridReader.QuerySitemap(running, sitemap, new UrlQuery { Limit = 1 }, cache)!));

            // The crawl reads on: the sitemap lists more URLs.
            Change(running, $"INSERT INTO sitemap_urls (sitemap_id, url_id) SELECT {sitemap}, id FROM urls WHERE id NOT IN (SELECT url_id FROM sitemap_urls WHERE sitemap_id = {sitemap}) LIMIT 7");
            Thread.Sleep(REST);

            // The total is counted again; the rows are still the count of a moment ago, and the total is that count.
            var unfiltered = UrlGridReader.QuerySitemap(running, sitemap, new UrlQuery { Limit = 1 }, cache)!;
            Assert.Equal(unfiltered.Matching, unfiltered.Total);
            Thread.Sleep(PART);
            Assert.Equal((listed + 7, listed + 7), Counts(UrlGridReader.QuerySitemap(running, sitemap, new UrlQuery { Limit = 1 }, cache)!));
        }

        static (long Matching, long Total) Counts(UrlPage page) => (page.Matching, page.Total);
    }

    /// <summary>
    /// Every kind of links read, the facets included, from several readers through one cache, over and over while a crawl
    /// of the synthetic site grows underneath; robots.txt and the sitemaps with them. No read fails, and each answer agrees
    /// with itself: the rows a page holds are the rows its count and offset say it holds, in the order asked, its cursor
    /// is there when more follow, its count is within the total, and each facet's counts add up to the count.
    /// </summary>
    [Fact]
    public async Task EveryReadOfACrawlAsItGrowsAgreesWithItself() => await ReadACrawlAsItGrowsAsync();

    /// <summary>
    /// The same, with the crawl's writer folding its write-ahead log into the file after every few pages it stores
    /// (it does once the log passes a size, which a crawl this small never reaches): for each fold the new reads wait
    /// while those part-way through the file end. Still no read fails and each answer agrees with itself, the reads
    /// still get through many of the crawl's moments, and the log was folded over and over, which only the writer does
    /// while the crawl runs: nothing else makes the log's file smaller.
    /// </summary>
    [Fact]
    public async Task EveryReadOfACrawlWhoseLogIsFoldedOverAndOverAgreesWithItself()
    {
        CrawlDatabase.LogLimitBytes = FOLDED_OFTEN_BYTES;
        var folds = await ReadACrawlAsItGrowsAsync();
        Assert.True(folds >= 50, $"The log was seen folded only {folds} times.");
    }

    /// <summary>
    /// The crawl's write-ahead log while the links grid is read all through the crawl, a read always part-way through
    /// the file, as the Links screen's are on a crawl of any size (each a pass over the links, one after another). SQLite
    /// starts a log again only when no read is part-way through it, so the log grew by everything the crawl wrote, to more
    /// than the file itself. The writer folds it in once it passes its limit: it stays within a few batches of that, the
    /// crawl stores what it would have, and the reads, which wait out each fold, all answer.
    /// </summary>
    [Fact]
    public async Task TheLogOfACrawlWhoseLinksAreReadAllTheWhileStaysSmall()
    {
        CrawlDatabase.LogLimitBytes = SMALL_LOG_BYTES;
        await using var site = await SyntheticSiteServer.StartAsync(new SyntheticSiteOptions { PageCount = 4_000 });
        var path = _files.NewPath();
        await using var job = TestCrawls.Start(new CrawlOptions { StartUrl = site.BaseUri.AbsoluteUri, Concurrency = 8 }, path);
        await TestCrawls.WaitUntilAsync(path, "SELECT COUNT(*) FROM links");

        // Two readers in relay: each holds its read until the other has begun one, so there's never a moment without one
        // unless something keeps new reads from beginning, as a fold does: then the read in flight ends by itself.
        var cache = new ReadCache();
        var began = 0;
        long peak = 0;
        UrlQuery newest = new() { Search = "product", Sort = [new UrlSort("id", true)], Limit = LIMIT };
        var readers = Enumerable.Range(0, 2).Select(_ => Task.Run(() =>
        {
            var reads = 0;
            while (!job.Completion.IsCompleted)
            {
                var page = GridReadContext.Read(path, cache, CancellationToken.None, context =>
                {
                    var mine = Interlocked.Increment(ref began);
                    var read = LinkGridReader.Query(context, newest);
                    for (var held = Stopwatch.StartNew(); (Volatile.Read(ref began) == mine || held.Elapsed < RELAY_LEAST) && held.Elapsed < RELAY_HOLD && !job.Completion.IsCompleted;)
                    {
                        context.Token.ThrowIfCancellationRequested();
                        InterlockedMax(ref peak, CrawlDatabase.LogBytes(path));
                        Thread.Sleep(1);
                    }

                    return read;
                });
                Assert.Equal(Math.Min(LIMIT, page.Matching), page.Rows.Count);
                reads++;
            }

            return reads;
        })).ToArray();

        Assert.Equal(CrawlStatus.Completed, (await job.Completion.WaitAsync(TestCrawls.TIMEOUT)).Status);
        var reads = (await Task.WhenAll(readers)).Sum();
        Assert.True(reads > 8, $"Only {reads} reads were made while the crawl ran.");

        // Left to grow, the log of this crawl reaches about three times the file's size.
        Assert.True(peak >= SMALL_LOG_BYTES, $"The log only reached {peak:N0} bytes, so it was never due to be folded.");
        Assert.True(peak <= LARGEST_SMALL_LOG_BYTES, $"The log reached {peak:N0} bytes, with a limit of {SMALL_LOG_BYTES:N0} (the file is {new FileInfo(path).Length:N0} bytes).");
        Assert.Equal(site.Site.GetExpectations().HtmlPages, CrawlStatsReader.Read(path).HtmlPageCount);
    }

    private static void InterlockedMax(ref long target, long value)
    {
        for (var seen = Volatile.Read(ref target); value > seen; seen = Volatile.Read(ref target))
        {
            Interlocked.CompareExchange(ref target, value, seen);
        }
    }

    /// <summary>The hammer: every read, over and over, of a crawl of the synthetic site as it grows. How often the log was seen folded.</summary>
    private async Task<int> ReadACrawlAsItGrowsAsync()
    {
        await using var site = await SyntheticSiteServer.StartAsync(new SyntheticSiteOptions { PageCount = 400 });
        var path = _files.NewPath();
        await using var job = TestCrawls.Start(new CrawlOptions { StartUrl = site.BaseUri.AbsoluteUri, Concurrency = 4, MaxRequestsPerSecond = 80 }, path);
        await TestCrawls.WaitUntilAsync(path, "SELECT COUNT(*) FROM links");

        var cache = new ReadCache();
        ConcurrentQueue<string> wrong = new();
        ConcurrentDictionary<long, int> totals = new();
        ConcurrentDictionary<(string Set, long Total), (long Matching, long[] Newest)> answers = new();
        var readers = Enumerable.Range(0, READERS).Select(reader => Task.Run(() =>
        {
            var reads = 0;
            for (var round = reader; !job.Completion.IsCompleted && wrong.Count < 20; round++)
            {
                reads += ReadEverything(path, cache, round, wrong, totals, answers);
            }

            return reads;
        })).ToArray();

        // The log's file gets smaller only when the writer folds it in and empties it.
        var folds = 0;
        for (long last = 0, now; !job.Completion.IsCompleted; last = now)
        {
            now = CrawlDatabase.LogBytes(path);
            folds += now < last ? 1 : 0;
            await Task.Delay(2);
        }

        Assert.Equal(CrawlStatus.Completed, (await job.Completion.WaitAsync(TestCrawls.TIMEOUT)).Status);
        var reads = (await Task.WhenAll(readers)).Sum();
        Assert.True(wrong.IsEmpty, $"{wrong.Count} of {reads} reads were wrong:\n{string.Join("\n", wrong.Take(10))}");

        // The readers did read a crawl that was growing, through many of its moments.
        Assert.True(reads > 500, $"Only {reads} reads were made while the crawl ran.");
        Assert.True(totals.Count >= 3, $"The reads saw the crawl at only {totals.Count} sizes.");
        Assert.InRange(totals.Keys.Max(), 5_000, TestCrawls.Count(path, "SELECT COUNT(*) FROM links"));

        // And each answer was right, not only at one with itself: this crawl stored every link once and none again (its
        // links' ids have no gaps), so a moment with so many links had the first so many, and what matched a filter on
        // the link alone then, and which were the newest of them, the finished file still says.
        Assert.Equal(TestCrawls.Count(path, "SELECT COUNT(*) FROM links"), TestCrawls.Count(path, "SELECT MAX(rowid) FROM links"));
        Assert.True(answers.Count >= 2 * TRUTHS.Count, $"Only {answers.Count} answers to hold to the file.");
        foreach (var ((set, total), (matching, newest)) in answers)
        {
            var truth = TestCrawls.Rows(path, $"SELECT printf('%012d', rowid) FROM links WHERE rowid <= {total} AND {TRUTHS[set]} ORDER BY rowid DESC LIMIT {LIMIT}").Select(long.Parse).OrderDescending();
            Assert.True(matching == TestCrawls.Count(path, $"SELECT COUNT(*) FROM links WHERE rowid <= {total} AND {TRUTHS[set]}") && newest.SequenceEqual(truth),
                $"{set}, of {total} links: {matching} matched, the newest {string.Join(",", newest.Take(3))}; the file says {string.Join(",", truth.Take(3))}");
        }

        return folds;
    }

    /// <summary>One round of every read, each answer checked against itself; what's wrong is noted, and the reads counted.</summary>
    private static int ReadEverything(string path, ReadCache cache, int round, ConcurrentQueue<string> wrong, ConcurrentDictionary<long, int> totals,
        ConcurrentDictionary<(string Set, long Total), (long Matching, long[] Newest)> answers)
    {
        var reads = 0;
        void Read(string what, Func<string?> read)
        {
            reads++;
            try
            {
                if (read() is { } problem)
                {
                    wrong.Enqueue($"{what}: {problem}");
                }
            }
            catch (Exception exception)
            {
                wrong.Enqueue($"{what}: {exception.GetType().Name}: {exception.Message}");
            }
        }

        LinkGridPage Page(UrlQuery query)
        {
            var page = LinkGridReader.Query(path, query with { Columns = SHOWN }, cache);
            if (page.Summary.Reason == SummaryUnavailableReason.Crawling)
            {
                totals.TryAdd(page.Total, 0);
            }

            return page;
        }

        // Each reader starts its round at another filter set, so the readers count different things at any one time.
        foreach (var (name, filters, search) in SETS.Skip(round % SETS.Length).Concat(SETS.Take(round % SETS.Length)))
        {
            var query = new UrlQuery { Filters = filters, Search = search, Limit = LIMIT };
            var newest = query with { Sort = [new UrlSort("id", true)] };
            long matching = 0;
            Read($"{name}, first page", () =>
            {
                var page = Page(query);
                matching = page.Matching;
                return Wrong(page, query);
            });
            Read($"{name}, newest first", () =>
            {
                var page = Page(newest);
                if (TRUTHS.ContainsKey(name) && page.Summary.Reason == SummaryUnavailableReason.Crawling)
                {
                    // Two answers for one moment must be one answer.
                    var answer = (page.Matching, Newest: page.Rows.Select(row => row.Id).ToArray());
                    var first = answers.GetOrAdd((name, page.Total), answer);
                    if (first.Matching != answer.Matching || !first.Newest.SequenceEqual(answer.Newest))
                    {
                        return $"of {page.Total} links, {answer.Matching} match where {first.Matching} did";
                    }
                }

                return Wrong(page, newest);
            });
            foreach (var offset in new[] { matching / 2, Math.Max(0, matching - 20), matching + 10 })
            {
                var at = query with { Offset = (int)offset };
                Read($"{name}, from {offset}", () => Wrong(Page(at), at));
                Read($"{name}, newest first from {offset}", () => Wrong(Page(newest with { Offset = (int)offset }), newest with { Offset = (int)offset }));
            }

            Read($"{name}, by cursor", () => WrongWalk(Page, query));
            Read($"{name}, newest first by cursor", () => WrongWalk(Page, newest));
            Read($"{name}, facets", () => Wrong(LinkGridReader.Facets(path, new UrlFacetQuery { Filters = filters, Search = search }, cache), filters, search));
            foreach (var sort in TARGET_SORTS)
            {
                var sorted = query with { Sort = [sort] };
                Read($"{name}, by {sort.Column}", () => Wrong(Page(sorted), sorted));
                Read($"{name}, by {sort.Column} from {matching / 2}", () => Wrong(Page(sorted with { Offset = (int)(matching / 2) }), sorted with { Offset = (int)(matching / 2) }));
                Read($"{name}, by {sort.Column} by cursor", () => WrongWalk(Page, sorted));
            }
        }

        Read("robots.txt", () =>
        {
            var origins = RobotsReader.ReadOrigins(path, limit: 1_000, cache: cache);
            if (origins.Total != origins.Origins.Count || origins.Blocked != origins.Origins.Sum(origin => origin.Blocked))
            {
                return $"{origins.Origins.Count} origins of {origins.Total} block {origins.Origins.Sum(origin => origin.Blocked)} URLs, and the page says {origins.Blocked}";
            }

            var first = origins.Origins[0].Origin;
            var file = RobotsReader.ReadFile(path, first, cache);
            var urls = RobotsReader.ReadBlocked(path, first, limit: LIMIT, cache: cache);
            return file is null || urls is null ? $"{first} is listed and has no robots.txt"
                : file.Origin.Blocked != file.Lines.Sum(line => line.Blocked ?? 0) + file.Unmatched.Unreachable + file.Unmatched.Unknown
                    ? $"{first} blocks {file.Origin.Blocked} URLs, and its lines account for {file.Lines.Sum(line => line.Blocked ?? 0)}"
                : urls.Matching != urls.Total || urls.Rows.Count != Math.Min(LIMIT, urls.Total) ? $"{first}: {urls.Rows.Count} rows of {urls.Matching} blocked URLs, of {urls.Total}"
                : null;
        });
        Read("sitemaps", () =>
        {
            foreach (var sitemap in SitemapListReader.Read(path, cache).Sitemaps)
            {
                if (sitemap.Urls.Fetched > sitemap.Urls.Listed || sitemap.Urls.Indexable + sitemap.Urls.NonIndexable > sitemap.Urls.Listed)
                {
                    return $"{sitemap.Url} lists {sitemap.Urls.Listed}: {sitemap.Urls.Fetched} fetched, {sitemap.Urls.Indexable} indexable, {sitemap.Urls.NonIndexable} not";
                }

                if (UrlGridReader.QuerySitemap(path, sitemap.Id, new UrlQuery { Limit = 1 }, cache) is { } listed && listed.Matching != listed.Total)
                {
                    return $"{sitemap.Url}: {listed.Matching} URLs match no filter, of {listed.Total}";
                }
            }

            return null;
        });
        return reads;
    }

    /// <summary>What's wrong with a page read by offset, by its own numbers; null when it agrees with itself.</summary>
    private static string? Wrong(LinkGridPage page, UrlQuery query)
    {
        var held = Math.Clamp(page.Matching - query.Offset, 0, query.Limit);
        return page.Offset != query.Offset ? $"it says it starts at {page.Offset}"
            : page.Matching > page.Total || (query.Filters.Count == 0 && query.Search is null && page.Matching != page.Total) ? $"{page.Matching} match, of {page.Total}"
            : page.Rows.Count != held ? $"{page.Rows.Count} rows from {query.Offset}, with {page.Matching} matching"
            : (page.Next is null) != (query.Offset + page.Rows.Count >= page.Matching) ? $"its cursor is {page.Next ?? "null"} after {query.Offset + page.Rows.Count} of {page.Matching}"
            : WrongRows(page, query, null);
    }

    /// <summary>What's wrong with the rows of a page: each a link with its values, once, in the order asked, and of the kind a filter asks for.</summary>
    private static string? WrongRows(LinkGridPage page, UrlQuery query, long? after)
    {
        var descending = query.Sort is [{ Descending: true }];
        var byTarget = query.Sort is [{ Column: not "id" }];
        var onlyHyperlinks = query.Filters is [{ Column: "kind" }];
        HashSet<long> targets = [];
        var (last, lastTarget) = (after, (long?)null);
        foreach (var row in page.Rows)
        {
            if (row.Values is not [long id, var kind, { } to] || id != row.Id)
            {
                return $"link {row.Id} has the values {string.Join(", ", row.Values)}";
            }

            var target = Convert.ToInt64(to, CultureInfo.InvariantCulture);

            if (onlyHyperlinks && kind as string != "hyperlink")
            {
                return $"link {row.Id} is of kind {kind}, among hyperlinks";
            }

            // In crawl order, or with a target's links together, in crawl order.
            if (byTarget ? target != lastTarget && !targets.Add(target) : last is not null && (descending ? id >= last : id <= last))
            {
                return $"link {row.Id} (to {target}) comes after link {last} (to {lastTarget})";
            }

            if (byTarget && target == lastTarget && id <= last)
            {
                return $"link {row.Id} comes after link {last}, both to {target}";
            }

            (last, lastTarget) = (id, target);
        }

        return page.Rows.Select(row => row.Id).Distinct().Count() == page.Rows.Count ? null : "a link is there twice";
    }

    /// <summary>What's wrong with three pages read by cursor, each from where the last one stopped.</summary>
    private static string? WrongWalk(Func<UrlQuery, LinkGridPage> read, UrlQuery query)
    {
        string? after = null;
        long? last = null;
        for (var pages = 0; pages < 3; pages++)
        {
            LinkGridPage page;
            try
            {
                page = read(query with { After = after });
            }
            catch (UrlQueryException refused) when (query.Sort is [{ Column: not "id" }] && refused.Errors.ContainsKey("after"))
            {
                // By contract: the cursor's link sorts by its target's value, which the crawl changed since (it fetched the
                // target, say), so the walk starts from an offset again.
                return null;
            }

            var problem = page.Offset != (after is null ? 0 : null) ? $"it says it starts at {page.Offset}"
                : page.Rows.Count > query.Limit || (page.Next is not null && page.Rows.Count != query.Limit) ? $"{page.Rows.Count} rows, and its cursor is {page.Next ?? "null"}"
                : page.Matching > page.Total ? $"{page.Matching} match, of {page.Total}"
                : WrongRows(page, query, query.Sort is [{ Column: not "id" }] ? null : last);
            if (problem is not null)
            {
                return $"page {pages + 1}: {problem}";
            }

            if (page.Next is null)
            {
                return null;
            }

            (after, last) = (page.Next, page.Rows[^1].Id);
        }

        return null;
    }

    /// <summary>What's wrong with a filter set's facets: every link counts once in each.</summary>
    private static string? Wrong(UrlFacets facets, UrlFilter[] filters, string? search)
    {
        if (facets.Matching > facets.Total || (filters.Length == 0 && search is null && facets.Matching != facets.Total))
        {
            return $"{facets.Matching} match, of {facets.Total}";
        }

        foreach (var facet in LinkGridReader.FACETS)
        {
            if (!facets.Facets.TryGetValue(facet, out var buckets) || buckets.Sum(bucket => bucket.Count) != facets.Matching)
            {
                return $"{facet} counts {buckets?.Sum(bucket => bucket.Count)} links, of {facets.Matching} matching";
            }
        }

        return filters is [{ Column: "kind" }] && facets.Matching > 0 && facets.Facets["kind"] is not [{ Value: "hyperlink" }] ? "hyperlinks are of other kinds too" : null;
    }

    /// <summary>Every link of a query, read by cursor to its end: a walk that doesn't end fails.</summary>
    private static List<long> Walk(string path, UrlQuery query, ReadCache cache)
    {
        List<long> ids = [];
        string? after = null;
        for (var pages = 0; pages < 1_000; pages++)
        {
            var page = LinkGridReader.Query(path, query with { After = after }, cache);
            ids.AddRange(page.Rows.Select(row => row.Id));
            if (page.Next is null)
            {
                return ids;
            }

            after = page.Next;
        }

        throw new InvalidOperationException("The cursor never came to an end.");
    }
}
