using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Tests.Support;
using static ShoutingIguana.Engine.Tests.Support.GridQueries;

namespace ShoutingIguana.Engine.Tests.Storage;

/// <summary>
/// The checks the user hid, left out of the URL grid's issue counts (<c>issues</c>, <c>errors</c>, <c>warnings</c>,
/// <c>notices</c>) as every other count in the app leaves them out: in the values read, the sorts, the filters, the
/// severity facet and the counts kept between reads, against a reference worked out without those checks (round 5 review).
/// </summary>
[Collection(GridCrawlCollection.NAME)]
public sealed class HiddenChecksGridTests(GridCrawl crawl)
{
    private const int PAGE = 13;

    private static readonly string[] ISSUE_COLUMNS = ["issues", "errors", "warnings", "notices"];

    /// <summary>The check of each severity that flagged the most URLs on the synthetic site: hiding them changes every column.</summary>
    private IReadOnlyList<string> Hidden() =>
    [
        .. crawl.Reference.Issues.Values.SelectMany(issues => issues.Select(issue => issue.Issue))
            .GroupBy(issue => issue, StringComparer.Ordinal)
            .GroupBy(group => IssueCatalog.Find(group.Key)!.Severity)
            .Select(severity => severity.OrderByDescending(group => group.Count()).ThenBy(group => group.Key, StringComparer.Ordinal).First().Key)
            .Order(StringComparer.Ordinal)
    ];

    [Fact]
    public void TheHiddenChecksCoverEverySeverity()
    {
        var hidden = Hidden();
        Assert.Equal(3, hidden.Select(id => IssueCatalog.Find(id)!.Severity).Distinct().Count());
        var reference = crawl.Reference.WithoutChecks(hidden);
        foreach (var column in ISSUE_COLUMNS)
        {
            Assert.Contains(crawl.Reference.Rows.Keys, id => !Equals(crawl.Reference.Rows[id][column], reference.Rows[id][column]));
        }
    }

    [Fact]
    public void TheIssueCountsLeaveTheHiddenChecksOut()
    {
        var hidden = Hidden();
        var reference = crawl.Reference.WithoutChecks(hidden);
        var rows = ReadAll(new UrlQuery { Columns = ["url", .. ISSUE_COLUMNS], HiddenChecks = hidden, Limit = 1_000 });
        Assert.Equal(reference.Rows.Keys.Order(), rows.Keys);
        foreach (var (id, values) in rows)
        {
            foreach (var column in ISSUE_COLUMNS)
            {
                Assert.True(Equals(reference.Rows[id][column], values[column]), $"URL {id} {column}: expected {reference.Rows[id][column]}, read {values[column]}");
            }
        }

        // Without hidden checks, the columns are the summary's, as before.
        var plain = ReadAll(new UrlQuery { Columns = ["url", .. ISSUE_COLUMNS], Limit = 1_000 });
        Assert.All(plain, row => Assert.All(ISSUE_COLUMNS, column => Assert.Equal(crawl.Reference.Rows[row.Key][column], row.Value[column])));
    }

    [Theory]
    [InlineData("issues", false)]
    [InlineData("issues", true)]
    [InlineData("errors", false)]
    [InlineData("errors", true)]
    [InlineData("notices", true)]
    public void TheIssueCountsSortWithoutTheHiddenChecks(string column, bool descending)
    {
        var hidden = Hidden();
        var reference = crawl.Reference.WithoutChecks(hidden);
        UrlSort[] sort = [new(column, descending)];
        var expected = reference.Order(sort, reference.Rows.Keys);
        Assert.NotEqual(expected, crawl.Reference.Order(sort, crawl.Reference.Rows.Keys));
        var query = new UrlQuery { Sort = sort, HiddenChecks = hidden };
        var cache = new ReadCache();
        Assert.Equal(expected, ByOffset(query, cache));
        Assert.Equal(expected, ByCursor(query, cache));
        foreach (var offset in new[] { expected.Count / 2, expected.Count / 2 + 1, expected.Count - PAGE })
        {
            var page = UrlGridReader.Query(crawl.Path, query with { Offset = offset, Limit = PAGE }, cache);
            Assert.Equal(expected.Skip(offset).Take(PAGE), page.Rows.Select(row => row.Id));
        }

        // A second key after it, as the grid sorts by several.
        UrlSort[] two = [new(column, descending), new("url")];
        Assert.Equal(reference.Order(two, reference.Rows.Keys), ByCursor(query with { Sort = two }, cache));
    }

