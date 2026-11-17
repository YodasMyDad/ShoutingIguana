using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Tests.Support;
using static ShoutingIguana.Engine.Tests.Support.GridQueries;

namespace ShoutingIguana.Engine.Tests.Storage;

/// <summary>
/// The URL grid read page by page against a reference worked out from the crawl's rows without the readers: every column's
/// values, every sort both ways with unknown values last, every filter operator, offsets and cursors alike, and the counts.
/// Pages are small and odd-sized, so their edges fall inside runs of equal and unknown values.
/// </summary>
[Collection(GridCrawlCollection.NAME)]
public sealed class UrlGridReaderTests(GridCrawl crawl)
{
    private const int PAGE = 13;

    /// <summary>The start of every URL of the synthetic site, which runs on the loopback address.</summary>
    private const string LOCAL = "http://127.0.0.1:";

    /// <summary>
    /// Every column but the two read from each page's stored details and the two whose value is a record (a redirect's hops,
    /// robots.txt's rule), which tests of their own check (<see cref="EvidenceReadTests"/>).
    /// </summary>
    private static readonly string[] COLUMNS =
    [
        .. UrlGridReader.Columns.Select(column => column.Id).Where(id => id is not ("metaRobots" or "xRobotsTag" or "redirectChain" or "robotsRule"))
    ];

    public static TheoryData<string, bool> Sorts()
    {
        TheoryData<string, bool> sorts = [];
        foreach (var column in UrlGridReader.Columns.Where(column => column.Sortable))
        {
            sorts.Add(column.Id, false);
            sorts.Add(column.Id, true);
        }

        return sorts;
    }

    [Fact]
    public void EveryColumnHoldsWhatTheCrawlHolds()
    {
        var rows = ReadAll(new UrlQuery { Columns = COLUMNS, Limit = 1_000 });
        Assert.Equal(crawl.Reference.Rows.Keys.Order(), rows.Keys);
        foreach (var (id, values) in rows)
        {
            foreach (var column in COLUMNS)
            {
                Assert.True(Same(crawl.Reference.Rows[id][column], values[column]),
                    $"URL {id} {column}: expected {crawl.Reference.Rows[id][column] ?? "null"}, read {values[column] ?? "null"}");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Sorts))]
    public void EverySortPagesAlikeByOffsetAndByCursor(string column, bool descending)
    {
        UrlSort[] sort = [new(column, descending)];
        var expected = crawl.Reference.Order(sort, crawl.Reference.Rows.Keys);
        var cache = new ReadCache();
        Assert.Equal(expected, ByOffset(new UrlQuery { Sort = sort }, cache));
        Assert.Equal(expected, ByCursor(new UrlQuery { Sort = sort }, cache));

        // A jump anywhere, past half way too, where the page is read backwards from the far end.
        foreach (var offset in new[] { expected.Count / 2, expected.Count / 2 + 1, expected.Count - PAGE, expected.Count - 1 })
        {
            var page = UrlGridReader.Query(crawl.Path, new UrlQuery { Sort = sort, Offset = offset, Limit = PAGE }, cache);
            Assert.Equal(expected.Skip(offset).Take(PAGE), page.Rows.Select(row => row.Id));
        }
    }

