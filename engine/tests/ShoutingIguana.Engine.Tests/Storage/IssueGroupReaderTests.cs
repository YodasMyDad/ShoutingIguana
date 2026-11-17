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

    /// <summary>A check's rows in crawl order, asked for: without an order they come by importance (<see cref="EvidenceReadTests"/>).</summary>
    private static readonly IReadOnlyList<UrlSort> CRAWL_ORDER = [new UrlSort("id")];

    [Fact]
    public void EveryCheckGroupsAsTheCatalogSays()
    {
        var checks = Checks();
        Assert.Equal(Enum.GetValues<IssueGrouping>(), checks.Select(check => IssueCatalog.Find(check)!.Grouping).Distinct().Order());
        foreach (var check in checks)
        {
            var grouping = IssueCatalog.Find(check)!.Grouping;
            var total = Flagged(check).Count;
            // Largest first, as asked (image-too-large's own is by size); what each group's URL is, EvidenceReadTests checks.
            var page = IssueGroupReader.ReadGroups(crawl.Path, check, 0, 1_000, IssueGroupSort.Pages)!;
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
            Assert.Equal(expected, page.Groups.Select(Bare));
            Assert.Equal(expected.Count, page.GroupCount);

            // A page is in a group for each URL its issue names, so a check grouped by the other URL can count it more than once.
            Assert.All(page.Groups, group => Assert.Equal(group.Pages, group.Urls));
            var pages = page.Groups.Sum(group => group.Urls);
            Assert.True(grouping == IssueGrouping.RelatedUrl ? pages >= total : pages == total, $"{check}: {pages} pages in groups, {total} flagged");

            // Paged, the same groups in the same order.
            List<IssueGroup> paged = [];
            for (var offset = 0; offset < expected.Count; offset += 2)
            {
                var part = IssueGroupReader.ReadGroups(crawl.Path, check, offset, 2, IssueGroupSort.Pages)!;
                Assert.Equal(offset, part.Offset);
                Assert.Equal(expected.Count, part.GroupCount);
                paged.AddRange(part.Groups.Select(Bare));
            }

            Assert.Equal(expected, paged);
        }
    }

    /// <summary>
    /// A check grouped by the other URL stores every issue under the URLs it names, its related URL among them, or under no
    /// other URL (0) when it names none: only a link to a section missing on the page itself names none. A duplicate is
    /// stored under its cluster's first page, which is stored under itself. One grouped by detail may leave the other URL
    /// out: no detail is a group of its own (robots.txt's rules, against robots.txt that couldn't be read).
    /// </summary>
    [Fact]
    public void ACheckGroupedByTheOtherUrlStoresEveryIssueUnderTheUrlsItNames()
    {
        string[] duplicates = ["title-duplicate", "description-duplicate", "h1-duplicate", "duplicate-content", "near-duplicate-content"];
        foreach (var check in Checks().Where(check => IssueCatalog.Find(check)!.Grouping == IssueGrouping.RelatedUrl))
        {
            foreach (var issue in Flagged(check))
            {
                var targets = crawl.Reference.Targets[(check, issue.Url)];
                if (duplicates.Contains(check))
                {
                    var first = Assert.Single(targets);
                    Assert.True(first <= issue.Url && crawl.Reference.Targets[(check, first)].SequenceEqual([first]), $"{check} on URL {issue.Url} names {first}");
                }
                else if (issue.Related is { } related)
                {
                    Assert.Contains(related, targets);
                    Assert.DoesNotContain(issue.Url, targets);
                }
                else
                {
                    Assert.Equal("broken-fragment-links", check);
                    Assert.Equal([0], targets);
                }
            }
        }

        // Every internal URL a page links to that answers with an error is named, as many as its detail counts, and nothing it
        // doesn't link to (a redirect to an error, a loop and a timeout are broken too).
        var links = TestCrawls.Rows(crawl.Path, $"SELECT from_id, to_id FROM links WHERE kind = 0 AND from_id <> to_id AND (flags & {(int)ShoutingIguana.Engine.Parsing.Models.LinkFlags.SamePage}) = 0")
            .Select(row => row.Split(" | ").Select(int.Parse).ToArray()).ToLookup(row => row[0], row => row[1]);
        foreach (var issue in Flagged("broken-internal-links"))
        {
            var targets = crawl.Reference.Targets[("broken-internal-links", issue.Url)];
            var errors = links[issue.Url].Where(to => (bool)crawl.Reference.Rows[to]["isInternal"]! && crawl.Reference.Rows[to]["outcome"] is "clientError" or "serverError");
            Assert.Superset(errors.ToHashSet(), targets.ToHashSet());
            Assert.Subset(links[issue.Url].ToHashSet(), targets.ToHashSet());
            Assert.StartsWith($"{targets.Count} broken URL", issue.Detail);
        }
    }

    /// <summary>
    /// An earlier engine's analysis stored only the first other URL, and leaves what a later one stored behind, as it doesn't
    /// know to clear it: its groups are its issues' own, by detail and first other URL.
    /// </summary>
    [Fact]
    public void AnEarlierEnginesAnalysisIsGroupedByItsIssuesOwn()
    {
        var copy = crawl.Copy();
        Change(copy, "UPDATE analysis SET analysis_version = 3");
        foreach (var check in Checks().Where(check => IssueCatalog.Find(check)!.Grouping != IssueGrouping.None))
        {
            var grouping = IssueCatalog.Find(check)!.Grouping;
            var issues = Flagged(check);
            List<IssueGroup> expected =
            [
                .. issues.GroupBy(issue => (Detail: grouping == IssueGrouping.RelatedUrl ? null : issue.Detail, issue.Related))
                    .Select(group =>
                    {
                        var pages = group.LongCount();
                        var template = IssueGroupReader.IsLikelyTemplate(grouping, pages, issues.Count, group.Key.Related is not null || check == "broken-fragment-links");
                        return new IssueGroup(group.Key.Detail, group.Key.Related, group.Key.Related is { } id ? (string?)crawl.Reference.Rows[id]["url"] : null,
                            pages, (double)pages / issues.Count, template);
                    })
                    .OrderByDescending(group => group.Pages).ThenBy(group => group.Detail, StringComparer.Ordinal).ThenBy(group => group.RelatedUrlId)
            ];
            Assert.Equal(expected, IssueGroupReader.ReadGroups(copy, check, 0, 1_000, IssueGroupSort.Pages)!.Groups.Select(Bare));
        }

        // Nor is a URL named by a later analysis's other URLs.
        var gone = crawl.Reference.Rows.Single(row => ((string)row.Value["url"]!).EndsWith("/plants/gone", StringComparison.Ordinal)).Key;
        var naming = crawl.Reference.Issues.Where(entry => entry.Key != gone).SelectMany(entry => entry.Value).Count(issue => issue.Related == gone);
        Assert.Equal(naming, UrlInspectorReader.Read(copy, gone)!.NamedBy!.Total);
    }

    /// <summary>
    /// The inspector gives each issue the key of a group it's in, which finds that group among the check's groups and among
    /// its rows: the other URL for a check grouped by it (the one the issue shows, when it's grouped under it), the fault for a
    /// check grouped by one, and nothing for a check that isn't grouped.
    /// </summary>
    [Fact]
    public void TheInspectorGivesEachIssueTheKeyOfItsGroup()
    {
        Dictionary<(string Check, IssueGroupKey Key), List<int>> members = [];
        foreach (var (url, issues) in crawl.Reference.Issues)
        {
            var inspected = UrlInspectorReader.Read(crawl.Path, url)!.Issues!;
            Assert.Equal(issues.Select(issue => issue.Issue).Order(StringComparer.Ordinal), inspected.Select(issue => issue.CheckId).Order(StringComparer.Ordinal));
            foreach (var issue in inspected)
            {
                var grouping = IssueCatalog.Find(issue.CheckId)!.Grouping;
                var stored = issues.Single(other => other.Issue == issue.CheckId);
                if (grouping == IssueGrouping.None)
                {
                    Assert.Null(issue.Group);
                    continue;
                }

                var keys = Keys(issue.CheckId, grouping, (url, stored.Detail, stored.Related)).ToList();
                Assert.Contains((issue.Group!.Detail, issue.Group.RelatedUrlId), keys);

                // The group of the URL the issue shows, when it's in that one.
                if (grouping == IssueGrouping.RelatedUrl && keys.Contains((null, stored.Related)))
                {
                    Assert.Equal(stored.Related, issue.Group.RelatedUrlId);
                }

                if (!members.TryGetValue((issue.CheckId, issue.Group), out var urls))
                {
                    members[(issue.CheckId, issue.Group)] = urls = [];
                }

                urls.Add(url);
            }
        }

        // Each key is one of its check's groups, whose rows hold every URL the inspector gave it for.
        Assert.Equal(Checks().Where(check => IssueCatalog.Find(check)!.Grouping != IssueGrouping.None), members.Keys.Select(key => key.Check).Distinct().Order(CatalogOrder()));
        foreach (var ((check, key), urls) in members)
        {
            var group = Assert.Single(IssueGroupReader.ReadGroups(crawl.Path, check, 0, 1_000)!.Groups, group => (group.Detail, group.RelatedUrlId) == (key.Detail, key.RelatedUrlId));
            var rows = IssueGroupReader.ReadRows(crawl.Path, check, new UrlQuery { Limit = 1_000 }, key)!.Page!;
            Assert.Equal(group.Pages, rows.Total);
            Assert.Subset(rows.Rows.Select(row => row.Id).ToHashSet(), urls.ToHashSet());
        }

        // A fault's key is the fault, not the page's own detail, which differs from page to page.
        foreach (var check in new[] { IssueCatalog.CANONICAL_RELATIVE, IssueCatalog.CANONICAL_INVALID, IssueCatalog.STRUCTURED_DATA_INVALID })
        {
            var (url, detail, _) = Flagged(check.Id).First();
            var issue = UrlInspectorReader.Read(crawl.Path, url)!.Issues!.Single(issue => issue.CheckId == check.Id);
            Assert.Equal(new IssueGroupKey(check.FaultOf!(detail!), null), issue.Group);
        }

        Assert.NotEqual(Flagged("structured-data-invalid").First().Detail, crawl.Reference.Faults[("structured-data-invalid", Flagged("structured-data-invalid").First().Url)]);

        // An analysis from before the groups were stored is grouped by each issue's own detail and other URL, and so keyed.
        var earlier = crawl.Copy();
        Change(earlier, "UPDATE analysis SET analysis_version = 3");
        foreach (var check in Checks().Where(check => IssueCatalog.Find(check)!.Grouping != IssueGrouping.None))
        {
            var grouping = IssueCatalog.Find(check)!.Grouping;
            var (url, detail, related) = Flagged(check).First();
            var key = UrlInspectorReader.Read(earlier, url)!.Issues!.Single(issue => issue.CheckId == check).Group;
            Assert.Equal(new IssueGroupKey(grouping == IssueGrouping.RelatedUrl ? null : detail, related), key);
            Assert.Contains(IssueGroupReader.ReadGroups(earlier, check, 0, 1_000)!.Groups, group => (group.Detail, group.RelatedUrlId) == (key!.Detail, key.RelatedUrlId));
        }

        // An analysis from before the index of the URLs each issue names reads the same keys through the table's own key.
        var unindexed = crawl.Copy();
        Change(unindexed, "DROP INDEX ix_issue_targets_url");
        var broken = Flagged("broken-internal-links").First().Url;
        Assert.Equal(UrlInspectorReader.Read(crawl.Path, broken)!.Issues!.Select(issue => issue.Group), UrlInspectorReader.Read(unindexed, broken)!.Issues!.Select(issue => issue.Group));
    }

    /// <summary>Each analysis indexes a check's issues as they're grouped, which the groups read without the table.</summary>
    [Fact]
    public void EachAnalysisIndexesTheIssuesAsTheyAreGrouped()
    {
        using var connection = CrawlDatabase.OpenReader(crawl.Path);
        using var command = connection.CreateCommand();
        List<string> Plan(string sql)
        {
            command.CommandText = "EXPLAIN QUERY PLAN " + sql;
            using var reader = command.ExecuteReader();
            List<string> plan = [];
            while (reader.Read())
            {
                plan.Add(reader.GetString(3));
            }

            return plan;
        }

        Assert.Contains(Plan("SELECT detail, related_url_id, COUNT(*) FROM issues WHERE issue = 'title-too-long' GROUP BY detail, related_url_id"),
            step => step.Contains("COVERING INDEX ix_issues_group", StringComparison.Ordinal));

        // The groups by other URL, and one group's URLs, on the key of the URLs each issue names; the issues naming a URL on
        // the index by named URL; a fault's groups on its key.
        Assert.Equal(["SEARCH issue_targets USING PRIMARY KEY (issue=?)"],
            Plan("SELECT related_url_id, COUNT(*) FROM issue_targets WHERE issue = 'title-duplicate' GROUP BY related_url_id"));
        Assert.All(Plan("SELECT url_id FROM issue_targets WHERE issue = 'title-duplicate' AND related_url_id = 7"), step =>
            Assert.Matches(@"^SEARCH issue_targets USING (PRIMARY KEY|COVERING INDEX ix_issue_targets_named) \((issue=\? AND related_url_id=\?|related_url_id=\? AND issue=\?)\)$", step));
        Assert.Equal(["SEARCH issue_targets USING COVERING INDEX ix_issue_targets_named (related_url_id=?)"],
            Plan("SELECT issue, COUNT(*) FROM issue_targets WHERE related_url_id = 7 AND url_id <> 7 GROUP BY issue"));
        Assert.Equal(["SEARCH issue_faults USING PRIMARY KEY (issue=?)"],
            Plan("SELECT fault, COUNT(*) FROM issue_faults WHERE issue = 'structured-data-invalid' GROUP BY fault"));
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
            // In crawl order, asked for: without an order, a check's rows come by importance (EvidenceReadTests).
            var page = IssueGroupReader.ReadRows(crawl.Path, check, new UrlQuery { Columns = columns, Sort = CRAWL_ORDER, Limit = 1_000 })!;
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
                var expected = Flagged(check).Where(issue => InGroup(check, grouping, issue, group)).Select(issue => issue.Url).Order().ToList();
                Assert.Equal(group.Pages, expected.Count);
                var key = new IssueGroupKey(group.Detail, group.RelatedUrlId);
                var rows = IssueGroupReader.ReadRows(crawl.Path, check, new UrlQuery { Sort = CRAWL_ORDER, Limit = 1_000 }, key)!.Page!;
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
        var byDetail = IssueGroupReader.ReadRows(crawl.Path, check, new UrlQuery { Filters = [Text("detail", FilterOperator.Eq, detail)], Sort = CRAWL_ORDER, Limit = 1_000 })!.Page!;
        Assert.Equal(flagged.Where(issue => issue.Detail == detail).Select(issue => issue.Url).Order(), byDetail.Rows.Select(row => row.Id));
        Assert.Equal(flagged.Count, byDetail.Total);

        var related = IssueGroupReader.ReadRows(crawl.Path, check, new UrlQuery
        {
            Filters = [new UrlFilter("relatedStatusCode", FilterOperator.IsNotEmpty), Boolean("isInternal", true)], Sort = CRAWL_ORDER, Limit = 1_000
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
            // The same order in both: by importance needs the summary's counts (without one it's by clicks and URL).
            var query = new UrlQuery { Columns = ["url", "statusCode", "detail", "relatedUrlId", "relatedUrl", "relatedStatusCode", "inlinks"], Sort = CRAWL_ORDER, Limit = 1_000 };
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
        Assert.Null(skipped.GroupCount);
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

    /// <summary>Check ids in the order the catalog lists them.</summary>
    private static Comparer<string> CatalogOrder()
    {
        var order = IssueCatalog.All.Select((check, index) => (check.Id, index)).ToDictionary(entry => entry.Id, entry => entry.index, StringComparer.Ordinal);
        return Comparer<string>.Create((one, other) => order[one].CompareTo(order[other]));
    }

    /// <summary>The check's issues: the URL flagged, the detail and the other URL involved.</summary>
    private List<(int Url, string? Detail, int? Related)> Flagged(string check) =>
    [
        .. crawl.Reference.Issues.SelectMany(entry => entry.Value.Where(issue => issue.Issue == check).Select(issue => (entry.Key, issue.Detail, issue.Related)))
    ];

    /// <summary>
    /// The check's groups worked out from its issues: by every URL each names, as the analysis stored them (no URL a group of
    /// its own), for a check grouped by the other URL; by the fault, for one with a fault; otherwise by detail and other URL.
    /// Largest first, then by detail and other URL, unknown first.
    /// </summary>
    private static IssueGroup Bare(IssueGroup group) => group with { Related = null, SharedText = null };

    private List<IssueGroup> ExpectedGroups(string check, IssueGrouping grouping)
    {
        var issues = Flagged(check);
        return
        [
            .. issues.SelectMany(issue => Keys(check, grouping, issue).Select(key => (Key: key, issue.Url)))
                .GroupBy(entry => entry.Key, entry => entry.Url)
                .Select(group =>
                {
                    var pages = group.LongCount();
                    var shares = group.Key.Related is not null || check == "broken-fragment-links";
                    var template = pages >= 10 && pages * 2 >= issues.Count && (shares || grouping == IssueGrouping.PageDetail);
                    return new IssueGroup(group.Key.Detail, group.Key.Related, group.Key.Related is { } id ? (string?)crawl.Reference.Rows[id]["url"] : null,
                        pages, (double)pages / issues.Count, template);
                })
                .OrderByDescending(group => group.Pages).ThenBy(group => group.Detail, StringComparer.Ordinal).ThenBy(group => group.RelatedUrlId)
        ];
    }

    /// <summary>The groups an issue is in: one per URL it names for a check grouped by the other URL, otherwise one.</summary>
    private IEnumerable<(string? Detail, int? Related)> Keys(string check, IssueGrouping grouping, (int Url, string? Detail, int? Related) issue) =>
        grouping == IssueGrouping.RelatedUrl
            ? crawl.Reference.Targets[(check, issue.Url)].Select(target => ((string?)null, target == 0 ? null : (int?)target))
            : IssueCatalog.Find(check)!.FaultOf is not null
                ? [(crawl.Reference.Faults[(check, issue.Url)], null)]
                : [(issue.Detail, issue.Related)];

    private bool InGroup(string check, IssueGrouping grouping, (int Url, string? Detail, int? Related) issue, IssueGroup group) =>
        Keys(check, grouping, issue).Contains((group.Detail, group.RelatedUrlId));

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
            if (after is not null)
            {
                GridWalks.AssertGoesOn(page.Rows.Count, ids.Count, page.Matching);
            }
        }
        while (after is not null);

        return ids;
    }
}