    public static TheoryData<string, UrlFilter[]> Filters() => new()
    {
        { "errors gte 1", [Number("errors", FilterOperator.Gte, 1)] },
        { "issues eq 0", [Number("issues", FilterOperator.Eq, 0)] },
        { "warnings between", [new UrlFilter("warnings", FilterOperator.Between) { Min = 1, Max = 2 }] },
        { "notices ne 0", [Number("notices", FilterOperator.Ne, 0)] },
        { "with another filter", [Names("type", FilterOperator.In, "page"), Number("errors", FilterOperator.Eq, 0)] }
    };

    [Theory]
    [MemberData(nameof(Filters))]
    public void TheIssueCountsFilterWithoutTheHiddenChecks(string name, UrlFilter[] filters)
    {
        var hidden = Hidden();
        var reference = crawl.Reference.WithoutChecks(hidden);
        var matched = reference.Matching(filters).ToHashSet();
        Assert.True(matched.Count > 0, name);
        Assert.False(matched.SetEquals(crawl.Reference.Matching(filters)), $"{name}: hiding the checks should change what it matches");
        foreach (var sort in new UrlSort[][] { [], [new UrlSort("errors", true)], [new UrlSort("url")] })
        {
            var expected = reference.Order(sort, matched);
            var query = new UrlQuery { Filters = filters, Sort = sort, HiddenChecks = hidden };
            var cache = new ReadCache();
            Assert.Equal(expected, ByOffset(query, cache));
            Assert.Equal(expected, ByCursor(query, cache));
            Assert.Equal(matched.Count, UrlGridReader.Query(crawl.Path, query with { Limit = 1 }).Matching);
        }
    }

    /// <summary>A count kept for a query is never given for the same query with checks hidden, nor the other way round.</summary>
    [Fact]
    public void AQueryWithHiddenChecksNeverSharesACountWithoutThem()
    {
        var hidden = Hidden();
        var reference = crawl.Reference.WithoutChecks(hidden);
        UrlFilter[] filters = [Number("errors", FilterOperator.Gte, 1)];
        var plain = new UrlQuery { Filters = filters, Limit = 1 };
        var hiding = plain with { HiddenChecks = hidden };
        var expectedPlain = crawl.Reference.Matching(filters).Count();
        var expectedHiding = reference.Matching(filters).Count();
        Assert.NotEqual(expectedPlain, expectedHiding);

        var cache = new ReadCache();
        Assert.Equal(expectedPlain, UrlGridReader.Query(crawl.Path, plain, cache).Matching);
        Assert.Equal(expectedHiding, UrlGridReader.Query(crawl.Path, hiding, cache).Matching);
        Assert.Equal(expectedPlain, UrlGridReader.Query(crawl.Path, plain, cache).Matching);

        // The other way round, on a cache of its own; and the order the ids are given in doesn't matter.
        var other = new ReadCache();
        Assert.Equal(expectedHiding, UrlGridReader.Query(crawl.Path, hiding with { HiddenChecks = [.. hidden.Reverse()] }, other).Matching);
        Assert.Equal(expectedPlain, UrlGridReader.Query(crawl.Path, plain, other).Matching);

        // A cursor from one isn't read on by the other: the rows after it are another query's.
        var first = UrlGridReader.Query(crawl.Path, new UrlQuery { Sort = [new UrlSort("errors", true)], Limit = PAGE }, cache);
        var refused = Assert.Throws<UrlQueryException>(() =>
            UrlGridReader.Query(crawl.Path, new UrlQuery { Sort = [new UrlSort("errors", true)], Limit = PAGE, After = first.Next, HiddenChecks = hidden }, cache));
        Assert.Contains("after", refused.Errors.Keys);
    }

    /// <summary>The severity facet counts the URLs with issues of each severity as the columns do, the hidden checks left out.</summary>
    [Fact]
    public void TheSeverityFacetLeavesTheHiddenChecksOut()
    {
        var hidden = Hidden();
        var reference = crawl.Reference.WithoutChecks(hidden);
        var cache = new ReadCache();
        foreach (var filters in new UrlFilter[][] { [], [Names("type", FilterOperator.In, "page")] })
        {
            var ids = reference.Matching(filters).ToList();
            List<(object?, long)> expected =
            [
                .. new[] { ("error", "errors"), ("warning", "warnings"), ("notice", "notices") }
                    .Select(severity => ((object?)severity.Item1, (long)ids.Count(id => (long)reference.Rows[id][severity.Item2]! > 0)))
                    .Append(("none", ids.Count(id => (long)reference.Rows[id]["issues"]! == 0)))
                    .Where(bucket => bucket.Item2 > 0)
            ];

            // Asked for alone, and with every facet (one pass over the matching rows when filtered); kept counts apart.
            foreach (var facets in new IReadOnlyList<string>[] { ["severity"], UrlGridReader.FACETS })
            {
                var plain = UrlGridReader.Facets(crawl.Path, new UrlFacetQuery { Filters = filters, Facets = facets }, cache).Facets["severity"];
                var hiding = UrlGridReader.Facets(crawl.Path, new UrlFacetQuery { Filters = filters, Facets = facets, HiddenChecks = hidden }, cache).Facets["severity"];
                Assert.Equal(expected, hiding.Select(bucket => (bucket.Value, bucket.Count)));
                Assert.NotEqual(expected, plain.Select(bucket => (bucket.Value, bucket.Count)));

                // Each bucket's filter opens its URLs in the grid with the same checks hidden.
                foreach (var bucket in hiding)
                {
                    var opened = UrlGridReader.Query(crawl.Path, new UrlQuery { Filters = [.. filters, bucket.Filter], HiddenChecks = hidden, Limit = 1 }, cache).Matching;
                    Assert.Equal(bucket.Count, opened);
                }
            }
        }
    }

