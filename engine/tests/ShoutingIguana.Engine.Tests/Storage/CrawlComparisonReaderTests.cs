using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Tests.Support;
using ShoutingIguana.TestSite;

namespace ShoutingIguana.Engine.Tests.Storage;

/// <summary>
/// A crawl compared with another crawl of the site (the fix loop), against the synthetic site's revisions: revision 1
/// "after the fixes" compared with revision 0 gives exactly the changes the site plants, by check, by row and by cause,
/// over HTTP and HTTPS. The states only a crawl's options or a changed analysis give are built on copies of the files.
/// </summary>
[Collection(ComparisonCrawlsCollection.NAME)]
public sealed class CrawlComparisonReaderTests(ComparisonCrawls crawls)
{
    private const string TITLE_TOO_LONG = "title-too-long";
    private const string BROKEN_LINKS = "broken-internal-links";

    private string Url(string path) => new Uri(crawls.Server.BaseUri, path).AbsoluteUri;

    /// <summary>Revision 1 compared with revision 0: every check's state and counts, and every row's state, exactly as the site planted them.</summary>
    [Fact]
    public void RevisionOneComparedWithRevisionZeroIsExactlyTheSitesChanges()
    {
        var mismatches = ComparisonRows.Mismatches(crawls.Now, crawls.Then, crawls.Site.GetChanges(0, 1, ExpectedJudging.Of));
        Assert.True(mismatches.Count == 0, string.Join(Environment.NewLine, mismatches));

        // Two complete crawls with the same options, analysed alike: like for like.
        var summary = CrawlComparisonReader.ReadSummary(crawls.Now, crawls.Then)!;
        Assert.Equal(Comparability.Full, summary.Comparable);
        Assert.Empty(summary.Differences);
        Assert.Empty(summary.PreviousNeedsAnalysis);

        // Every state two complete crawls can give is planted; the rest are built from crawl files below.
        Assert.Superset(new HashSet<CheckChangeState> { CheckChangeState.New, CheckChangeState.Fixed, CheckChangeState.Fewer, CheckChangeState.More, CheckChangeState.Same },
            summary.Checks.Select(check => check.State).ToHashSet());
        var rows = ComparisonRows.Read(crawls.Now, crawls.Then, summary);
        Assert.Superset(new HashSet<RowChangeState>
        {
            RowChangeState.Still, RowChangeState.New, RowChangeState.NewUrl, RowChangeState.Fixed, RowChangeState.Gone, RowChangeState.NotChecked
        }, rows.Select(row => row.State).ToHashSet());

        // The totals add every check up.
        Assert.Equal(summary.Checks.Count, summary.Totals.Checks.Values.Sum());
        Assert.Equal(summary.Checks.Aggregate(RowSplit.None, (sum, check) => sum.Plus(check.Rows!)), summary.Totals.Issues);
        Assert.All(summary.Checks, check => Assert.Equal(summary.Checks.Count(other => other.State == check.State), summary.Totals.Checks[check.State]));

        // The checks come in this crawl's fix-first order, then catalog order.
        var fixFirst = FixFirstOrder.Of(IssueReader.ReadCounts(crawls.Now));
        Assert.Equal(fixFirst, summary.Checks.Take(fixFirst.Count).Select(check => check.CheckId));

        // The score: each side's own, and the change between them.
        var (then, now) = (IssueReader.ReadAnalysis(crawls.Then)!.Score!, IssueReader.ReadAnalysis(crawls.Now)!.Score!);
        Assert.Equal((then, now, now.Value - then.Value), (summary.Score!.Then, summary.Score.Now, summary.Score.Change));
        var linesNow = IssueReader.ReadResults(crawls.Now)!.Score!.Lines.Where(line => line.Points > 0).ToDictionary(line => line.CheckId, line => line.Points);
        Assert.All(linesNow, line => Assert.Equal(line.Value, summary.Score.Lines.Single(change => change.CheckId == line.Key).Now));
        Assert.All(summary.Score.Lines, line => Assert.True(line.Then > 0 || line.Now > 0));
    }

    /// <summary>
    /// Over HTTPS, revision 1 also mends the site's plain-HTTP side: its robots.txt answers and its home page redirects, so
    /// the site's other version and its robots.txt are fixed.
    /// </summary>
    [Fact]
    public async Task OverHttpsTheComparisonIsExactlyTheSitesChangesToo()
    {
        var (then, now) = await crawls.HttpsAsync();
        var mismatches = ComparisonRows.Mismatches(now, then, crawls.HttpsSite.GetChanges(0, 1, ExpectedJudging.Of));
        Assert.True(mismatches.Count == 0, string.Join(Environment.NewLine, mismatches));

        var summary = CrawlComparisonReader.ReadSummary(now, then)!;
        Assert.Equal(CheckChangeState.Fixed, summary.Checks.Single(check => check.CheckId == "duplicate-site-version").State);
        Assert.Equal(CheckChangeState.Fixed, summary.Checks.Single(check => check.CheckId == "robots-txt-unreachable-other-origin").State);
    }

