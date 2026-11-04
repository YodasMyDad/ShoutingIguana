using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Tests.Support;
using static ShoutingIguana.Engine.Tests.Support.GridQueries;

namespace ShoutingIguana.Engine.Tests.Storage;

/// <summary>
/// The URL grid's facets against the reference: each value's count, in the facet's order, counted in one pass or facet by
/// facet alike, and the filter each value carries, which opens exactly that many URLs in the grid. Without a URL summary,
/// the facets that need one are left out and the others are counted from the crawl's own tables, as the summary would.
/// </summary>
[Collection(GridCrawlCollection.NAME)]
public sealed class UrlFacetsTests(GridCrawl crawl)
{
    /// <summary>The response time buckets, in order, and the lowest time in each.</summary>
    private static readonly (string Name, long Min, long? Max)[] RESPONSE_TIMES =
        [("0-249", 0, 249), ("250-499", 250, 499), ("500-999", 500, 999), ("1000-1999", 1_000, 1_999), ("2000+", 2_000, null)];

    private static readonly string[] SUMMARY_FACETS = ["inSitemap", "severity"];

    public static TheoryData<string, UrlFilter[], string?> Selections() => new()
    {
        { "the whole crawl", [], null },
        { "pages", [Names("type", FilterOperator.In, "page")], null },
        { "a rare filter", [Names("statusClass", FilterOperator.In, "4xx")], null },
        { "two filters", [Boolean("isInternal", true), Number("depth", FilterOperator.Lte, 2)], null },
        { "a search", [], "/p/1" },
        { "a check", [Names("check", FilterOperator.In, "title-too-long", "internal-client-error")], null },
        { "nothing", [Number("statusCode", FilterOperator.Eq, 999)], null }
    };

    [Theory]
    [MemberData(nameof(Selections))]
    public void EachValueCountsItsUrlsAndItsFilterOpensThem(string name, UrlFilter[] filters, string? search)
    {
        var matched = crawl.Reference.Matching(filters, search).ToList();
        Assert.True(matched.Count > 0 || name == "nothing", $"The case '{name}' should match something on the synthetic site");
        var cache = new ReadCache();

        // All at once (in one pass, when filtered), and each on its own.
        var together = UrlGridReader.Facets(crawl.Path, new UrlFacetQuery { Filters = filters, Search = search }, cache);
        Assert.Equal(matched.Count, together.Matching);
        Assert.Equal(crawl.Reference.Rows.Count, together.Total);
        Assert.Equal(SummaryState.Present, together.Summary);
        Assert.Empty(together.Unavailable);
        Assert.Equal(UrlGridReader.FACETS, together.Facets.Keys);
        foreach (var facet in UrlGridReader.FACETS)
        {
            var expected = Expected(facet, matched);
            Assert.Equal(expected, together.Facets[facet].Select(bucket => (bucket.Value, bucket.Count)));
            var alone = UrlGridReader.Facets(crawl.Path, new UrlFacetQuery { Filters = filters, Search = search, Facets = [facet] }, new ReadCache());
            Assert.Equal([facet], alone.Facets.Keys);
            Assert.Equal(expected, alone.Facets[facet].Select(bucket => (bucket.Value, bucket.Count)));
            Assert.Equal(Describe(together.Facets[facet]), Describe(alone.Facets[facet]));

            // Every count opens its URLs.
            foreach (var bucket in together.Facets[facet])
            {
                var opened = UrlGridReader.Query(crawl.Path, new UrlQuery { Filters = [.. filters, bucket.Filter], Search = search, Limit = 1 }, cache);
                Assert.True(bucket.Count == opened.Matching, $"{facet} {bucket.Value ?? "null"}: counted {bucket.Count}, its filter opens {opened.Matching}");
            }
        }
    }

    [Fact]
    public void ResponseTimesAreBucketedWithTheirBounds()
    {
        var buckets = UrlGridReader.Facets(crawl.Path, new UrlFacetQuery { Facets = ["responseTime"] }).Facets["responseTime"];
        Assert.NotEmpty(buckets);
        foreach (var bucket in buckets)
        {
            if (bucket.Value is null)
            {
                Assert.Null(bucket.Min);
                Assert.Null(bucket.Max);
                Assert.Equal(new UrlFilter("responseMs", FilterOperator.IsEmpty), bucket.Filter);
                continue;
            }

            var range = RESPONSE_TIMES.Single(range => range.Name == (string)bucket.Value);
            Assert.Equal(range.Min, bucket.Min);
            Assert.Equal(range.Max, bucket.Max);
            Assert.Equal(range.Max is null ? FilterOperator.Gte : FilterOperator.Between, bucket.Filter.Operator);
        }
    }

