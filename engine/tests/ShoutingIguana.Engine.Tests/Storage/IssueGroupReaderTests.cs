using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Tests.Support;
using static ShoutingIguana.Engine.Tests.Support.GridQueries;

namespace ShoutingIguana.Engine.Tests.Storage;

/// <summary>
/// A check's issues grouped as the catalog says, against the reference: each group's URLs, share and template hint, in
/// order and paged; a check's rows as a URL grid, all of them or one group's, with the issue's own columns, filtered and
/// sorted as the URL grid is; and no groups or rows for a check that wasn't run, or a crawl never analysed.
/// </summary>
[Collection(GridCrawlCollection.NAME)]
public sealed class IssueGroupReaderTests(GridCrawl crawl)
{
    private const int PAGE = 5;

    [Fact]
    public void EveryCheckGroupsAsTheCatalogSays()
    {
        var checks = Checks();
        Assert.Equal(Enum.GetValues<IssueGrouping>(), checks.Select(check => IssueCatalog.Find(check)!.Grouping).Distinct().Order());
        foreach (var check in checks)
        {
            var grouping = IssueCatalog.Find(check)!.Grouping;
            var total = Flagged(check).Count;
            var page = IssueGroupReader.ReadGroups(crawl.Path, check, 0, 1_000)!;
            Assert.Equal(check, page.CheckId);
            Assert.Equal(grouping, page.Grouping);
            Assert.Equal(total, page.Total);
            if (grouping == IssueGrouping.None)
            {
                Assert.Equal(0, page.GroupCount);
                Assert.Empty(page.Groups);
                continue;
            }

            var expected = ExpectedGroups(check, grouping);
            Assert.Equal(expected, page.Groups);
            Assert.Equal(expected.Count, page.GroupCount);
            Assert.Equal(total, page.Groups.Sum(group => group.Pages));

            // Paged, the same groups in the same order.
            List<IssueGroup> paged = [];
            for (var offset = 0; offset < expected.Count; offset += 2)
            {
                var part = IssueGroupReader.ReadGroups(crawl.Path, check, offset, 2)!;
                Assert.Equal(offset, part.Offset);
                Assert.Equal(expected.Count, part.GroupCount);
                paged.AddRange(part.Groups);
            }

            Assert.Equal(expected, paged);
        }
    }

    /// <summary>
    /// A check grouped by the other URL names one on every issue, or its groups would fold into one. One grouped by detail
    /// may leave it out: no detail is a group of its own (robots.txt's rules, against robots.txt that couldn't be read).
    /// </summary>
    [Fact]
    public void ACheckGroupedByTheOtherUrlAlwaysNamesOne()
    {
        foreach (var check in Checks().Where(check => IssueCatalog.Find(check)!.Grouping == IssueGrouping.RelatedUrl))
        {
            Assert.All(Flagged(check), issue => Assert.True(issue.Related is not null, $"{check} on URL {issue.Url} names no other URL"));
        }
    }