    /// <summary>A crawl compared with itself: every check the same, every row still there, nothing cleared.</summary>
    [Fact]
    public void ACrawlComparedWithItselfIsTheSame()
    {
        var summary = CrawlComparisonReader.ReadSummary(crawls.Now, crawls.Now)!;
        Assert.All(summary.Checks, check => Assert.Equal((check.CheckId, CheckChangeState.Same), (check.CheckId, check.State)));
        Assert.All(summary.Checks, check => Assert.Equal(RowSplit.None with { Still = check.Now!.Value }, check.Rows));
        Assert.All(ComparisonRows.Read(crawls.Now, crawls.Now, summary), row => Assert.Equal(RowChangeState.Still, row.State));
    }

    /// <summary>
    /// The long titles: one shortened (fixed), one page now an error (not checked, never fixed, with what it answers now,
    /// and a new server error), one left (still, with its detail then), one new page (a new URL).
    /// </summary>
    [Fact]
    public void EachRowSaysHowItMoved()
    {
        var rows = IssueGroupReader.ReadRows(crawls.Now, TITLE_TOO_LONG, new UrlQuery { Columns = ["url", "detail"] }, null, crawls.Then)!;
        var changes = rows.Page!.Rows.Select((row, index) => ((string)row.Values[1]!, rows.Changes![index]!)).ToDictionary(row => row.Item1, row => row.Item2);
        Assert.Equal(RowChangeState.NewUrl, changes[Url("/plants/title-long-new")].State);
        var still = changes[Url("/plants/title-wide-long")];
        Assert.Equal(RowChangeState.Still, still.State);
        var detailThen = IssueReader.ReadAll(crawls.Then, TITLE_TOO_LONG).Single(row => row.Url == Url("/plants/title-wide-long")).Detail;
        Assert.Equal(detailThen, still.DetailThen);
        Assert.Null(changes[Url("/plants/title-long-new")].DetailThen);
        Assert.Equal(new PageComparison(Comparability.Full), rows.Compared);

        var cleared = CrawlComparisonReader.ReadCleared(crawls.Now, crawls.Then, TITLE_TOO_LONG, new ClearedQuery())!;
        var fixedRow = cleared.Rows.Single(row => row.Url == Url("/plants/title-long"));
        Assert.Equal((RowChangeState.Fixed, (NotCheckedReason?)null), (fixedRow.State, fixedRow.Reason));
        Assert.Equal(Id(crawls.Now, fixedRow.Url), fixedRow.UrlId);
        Assert.Equal(Id(crawls.Then, fixedRow.Url), fixedRow.ThenUrlId);
        Assert.Equal(IssueReader.ReadAll(crawls.Then, TITLE_TOO_LONG).Single(row => row.Url == fixedRow.Url).Detail, fixedRow.DetailThen);

        var caps = cleared.Rows.Single(row => row.Url == Url("/plants/title-caps"));
        Assert.Equal((RowChangeState.NotChecked, new NotCheckedReason(NotCheckedCode.Failed, "500 Internal Server Error")), (caps.State, caps.Reason));
        Assert.Equal(RowChangeState.New, ServerErrors().Single(row => row.Url == Url("/plants/title-caps")).State);

        // The short-title page was removed and unlinked: gone, with no id now.
        var gone = CrawlComparisonReader.ReadCleared(crawls.Now, crawls.Then, "title-too-short", new ClearedQuery())!.Rows.Single(row => row.Url == Url("/plants/title-short"));
        Assert.Equal((RowChangeState.Gone, (int?)null), (gone.State, gone.UrlId));

        List<(string Url, RowChangeState State)> ServerErrors()
        {
            var page = IssueGroupReader.ReadRows(crawls.Now, "internal-server-error", new UrlQuery { Columns = ["url"] }, null, crawls.Then)!;
            return [.. page.Page!.Rows.Select((row, index) => ((string)row.Values[1]!, page.Changes![index]!.State))];
        }
    }