    [Fact]
    public void CountsAreKeptWhileTheCrawlStandsStill()
    {
        var cache = new ReadCache();
        UrlFacetQuery[] queries =
        [
            new(),
            new() { Filters = [Names("type", FilterOperator.In, "page")] },
            new() { Filters = [Names("type", FilterOperator.In, "page")], Facets = ["depth"] }
        ];
        foreach (var query in queries)
        {
            var first = UrlGridReader.Facets(crawl.Path, query, cache);
            var kept = cache.Count;
            var again = UrlGridReader.Facets(crawl.Path, query, cache);
            Assert.Equal(kept, cache.Count);
            Assert.Equal(first.Facets.Keys, again.Facets.Keys);
            Assert.All(first.Facets.Keys, facet => Assert.Equal(Describe(first.Facets[facet]), Describe(again.Facets[facet])));
        }
    }

    [Fact]
    public void WithoutASummaryTheFacetsThatNeedItAreLeftOut()
    {
        var copy = crawl.Copy();
        Stamp(copy);
        UrlFilter[][] selections = [[], [Names("type", FilterOperator.In, "page")], [Names("statusClass", FilterOperator.In, "3xx", "4xx")]];
        foreach (var filters in selections)
        {
            var withSummary = UrlGridReader.Facets(crawl.Path, new UrlFacetQuery { Filters = filters });
            var without = UrlGridReader.Facets(copy, new UrlFacetQuery { Filters = filters });
            Assert.Equal(new SummaryState(false, SummaryUnavailableReason.CrawlChanged), without.Summary);
            Assert.Equal(SUMMARY_FACETS, without.Unavailable);
            Assert.Equal(UrlGridReader.FACETS.Except(SUMMARY_FACETS), without.Facets.Keys);
            Assert.Equal(withSummary.Matching, without.Matching);

            // The crawl's own tables count as the summary does, and each count still opens its URLs.
            foreach (var facet in without.Facets.Keys)
            {
                Assert.Equal(Describe(withSummary.Facets[facet]), Describe(without.Facets[facet]));
                foreach (var bucket in without.Facets[facet])
                {
                    Assert.Equal(bucket.Count, UrlGridReader.Query(copy, new UrlQuery { Filters = [.. filters, bucket.Filter], Limit = 1 }).Matching);
                }
            }
        }

        // Asked for alone, a facet that needs the summary is only listed as unavailable; a filter that needs it is refused.
        var severity = UrlGridReader.Facets(copy, new UrlFacetQuery { Facets = ["severity"] });
        Assert.Equal(["severity"], severity.Unavailable);
        Assert.Empty(severity.Facets);
        var refused = Assert.Throws<NeedsAnalysisException>(() =>
            UrlGridReader.Facets(copy, new UrlFacetQuery { Filters = [Number("inlinks", FilterOperator.Gt, 0)], Facets = ["type"] }));
        Assert.Equal(SummaryUnavailableReason.CrawlChanged, refused.Reason);
        Assert.Equal(["inlinks"], refused.Columns);
    }

    /// <summary>
    /// A value no column has, which only a crafted file holds (an indexability reason past the enum's or negative, a negative
    /// depth, status or time, an in-sitemap that's neither true nor false), counts as unknown however the facets are counted,
    /// in one pass or each on its own, and never in another value's count, whose filter still opens exactly its URLs.
    /// </summary>
    [Fact]
    public void ACraftedValueCountsAsUnknownAndNeverAsAnother()
    {
        var copy = crawl.Copy();
        var pages = crawl.Reference.Rows.Where(row => (string?)row.Value["type"] == "page" && row.Value["statusCode"] is 200L).Select(row => row.Key).Take(4).ToList();
        Change(copy, $"""
            UPDATE url_summary SET indexability_reason = -16 WHERE url_id = {pages[0]};
            UPDATE url_summary SET indexability_reason = 99, depth = -3 WHERE url_id = {pages[1]};
            UPDATE url_summary SET response_ms = -5, in_sitemap = 2 WHERE url_id = {pages[2]};
            UPDATE url_summary SET status_code = -5 WHERE url_id = {pages[3]};
            """);
        UrlFilter[] filters = [Boolean("isInternal", true)];
        var together = UrlGridReader.Facets(copy, new UrlFacetQuery { Filters = filters });
        foreach (var facet in UrlGridReader.FACETS.Where(facet => facet != "severity"))
        {
            var alone = UrlGridReader.Facets(copy, new UrlFacetQuery { Filters = filters, Facets = [facet] }).Facets[facet];
            Assert.Equal(Describe(alone), Describe(together.Facets[facet]));
            Assert.Equal(together.Matching, together.Facets[facet].Sum(bucket => bucket.Count));
            foreach (var bucket in together.Facets[facet].Where(bucket => bucket.Value is not null))
            {
                var opened = UrlGridReader.Query(copy, new UrlQuery { Filters = [.. filters, bucket.Filter], Limit = 1 }).Matching;
                Assert.True(bucket.Count == opened, $"{facet} {bucket.Value}: counted {bucket.Count}, its filter opens {opened}");
            }
        }

        // A status code that can't be one is still a status code: the other class, as its column says.
        Assert.Equal(UrlGridReader.Query(copy, new UrlQuery { Filters = [.. filters, Names("statusClass", FilterOperator.In, "other")] }).Matching,
            together.Facets["statusClass"].Single(bucket => (string?)bucket.Value == "other").Count);
    }