    public static TheoryData<string, UrlFilter[]> Filters() => new()
    {
        { "number eq", [Number("statusCode", FilterOperator.Eq, 404)] },
        { "number ne", [Number("statusCode", FilterOperator.Ne, 200)] },
        { "number lt", [Number("depth", FilterOperator.Lt, 2)] },
        { "number lte", [Number("depth", FilterOperator.Lte, 2)] },
        { "number gt", [Number("inlinks", FilterOperator.Gt, 30)] },
        { "number gte", [Number("wordCount", FilterOperator.Gte, 250)] },
        { "number between", [new UrlFilter("sizeBytes", FilterOperator.Between) { Min = 1_000, Max = 20_000 }] },
        { "number at least", [new UrlFilter("responseMs", FilterOperator.Between) { Min = 1 }] },
        { "number at most", [new UrlFilter("depth", FilterOperator.Between) { Max = 1 }] },
        { "number in", [Numbers("statusCode", FilterOperator.In, 301, 302, 404)] },
        { "number not in", [Numbers("statusCode", FilterOperator.NotIn, 200)] },
        { "number empty", [new UrlFilter("statusCode", FilterOperator.IsEmpty)] },
        { "number not empty", [new UrlFilter("depth", FilterOperator.IsNotEmpty)] },
        { "enum in", [Names("type", FilterOperator.In, "image", "stylesheet")] },
        { "enum not in", [Names("type", FilterOperator.NotIn, "page")] },
        { "status class 4xx", [Names("statusClass", FilterOperator.In, "4xx")] },
        { "status classes", [Names("statusClass", FilterOperator.In, "3xx", "5xx", "noResponse")] },
        { "status class other", [Names("statusClass", FilterOperator.In, "other")] },
        { "status class not 2xx", [Names("statusClass", FilterOperator.NotIn, "2xx")] },
        { "status class empty", [new UrlFilter("statusClass", FilterOperator.IsEmpty)] },
        { "indexability in", [Names("indexability", FilterOperator.In, "noindex", "canonicalised")] },
        { "indexability empty", [new UrlFilter("indexability", FilterOperator.IsEmpty)] },
        { "outcome in", [Names("outcome", FilterOperator.In, "redirect", "clientError")] },
        { "outcome not in", [Names("outcome", FilterOperator.NotIn, "success")] },
        { "source", [Names("source", FilterOperator.In, "sitemap", "siteVersion")] },
        { "boolean true", [Boolean("indexable", true)] },
        { "boolean false", [Boolean("indexable", false)] },
        { "boolean empty", [new UrlFilter("indexable", FilterOperator.IsEmpty)] },
        { "indexable is not true", [NotBoolean("indexable", true)] },
        { "indexable is not false", [NotBoolean("indexable", false)] },
        { "noindex is not false", [NotBoolean("noindex", false)] },
        { "in sitemap is not true", [NotBoolean("inSitemap", true)] },
        { "fetched is not true", [NotBoolean("fetched", true)] },
        { "internal is not true", [NotBoolean("isInternal", true)] },
        { "boolean not empty", [new UrlFilter("inSitemap", FilterOperator.IsNotEmpty)] },
        { "indexable not empty", [new UrlFilter("indexable", FilterOperator.IsNotEmpty)] },
        { "fetched", [Boolean("fetched", false)] },
        { "internal", [Boolean("isInternal", false)] },
        { "noindex", [Boolean("noindex", true)] },
        { "nofollow not empty", [new UrlFilter("nofollow", FilterOperator.IsNotEmpty)] },
        { "text contains", [Text("title", FilterOperator.Contains, "PRODUCT 1")] },
        { "text not contains", [Text("title", FilterOperator.NotContains, "product")] },
        { "text starts with", [Text("url", FilterOperator.StartsWith, "HTTP://127.0.0.1:")] },
        { "text ends with", [Text("url", FilterOperator.EndsWith, "/")] },
        { "text eq", [Text("contentType", FilterOperator.Eq, "text/html")] },
        { "text ne", [Text("contentType", FilterOperator.Ne, "text/html")] },
        { "text empty", [new UrlFilter("description", FilterOperator.IsEmpty)] },
        { "text not empty", [new UrlFilter("h1", FilterOperator.IsNotEmpty)] },
        { "text regex", [Text("url", FilterOperator.Regex, @"/p/1\d$")] },
        { "text not regex", [Text("url", FilterOperator.NotRegex, @"/p/\d+$")] },
        { "text like wildcards", [Text("url", FilterOperator.Contains, "_%")] },
        { "url of another URL", [Text("finalUrl", FilterOperator.Contains, "/p/")] },
        { "redirect target", [Text("redirectTarget", FilterOperator.StartsWith, LOCAL)] },
        { "redirect target empty", [new UrlFilter("redirectTarget", FilterOperator.IsEmpty)] },
        { "redirect target status", [Number("redirectTargetStatusCode", FilterOperator.Gte, 300)] },
        { "canonical", [Text("canonicalUrl", FilterOperator.NotContains, "/p/")] },
        { "title length", [Number("titleLength", FilterOperator.Gt, 60)] },
        { "issues", [Number("errors", FilterOperator.Gte, 1), Number("warnings", FilterOperator.Eq, 0)] },
        { "three filters", [Boolean("isInternal", true), Names("type", FilterOperator.In, "page"), Number("statusCode", FilterOperator.Eq, 200)] },
        { "a rare one with a common one", [Names("indexability", FilterOperator.In, "noindex"), Number("statusCode", FilterOperator.Eq, 200)] },
        { "ttfb", [Number("ttfbMs", FilterOperator.Gte, 0)] },
        { "h1 count", [Number("h1Count", FilterOperator.Ne, 1)] }
    };

    /// <summary>
    /// A URL with a status code has an outcome too, so the grid's "Not crawled" (no code, no outcome) is "outcome is
    /// empty" alone, a filter that leaves out the URLs blocked by robots.txt, which have an outcome but no code (round 5
    /// review R1-7).
    /// </summary>
    [Fact]
    public void AUrlWithAStatusCodeHasAnOutcome()
    {
        Assert.Empty(crawl.Reference.Matching([new UrlFilter("statusCode", FilterOperator.IsNotEmpty), new UrlFilter("outcome", FilterOperator.IsEmpty)]));
        Assert.NotEmpty(crawl.Reference.Matching([new UrlFilter("statusCode", FilterOperator.IsEmpty), new UrlFilter("outcome", FilterOperator.IsNotEmpty)]));
    }

    [Theory]
    [MemberData(nameof(Filters))]
    public void EveryFilterMatchesWhatTheContractSays(string name, UrlFilter[] filters)
    {
        var matched = crawl.Reference.Matching(filters).ToHashSet();
        Assert.True(matched.Count > 0 || name is "status class other" or "number empty" or "status class empty" or "text like wildcards", $"The case '{name}' should match something on the synthetic site");
        foreach (var sort in new UrlSort[][] { [], [new UrlSort("responseMs", true)], [new UrlSort("url")], [new UrlSort("inlinks")] })
        {
            var expected = crawl.Reference.Order(sort, matched);
            var query = new UrlQuery { Filters = filters, Sort = sort };
            var cache = new ReadCache();
            Assert.Equal(expected, ByOffset(query, cache));
            Assert.Equal(expected, ByCursor(query, cache));
            Assert.Equal(matched.Count, UrlGridReader.Query(crawl.Path, query with { Limit = 1 }).Matching);
        }
    }