    /// <summary>
    /// An id the catalog doesn't have (a check hidden by another version, kept in the app's settings) is taken, as the
    /// report takes it, and hides nothing the crawl doesn't have under it: a stale id never breaks the grid.
    /// </summary>
    [Fact]
    public void AnUnknownHiddenCheckIsTakenAndHidesNothingItDoesNotHave()
    {
        var hidden = Hidden();
        var reference = crawl.Reference.WithoutChecks(hidden);
        IReadOnlyList<string> stale = [.. hidden, "no-such-check", "a-check-gone"];
        var rows = ReadAll(new UrlQuery { Columns = ["url", .. ISSUE_COLUMNS], HiddenChecks = stale, Limit = 1_000 });
        Assert.All(rows, row => Assert.All(ISSUE_COLUMNS, column => Assert.Equal(reference.Rows[row.Key][column], row.Value[column])));

        // Only unknown ones: every count as it is.
        var none = ReadAll(new UrlQuery { Columns = ["url", .. ISSUE_COLUMNS], HiddenChecks = ["no-such-check"], Limit = 1_000 });
        Assert.All(none, row => Assert.All(ISSUE_COLUMNS, column => Assert.Equal(crawl.Reference.Rows[row.Key][column], row.Value[column])));

        // As many as the settings may hold, nearly all stale.
        IReadOnlyList<string> many = [.. hidden, .. Enumerable.Range(0, UrlQueryCompiler.MAX_HIDDEN_CHECKS - hidden.Count).Select(index => $"gone-{index}")];
        UrlFilter[] filters = [Number("errors", FilterOperator.Gte, 1)];
        Assert.Equal(reference.Matching(filters).Count(), UrlGridReader.Query(crawl.Path, new UrlQuery { Filters = filters, HiddenChecks = many, Limit = 1 }).Matching);
        var severity = UrlGridReader.Facets(crawl.Path, new UrlFacetQuery { Facets = ["severity"], HiddenChecks = stale }).Facets["severity"];
        Assert.Equal(reference.Rows.Values.Count(row => (long)row["errors"]! > 0), severity.Single(bucket => (string?)bucket.Value == "error").Count);
    }

    [Fact]
    public void AHiddenChecksListThatCantBeOneIsAFieldError()
    {
        var empty = Assert.Throws<UrlQueryException>(() => UrlGridReader.Query(crawl.Path, new UrlQuery { HiddenChecks = ["title-too-long", null!] }));
        Assert.Equal(["hiddenChecks"], empty.Errors.Keys);

        var tooLong = Assert.Throws<UrlQueryException>(() =>
            UrlGridReader.Facets(crawl.Path, new UrlFacetQuery { HiddenChecks = [new string('x', UrlQueryCompiler.MAX_HIDDEN_CHECK_LENGTH + 1)] }));
        Assert.Equal(["hiddenChecks"], tooLong.Errors.Keys);
        UrlGridReader.Query(crawl.Path, new UrlQuery { HiddenChecks = [new string('x', UrlQueryCompiler.MAX_HIDDEN_CHECK_LENGTH)], Limit = 1 });

        var tooMany = Assert.Throws<UrlQueryException>(() =>
            UrlGridReader.Query(crawl.Path, new UrlQuery { HiddenChecks = [.. Enumerable.Repeat("title-too-long", UrlQueryCompiler.MAX_HIDDEN_CHECKS + 1)] }));
        Assert.Equal(["hiddenChecks"], tooMany.Errors.Keys);
        var links = Assert.Throws<UrlQueryException>(() => LinkGridReader.Query(crawl.Path, new UrlQuery { HiddenChecks = [null!] }));
        Assert.Equal(["hiddenChecks"], links.Errors.Keys);
    }

