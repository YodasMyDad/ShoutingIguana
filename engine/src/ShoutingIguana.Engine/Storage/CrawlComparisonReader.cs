using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// A crawl compared with another crawl of the site (the fix loop): by check, by cause and by URL. Read only. Each file is
/// opened as the grids' readers open it, one snapshot each through its own read gate, and the two are joined in memory a
/// check at a time, keyed by the URL's text (<see cref="Urls.UrlKey"/>), as ids are per file: no <c>ATTACH</c>, and
/// nothing is written. A URL a check flagged then and not now is fixed only if this crawl read it in a way the check can
/// judge (<see cref="CheckJudging"/>), gone if this crawl didn't find it, and otherwise not checked, never fixed.
/// <list type="bullet">
/// <item><see cref="ReadSummary"/>: how far the two can be compared, the score then and now, and every check that ran in
/// either with its state and how its rows moved.</item>
/// <item><see cref="ReadRowChanges"/> and <see cref="ReadGroupChanges"/>: the change of each row or group on one page of a
/// check's rows or groups (<see cref="IssueGroupReader"/>'s <c>compareWith</c>).</item>
/// <item><see cref="ReadCleared"/>: what a check flagged then and doesn't flag now, by URL or by cause, a page at a time.</item>
/// </list>
/// What it reads is kept in the read cache under both files' stamps and analyses: the summary, and for one check the join
/// behind its causes and cleared rows. Memory is a check's rows then at a time (some 40 bytes a URL), never a whole file.
/// </summary>
public static partial class CrawlComparisonReader
{
    /// <summary>
    /// The comparison of a crawl (<paramref name="currentPath"/>) with another crawl of the site (<paramref name="otherPath"/>).
    /// Null when either was never analysed.
    /// </summary>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    /// <exception cref="InvalidOperationException">A file isn't a crawl, or was written with another schema.</exception>
    public static CrawlComparison? ReadSummary(string currentPath, string otherPath, ReadCache? cache = null, CancellationToken cancellationToken = default) =>
        Read(currentPath, otherPath, cache, cancellationToken, Summary);

    /// <summary>
    /// How each of the URLs a check flags now (<paramref name="urlIds"/>, ids in this crawl, as a page of its rows lists them)
    /// moved since the other crawl, by at most a few index seeks each. Null when either crawl was never analysed.
    /// </summary>
    /// <inheritdoc cref="ReadSummary" path="/exception"/>
    public static RowChanges? ReadRowChanges(string currentPath, string otherPath, string checkId, IReadOnlyList<int> urlIds, ReadCache? cache = null,
        CancellationToken cancellationToken = default) =>
        Read(currentPath, otherPath, cache, cancellationToken, (now, then) => RowChangesOf(now, then, checkId, urlIds));

    /// <summary>
    /// How each of a check's groups (a page of <see cref="IssueGroupReader.ReadGroups(string, string, int, int, IssueGroupSort?, ReadCache?, CancellationToken)"/>)
    /// moved since the other crawl, and how many causes then have no URL now. Null when either crawl was never analysed.
    /// </summary>
    /// <inheritdoc cref="ReadSummary" path="/exception"/>
    public static GroupChanges? ReadGroupChanges(string currentPath, string otherPath, string checkId, IReadOnlyList<IssueGroup> groups, ReadCache? cache = null,
        CancellationToken cancellationToken = default) =>
        Read(currentPath, otherPath, cache, cancellationToken, (now, then) => GroupChangesOf(now, then, checkId, groups));

    /// <summary>
    /// One page of what a check flagged in the other crawl and doesn't flag now: each URL, in the other crawl's order by
    /// importance, or each cause with no URL now, largest first. Null when either crawl was never analysed.
    /// </summary>
    /// <exception cref="UrlQueryException">The paging or the states are out of range.</exception>
    /// <inheritdoc cref="ReadSummary" path="/exception"/>
    public static ClearedPage? ReadCleared(string currentPath, string otherPath, string checkId, ClearedQuery query, ReadCache? cache = null,
        CancellationToken cancellationToken = default)
    {
        Validate(query);
        return Read(currentPath, otherPath, cache, cancellationToken, (now, then) => Cleared(now, then, checkId, query));
    }