    /// <summary>Without a URL summary, every filter on the crawl's own columns matches what it does with one.</summary>
    [Theory]
    [MemberData(nameof(Filters))]
    public void EveryFilterMatchesTheSameWithoutASummary(string name, UrlFilter[] filters)
    {
        if (filters.Any(filter => filter.Column != "check" && UrlColumns.Find(filter.Column, issueColumns: false)!.NeedsAnalysis))
        {
            return;
        }

        foreach (var sort in new UrlSort[][] { [], [new UrlSort("responseMs", true)], [new UrlSort("url")] })
        {
            var query = new UrlQuery { Filters = filters, Sort = sort };
            var expected = crawl.Reference.Order(sort, crawl.Reference.Matching(filters));
            Assert.True(expected.SequenceEqual(ByOffset(query, new ReadCache(), crawl.Changed)), $"{name}, sorted by {string.Join(",", sort.Select(key => key.Column))}");
            Assert.Equal(expected, ByCursor(query, new ReadCache(), crawl.Changed));
        }
    }

    [Fact]
    public void MultipleKeysSortInTurnEachWithUnknownLast()
    {
        UrlSort[][] sorts =
        [
            [new("depth"), new("inlinks", true)],
            [new("statusCode", true), new("wordCount")],
            [new("indexable"), new("titleLength", true), new("url")],
            [new("type"), new("url", true)]
        ];
        foreach (var sort in sorts)
        {
            var expected = crawl.Reference.Order(sort, crawl.Reference.Rows.Keys);
            Assert.Equal(expected, ByOffset(new UrlQuery { Sort = sort }, new ReadCache()));
            Assert.Equal(expected, ByCursor(new UrlQuery { Sort = sort }, new ReadCache()));
        }
    }

    [Fact]
    public void SearchMatchesTheUrlOrTheTitle()
    {
        foreach (var text in new[] { "/p/12", "product 7", "nothing at all matches this" })
        {
            var expected = crawl.Reference.Order([new UrlSort("url")], crawl.Reference.Matching([], text));
            Assert.Equal(expected, ByOffset(new UrlQuery { Search = text, Sort = [new UrlSort("url")] }, new ReadCache()));
            Assert.Equal(expected, ByOffset(new UrlQuery { Search = text, Sort = [new UrlSort("url")] }, new ReadCache(), crawl.Changed));
        }
    }

    [Fact]
    public void TheCheckFilterMatchesTheUrlsItFlagged()
    {
        var flagged = crawl.Reference.Issues.SelectMany(issue => issue.Value.Select(item => item.Issue)).Distinct().Order(StringComparer.Ordinal).ToList();
        Assert.NotEmpty(flagged);
        foreach (var check in flagged)
        {
            UrlFilter[] filters = [Names("check", FilterOperator.In, check)];
            Assert.Equal(crawl.Reference.Order([], crawl.Reference.Matching(filters)), ByOffset(new UrlQuery { Filters = filters }, new ReadCache()));
        }

        // Several checks, none of them, and one group of one.
        UrlFilter[] several = [Names("check", FilterOperator.In, "broken-internal-links", "internal-client-error")];
        Assert.Equal(crawl.Reference.Order([], crawl.Reference.Matching(several)), ByOffset(new UrlQuery { Filters = several }, new ReadCache()));
        UrlFilter[] none = [Names("check", FilterOperator.NotIn, "orphan-page", "not-in-sitemap")];
        Assert.Equal(crawl.Reference.Order([], crawl.Reference.Matching(none)), ByOffset(new UrlQuery { Filters = none }, new ReadCache()));

        (string check0, (string Issue, string? Detail, int? Related) issue) = crawl.Reference.Issues.SelectMany(entry => entry.Value).Where(item => IssueCatalog.Find(item.Issue)!.Grouping != IssueGrouping.None)
            .GroupBy(item => (item.Issue, item.Detail, item.Related)).OrderByDescending(group => group.Count()).Select(group => (group.Key.Issue, group.First())).First();
        UrlFilter[] group = [Names("check", FilterOperator.In, check0) with { Group = new IssueGroupKey(issue.Detail, issue.Related) }];
        var expected = crawl.Reference.Order([new UrlSort("url")], crawl.Reference.Matching(group));
        Assert.NotEmpty(expected);
        Assert.Equal(expected, ByOffset(new UrlQuery { Filters = group, Sort = [new UrlSort("url")] }, new ReadCache()));
    }

    [Fact]
    public void CountsAreKeptWhileTheCrawlStandsStill()
    {
        var cache = new ReadCache();
        var query = new UrlQuery { Filters = [Names("statusClass", FilterOperator.In, "4xx")], Sort = [new UrlSort("depth")], Limit = 5 };
        var first = UrlGridReader.Query(crawl.Path, query, cache);
        var kept = cache.Count;
        Assert.True(kept > 0);
        Assert.Equal(crawl.Reference.Rows.Count, first.Total);
        Assert.Equal(crawl.Reference.Matching(query.Filters).Count(), first.Matching);

        // Scrolling on counts nothing again.
        var second = UrlGridReader.Query(crawl.Path, query with { Offset = 5 }, cache);
        Assert.Equal(kept, cache.Count);
        Assert.Equal(first.Matching, second.Matching);

        // A crawl continued since has counts of its own.
        var copy = crawl.Copy();
        Stamp(copy);
        var changed = UrlGridReader.Query(copy, query, cache);
        Assert.True(cache.Count > kept);
        Assert.Equal(first.Matching, changed.Matching);
    }