    /// <summary>A hidden check no URL has, or one the crawl's analysis didn't run, changes nothing.</summary>
    [Fact]
    public void AHiddenCheckWithNoIssuesChangesNothing()
    {
        var flagged = crawl.Reference.Issues.Values.SelectMany(issues => issues.Select(issue => issue.Issue)).ToHashSet(StringComparer.Ordinal);
        var quiet = IssueCatalog.All.First(definition => !flagged.Contains(definition.Id)).Id;
        var rows = ReadAll(new UrlQuery { Columns = ["url", .. ISSUE_COLUMNS], HiddenChecks = [quiet], Limit = 1_000 });
        Assert.All(rows, row => Assert.All(ISSUE_COLUMNS, column => Assert.Equal(crawl.Reference.Rows[row.Key][column], row.Value[column])));
    }

    /// <summary>
    /// The links grid shows the URL grid's columns at each end of a link: their issue counts leave the hidden checks out
    /// too, in the values, a filter, a sort by the target's, and the URL grid narrowed to a links view's targets.
    /// </summary>
    [Fact]
    public void ALinksViewReadsItsEndsIssueCountsWithoutTheHiddenChecks()
    {
        var hidden = Hidden();
        var links = LinkReference.Load(crawl.Path, crawl.Reference.WithoutChecks(hidden));
        var plain = LinkReference.Load(crawl.Path, crawl.Reference);
        UrlFilter[] filters = [Number("to.errors", FilterOperator.Gte, 1)];
        var sort = new UrlSort("to.issues", true);
        var matched = links.Matching(filters);
        Assert.NotEqual(plain.Matching(filters).Count, matched.Count);
        var expected = links.Order(matched, sort).Select(link => link.Id).ToList();

        string[] columns = ["to.issues", "to.errors", "from.warnings", "from.notices"];
        var cache = new ReadCache();
        List<long> read = [];
        string? after = null;
        do
        {
            var page = LinkGridReader.Query(crawl.Path, new UrlQuery { Columns = columns, Filters = filters, Sort = [sort], HiddenChecks = hidden, Limit = 97, After = after }, cache);
            foreach (var row in page.Rows)
            {
                read.Add(row.Id);
                foreach (var column in columns)
                {
                    var expectedValue = links.Value(links.ById[row.Id], column);
                    Assert.True(Equals(expectedValue, row.Values[page.Columns.ToList().IndexOf(column)]), $"link {row.Id} {column}: expected {expectedValue}");
                }
            }

            after = page.Next;
        }
        while (after is not null);

        Assert.Equal(expected, read);
        Assert.Equal(plain.Matching(filters).Count, LinkGridReader.Query(crawl.Path, new UrlQuery { Filters = filters, Limit = 1 }, cache).Matching);

        // The URL grid narrowed to the targets of those links: those whose own errors, the hidden checks left out, are some.
        var ends = new UrlLinkEnds(LinkEnd.To, filters, null);
        var targets = matched.Select(link => link.To).Distinct().Count();
        Assert.Equal(targets, UrlGridReader.Query(crawl.Path, new UrlQuery { LinkEnds = ends, HiddenChecks = hidden, Limit = 1 }, cache).Matching);
        Assert.Equal(plain.Matching(filters).Select(link => link.To).Distinct().Count(), UrlGridReader.Query(crawl.Path, new UrlQuery { LinkEnds = ends, Limit = 1 }, cache).Matching);
    }

    private Dictionary<int, Dictionary<string, object?>> ReadAll(UrlQuery query)
    {
        Dictionary<int, Dictionary<string, object?>> rows = [];
        for (var offset = 0; ; offset += query.Limit)
        {
            var page = UrlGridReader.Query(crawl.Path, query with { Offset = offset });
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

    private List<int> ByOffset(UrlQuery query, ReadCache cache)
    {
        List<int> ids = [];
        for (var offset = 0; ; offset += PAGE)
        {
            var page = UrlGridReader.Query(crawl.Path, query with { Offset = offset, Limit = PAGE }, cache);
            ids.AddRange(page.Rows.Select(row => row.Id));
            if (page.Next is null)
            {
                Assert.Equal(page.Matching, ids.Count);
                return ids;
            }
        }
    }

    private List<int> ByCursor(UrlQuery query, ReadCache cache)
    {
        List<int> ids = [];
        string? after = null;
        do
        {
            var page = UrlGridReader.Query(crawl.Path, query with { After = after, Limit = PAGE }, cache);
            ids.AddRange(page.Rows.Select(row => row.Id));
            after = page.Next;
        }
        while (after is not null);

        return ids;
    }
}
