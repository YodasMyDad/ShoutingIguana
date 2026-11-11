using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Reporting.Models;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Reporting;

/// <summary>
/// Where the report and the workbook get what isn't theirs to decide. The score and the order to fix problems in are
/// the analysis's, stored with it (<see cref="AnalysisResults.Score"/>, <see cref="AnalysisResults.FixFirst"/>), so both
/// documents say the number every other reader says. The words are the catalog's (<see cref="IssuePlainTexts"/>,
/// <see cref="PlainWords"/>), each limit in them filled from the settings the analysis ran with
/// (<see cref="PlainLimits"/>), so a report of a crawl analysed with a title limit of 70 says 70. Everything else in the
/// two documents reads what this gives.
/// </summary>
internal static class ReportSources
{
    /// <summary>The checks that found something, in the order to fix them: the report's numbers and the workbook's sheets follow it.</summary>
    public static IReadOnlyList<string> FixFirst(AnalysisResults results) => results.FixFirst;

    /// <summary>The thresholds the analysis ran with, which its words' limits are said by; an engine that didn't store them ran with the defaults.</summary>
    public static AnalysisSettings Settings(AnalysisResults results) => results.Analysis.Settings ?? new AnalysisSettings();

    /// <summary>
    /// A check's words: its plain words, their limits filled. A check with none has its technical texts stand in: its
    /// title, what's wrong and why it matters as two paragraphs, its fix as one step, and for a check that found nothing,
    /// its title with "none found".
    /// </summary>
    public static CheckWords Words(IssueDefinition check, AnalysisSettings settings) => PlainLimits.Fill(check.Plain, settings) is { } plain
        ? new CheckWords(plain.Title, plain.What, plain.Fix, plain.Fixer, plain.Passed, plain.OneFix, IsPlain: true) { LooksFor = plain.LooksFor }
        : new CheckWords(check.Title, [check.Description, check.Why], [check.Fix], Fixer: null, $"{check.Title}: none found", OneFix: null, IsPlain: false);

    /// <summary>A category's title in the report: its plain one, or its technical one while it has none.</summary>
    public static string Category(IssueCategory category) => PlainWords.CategoryTitle(category) ?? category.DisplayName();

    /// <summary>
    /// Why a check couldn't run, and what would let it: the reason's plain words, or the analysis's own sentence for a
    /// reason that has none, or that an engine from before the reasons didn't store.
    /// </summary>
    public static (string Why, string? Action) Skipped(SkippedCheck? reason, AnalysisSettings settings)
    {
        if (reason is null)
        {
            return ("The analysis didn’t run this check.", null);
        }

        return PlainLimits.Fill(PlainWords.SkipReason(reason.Reason), settings) is { } plain ? (plain.Text, plain.Action) : (reason.Text, null);
    }
}