    [Fact]
    public void WithoutASummaryTheGridReadsTheCrawlsOwnTables()
    {
        var copy = crawl.Copy();
        Stamp(copy);
        var crawlColumns = UrlGridReader.Columns.Where(column => !column.NeedsAnalysis && COLUMNS.Contains(column.Id)).Select(column => column.Id).ToList();
        var page = UrlGridReader.Query(copy, new UrlQuery { Columns = COLUMNS, Limit = 1 });
        Assert.Equal(new SummaryState(false, SummaryUnavailableReason.CrawlChanged), page.Summary);
        Assert.Equal(COLUMNS.Except(crawlColumns).Order(StringComparer.Ordinal), page.Unavailable.Order(StringComparer.Ordinal));

        // The crawl's own columns hold what the summary's copies of them do.
        var rows = ReadAll(new UrlQuery { Columns = crawlColumns, Limit = 1_000 }, copy);
        foreach (var (id, values) in rows)
        {
            foreach (var column in crawlColumns)
            {
                Assert.True(Same(crawl.Reference.Rows[id][column], values[column]),
                    $"Without a summary, URL {id} {column}: expected {crawl.Reference.Rows[id][column] ?? "null"}, read {values[column] ?? "null"}");
            }
        }

        // Its sorts and filters work too, and those on the summary's columns are refused, not ignored.
        UrlFilter[] filters = [Names("type", FilterOperator.In, "page"), Boolean("indexable", true), Number("statusCode", FilterOperator.Eq, 200)];
        UrlSort[] sort = [new("responseMs", true)];
        Assert.Equal(crawl.Reference.Order(sort, crawl.Reference.Matching(filters)), ByOffset(new UrlQuery { Filters = filters, Sort = sort }, new ReadCache(), copy));
        var refused = Assert.Throws<NeedsAnalysisException>(() =>
            UrlGridReader.Query(copy, new UrlQuery { Filters = [Number("inlinks", FilterOperator.Gt, 1), Names("check", FilterOperator.In, "orphan-page")], Sort = [new UrlSort("issues")] }));
        Assert.Equal(SummaryUnavailableReason.CrawlChanged, refused.Reason);
        Assert.Equal(["inlinks", "issues"], refused.Columns);
    }

    /// <summary>
    /// The check filter needs only the last analysis's issues, which a crawl continued since still has: a group's count
    /// opens its URLs in the grid as its rows do. A crawl never analysed has none.
    /// </summary>
    [Fact]
    public void WithoutASummaryTheCheckFilterReadsTheLastAnalysissIssues()
    {
        var copy = crawl.Copy();
        Stamp(copy);
        foreach (var check in crawl.Reference.Issues.SelectMany(issue => issue.Value.Select(item => item.Issue)).Distinct(StringComparer.Ordinal))
        {
            foreach (var op in new[] { FilterOperator.In, FilterOperator.NotIn })
            {
                UrlFilter[] filters = [Names("check", op, check), Names("type", FilterOperator.In, "page")];
                Assert.Equal(crawl.Reference.Order([new UrlSort("url")], crawl.Reference.Matching(filters)),
                    ByOffset(new UrlQuery { Filters = filters, Sort = [new UrlSort("url")] }, new ReadCache(), copy));
            }
        }

        var neverAnalysed = crawl.Copy();
        Change(neverAnalysed, "DELETE FROM analysis");
        var refused = Assert.Throws<NeedsAnalysisException>(() => UrlGridReader.Query(neverAnalysed, new UrlQuery { Filters = [Names("check", FilterOperator.In, "orphan-page")] }));
        Assert.Equal(["check"], refused.Columns);
    }

    [Fact]
    public void TheSummarySaysWhyItIsntThere()
    {
        var engineChanged = crawl.Copy();
        Change(engineChanged, $"UPDATE analysis SET analysis_version = {AnalysisFreshness.VERSION - 1}");
        Assert.Equal(SummaryUnavailableReason.EngineChanged, UrlGridReader.Query(engineChanged, new UrlQuery()).Summary.Reason);

        var neverAnalysed = crawl.Copy();
        Change(neverAnalysed, "DELETE FROM analysis");
        Assert.Equal(SummaryUnavailableReason.NotAnalyzed, UrlGridReader.Query(neverAnalysed, new UrlQuery()).Summary.Reason);

        // Running in another program: the file says so, and someone holds its lock. Without the lock, it was interrupted.
        var running = crawl.Copy();
        Change(running, $"UPDATE crawl SET status = {(int)CrawlStatus.Running}, updated_at = updated_at + 1");
        Assert.Equal(SummaryUnavailableReason.CrawlChanged, UrlGridReader.Query(running, new UrlQuery()).Summary.Reason);
        using (new FileStream(running + "-lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1,
                   OperatingSystem.IsWindows() ? FileOptions.DeleteOnClose : FileOptions.None))
        {
            var live = UrlGridReader.Query(running, new UrlQuery { Filters = [Names("type", FilterOperator.In, "page")] });
            Assert.Equal(SummaryUnavailableReason.Crawling, live.Summary.Reason);
        }

        Assert.Equal(SummaryState.Present, UrlGridReader.Query(crawl.Path, new UrlQuery()).Summary);
    }

