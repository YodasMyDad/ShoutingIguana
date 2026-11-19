using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Reporting.Models;

/// <summary>A check that found something, as the report and the workbook tell it.</summary>
/// <param name="Number">Its place in "Fix first" order, from 1: the report's list, its section and the workbook's sheet all carry it.</param>
/// <param name="Count">The URLs it flagged.</param>
/// <param name="Line">How the score counts it; null when there is no score.</param>
/// <param name="Examples">Its first pages, in crawl order, as many as the report was asked to list.</param>
/// <param name="OneFix">The cause most of its pages share, when one fix probably clears them.</param>
internal sealed record ReportProblem(
    int Number,
    IssueDefinition Check,
    CheckWords Words,
    long Count,
    ScoreLine? Line,
    IReadOnlyList<ReportExample> Examples,
    ReportOneFix? OneFix)
{
    /// <summary>
    /// For a check of the site's own files (<see cref="IssueDefinition.About"/>): the robots.txt or sitemaps it's about, which
    /// the report names where its row would name the home page. Null for any other check, and an analysis that stored none.
    /// </summary>
    public IReadOnlyList<string>? Files { get; init; }

    /// <summary>
    /// The other checks a fix of this one clears, each with the pages it clears there (the analysis's, <see
    /// cref="Storage.IssueCauseReader"/>): one it clears entirely, of the same severity, is one job with it (<see
    /// cref="ReportJobs"/>). Empty when it clears none, or for an analysis that can't tell.
    /// </summary>
    public IReadOnlyList<ClearedCheck> Clears { get; init; } = [];
}