    /// <summary>
    /// A duplicate cluster is matched by the pages it shares, not by its name (its first page): the pair /p/184 and /p/185,
    /// whose first page was renamed and whose title /p/186 took, is one cluster named /p/185 now, the same size; the pair
    /// /p/36 and /p/37, whose copy took its own title, is a cluster then with no URL now.
    /// </summary>
    [Fact]
    public void DuplicateClustersAreMatchedByThePagesTheyShare()
    {
        // A cluster is named by one of its pages (the first found), so each is found here by the pages it holds.
        var groups = IssueGroupReader.ReadGroups(crawls.Now, "title-duplicate", 0, UrlGridReader.MAX_LIMIT, null, crawls.Then)!;
        var before = IssueGroupReader.ReadGroups(crawls.Then, "title-duplicate", 0, UrlGridReader.MAX_LIMIT)!.Groups;
        var renamed = groups.Groups.Single(group => Members(crawls.Now, "title-duplicate", group).Contains(Url("/p/185")));
        Assert.Equal([Url("/p/185"), Url("/p/186")], Members(crawls.Now, "title-duplicate", renamed).Order(StringComparer.Ordinal));
        var renamedThen = before.Single(group => Members(crawls.Then, "title-duplicate", group).Contains(Url("/p/185")));
        Assert.Equal([Url("/p/184"), Url("/p/185")], Members(crawls.Then, "title-duplicate", renamedThen).Order(StringComparer.Ordinal));
        Assert.Equal(new GroupChange(GroupChangeState.Same, 2), renamed.Change);

        // Its name then names no cluster now, and it isn't among the fixed: it's the same cluster.
        Assert.DoesNotContain(groups.Groups, group => group.RelatedUrl == renamedThen.RelatedUrl);
        var fixedGroups = CrawlComparisonReader.ReadCleared(crawls.Now, crawls.Then, "title-duplicate", new ClearedQuery(ClearedLevel.Groups, Limit: UrlGridReader.MAX_LIMIT))!;
        Assert.DoesNotContain(fixedGroups.Groups, group => group.RelatedUrl == renamedThen.RelatedUrl);

        // The pair whose copy took its own title: fixed, both pages.
        Assert.Equal(groups.Compared!.FixedGroups, fixedGroups.Total);
        var pairThen = before.Single(group => Members(crawls.Then, "title-duplicate", group).Contains(Url("/p/36")));
        Assert.Equal([Url("/p/36"), Url("/p/37")], Members(crawls.Then, "title-duplicate", pairThen).Order(StringComparer.Ordinal));
        Assert.NotNull(pairThen.SharedText);
        Assert.Equal(new ClearedGroup(null, pairThen.RelatedUrl, pairThen.SharedText, 2, 2, 0, 0), fixedGroups.Groups.Single(group => group.RelatedUrl == pairThen.RelatedUrl));
    }

    /// <summary>
    /// Every grouped check's causes, against each file's own groups: a cause now has the URLs its counterpart had then (by
    /// its other URL's text and detail, or for a duplicate cluster by the pages it shares most), and the causes then with no
    /// counterpart now are the fixed groups, the ones the cleared causes list.
    /// </summary>
    [Fact]
    public void GroupChangesAgreeWithEachFilesGroups()
    {
        var summary = CrawlComparisonReader.ReadSummary(crawls.Now, crawls.Then)!;
        var grouped = summary.Checks.Where(check => check.Then is not null && check.Now is not null && IssueCatalog.Find(check.CheckId)!.Grouping != IssueGrouping.None).ToList();
        Assert.Contains(grouped, check => check.Rows!.Fixed > 0 && IssueCatalog.Find(check.CheckId)!.Grouping == IssueGrouping.RelatedUrl);
        foreach (var check in grouped)
        {
            var now = IssueGroupReader.ReadGroups(crawls.Now, check.CheckId, 0, UrlGridReader.MAX_LIMIT, null, crawls.Then)!;
            var then = IssueGroupReader.ReadGroups(crawls.Then, check.CheckId, 0, UrlGridReader.MAX_LIMIT)!;
            Assert.True(now.GroupCount <= UrlGridReader.MAX_LIMIT && then.GroupCount <= UrlGridReader.MAX_LIMIT, check.CheckId);

            List<(IssueGroup Now, long Then)> expected;
            HashSet<IssueGroup> matched;
            if (check.CheckId is "title-duplicate" or "description-duplicate" or "h1-duplicate" or "duplicate-content" or "near-duplicate-content")
            {
                // A cluster's pages, by URL, read from each file's rows narrowed to it.
                var clustersThen = then.Groups.ToDictionary(group => group, group => Members(crawls.Then, check.CheckId, group));
                expected = [];
                matched = [];
                foreach (var group in now.Groups)
                {
                    var pages = Members(crawls.Now, check.CheckId, group);
                    var best = clustersThen.Select(cluster => (Cluster: cluster.Key, Shared: cluster.Value.Count(pages.Contains)))
                        .Where(cluster => cluster.Shared > 0)
                        .OrderByDescending(cluster => cluster.Shared).ThenBy(cluster => cluster.Cluster.RelatedUrlId)
                        .Select(cluster => cluster.Cluster).FirstOrDefault();
                    expected.Add((group, best?.Pages ?? 0));
                    if (best is not null)
                    {
                        matched.Add(best);
                    }
                }
            }
            else
            {
                var byKey = then.Groups.ToDictionary(group => (group.Detail, group.RelatedUrl));
                expected = [.. now.Groups.Select(group => (group, byKey.GetValueOrDefault((group.Detail, group.RelatedUrl))?.Pages ?? 0))];
                var keysNow = now.Groups.Select(group => (group.Detail, group.RelatedUrl)).ToHashSet();
                matched = [.. then.Groups.Where(group => keysNow.Contains((group.Detail, group.RelatedUrl)))];
            }

            foreach (var (group, pagesThen) in expected)
            {
                var state = pagesThen == 0 ? GroupChangeState.New
                    : group.Pages < pagesThen ? GroupChangeState.Fewer
                    : group.Pages > pagesThen ? GroupChangeState.More
                    : GroupChangeState.Same;
                Assert.Equal((check.CheckId, group.Detail, group.RelatedUrl, new GroupChange(state, pagesThen)), (check.CheckId, group.Detail, group.RelatedUrl, group.Change));
            }

            Assert.Equal((check.CheckId, then.Groups.Count - matched.Count), (check.CheckId, now.Compared!.FixedGroups));
            var cleared = CrawlComparisonReader.ReadCleared(crawls.Now, crawls.Then, check.CheckId, new ClearedQuery(ClearedLevel.Groups, Limit: UrlGridReader.MAX_LIMIT))!;
            Assert.Equal(now.Compared.FixedGroups, cleared.Total);
            Assert.Equal(
                then.Groups.Where(group => !matched.Contains(group)).Select(group => (group.Detail, group.RelatedUrl, group.Pages)).Order(),
                cleared.Groups.Select(group => (group.Detail, group.RelatedUrl, group.Then)).Order());

            // A cause's URLs then are each still flagged now (for another cause), or cleared, and those add up.
            Assert.All(cleared.Groups, group => Assert.True(group.Fixed + group.Gone + group.NotChecked <= group.Then, $"{check.CheckId}: {group}"));
        }
    }

