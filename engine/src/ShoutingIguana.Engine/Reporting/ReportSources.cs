using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Reporting.Models;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Reporting;

/// <summary>
/// Where the report and the workbook get what isn't theirs to decide: a crawl's score and the order to fix its problems
/// in, each check's words, each category's, and each skip reason's. Everything else in the two documents reads these
/// models (<see cref="ReportScore"/>, <see cref="CheckWords"/>), so this is the one place that changes when the score
/// and the plain words come from the analysis and the catalog.
/// </summary>
internal static class ReportSources
{
    /// <summary>The score's version, for a document that has no score to take it from.</summary>
    public static int ScoreVersion => StandInScore.VERSION;

    /// <summary>The checks that found something, in the order to fix them.</summary>
    public static IReadOnlyList<string> FixFirst(AnalysisResults results) => StandInScore.FixFirst(results.Counts);

    /// <summary>The crawl's score; null, with why, when it has none.</summary>
    public static (ReportScore? Score, NoScoreReason? Why) Score(AnalysisResults results, IReadOnlyList<string> fixFirst, ScoreCovers covers) =>
        StandInScore.Of(results.Counts, fixFirst, covers) is { } score ? (score, null) : (null, NoScoreReason.NoPages);

    /// <summary>
    /// A check's words. Its plain words where it has them; otherwise its technical texts stand in: its title, what's wrong
    /// and why it matters as two paragraphs, its fix as one step, and for a check that found nothing, its title with "none
    /// found".
    /// </summary>
    public static CheckWords Words(IssueDefinition check) =>
        new(check.Title, [check.Description, check.Why], [check.Fix], Fixer: null, $"{check.Title}: none found", OneFix: null, IsPlain: false);

    /// <summary>A category's title in the report.</summary>
    public static string Category(IssueCategory category) => category.DisplayName();

    /// <summary>Why a check couldn't run, and what would let it, from the analysis's reason.</summary>
    public static (string Why, string? Action) Skipped(SkippedCheck? reason) => (reason?.Text ?? "The analysis didn’t run this check.", null);
}
