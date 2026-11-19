using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Reporting.Models;

/// <summary>A check that found something, as the report and the workbook tell it.</summary>
/// <param name="Number">
/// Its job's number (<see cref="ReportJobs"/>), from 1 in "Fix first" order: the report's list and its section carry it,
/// and the workbook's sheet; a problem folded under another (<see cref="Along"/>) has its job's and shows none of its own.
/// </param>
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

    /// <summary>Its sheet in the workbook, which the report names for its full list: its job's number, and a letter when it's folded ("2a").</summary>
    public string Sheet { get; init; } = Number.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The check of the job it's folded under, one a fix of which clears it entirely; null for a job.</summary>
    public string? Along { get; init; }

    /// <summary>Its place in "Fix first" order, from 1: the five "Fix these first" lists are the first five, as the score's "if fixed" has them.</summary>
    public int Rank { get; init; } = Number;
}