    /// <summary>
    /// What a check no longer flags, a page at a time in the other crawl's order by importance (as its rows come by default),
    /// narrowed by state and by one cause then; and the broken links' causes, each with how its pages moved.
    /// </summary>
    [Fact]
    public void ClearedRowsComeInTheOtherCrawlsOrderAPageAtATime()
    {
        foreach (var checkId in new[] { "internal-client-error", BROKEN_LINKS, "title-duplicate", TITLE_TOO_LONG })
        {
            var all = CrawlComparisonReader.ReadCleared(crawls.Now, crawls.Then, checkId, new ClearedQuery(Limit: UrlGridReader.MAX_LIMIT))!;
            Assert.True(all.Rows.Count >= 2, checkId);
            Assert.Equal(all.Rows.Count, all.Total);

            List<ClearedIssue> paged = [];
            for (var offset = 0; offset < all.Total; offset += 2)
            {
                var page = CrawlComparisonReader.ReadCleared(crawls.Now, crawls.Then, checkId, new ClearedQuery(Offset: offset, Limit: 2))!;
                Assert.Equal((offset, all.Total), (page.Offset, page.Total));
                paged.AddRange(page.Rows);
            }

            Assert.Equal(all.Rows, paged);

            // The other crawl's rows by importance, those no longer flagged.
            var cleared = all.Rows.Select(row => row.ThenUrlId).ToHashSet();
            var order = IssueGroupReader.ReadRows(crawls.Then, checkId, new UrlQuery { Limit = UrlGridReader.MAX_LIMIT })!.Page!.Rows.Select(row => row.Id).Where(cleared.Contains);
            Assert.Equal(order, all.Rows.Select(row => row.ThenUrlId));

            // Narrowed by state.
            foreach (var state in new[] { RowChangeState.Fixed, RowChangeState.Gone, RowChangeState.NotChecked })
            {
                var only = CrawlComparisonReader.ReadCleared(crawls.Now, crawls.Then, checkId, new ClearedQuery(States: [state], Limit: UrlGridReader.MAX_LIMIT))!;
                Assert.Equal(all.Rows.Where(row => row.State == state), only.Rows);
                Assert.Equal(only.Rows.Count, only.Total);
            }
        }

        // The broken link restored at /broken/29: its page, narrowed to that cause then.
        var restored = CrawlComparisonReader.ReadCleared(crawls.Now, crawls.Then, BROKEN_LINKS, new ClearedQuery(Group: new ClearedGroupKey(null, Url("/broken/29"))))!;
        Assert.Equal([(Url("/p/29"), RowChangeState.Fixed, Url("/broken/29"))], restored.Rows.Select(row => (row.Url, row.State, row.RelatedUrlThen)));
        Assert.Equal(0, CrawlComparisonReader.ReadCleared(crawls.Now, crawls.Then, BROKEN_LINKS, new ClearedQuery(Group: new ClearedGroupKey(null, Url("/no/such/url"))))!.Total);

        // By cause: the restored link, the dropped one and the moved one have no page linking them now.
        var causes = CrawlComparisonReader.ReadCleared(crawls.Now, crawls.Then, BROKEN_LINKS, new ClearedQuery(ClearedLevel.Groups, Limit: UrlGridReader.MAX_LIMIT))!;
        Assert.Contains(new ClearedGroup(null, Url("/broken/29"), null, 1, 1, 0, 0), causes.Groups);
        Assert.Contains(new ClearedGroup(null, Url("/broken/58"), null, 1, 1, 0, 0), causes.Groups);
        Assert.Contains(causes.Groups, group => group.RelatedUrl == Url("/broken/87") && group.Fixed + group.Gone + group.NotChecked == 0);

        // A state a cleared row can't be, the paging out of range, or a cause of a check without causes: refused.
        Assert.Throws<UrlQueryException>(() => CrawlComparisonReader.ReadCleared(crawls.Now, crawls.Then, BROKEN_LINKS, new ClearedQuery(States: [RowChangeState.Still])));
        Assert.Throws<UrlQueryException>(() => CrawlComparisonReader.ReadCleared(crawls.Now, crawls.Then, BROKEN_LINKS, new ClearedQuery(Limit: 0)));
        Assert.Throws<UrlQueryException>(() => CrawlComparisonReader.ReadCleared(crawls.Now, crawls.Then, TITLE_TOO_LONG, new ClearedQuery(Group: new ClearedGroupKey("x", null))));
    }