    /// <summary>
    /// Both files opened as the grids' readers open them, each in one snapshot through its own read gate. A writer of
    /// either file that needs it for a moment stops the read, which reads again from new snapshots.
    /// </summary>
    internal static T Read<T>(string currentPath, string otherPath, ReadCache? cache, CancellationToken cancellationToken, Func<GridReadContext, GridReadContext, T> read) =>
        GridReadContext.Read(currentPath, cache, cancellationToken, now => GridReadContext.Read(otherPath, cache, now.Token, then => read(now, then)));

    /// <summary>The two files' analyses, options and statuses, and how far they can be compared; null when either was never analysed.</summary>
    internal static ComparisonPair? Pair(GridReadContext now, GridReadContext then) =>
        now.Analysis is null || then.Analysis is null ? null : Kept(now, then, "pair", () =>
        {
            var (fileThen, fileNow) = (FileOf(then), FileOf(now));
            return new ComparisonPair(fileThen, fileNow, CrawlComparability.Judge(fileThen.Compared, fileNow.Compared));
        });

    private static CrawlComparison? Summary(GridReadContext now, GridReadContext then)
    {
        if (Pair(now, then) is not { } pair)
        {
            return null;
        }

        return Kept(now, then, "summary", () =>
        {
            // Without its rows (counts only), a check at 0 now is unsure only when this crawl may not have read what the
            // other flagged: it stopped early, or read far fewer pages.
            var unsure = pair.Verdict.Differences.Any(difference =>
                difference is { Code: ComparisonDifferenceCode.StoppedEarly, Side: ComparisonSide.Now or ComparisonSide.Both } ||
                difference.Code == ComparisonDifferenceCode.Coverage && pair.Now.Results.Analysis.PagesAnalyzed < pair.Then.Results.Analysis.PagesAnalyzed);
            List<CheckChange> checks = [];
            foreach (var checkId in CheckOrder(pair))
            {
                var (then_, now_) = (pair.Then.Count(checkId), pair.Now.Count(checkId));
                var rows = pair.CountsOnly ? null : RowPass(now, then, pair, checkId).Split;
                checks.Add(new CheckChange(
                    checkId,
                    StateOf(then_, now_, rows, unsure),
                    pair.Now.Severities.TryGetValue(checkId, out var severity) ? severity : null,
                    pair.Then.Severities.TryGetValue(checkId, out var severityThen) ? severityThen : null,
                    then_,
                    now_,
                    rows));
            }

            var totals = new ComparisonTotals(
                Enum.GetValues<CheckChangeState>().ToDictionary(state => state, state => checks.Count(check => check.State == state)),
                pair.CountsOnly ? null : checks.Aggregate(RowSplit.None, (sum, check) => sum.Plus(check.Rows!)));
            return new CrawlComparison(pair.Verdict.Comparable, pair.Verdict.Differences, pair.Verdict.PreviousNeedsAnalysis, ScoreOf(pair), checks, totals);
        });
    }

    /// <summary>
    /// A check's state: <c>newCheck</c> or <c>notCheckedNow</c> when it didn't run on one side, else by its counts, a check
    /// at 0 now reaching <c>fixed</c> only with nothing it flagged then left unchecked. Without its rows (counts only), that
    /// is unsure only when a crawl didn't read all it set out to.
    /// </summary>
    internal static CheckChangeState StateOf(long? then, long? now, RowSplit? rows, bool unsure) => (then, now) switch
    {
        (null, _) => CheckChangeState.NewCheck,
        (_, null) => CheckChangeState.NotCheckedNow,
        (0, > 0) => CheckChangeState.New,
        (> 0, 0) => (rows is null ? unsure : rows.NotChecked > 0) ? CheckChangeState.Fewer : CheckChangeState.Fixed,
        var (was, @is) when @is < was => CheckChangeState.Fewer,
        var (was, @is) when @is > was => CheckChangeState.More,
        _ => CheckChangeState.Same
    };