    /// <summary>Each analysis indexes a check's issues as they're grouped, which the groups read without the table.</summary>
    [Fact]
    public void EachAnalysisIndexesTheIssuesAsTheyAreGrouped()
    {
        using var connection = CrawlDatabase.OpenReader(crawl.Path);
        using var command = connection.CreateCommand();
        command.CommandText = "EXPLAIN QUERY PLAN SELECT detail, related_url_id, COUNT(*) FROM issues WHERE issue = 'title-duplicate' GROUP BY detail, related_url_id";
        using var reader = command.ExecuteReader();
        List<string> plan = [];
        while (reader.Read())
        {
            plan.Add(reader.GetString(3));
        }

        Assert.Contains(plan, step => step.Contains("COVERING INDEX ix_issues_group", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(IssueGrouping.PageDetail, 10, 20, false, true)]
    [InlineData(IssueGrouping.PageDetail, 9, 9, false, false)]
    [InlineData(IssueGrouping.PageDetail, 10, 21, false, false)]
    [InlineData(IssueGrouping.ResponseDetail, 50, 50, false, false)]
    [InlineData(IssueGrouping.ResponseDetail, 50, 60, true, true)]
    [InlineData(IssueGrouping.RelatedUrl, 10, 20, true, true)]
    [InlineData(IssueGrouping.RelatedUrl, 30, 30, false, false)]
    public void ALikelyTemplateHoldsHalfTheCheckAndTenUrlsAndSharesItsCause(IssueGrouping grouping, long pages, long total, bool sharesUrl, bool expected) =>
        Assert.Equal(expected, IssueGroupReader.IsLikelyTemplate(grouping, pages, total, sharesUrl));

    /// <summary>The same fault in the HTML of most of a check's pages is one group, a likely template fault, whose rows open alone.</summary>
    [Fact]
    public void ATemplateFaultIsOneGroup()
    {
        var copy = crawl.Copy();
        Change(copy, """
            INSERT INTO issues (issue, url_id, detail, related_url_id)
            SELECT 'open-graph-missing', s.url_id, 'Missing og:image', NULL FROM url_summary s JOIN urls u ON u.id = s.url_id
            WHERE s.resource_type = 0 AND s.status_code = 200 AND u.url LIKE '%/p/1__'
              AND NOT EXISTS (SELECT 1 FROM issues i WHERE i.issue = 'open-graph-missing' AND i.url_id = s.url_id);
            UPDATE issue_types SET url_count = (SELECT COUNT(*) FROM issues WHERE issue = 'open-graph-missing') WHERE id = 'open-graph-missing';
            """);
        var page = IssueGroupReader.ReadGroups(copy, "open-graph-missing", 0, 100)!;
        var template = page.Groups[0];
        Assert.Equal("Missing og:image", template.Detail);
        Assert.Null(template.RelatedUrlId);
        Assert.True(template is { LikelyTemplate: true, Pages: >= IssueGroupReader.TEMPLATE_MIN_PAGES });
        Assert.Equal((double)template.Pages / page.Total!.Value, template.Share);
        Assert.All(page.Groups.Skip(1), group => Assert.False(group.LikelyTemplate));

        var rows = IssueGroupReader.ReadRows(copy, "open-graph-missing", new UrlQuery { Columns = ["url", "detail"], Limit = 1_000 }, new IssueGroupKey("Missing og:image", null))!.Page!;
        Assert.Equal(template.Pages, rows.Total);
        Assert.Equal(template.Pages, rows.Rows.Count);
        Assert.All(rows.Rows, row => Assert.Equal("Missing og:image", row.Values[2]));
    }

    [Fact]
    public void ACheckRowsAreTheUrlsItFlaggedWithTheIssuesOwnColumns()
    {
        string[] columns = ["url", "detail", "relatedUrlId", "relatedUrl", "relatedStatusCode"];
        foreach (var check in Checks())
        {
            var expected = Flagged(check).OrderBy(issue => issue.Url)
                .Select(issue => new object?[]
                {
                    (long)issue.Url, crawl.Reference.Rows[issue.Url]["url"], issue.Detail, (long?)issue.Related,
                    issue.Related is { } related ? crawl.Reference.Rows[related]["url"] : null,
                    issue.Related is { } target ? crawl.Reference.Rows[target]["statusCode"] : null
                }).ToList();
            var page = IssueGroupReader.ReadRows(crawl.Path, check, new UrlQuery { Columns = columns, Limit = 1_000 })!;
            Assert.Equal(SummaryState.Present, page.Summary);
            Assert.Equal(["id", .. columns], page.Page!.Columns);
            Assert.Equal((long)expected.Count, page.Page.Total);
            Assert.Equal((long)expected.Count, page.Page.Matching);
            Assert.Equal(expected, page.Page.Rows.Select(row => row.Values.ToArray()));
        }

        // Without columns, a row has its URL, the detail and the other URL.
        Assert.Equal(["id", "url", "detail", "relatedUrl"], IssueGroupReader.ReadRows(crawl.Path, Checks()[0], new UrlQuery())!.Page!.Columns);
    }

    /// <summary>Every group's count opens its URLs: in the check's rows, and in the URL grid with the check filter's group.</summary>
    [Fact]
    public void AGroupsRowsAreItsUrls()
    {
        foreach (var check in Checks().Where(check => IssueCatalog.Find(check)!.Grouping != IssueGrouping.None))
        {
            var grouping = IssueCatalog.Find(check)!.Grouping;
            foreach (var group in IssueGroupReader.ReadGroups(crawl.Path, check, 0, 1_000)!.Groups)
            {
                var expected = Flagged(check).Where(issue => issue.Related == group.RelatedUrlId && (grouping == IssueGrouping.RelatedUrl || issue.Detail == group.Detail))
                    .Select(issue => issue.Url).Order().ToList();
                Assert.Equal(group.Pages, expected.Count);
                var key = new IssueGroupKey(group.Detail, group.RelatedUrlId);
                var rows = IssueGroupReader.ReadRows(crawl.Path, check, new UrlQuery { Limit = 1_000 }, key)!.Page!;
                Assert.Equal(expected, rows.Rows.Select(row => row.Id));
                Assert.Equal(group.Pages, rows.Total);

                var opened = UrlGridReader.Query(crawl.Path, new UrlQuery { Filters = [Names("check", FilterOperator.In, check) with { Group = key }], Limit = 1_000 });
                Assert.Equal(expected, opened.Rows.Select(row => row.Id));
            }
        }
    }

    [Fact]
    public void ACheckRowsFilterAndSortAsTheUrlGridDoes()
    {
        // The check with the most distinct details, so a sort by detail has runs of equal values to page through.
        var check = Checks().Where(check => IssueCatalog.Find(check)!.Grouping != IssueGrouping.None)
            .OrderByDescending(check => Flagged(check).Select(issue => issue.Detail).Distinct().Count()).ThenBy(check => check, StringComparer.Ordinal).First();
        var flagged = Flagged(check);
        Assert.True(flagged.Count > PAGE);
        foreach (var descending in new[] { false, true })
        {
            // Unknown last, and ties in the order of the last key, as in the URL grid.
            var expected = flagged.OrderBy(issue => issue.Detail is null).ThenBy(issue => issue.Detail, descending ? Comparer<string?>.Create((a, b) => string.CompareOrdinal(b, a)) : (IComparer<string?>)StringComparer.Ordinal)
                .ThenBy(issue => descending ? -issue.Url : issue.Url).Select(issue => issue.Url).ToList();
            var query = new UrlQuery { Columns = ["url", "detail"], Sort = [new UrlSort("detail", descending)] };
            Assert.Equal(expected, ByOffset(check, query));
            Assert.Equal(expected, ByCursor(check, query));
        }

        // The issue's own columns filter, alone and with the URL grid's.
        var detail = flagged.First(issue => issue.Detail is not null).Detail!;
        var byDetail = IssueGroupReader.ReadRows(crawl.Path, check, new UrlQuery { Filters = [Text("detail", FilterOperator.Eq, detail)], Limit = 1_000 })!.Page!;
        Assert.Equal(flagged.Where(issue => issue.Detail == detail).Select(issue => issue.Url).Order(), byDetail.Rows.Select(row => row.Id));
        Assert.Equal(flagged.Count, byDetail.Total);

        var related = IssueGroupReader.ReadRows(crawl.Path, check, new UrlQuery
        {
            Filters = [new UrlFilter("relatedStatusCode", FilterOperator.IsNotEmpty), Boolean("isInternal", true)], Limit = 1_000
        })!.Page!;
        Assert.Equal(flagged.Where(issue => issue.Related is { } id && crawl.Reference.Rows[id]["statusCode"] is not null && (bool)crawl.Reference.Rows[issue.Url]["isInternal"]!)
            .Select(issue => issue.Url).Order(), related.Rows.Select(row => row.Id));
    }

    [Fact]
    public void WithoutASummaryACheckRowsReadTheCrawlsOwnTables()
    {
        var copy = crawl.Copy();
        Stamp(copy);
        foreach (var check in Checks())
        {
            var query = new UrlQuery { Columns = ["url", "statusCode", "detail", "relatedUrlId", "relatedUrl", "relatedStatusCode", "inlinks"], Limit = 1_000 };
            var withSummary = IssueGroupReader.ReadRows(crawl.Path, check, query)!.Page!;
            var without = IssueGroupReader.ReadRows(copy, check, query)!;
            Assert.Equal(new SummaryState(false, SummaryUnavailableReason.CrawlChanged), without.Summary);
            Assert.Equal(["inlinks"], without.Page!.Unavailable);
            Assert.Equal(withSummary.Total, without.Page.Total);
            Assert.Equal(withSummary.Rows.Select(row => row.Values.Take(7).ToArray()), without.Page.Rows.Select(row => row.Values.ToArray()));
        }

        var refused = Assert.Throws<NeedsAnalysisException>(() => IssueGroupReader.ReadRows(copy, Checks()[0], new UrlQuery { Sort = [new UrlSort("inlinks")] }));
        Assert.Equal(["inlinks"], refused.Columns);

        // The groups are the analysis's, whatever the crawl did since.
        Assert.Equal(IssueGroupReader.ReadGroups(crawl.Path, Checks()[0], 0, 100)!.Groups, IssueGroupReader.ReadGroups(copy, Checks()[0], 0, 100)!.Groups);
    }

    [Fact]
    public void ACheckNotRunHasNoGroupsAndNoRowsNeverZero()
    {
        var copy = crawl.Copy();
        var check = Checks().First(check => IssueCatalog.Find(check)!.Grouping == IssueGrouping.RelatedUrl);
        Change(copy, "DELETE FROM issue_types WHERE id = $check", ("$check", check));
        var skipped = IssueGroupReader.ReadGroups(copy, check, 0, 100)!;
        Assert.Null(skipped.Total);
        Assert.Empty(skipped.Groups);
        Assert.Null(IssueGroupReader.ReadRows(copy, check, new UrlQuery())!.Page);

        // A check the engine doesn't know, and the file doesn't hold, is as good as not run.
        var unknown = IssueGroupReader.ReadGroups(crawl.Path, "no-such-check", 0, 100)!;
        Assert.Equal(IssueGrouping.None, unknown.Grouping);
        Assert.Null(unknown.Total);
        Assert.Null(IssueGroupReader.ReadRows(crawl.Path, "no-such-check", new UrlQuery())!.Page);

        var neverAnalysed = crawl.Copy();
        Change(neverAnalysed, "DELETE FROM analysis");
        Assert.Null(IssueGroupReader.ReadGroups(neverAnalysed, check, 0, 100));
        Assert.Null(IssueGroupReader.ReadRows(neverAnalysed, check, new UrlQuery()));
    }

    [Fact]
    public void AGroupsQueryThatCantRunIsRefused()
    {
        var none = Checks().First(check => IssueCatalog.Find(check)!.Grouping == IssueGrouping.None);
        var grouped = Checks().First(check => IssueCatalog.Find(check)!.Grouping != IssueGrouping.None);
        (string Field, Action Read)[] refused =
        [
            ("limit", () => IssueGroupReader.ReadGroups(crawl.Path, grouped, 0, 0)),
            ("limit", () => IssueGroupReader.ReadGroups(crawl.Path, grouped, 0, 1_001)),
            ("offset", () => IssueGroupReader.ReadGroups(crawl.Path, grouped, -1, 10)),
            ("group", () => IssueGroupReader.ReadRows(crawl.Path, none, new UrlQuery(), new IssueGroupKey(null, null))),
            ("sort[0].column", () => IssueGroupReader.ReadRows(crawl.Path, grouped, new UrlQuery { Sort = [new UrlSort("relatedUrl")] })),
            ("columns[0]", () => IssueGroupReader.ReadRows(crawl.Path, grouped, new UrlQuery { Columns = ["colour"] }))
        ];
        foreach (var (field, read) in refused)
        {
            Assert.Contains(field, Assert.Throws<UrlQueryException>(read).Errors.Keys);
        }
    }

    /// <summary>The checks that flagged something on the synthetic site, in catalog order.</summary>
    private List<string> Checks()
    {
        var flagged = crawl.Reference.Issues.SelectMany(entry => entry.Value).Select(issue => issue.Issue).ToHashSet(StringComparer.Ordinal);
        return [.. IssueCatalog.All.Select(check => check.Id).Where(flagged.Contains)];
    }

    /// <summary>The check's issues: the URL flagged, the detail and the other URL involved.</summary>
    private List<(int Url, string? Detail, int? Related)> Flagged(string check) =>
    [
        .. crawl.Reference.Issues.SelectMany(entry => entry.Value.Where(issue => issue.Issue == check).Select(issue => (entry.Key, issue.Detail, issue.Related)))
    ];

    /// <summary>The check's groups worked out from its issues: largest first, then by detail and other URL, unknown first.</summary>
    private List<IssueGroup> ExpectedGroups(string check, IssueGrouping grouping)
    {
        var issues = Flagged(check);
        return
        [
            .. issues.GroupBy(issue => (Detail: grouping == IssueGrouping.RelatedUrl ? null : issue.Detail, issue.Related))
                .Select(group =>
                {
                    var pages = group.LongCount();
                    var template = pages >= 10 && pages * 2 >= issues.Count && (group.Key.Related is not null || grouping == IssueGrouping.PageDetail);
                    return new IssueGroup(group.Key.Detail, group.Key.Related, group.Key.Related is { } id ? (string?)crawl.Reference.Rows[id]["url"] : null,
                        pages, (double)pages / issues.Count, template);
                })
                .OrderByDescending(group => group.Pages).ThenBy(group => group.Detail, StringComparer.Ordinal).ThenBy(group => group.RelatedUrlId)
        ];
    }

    private List<int> ByOffset(string check, UrlQuery query)
    {
        List<int> ids = [];
        for (var offset = 0; ; offset += PAGE)
        {
            var page = IssueGroupReader.ReadRows(crawl.Path, check, query with { Offset = offset, Limit = PAGE })!.Page!;
            ids.AddRange(page.Rows.Select(row => row.Id));
            if (page.Next is null)
            {
                return ids;
            }
        }
    }

    private List<int> ByCursor(string check, UrlQuery query)
    {
        List<int> ids = [];
        string? after = null;
        do
        {
            var page = IssueGroupReader.ReadRows(crawl.Path, check, query with { After = after, Limit = PAGE })!.Page!;
            ids.AddRange(page.Rows.Select(row => row.Id));
            after = page.Next;
        }
        while (after is not null);

        return ids;
    }
}