    /// <summary>"Not checked isn't fixed", over every check: no row is fixed whose URL this crawl didn't read in a way its check judges.</summary>
    [Fact]
    public async Task NoRowIsFixedThatThisCrawlDidntJudge()
    {
        AssertNothingUnjudgedIsFixed(crawls.Now, crawls.Then);

        // A crawl that stopped before reading pages the other flagged: those are not checked, and never fixed.
        var stopped = await StoppedAsync(crawls.Now, [Url("/p/29"), Url("/plants/title-long")]);
        AssertNothingUnjudgedIsFixed(stopped, crawls.Then);
    }

    /// <summary>A check the previous analysis didn't run (newer than it, say) is a new check: what it flags now isn't compared.</summary>
    [Fact]
    public void ACheckThePreviousAnalysisDidntRunIsANewCheck()
    {
        var then = crawls.Copy(crawls.Then);
        Execute(then, $"DELETE FROM issue_types WHERE id = '{TITLE_TOO_LONG}'; DELETE FROM skipped_checks WHERE check_id = '{TITLE_TOO_LONG}'; DELETE FROM issues WHERE issue = '{TITLE_TOO_LONG}';");

        var check = CrawlComparisonReader.ReadSummary(crawls.Now, then)!.Checks.Single(check => check.CheckId == TITLE_TOO_LONG);
        Assert.Equal((CheckChangeState.NewCheck, (long?)null, 2L), (check.State, check.Then, check.Now!.Value));
        Assert.Equal(RowSplit.None with { NotComparedThen = 2 }, check.Rows);
        Assert.Null(check.SeverityThen);
        Assert.Equal(IssueCatalog.TITLE_TOO_LONG.Severity, check.Severity);

        var rows = IssueGroupReader.ReadRows(crawls.Now, TITLE_TOO_LONG, new UrlQuery(), null, then)!;
        Assert.All(rows.Changes!, change => Assert.Equal(new RowChange(RowChangeState.NotComparedThen), change));
        Assert.Null(CrawlComparisonReader.ReadCleared(crawls.Now, then, TITLE_TOO_LONG, new ClearedQuery())!.Total);
    }

    /// <summary>A check this analysis skipped is not checked now: everything it flagged then is not checked, saying why.</summary>
    [Fact]
    public void ACheckSkippedNowIsNotCheckedNow()
    {
        var now = crawls.Copy(crawls.Now);
        Execute(now, $"""
            DELETE FROM issue_types WHERE id = '{TITLE_TOO_LONG}'; DELETE FROM issues WHERE issue = '{TITLE_TOO_LONG}';
            INSERT INTO skipped_checks (check_id, reason, text) VALUES ('{TITLE_TOO_LONG}', 'test', 'Skipped for the test.');
            """);

        var check = CrawlComparisonReader.ReadSummary(now, crawls.Then)!.Checks.Single(check => check.CheckId == TITLE_TOO_LONG);
        Assert.Equal((CheckChangeState.NotCheckedNow, 3L, (long?)null), (check.State, check.Then!.Value, check.Now));
        Assert.Equal(RowSplit.None with { NotChecked = 3 }, check.Rows);

        var cleared = CrawlComparisonReader.ReadCleared(now, crawls.Then, TITLE_TOO_LONG, new ClearedQuery())!;
        Assert.Equal(3, cleared.Total);
        Assert.All(cleared.Rows, row => Assert.Equal((RowChangeState.NotChecked, new NotCheckedReason(NotCheckedCode.CheckSkipped, "Skipped for the test.")), (row.State, row.Reason)));

        // Each with its id in this crawl, which has them all.
        Assert.Equal(cleared.Rows.Select(row => Id(now, row.Url)), cleared.Rows.Select(row => row.UrlId));
        Assert.All(cleared.Rows, row => Assert.NotNull(row.UrlId));
    }

