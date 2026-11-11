using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Engine.Analysis;

/// <summary>
/// The site's score, version 1 (docs/v2-ui-simple.md, "The score"). It starts at 100, and each check that found a
/// problem takes points off: up to 10 for an error (60 if it is a fault of the whole site) and up to 4 for a warning, by
/// how much of the site it affects. While any error stands the score can't be above <see cref="SERIOUS_CEILING"/>, and
/// notices take nothing off.
/// <para>
/// It is worked out once, when a crawl is analysed, from that analysis's own counts, and stored with it
/// (<see cref="Storage.IssueReader.ReadResults"/>), so the app, the report and the command line give the same number, and
/// no count from one moment is ever set against a total from another.
/// </para>
/// </summary>
public static class ScoreCalculator
{
    /// <summary>
    /// The formula's version, stored with every score. Bump it with any change to what the same analysis would score:
    /// the points, the bands, the ceiling, what a count is a share of, or which checks the catalog marks
    /// <see cref="IssueDefinition.SiteWide"/> or gives another <see cref="IssueDefinition.Subject"/>. An analysis scored
    /// by another version is out of date (<see cref="OutOfDateReason.EngineChanged"/>).
    /// </summary>
    public const int VERSION = 1;

    /// <summary>The most a site can score while an error check has issues: "Very good" never sits beside a serious problem.</summary>
    public const int SERIOUS_CEILING = 89;

    /// <summary>How many problems "Fix first" leads with: <see cref="SiteScore.IfFixed"/> is the score without them.</summary>
    public const int FIX_FIRST = 5;

    private const int FULL_SCORE = 100;

    /// <summary>
    /// The score of an analysis, or null when it read no page: there is nothing honest to say of a site nothing was read of.
    /// </summary>
    /// <param name="checks">Every check that ran, with the URLs it flagged (0 for one that found nothing).</param>
    /// <param name="checksSkipped">The checks the analysis couldn't run.</param>
    /// <param name="pages">The pages the analysis read.</param>
    /// <param name="urls">The URLs crawled, as they stood when the analysis ran.</param>
    /// <param name="incomplete">Why the crawl didn't read all of the site, or null when it did: the score is then partial.</param>
    /// <param name="blockedByRobots">The site's URLs robots.txt kept the crawler out of.</param>
    public static SiteScore? Calculate(IReadOnlyList<IssueCount> checks, int checksSkipped, long pages, long urls, ScoreIncompleteReason? incomplete,
        long blockedByRobots)
    {
        if (pages <= 0)
        {
            return null;
        }

        List<ScoreLine> lines = [.. FixFirstOrder.Of(checks).Select(check => Line(check, pages, urls))];
        var (value, lost, beforeCeiling, held) = Add(lines);

        // The first problems to fix: notices come last in the order, and cost nothing, so they are no part of it.
        List<string> first = [.. lines.Where(line => line.Severity != IssueSeverity.Notice).Take(FIX_FIRST).Select(line => line.CheckId)];
        var without = Add([.. lines.Where(line => !first.Contains(line.CheckId))]).Value;

        return new SiteScore(VERSION, value, BandOf(value), lost, beforeCeiling, held, incomplete is not null,
            new ScoreCoverage(pages, urls, checks.Count, checksSkipped, incomplete is null, incomplete, blockedByRobots), lines,
            new ScoreIfFixed(first, without));
    }

    /// <summary>The score as a word: 90 and over is very good, 70 and over good, 50 and over needs work, under 50 poor.</summary>
    public static ScoreBand BandOf(int value) => value switch
    {
        >= 90 => ScoreBand.VeryGood,
        >= 70 => ScoreBand.Good,
        >= 50 => ScoreBand.NeedsWork,
        _ => ScoreBand.Poor
    };

    /// <summary>
    /// How much of the site a count is: half or more is most, 1 in 10 or more many, 1 in 100 or more some, and under that a
    /// few. Whole numbers throughout, so 1 of 100 is "some" on every machine.
    /// </summary>
    public static ScoreExtent ExtentOf(long count, long of) =>
        count * 2 >= of ? ScoreExtent.Most
        : count * 10 >= of ? ScoreExtent.Many
        : count * 100 >= of ? ScoreExtent.Some
        : ScoreExtent.Few;

    /// <summary>The points a check takes off, by its severity and how much of the site it affects.</summary>
    public static int Points(IssueSeverity severity, ScoreExtent extent) => (severity, extent) switch
    {
        (IssueSeverity.Error, ScoreExtent.Few) => 2,
        (IssueSeverity.Error, ScoreExtent.Some) => 4,
        (IssueSeverity.Error, ScoreExtent.Many) => 7,
        (IssueSeverity.Error, ScoreExtent.Most) => 10,
        (IssueSeverity.Error, ScoreExtent.Site) => 60,
        (IssueSeverity.Warning, ScoreExtent.Few) => 1,
        (IssueSeverity.Warning, ScoreExtent.Some) => 2,
        (IssueSeverity.Warning, ScoreExtent.Many) => 3,
        (IssueSeverity.Warning, ScoreExtent.Most or ScoreExtent.Site) => 4,
        _ => 0
    };

    /// <summary>
    /// A page check's count is a share of the pages read, and any other check's of the URLs crawled. A site-wide fault
    /// is the whole site, whatever its count.
    /// </summary>
    private static ScoreLine Line(IssueCount check, long pages, long urls)
    {
        var unit = check.Issue.SiteWide ? ScoreUnit.Site : check.Issue.Subject == IssueSubject.Url ? ScoreUnit.Urls : ScoreUnit.Pages;
        var of = unit == ScoreUnit.Pages ? pages : urls;
        var extent = check.Issue.SiteWide ? ScoreExtent.Site : ExtentOf(check.Count, of);
        return new ScoreLine(check.Issue.Id, check.Issue.Severity, check.Count, of, unit, extent, Points(check.Issue.Severity, extent));
    }

    private static (int Value, int Lost, int BeforeCeiling, bool HeldBySerious) Add(IReadOnlyList<ScoreLine> lines)
    {
        var lost = lines.Sum(line => line.Points);
        var beforeCeiling = Math.Max(0, FULL_SCORE - lost);
        var serious = lines.Any(line => line.Severity == IssueSeverity.Error);
        return (serious ? Math.Min(beforeCeiling, SERIOUS_CEILING) : beforeCeiling, lost, beforeCeiling, serious && beforeCeiling > SERIOUS_CEILING);
    }
}
