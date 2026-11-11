using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Reporting.Models;

namespace ShoutingIguana.Engine.Reporting;

/// <summary>
/// The score, version 1, worked out from an analysis's counts as the design's reference does
/// (<c>app/design/simple/build/lib/score.mjs</c>): it starts at 100, each check that found a problem takes points off
/// by its severity and how much of the site it affects, a fault of the whole site takes 60 (serious) or 4, things worth a
/// look take nothing, and while a serious problem stands the score can't pass 89.
/// <para>
/// <b>A stand-in.</b> The score is the analysis's to work out and store, so every reader shows one number; until the
/// report reads that one (<see cref="ReportSources"/>), this works it out the same way, so the document is whole.
/// </para>
/// </summary>
internal static class StandInScore
{
    public const int VERSION = 1;

    public const int SERIOUS_CEILING = 89;

    /// <summary>How many of the first problems "what it would be once fixed" takes away.</summary>
    public const int FIX_FIRST = 5;

    /// <summary>Faults of the whole site, not of pages: they score as the whole site whatever their count.</summary>
    private static readonly HashSet<string> SITE_WIDE = new(StringComparer.Ordinal)
    {
        "robots-txt-unreachable", "no-https", "site-hidden-from-search", "duplicate-site-version", "robots-txt-unreachable-other-origin"
    };

    /// <summary>The checks that found something, in the order to fix them: the most severe first, then the most affected.</summary>
    public static IReadOnlyList<string> FixFirst(IReadOnlyDictionary<string, long> counts) =>
    [
        .. counts.Where(count => count.Value > 0 && IssueCatalog.Find(count.Key) is not null)
            .OrderBy(count => IssueCatalog.Find(count.Key)!.Severity).ThenByDescending(count => count.Value).ThenBy(count => count.Key, StringComparer.Ordinal)
            .Select(count => count.Key)
    ];

    /// <summary>The score of an analysis's counts; null when no page was read, as there's nothing honest to say.</summary>
    /// <param name="counts">The URLs each check flagged.</param>
    /// <param name="fixFirst">The checks that found something, in the order to fix them.</param>
    public static ReportScore? Of(IReadOnlyDictionary<string, long> counts, IReadOnlyList<string> fixFirst, ScoreCovers covers)
    {
        if (covers.Pages <= 0)
        {
            return null;
        }

        List<ScoreLine> lines = [.. fixFirst.Select(id => Line(IssueCatalog.Find(id)!, counts[id], covers))];
        var (value, lost, before, held) = Sum(lines);
        var first = lines.Where(line => line.Severity != IssueSeverity.Notice).Take(FIX_FIRST).Select(line => line.CheckId).ToList();
        int? fixedValue = first.Count == 0 ? null : Sum([.. lines.Where(line => !first.Contains(line.CheckId))]).Value;
        return new ReportScore(VERSION, value, Band(value), lost, before, held, SERIOUS_CEILING, !covers.Complete, covers, lines, fixedValue, first);
    }

    private static (int Value, int Lost, int BeforeCeiling, bool Held) Sum(IReadOnlyList<ScoreLine> lines)
    {
        var lost = lines.Sum(line => line.Points);
        var before = Math.Max(0, 100 - lost);
        var serious = lines.Any(line => line.Severity == IssueSeverity.Error);
        return (serious ? Math.Min(before, SERIOUS_CEILING) : before, lost, before, serious && before > SERIOUS_CEILING);
    }

    private static ScoreLine Line(IssueDefinition check, long count, ScoreCovers covers)
    {
        if (SITE_WIDE.Contains(check.Id))
        {
            return new ScoreLine(check.Id, check.Severity, count, 1, ScoreUnit.Site, ScoreExtent.Site, Points(check.Severity, ScoreExtent.Site));
        }

        var unit = check.Subject == IssueSubject.Page ? ScoreUnit.Pages : ScoreUnit.Urls;
        var of = Math.Max(1, unit == ScoreUnit.Pages ? covers.Pages : covers.Urls);
        var share = Math.Min(1d, (double)count / of);
        var extent = share >= 0.5 ? ScoreExtent.Most : share >= 0.1 ? ScoreExtent.Many : share >= 0.01 ? ScoreExtent.Some : ScoreExtent.Few;
        return new ScoreLine(check.Id, check.Severity, count, of, unit, extent, Points(check.Severity, extent));
    }

    private static int Points(IssueSeverity severity, ScoreExtent extent) => (severity, extent) switch
    {
        (IssueSeverity.Error, ScoreExtent.Few) => 2,
        (IssueSeverity.Error, ScoreExtent.Some) => 4,
        (IssueSeverity.Error, ScoreExtent.Many) => 7,
        (IssueSeverity.Error, ScoreExtent.Most) => 10,
        (IssueSeverity.Error, _) => 60,
        (IssueSeverity.Warning, ScoreExtent.Few) => 1,
        (IssueSeverity.Warning, ScoreExtent.Some) => 2,
        (IssueSeverity.Warning, ScoreExtent.Many) => 3,
        (IssueSeverity.Warning, _) => 4,
        _ => 0
    };

    private static ScoreBand Band(int value) => value >= 90 ? ScoreBand.VeryGood : value >= 70 ? ScoreBand.Good : value >= 50 ? ScoreBand.NeedsWork : ScoreBand.Poor;
}