    /// <summary>
    /// The previous crawl analysed by an older engine: counts only (no row, cause or cleared row is matched), saying it needs
    /// analysing again; analysed again, the two are like for like and the comparison is exact.
    /// </summary>
    [Fact]
    public async Task AnOlderAnalysisIsComparedByItsCountsUntilAnalysedAgain()
    {
        var then = crawls.Copy(crawls.Then);
        Execute(then, $"UPDATE analysis SET analysis_version = {AnalysisFreshness.VERSION - 1}");

        var summary = CrawlComparisonReader.ReadSummary(crawls.Now, then)!;
        Assert.Equal(Comparability.CountsOnly, summary.Comparable);
        Assert.Equal([OutOfDateReason.EngineChanged], summary.PreviousNeedsAnalysis);
        var rules = Assert.Single(summary.Differences);
        Assert.Equal((ComparisonDifferenceCode.Rules, "analysisVersion", AnalysisFreshness.VERSION - 1, AnalysisFreshness.VERSION),
            (rules.Code, rules.Field, (int)rules.Then!, (int)rules.Now!));
        Assert.All(summary.Checks, check => Assert.Null(check.Rows));
        Assert.Null(summary.Totals.Issues);

        // The counts and the score are still compared.
        var exact = CrawlComparisonReader.ReadSummary(crawls.Now, crawls.Then)!;
        Assert.Equal(exact.Checks.Select(check => (check.CheckId, check.Then, check.Now)), summary.Checks.Select(check => (check.CheckId, check.Then, check.Now)));
        Assert.Equal(exact.Score, summary.Score! with { Lines = exact.Score!.Lines });

        // No row, cause or cleared row is matched.
        var rows = IssueGroupReader.ReadRows(crawls.Now, TITLE_TOO_LONG, new UrlQuery(), null, then)!;
        Assert.Equal(new PageComparison(Comparability.CountsOnly), rows.Compared);
        Assert.All(rows.Changes!, Assert.Null);
        var groups = IssueGroupReader.ReadGroups(crawls.Now, BROKEN_LINKS, 0, 10, null, then)!;
        Assert.Equal(new PageComparison(Comparability.CountsOnly), groups.Compared);
        Assert.All(groups.Groups, group => Assert.Null(group.Change));
        Assert.Null(CrawlComparisonReader.ReadCleared(crawls.Now, then, BROKEN_LINKS, new ClearedQuery())!.Total);

        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(then);
        var again = CrawlComparisonReader.ReadSummary(crawls.Now, then)!;
        Assert.Equal((Comparability.Full, 0, 0), (again.Comparable, again.Differences.Count, again.PreviousNeedsAnalysis.Count));
        var mismatches = ComparisonRows.Mismatches(crawls.Now, then, crawls.Site.GetChanges(0, 1, ExpectedJudging.Of));
        Assert.True(mismatches.Count == 0, string.Join(Environment.NewLine, mismatches));
    }

    /// <summary>The previous crawl analysed with other thresholds needs analysing again too, and is counts only until it is.</summary>
    [Fact]
    public void OtherSettingsNeedTheAnalysisAgain()
    {
        var then = crawls.Copy(crawls.Then);
        var settings = JsonNode.Parse(Scalar(then, "SELECT settings_json FROM analysis"))!;
        settings["minWordCount"] = new AnalysisSettings().MinWordCount + 1;
        Execute(then, "UPDATE analysis SET settings_json = $json", ("$json", settings.ToJsonString()));

        var summary = CrawlComparisonReader.ReadSummary(crawls.Now, then)!;
        Assert.Equal([OutOfDateReason.SettingsChanged], summary.PreviousNeedsAnalysis);
        Assert.Equal([new ComparisonDifference(ComparisonDifferenceCode.Rules, "settings")], summary.Differences);
        Assert.Equal(Comparability.CountsOnly, summary.Comparable);
    }

    /// <summary>
    /// A crawl that stopped early is compared in part: what this crawl didn't read is not checked (never fixed), saying it
    /// wasn't reached, and a check it would have cleared is fewer, not fixed; what the previous crawl didn't read is not
    /// compared then (never new).
    /// </summary>
    [Fact]
    public async Task AStoppedCrawlIsComparedInPart()
    {
        // This crawl stopped before the page whose broken link was restored, and the page whose long title was shortened.
        var now = await StoppedAsync(crawls.Now, [Url("/p/29"), Url("/plants/title-long")]);
        var summary = CrawlComparisonReader.ReadSummary(now, crawls.Then)!;
        Assert.Equal(Comparability.Partial, summary.Comparable);
        Assert.Contains(new ComparisonDifference(ComparisonDifferenceCode.StoppedEarly, Side: ComparisonSide.Now), summary.Differences);
        var cleared = CrawlComparisonReader.ReadCleared(now, crawls.Then, BROKEN_LINKS, new ClearedQuery())!;
        Assert.Equal((RowChangeState.NotChecked, new NotCheckedReason(NotCheckedCode.NotRead, "Not reached before the crawl stopped")),
            cleared.Rows.Where(row => row.Url == Url("/p/29")).Select(row => (row.State, row.Reason)).Single());
        Assert.Equal(RowChangeState.NotChecked, CrawlComparisonReader.ReadCleared(now, crawls.Then, TITLE_TOO_LONG, new ClearedQuery())!.Rows.Single(row => row.Url == Url("/plants/title-long")).State);

        // The previous crawl stopped before the page that answers 500 now: that new server error isn't compared then.
        var then = await StoppedAsync(crawls.Then, [Url("/plants/title-caps")]);
        var before = CrawlComparisonReader.ReadSummary(crawls.Now, then)!;
        Assert.Contains(new ComparisonDifference(ComparisonDifferenceCode.StoppedEarly, Side: ComparisonSide.Then), before.Differences);
        var errors = IssueGroupReader.ReadRows(crawls.Now, "internal-server-error", new UrlQuery { Columns = ["url"] }, null, then)!;
        Assert.Equal(new RowChange(RowChangeState.NotComparedThen),
            errors.Changes![errors.Page!.Rows.Select(row => (string)row.Values[1]!).ToList().IndexOf(Url("/plants/title-caps"))]);

        // Both stopped.
        Assert.Contains(new ComparisonDifference(ComparisonDifferenceCode.StoppedEarly, Side: ComparisonSide.Both), CrawlComparisonReader.ReadSummary(now, then)!.Differences);
    }