    /// <summary>A status outside 200 to 599, a server's own, is the class "other": filtered and counted alike, with or without a summary.</summary>
    [Fact]
    public void AnyOtherStatusIsTheOtherClass()
    {
        var copy = crawl.Copy();
        var id = crawl.Reference.Rows.First(row => row.Value["statusCode"] is 200L && (string?)row.Value["type"] == "page").Key;
        Change(copy, $"UPDATE responses SET status_code = 999 WHERE url_id = {id}; UPDATE url_summary SET status_code = 999 WHERE url_id = {id}");
        UrlFilter[] other = [Names("statusClass", FilterOperator.In, "other")];
        foreach (var stamped in new[] { false, true })
        {
            if (stamped)
            {
                Stamp(copy);
            }

            Assert.Equal([id], UrlGridReader.Query(copy, new UrlQuery { Filters = other }).Rows.Select(row => row.Id));
            Assert.Equal(1, UrlGridReader.Facets(copy, new UrlFacetQuery { Facets = ["statusClass"] }).Facets["statusClass"].Single(bucket => (string?)bucket.Value == "other").Count);
            var pages = UrlGridReader.Facets(copy, new UrlFacetQuery { Filters = [Names("type", FilterOperator.In, "page")] });
            Assert.Equal(1, pages.Facets["statusClass"].Single(bucket => (string?)bucket.Value == "other").Count);
            Assert.Equal(1, pages.Facets["statusCode"].Single(bucket => bucket.Value is 999L).Count);
        }
    }

    [Fact]
    public void AFacetQueryThatCantRunIsRefusedForTheFieldAtFault()
    {
        var unknown = Assert.Throws<UrlQueryException>(() => UrlGridReader.Facets(crawl.Path, new UrlFacetQuery { Facets = ["depth", "colour"] }));
        Assert.Equal(["facets[1]"], unknown.Errors.Keys);
        var filter = Assert.Throws<UrlQueryException>(() =>
            UrlGridReader.Facets(crawl.Path, new UrlFacetQuery { Filters = [Text("statusCode", FilterOperator.Contains, "4")], Search = new string('a', 1_001) }));
        Assert.Equal(["filters[0].op", "search"], filter.Errors.Keys.Order(StringComparer.Ordinal));
    }

    /// <summary>A facet's values with their counts among these URLs, worked out from the reference, in the facet's order.</summary>
    private List<(object? Value, long Count)> Expected(string facet, IReadOnlyCollection<int> ids)
    {
        var rows = ids.Select(id => crawl.Reference.Rows[id]).ToList();
        if (facet == "severity")
        {
            (string Name, string Column, bool None)[] severities = [("error", "errors", false), ("warning", "warnings", false), ("notice", "notices", false), ("none", "issues", true)];
            return
            [
                .. severities.Select(severity => ((object?)severity.Name, (long)rows.Count(row => (long)row[severity.Column]! > 0 != severity.None)))
                    .Where(bucket => bucket.Item2 > 0)
            ];
        }

        object? Value(Dictionary<string, object?> row) => facet == "responseTime"
            ? row["responseMs"] is long ms ? RESPONSE_TIMES.Last(range => ms >= range.Min).Name : null
            : row[facet];

        return
        [
            .. rows.GroupBy(Value).Select(group => (group.Key, (long)group.Count()))
                .OrderBy(bucket => bucket.Key is null).ThenBy(bucket => Rank(facet, bucket.Key))
        ];
    }

    /// <summary>Where a value comes in its facet: an enum's by its place among the column's values, a number by itself.</summary>
    private static long Rank(string facet, object? value) => value switch
    {
        null => long.MaxValue,
        bool flag => flag ? 1 : 0,
        long number => number,
        string name when facet == "responseTime" => RESPONSE_TIMES.Select(range => range.Name).ToList().IndexOf(name),
        string name => UrlGridReader.Columns.Single(column => column.Id == facet).Values.ToList().IndexOf(name),
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    /// <summary>A facet's values as text, their filters included, to compare two reads of it.</summary>
    private static List<string> Describe(IEnumerable<FacetBucket> buckets) =>
    [
        .. buckets.Select(bucket => $"{bucket.Value ?? "null"} {bucket.Count} [{bucket.Min} {bucket.Max}] {bucket.Filter.Column} {bucket.Filter.Operator} " +
                                    $"{bucket.Filter.Value?.Text}{bucket.Filter.Value?.Number}{bucket.Filter.Value?.Boolean} " +
                                    $"{string.Join(",", bucket.Filter.Values?.Select(value => value.Text) ?? [])} {bucket.Filter.Min} {bucket.Filter.Max}")
    ];
}