    /// <summary>
    /// A running crawl changes from one read to the next, so what it counts is kept only briefly: a burst of reads (a page,
    /// its prefetch, the facets) counts once, and a count is never older than <see cref="ReadCache.LIVE_FOR"/>.
    /// </summary>
    [Fact]
    public void ARunningCrawlsCountsAreKeptBriefly()
    {
        var running = crawl.Copy();
        Change(running, $"UPDATE crawl SET status = {(int)CrawlStatus.Running}, updated_at = updated_at + 1");
        var page = crawl.Reference.Rows.First(row => (string?)row.Value["type"] == "page").Key;
        UrlFilter[] pages = [Names("type", FilterOperator.In, "page")];
        using (new FileStream(running + "-lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1,
                   OperatingSystem.IsWindows() ? FileOptions.DeleteOnClose : FileOptions.None))
        {
            var cache = new ReadCache();
            var before = UrlGridReader.Query(running, new UrlQuery { Filters = pages, Limit = 1 }, cache);
            Assert.Equal(SummaryUnavailableReason.Crawling, before.Summary.Reason);
            Assert.True(cache.Count > 0);

            // The crawl moves on: a page turns out to be an image. Within the moment, the count stands; after it, it's read again.
            Change(running, "UPDATE responses SET content_type = 'image/png' WHERE url_id = $id", ("$id", page));
            Assert.Equal(before.Matching, UrlGridReader.Query(running, new UrlQuery { Filters = pages, Limit = 1 }, cache).Matching);
            Thread.Sleep(ReadCache.LIVE_FOR + TimeSpan.FromMilliseconds(200));
            Assert.Equal(before.Matching - 1, UrlGridReader.Query(running, new UrlQuery { Filters = pages, Limit = 1 }, cache).Matching);
        }
    }

    public static TheoryData<string, UrlQuery> BadQueries() => new()
    {
        { "columns[0]", new UrlQuery { Columns = ["noSuchColumn"] } },
        { "columns[1]", new UrlQuery { Columns = ["url", "detail"] } },
        { "filters[0].column", new UrlQuery { Filters = [Number("noSuchColumn", FilterOperator.Eq, 1)] } },
        { "filters[0].column", new UrlQuery { Filters = [new UrlFilter("metaRobots", FilterOperator.IsEmpty)] } },
        { "filters[0].op", new UrlQuery { Filters = [Text("statusCode", FilterOperator.Contains, "4")] } },
        { "filters[0].op", new UrlQuery { Filters = [Number("type", FilterOperator.Eq, 1)] } },
        { "filters[0].value", new UrlQuery { Filters = [Text("statusCode", FilterOperator.Eq, "404")] } },
        { "filters[0].value", new UrlQuery { Filters = [new UrlFilter("indexable", FilterOperator.Is)] } },
        { "filters[0].value", new UrlQuery { Filters = [Text("url", FilterOperator.Regex, "(unclosed")] } },
        { "filters[0].value", new UrlQuery { Filters = [Text("url", FilterOperator.Regex, @"(a)\1")] } },
        { "filters[0].value", new UrlQuery { Filters = [Text("url", FilterOperator.Regex, "(?<=x)y")] } },
        { "filters[0].value", new UrlQuery { Filters = [Text("title", FilterOperator.Contains, new string('a', 1_001))] } },
        { "filters[0].values", new UrlQuery { Filters = [Names("type", FilterOperator.In, "page", "spreadsheet")] } },
        { "filters[0].values", new UrlQuery { Filters = [new UrlFilter("statusCode", FilterOperator.In) { Values = [] }] } },
        { "filters[0].values", new UrlQuery { Filters = [Names("check", FilterOperator.In, "no-such-check")] } },
        { "filters[0].min", new UrlQuery { Filters = [new UrlFilter("depth", FilterOperator.Between)] } },
        { "filters[0].min", new UrlQuery { Filters = [new UrlFilter("depth", FilterOperator.Between) { Min = 5, Max = 2 }] } },
        { "filters[0].group", new UrlQuery { Filters = [Names("check", FilterOperator.In, "title-too-long") with { Group = new IssueGroupKey(null, null) }] } },
        { "filters[0].group", new UrlQuery { Filters = [Names("check", FilterOperator.In, "broken-internal-links", "image-broken") with { Group = new IssueGroupKey(null, 1) }] } },
        { "filters[0].group", new UrlQuery { Filters = [Number("depth", FilterOperator.Eq, 1) with { Group = new IssueGroupKey(null, 1) }] } },
        { "filters", new UrlQuery { Filters = [.. Enumerable.Repeat(Number("depth", FilterOperator.Gte, 0), 21)] } },
        { "sort[0].column", new UrlQuery { Sort = [new UrlSort("title")] } },
        { "sort[0].column", new UrlQuery { Sort = [new UrlSort("noSuchColumn")] } },
        { "sort[1].column", new UrlQuery { Sort = [new UrlSort("depth"), new UrlSort("depth", true)] } },
        { "sort", new UrlQuery { Sort = [new UrlSort("depth"), new UrlSort("url"), new UrlSort("inlinks"), new UrlSort("issues")] } },
        { "limit", new UrlQuery { Limit = 0 } },
        { "limit", new UrlQuery { Limit = 1_001 } },
        { "offset", new UrlQuery { Offset = -1 } },
        { "after", new UrlQuery { Offset = 10, After = "x" } },
        { "after", new UrlQuery { After = "not a cursor" } },
        { "search", new UrlQuery { Search = new string('a', 1_001) } },

        // What an operator doesn't take is refused, not ignored, and a list with nothing in a place is a field error.
        { "filters[0].values", new UrlQuery { Filters = [Number("statusCode", FilterOperator.Eq, 200) with { Values = [FilterValue.Of(200)] }] } },
        { "filters[0].value", new UrlQuery { Filters = [Names("type", FilterOperator.In, "page") with { Value = FilterValue.Of("page") }] } },
        { "filters[0].value", new UrlQuery { Filters = [new UrlFilter("depth", FilterOperator.Between) { Min = 1, Value = FilterValue.Of(2) }] } },
        { "filters[0].min", new UrlQuery { Filters = [Number("depth", FilterOperator.Eq, 1) with { Min = 0 }] } },
        { "filters[0].max", new UrlQuery { Filters = [new UrlFilter("title", FilterOperator.IsEmpty) { Max = 3 }] } },
        { "filters[0].value", new UrlQuery { Filters = [Names("check", FilterOperator.In, "title-too-long") with { Value = FilterValue.Of("x") }] } },
        { "columns[0]", new UrlQuery { Columns = [null!] } },
        { "filters[0]", new UrlQuery { Filters = [null!] } },
        { "sort[0]", new UrlQuery { Sort = [null!] } },
        { "filters[0].value", new UrlQuery { Filters = [Text("url", FilterOperator.Regex, "a{100000}")] } }
    };