    /// <summary>A crawl run with another URL limit is compared in part, the limit named: none then, 5,000 now.</summary>
    [Fact]
    public void AUrlLimitIsAPartialComparison()
    {
        var now = crawls.Copy(crawls.Now);
        var options = JsonNode.Parse(Scalar(now, "SELECT options_json FROM crawl"))!;
        options["maxUrls"] = 5_000;
        Execute(now, "UPDATE crawl SET options_json = $json", ("$json", options.ToJsonString()));

        var summary = CrawlComparisonReader.ReadSummary(now, crawls.Then)!;
        Assert.Equal(Comparability.Partial, summary.Comparable);
        var limit = Assert.Single(summary.Differences);
        Assert.Equal((ComparisonDifferenceCode.Limits, "maxUrls", (JsonNode?)null, 5_000), (limit.Code, limit.Field, limit.Then, (int)limit.Now!));

        // Partial still matches rows.
        Assert.All(summary.Checks, check => Assert.NotNull(check.Rows));
    }

    /// <summary>
    /// The HTTPS site against the plain-HTTP one: every URL's text differs, so only the counts and the score are compared,
    /// and no row or cause is.
    /// </summary>
    [Fact]
    public async Task AnotherOriginIsComparedByItsCountsOnly()
    {
        var (_, httpsNow) = await crawls.HttpsAsync();
        var summary = CrawlComparisonReader.ReadSummary(httpsNow, crawls.Then)!;
        Assert.Equal(Comparability.CountsOnly, summary.Comparable);
        var origin = Assert.Single(summary.Differences);
        Assert.Equal((ComparisonDifferenceCode.StartOrigin, crawls.Server.BaseUri.GetLeftPart(UriPartial.Authority), crawls.HttpsBaseUri.GetLeftPart(UriPartial.Authority)),
            (origin.Code, (string)origin.Then!, (string)origin.Now!));
        Assert.Empty(summary.PreviousNeedsAnalysis);
        Assert.All(summary.Checks, check => Assert.Null(check.Rows));
        Assert.Null(summary.Totals.Issues);
        Assert.NotNull(summary.Score!.Change);

        // The move to HTTPS fixed no-https, at the check level.
        Assert.Equal(CheckChangeState.Fixed, summary.Checks.Single(check => check.CheckId == "no-https").State);

        var rows = CrawlComparisonReader.ReadRowChanges(httpsNow, crawls.Then, TITLE_TOO_LONG, [1, 2, 3])!;
        Assert.Equal(new PageComparison(Comparability.CountsOnly), rows.Compared);
        Assert.All(rows.Changes, Assert.Null);
    }

    /// <summary>Read with another crawl to compare with, a check's rows and groups are as without it: the same order, columns and paging.</summary>
    [Fact]
    public void ComparingLeavesTheRowsAndGroupsAsTheyAre()
    {
        foreach (var checkId in new[] { BROKEN_LINKS, TITLE_TOO_LONG, "title-duplicate", "internal-client-error" })
        {
            foreach (var query in new[]
                     {
                         new UrlQuery { Columns = ["url", "statusCode", "detail"], Limit = 3 },
                         new UrlQuery { Columns = ["url", "inlinks"], Sort = [new UrlSort("url", Descending: true)], Offset = 1, Limit = 2 }
                     })
            {
                var plain = IssueGroupReader.ReadRows(crawls.Now, checkId, query)!;
                var compared = IssueGroupReader.ReadRows(crawls.Now, checkId, query, null, crawls.Then)!;
                Assert.Equal(Rows(plain.Page!), Rows(compared.Page!));
                Assert.Equal((plain.Page!.Matching, plain.Page.Total, plain.Page.Offset, plain.Page.Next), (compared.Page!.Matching, compared.Page.Total, compared.Page.Offset, compared.Page.Next));
                Assert.Equal(compared.Page.Rows.Count, compared.Changes!.Count);
                Assert.All(compared.Changes, Assert.NotNull);
                Assert.Null(plain.Changes);
            }

            var groups = IssueGroupReader.ReadGroups(crawls.Now, checkId, 1, 3)!;
            var comparedGroups = IssueGroupReader.ReadGroups(crawls.Now, checkId, 1, 3, null, crawls.Then)!;
            Assert.Equal(Groups(groups), Groups(comparedGroups));
            Assert.Equal((groups.Total, groups.GroupCount, groups.Offset), (comparedGroups.Total, comparedGroups.GroupCount, comparedGroups.Offset));
            Assert.Null(groups.Compared);
        }

        static List<string> Rows(UrlPage page) => [.. page.Rows.Select(row => $"{row.Id}: {string.Join(" | ", row.Values.Select(value => Convert.ToString(value, CultureInfo.InvariantCulture)))}")];

        static List<string> Groups(IssueGroupPage page) => [.. page.Groups.Select(group => $"{group.Detail} {group.RelatedUrlId} {group.RelatedUrl} {group.Pages} {group.Share} {group.LikelyTemplate} {group.SharedText}")];
    }