    /// <summary>
    /// Every check that ran in either crawl: this crawl's fix-first order (those that found something now), then catalog
    /// order, then those the catalog doesn't know, by id.
    /// </summary>
    private static IEnumerable<string> CheckOrder(ComparisonPair pair)
    {
        var ran = pair.Then.Results.Counts.Keys.Concat(pair.Now.Results.Counts.Keys).ToHashSet(StringComparer.Ordinal);
        return FixFirstOrder.Of(pair.Now.Results.Counts)
            .Concat(IssueCatalog.All.Select(check => check.Id))
            .Concat(ran.Order(StringComparer.Ordinal))
            .Where(ran.Contains)
            .Distinct(StringComparer.Ordinal);
    }

    /// <summary>The score then and now, with every check that took points on either side; null when either has no score.</summary>
    private static ScoreChange? ScoreOf(ComparisonPair pair)
    {
        var (analysisThen, analysisNow) = (pair.Then.Results.Analysis, pair.Now.Results.Analysis);
        if (analysisThen.Score is not { } then || analysisNow.Score is not { } now)
        {
            return null;
        }

        IReadOnlyList<ScoreLine> linesThen = pair.Then.Results.Score?.Lines ?? [];
        IReadOnlyList<ScoreLine> linesNow = pair.Now.Results.Score?.Lines ?? [];
        var pointsThen = linesThen.GroupBy(line => line.CheckId, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Sum(line => line.Points), StringComparer.Ordinal);
        var pointsNow = linesNow.GroupBy(line => line.CheckId, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Sum(line => line.Points), StringComparer.Ordinal);
        List<ScoreLineChange> lines =
        [
            .. linesNow.Select(line => line.CheckId).Concat(linesThen.Select(line => line.CheckId)).Distinct(StringComparer.Ordinal)
                .Select(checkId => new ScoreLineChange(checkId, pointsThen.GetValueOrDefault(checkId), pointsNow.GetValueOrDefault(checkId)))
                .Where(line => line.Then > 0 || line.Now > 0)
        ];
        var change = analysisThen.ScoreVersion is { } versionThen && versionThen == analysisNow.ScoreVersion ? now.Value - then.Value : (int?)null;
        return new ScoreChange(then, now, change, lines);
    }

    /// <summary>One file's side of the comparison, from the read's own snapshot.</summary>
    private static ComparedFile FileOf(GridReadContext context)
    {
        var results = IssueReader.ReadResults(context.Connection, context.Transaction)
                      ?? throw new InvalidOperationException("The crawl's analysis went missing within its snapshot.");
        Dictionary<string, IssueSeverity> severities = new(StringComparer.Ordinal);
        using (var command = context.Command("SELECT id, severity FROM issue_types", []))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                // A severity this engine doesn't know (a later engine's) is the catalog's, if the check is known.
                var checkId = reader.GetString(0);
                if (Enum.TryParse<IssueSeverity>(reader.GetString(1), ignoreCase: true, out var severity) && Enum.IsDefined(severity))
                {
                    severities[checkId] = severity;
                }
                else if (IssueCatalog.Find(checkId) is { } known)
                {
                    severities[checkId] = known.Severity;
                }
            }
        }

        return new ComparedFile(results, context.Options, context.Status, severities);
    }

    /// <summary>What a check needs of a URL to judge it; a check this engine doesn't know, the most: a page read again.</summary>
    private static UrlJudging JudgingOf(string checkId) => IssueCatalog.Find(checkId) is { } check ? CheckJudging.Of(check) : UrlJudging.Pages;

    /// <summary>
    /// Kept in the read cache under both files' stamps (each with its analysis), so a crawl continued or analysed again on
    /// either side reads afresh. Not kept while the other crawl is running: its stamp doesn't move until it ends.
    /// </summary>
    private static T Kept<T>(GridReadContext now, GridReadContext then, string key, Func<T> read) where T : notnull =>
        then.Stamp is { Live: false } stamp ? now.Cached($"compare|{stamp}|{key}", read) : read();

    /// <summary>As <see cref="Kept{T}"/>, for a check's join, of which only a few are kept.</summary>
    private static T KeptLarge<T>(GridReadContext now, GridReadContext then, string key, Func<T> read) where T : notnull =>
        then.Stamp is { Live: false } stamp ? now.CachedLarge($"compare|{stamp}|{key}", read) : read();
}