    [Theory]
    [MemberData(nameof(BadQueries))]
    public void AQueryThatCantRunIsRefusedForTheFieldAtFault(string field, UrlQuery query)
    {
        var refused = Assert.Throws<UrlQueryException>(() => UrlGridReader.Query(crawl.Path, query));
        Assert.Contains(field, refused.Errors.Keys);
        Assert.All(refused.Errors.Values.SelectMany(messages => messages), message => Assert.EndsWith(".", message));
    }

    [Fact]
    public void ACursorOnlyReadsOnTheQueryItCameFrom()
    {
        var page = UrlGridReader.Query(crawl.Path, new UrlQuery { Sort = [new UrlSort("depth")], Limit = 5 });
        Assert.NotNull(page.Next);
        var refused = Assert.Throws<UrlQueryException>(() => UrlGridReader.Query(crawl.Path, new UrlQuery { Sort = [new UrlSort("inlinks")], After = page.Next }));
        Assert.Contains("after", refused.Errors.Keys);

        // The columns may change: they aren't what the rows are read by.
        Assert.Equal(5, UrlGridReader.Query(crawl.Path, new UrlQuery { Sort = [new UrlSort("depth")], Columns = ["title"], After = page.Next, Limit = 5 }).Rows.Count);
    }

    /// <summary>
    /// A filter's text is quoted in the keys counts are kept by and in the hash a cursor carries, its quotes doubled, so text
    /// that spells out the rest of another query's conditions doesn't pass for that query.
    /// </summary>
    [Fact]
    public void NoFilterTextPassesForAnotherQuerysConditions()
    {
        const string REASON = "r.indexability_reason";
        var two = UrlQueryCompiler.Compile(new UrlQuery { Filters = [Text("title", FilterOperator.Eq, "x"), Text("description", FilterOperator.Eq, "y")] },
            GridMode.Summary, GridScope.AllUrls, REASON);

        // Its second condition's key ends with its value, quoted: a title that closes the first quote can spell out the rest.
        var forged = $"x'\n{two.Predicates[1].Key[..^1]}";
        var one = UrlQueryCompiler.Compile(new UrlQuery { Filters = [Text("title", FilterOperator.Eq, forged)] }, GridMode.Summary, GridScope.AllUrls, REASON);
        Assert.NotEqual(two.Hash, one.Hash);
    }