    /// <summary>A crawl never analysed has no comparison, on either side.</summary>
    [Fact]
    public void ACrawlNeverAnalysedHasNoComparison()
    {
        var unanalysed = crawls.Copy(crawls.Then);
        Execute(unanalysed, "DROP TABLE analysis");
        Assert.Null(CrawlComparisonReader.ReadSummary(crawls.Now, unanalysed));
        Assert.Null(CrawlComparisonReader.ReadSummary(unanalysed, crawls.Now));
        Assert.Null(CrawlComparisonReader.ReadRowChanges(crawls.Now, unanalysed, TITLE_TOO_LONG, [1]));
        var rows = IssueGroupReader.ReadRows(crawls.Now, TITLE_TOO_LONG, new UrlQuery(), null, unanalysed)!;
        Assert.Null(rows.Compared);
        Assert.Null(rows.Changes);
    }

    private static void AssertNothingUnjudgedIsFixed(string now, string then)
    {
        var summary = CrawlComparisonReader.ReadSummary(now, then)!;
        using var connection = CrawlDatabase.OpenReader(now);
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {CheckJudging.READING} FROM urls u LEFT JOIN responses r ON r.url_id = u.id WHERE u.id = $id";
        command.Parameters.AddWithValue("$id", 0);
        foreach (var check in summary.Checks)
        {
            var fixedRows = CrawlComparisonReader.ReadCleared(now, then, check.CheckId, new ClearedQuery(States: [RowChangeState.Fixed], Limit: UrlGridReader.MAX_LIMIT))!;
            Assert.Equal(check.Rows!.Fixed, fixedRows.Total ?? 0);
            foreach (var row in fixedRows.Rows)
            {
                command.Parameters["$id"].Value = row.UrlId!.Value;
                var reading = (UrlReading)Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
                Assert.True(CheckJudging.Judges(CheckJudging.Of(IssueCatalog.Find(check.CheckId)!), reading), $"{check.CheckId}: {row.Url} is fixed, read as {reading}");
            }

            // A check at 0 now is fixed only with nothing left unchecked.
            if (check.State == CheckChangeState.Fixed)
            {
                Assert.Equal(0, check.Rows.NotChecked);
            }
        }
    }

    /// <summary>
    /// A copy of a crawl that stopped before reading some of its URLs, analysed again: they are found and queued, with no
    /// response and no links of their own.
    /// </summary>
    private async Task<string> StoppedAsync(string path, IReadOnlyList<string> unread)
    {
        var copy = crawls.Copy(path);
        var ids = string.Join(", ", unread.Select(url => Id(copy, url) ?? throw new ArgumentException($"{url} isn't in the crawl.")));
        Execute(copy, $"""
            DELETE FROM responses WHERE url_id IN ({ids});
            DELETE FROM response_details WHERE url_id IN ({ids});
            DELETE FROM response_html WHERE url_id IN ({ids});
            DELETE FROM links WHERE from_id IN ({ids});
            UPDATE urls SET state = 0 WHERE id IN ({ids});
            UPDATE crawl SET status = 2, updated_at = updated_at + 1;
            """);
        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(copy);
        return copy;
    }

    private List<string> Members(string path, string checkId, IssueGroup group) =>
    [
        .. IssueGroupReader.ReadRows(path, checkId, new UrlQuery { Columns = ["url"], Limit = UrlGridReader.MAX_LIMIT }, new IssueGroupKey(group.Detail, group.RelatedUrlId))!
            .Page!.Rows.Select(row => (string)row.Values[1]!)
    ];

    private static int? Id(string path, string url)
    {
        using var connection = CrawlDatabase.OpenReader(path);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id FROM urls WHERE url = $url";
        command.Parameters.AddWithValue("$url", url);
        return command.ExecuteScalar() is long id ? (int)id : null;
    }

    private static string Scalar(string path, string sql)
    {
        using var connection = CrawlDatabase.OpenReader(path);
        return (string)CrawlDatabase.Scalar(connection, sql)!;
    }

    private static void Execute(string path, string sql, params (string Name, object Value)[] parameters)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        command.ExecuteNonQuery();
    }
}
