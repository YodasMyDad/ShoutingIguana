using ShoutingIguana.Engine.Analysis.Models;

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
    ReportOneFix? OneFix);