    /// <summary>
    /// A rare filter drives the query through its own index, whatever the sort; a common one leaves the sort's index to be
    /// walked; a sort without an index is a scan. The bundled SQLite can't tell these apart itself.
    /// </summary>
    [Fact]
    public void EachQueryRunsThePlanItsCountsCallFor()
    {
        Assert.Contains("ix_url_summary_indexability", Plan(new UrlQuery { Filters = [Names("indexability", FilterOperator.In, "noindex")], Sort = [new UrlSort("responseMs", true)] }));
        Assert.DoesNotContain("ix_url_summary_response_ms", Plan(new UrlQuery { Filters = [Names("indexability", FilterOperator.In, "noindex")], Sort = [new UrlSort("responseMs", true)] }));

        var common = Plan(new UrlQuery { Filters = [Names("type", FilterOperator.In, "page")], Sort = [new UrlSort("responseMs", true)] });
        Assert.Contains("ix_url_summary_response_ms", common);
        Assert.DoesNotContain("TEMP B-TREE", common);
        Assert.Contains("TEMP B-TREE", Plan(new UrlQuery { Sort = [new UrlSort("followedInlinks", true)] }));

        // A text filter is read once into the URL ids it matches: a rare one's ids drive, and a common one's are tested row
        // by row as the sort's index is walked, which stops once the page is full.
        Assert.Contains("json_each", Plan(new UrlQuery { Filters = [Text("title", FilterOperator.Contains, "product 12")], Sort = [new UrlSort("depth")] }), StringComparison.OrdinalIgnoreCase);
        var commonText = Plan(new UrlQuery { Filters = [Text("title", FilterOperator.Contains, "product")], Sort = [new UrlSort("depth")] });
        Assert.Contains("ix_url_summary_depth", commonText);
        Assert.DoesNotContain("TEMP B-TREE", commonText);
        Assert.DoesNotContain("responses", commonText);
    }

    [Fact]
    public void TheRobotsColumnsShowWhatThePageSent()
    {
        var page = UrlGridReader.Query(crawl.Path, new UrlQuery { Columns = ["metaRobots", "xRobotsTag", "noindex"], Filters = [Names("indexability", FilterOperator.In, "noindex")], Limit = 1_000 });
        Assert.NotEmpty(page.Rows);
        Assert.All(page.Rows, row =>
        {
            Assert.True(row.Values[3] is true);
            var sent = $"{row.Values[1]} {row.Values[2]}";
            Assert.Contains("noindex", sent, StringComparison.OrdinalIgnoreCase);
        });

        // An indexable page sends no noindex at all.
        var indexable = UrlGridReader.Query(crawl.Path, new UrlQuery { Columns = ["metaRobots", "xRobotsTag"], Filters = [Boolean("indexable", true)], Limit = 1_000 });
        Assert.All(indexable.Rows, row => Assert.DoesNotContain("noindex", $"{row.Values[1]} {row.Values[2]}", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LookupFindsAUrlAsTheCrawlStoresIt()
    {
        var url = $"{crawl.Server.BaseUri.AbsoluteUri.TrimEnd('/')}/p/7";
        var id = crawl.Reference.Rows.Single(row => (string?)row.Value["url"] == url).Key;
        var found = new UrlLookup(id, url, null);
        Assert.Equal(found, UrlGridReader.Lookup(crawl.Path, url));
        Assert.Equal(found, UrlGridReader.Lookup(crawl.Path, "  " + url.Replace("http://", "HTTP://", StringComparison.Ordinal) + "#reviews "));
        Assert.Equal(found, UrlGridReader.Lookup(crawl.Path, "/p/7"));
        Assert.Null(UrlGridReader.Lookup(crawl.Path, "/no/such/page"));
        Assert.Contains("url", Assert.Throws<UrlQueryException>(() => UrlGridReader.Lookup(crawl.Path, "not a url")).Errors.Keys);
        Assert.Contains("url", Assert.Throws<UrlQueryException>(() => UrlGridReader.Lookup(crawl.Path, "ftp://example.com/")).Errors.Keys);
        Assert.Contains("url", Assert.Throws<UrlQueryException>(() => UrlGridReader.Lookup(crawl.Path, "mailto:someone@example.com")).Errors.Keys);

        // Pasted without a scheme, it takes the site's.
        var authority = crawl.Server.BaseUri.Authority;
        Assert.Equal(found, UrlGridReader.Lookup(crawl.Path, $"{authority}/p/7"));
        Assert.Equal(found, UrlGridReader.Lookup(crawl.Path, $"//{authority}/p/7"));
    }

    /// <summary>
    /// A crawl started at the bare domain over http, which redirects to https://www., as most do: its pages live on the
    /// www origin, and the engine takes the bare domain and www, http and https, as one site.
    /// </summary>
    [Fact]
    public void LookupFindsAPageWhereTheSiteLives()
    {
        var copy = crawl.Copy();
        Change(copy, $"""
            UPDATE crawl SET start_url = 'http://Example.com';
            INSERT INTO urls (id, url, is_internal, fetch_mode, depth, state, skip_reason, source) VALUES
                (900001, 'http://example.com/', 1, 0, 0, 2, 0, 0), (900002, 'https://www.example.com/', 1, 0, 1, 2, 0, 1),
                (900003, 'https://www.example.com/rattan/', 1, 0, 1, 2, 0, 1), (900004, 'http://example.com/old/', 1, 0, 1, 2, 0, 1);
            INSERT INTO responses (url_id, fetched_at, outcome, status_code, ttfb_ms, response_ms, redirect_url_id, is_html, title_count,
                                   meta_description_count, h1_count, is_noindex, is_nofollow, indexability, indexability_reason, outlink_count)
            VALUES (900001, 0, {(int)FetchOutcome.Redirect}, 301, 1, 1, 900002, 0, 0, 0, 0, 0, 0, 0, 0, 0);
            """);
        var home = new UrlLookup(900002, "https://www.example.com/", null);
        var page = new UrlLookup(900003, "https://www.example.com/rattan/", null);

        // A path is on the origin the start URL's redirects end at, not the start URL's: "/" is the home page, not the redirect.
        Assert.Equal(home, UrlGridReader.Lookup(copy, "/"));
        Assert.Equal(page, UrlGridReader.Lookup(copy, "/rattan/"));

        // Only on the start URL's origin, it's found there, as another version of the URL asked for.
        Assert.Equal(new UrlLookup(900004, "http://example.com/old/", "https://www.example.com/old/"), UrlGridReader.Lookup(copy, "/old/"));

        // A URL on another version of the site is found on the version the crawl has, saying which was asked for.
        Assert.Equal(page with { Requested = "https://example.com/rattan/" }, UrlGridReader.Lookup(copy, "https://example.com/rattan/"));
        Assert.Equal(page with { Requested = "http://www.example.com/rattan/" }, UrlGridReader.Lookup(copy, "http://www.example.com/rattan/"));
        Assert.Equal(page with { Requested = "https://example.com/rattan/" }, UrlGridReader.Lookup(copy, "example.com/rattan/"));
        Assert.Equal(page, UrlGridReader.Lookup(copy, "www.example.com/rattan/"));
        Assert.Equal(page, UrlGridReader.Lookup(copy, "//www.example.com/rattan/"));
        Assert.Equal(home with { Requested = "https://example.com/" }, UrlGridReader.Lookup(copy, "example.com"));

        // The URL as asked comes first, the seed included; another site's URL isn't looked for on this one's versions.
        Assert.Equal(new UrlLookup(900001, "http://example.com/", null), UrlGridReader.Lookup(copy, "http://example.com/"));
        Assert.Null(UrlGridReader.Lookup(copy, "https://example.org/rattan/"));
        Assert.Null(UrlGridReader.Lookup(copy, "/no/such/page"));
    }

    /// <summary>The plan SQLite runs for a query's page, as its explanation reads.</summary>
    private string Plan(UrlQuery query)
    {
        List<(string Sql, IReadOnlyList<KeyValuePair<string, object>> Parameters)> ran = [];
        GridPlanner.Ran = (sql, parameters) => ran.Add((sql, parameters));
        try
        {
            UrlGridReader.Query(crawl.Path, query with { Limit = 5 });
        }
        finally
        {
            GridPlanner.Ran = null;
        }

        var (last, values) = ran[^1];
        using var connection = CrawlDatabase.OpenReader(crawl.Path);

        // The reader's own functions, which a plan only names.
        connection.CreateFunction<long, long?, bool>(ReaderFunctions.IN_SET, (_, _) => false);
        connection.CreateFunction<string, string?, bool>(ReaderFunctions.REGEX, (_, _) => false);
        connection.CreateFunction<string, string?, int>(ReaderFunctions.RESOURCE_TYPE, (_, _) => 0);
        using var command = connection.CreateCommand();
        command.CommandText = "EXPLAIN QUERY PLAN " + last;
        foreach (var (name, value) in values)
        {
            command.Parameters.AddWithValue(name, value);
        }

        using var reader = command.ExecuteReader();
        List<string> steps = [];
        while (reader.Read())
        {
            steps.Add(reader.GetString(3));
        }

        return string.Join(" ; ", steps);
    }

    private Dictionary<int, Dictionary<string, object?>> ReadAll(UrlQuery query, string? path = null)
    {
        Dictionary<int, Dictionary<string, object?>> rows = [];
        for (var offset = 0; ; offset += query.Limit)
        {
            var page = UrlGridReader.Query(path ?? crawl.Path, query with { Offset = offset });
            foreach (var row in page.Rows)
            {
                rows[row.Id] = page.Columns.Select((column, index) => (column, row.Values[index])).ToDictionary(cell => cell.column, cell => cell.Item2);
            }

            if (page.Next is null)
            {
                return rows.OrderBy(row => row.Key).ToDictionary();
            }
        }
    }

    private List<int> ByOffset(UrlQuery query, ReadCache cache, string? path = null)
    {
        List<int> ids = [];
        for (var offset = 0; ; offset += PAGE)
        {
            var page = UrlGridReader.Query(path ?? crawl.Path, query with { Offset = offset, Limit = PAGE }, cache);
            Assert.Equal(offset, page.Offset);
            ids.AddRange(page.Rows.Select(row => row.Id));
            if (page.Next is null)
            {
                Assert.Equal(page.Matching, ids.Count);
                return ids;
            }
        }
    }

    private List<int> ByCursor(UrlQuery query, ReadCache cache, string? path = null)
    {
        List<int> ids = [];
        string? after = null;
        do
        {
            var page = UrlGridReader.Query(path ?? crawl.Path, query with { After = after, Limit = PAGE }, cache);
            Assert.Equal(after is null ? 0 : null, page.Offset);
            ids.AddRange(page.Rows.Select(row => row.Id));
            after = page.Next;
            if (after is not null)
            {
                GridWalks.AssertGoesOn(page.Rows.Count, ids.Count, page.Matching);
            }
        }
        while (after is not null);

        return ids;
    }

    private static bool Same(object? expected, object? actual) => expected switch
    {
        null => actual is null,
        long number => actual is long read && read == number,
        _ => expected.Equals(actual)
    };
}
