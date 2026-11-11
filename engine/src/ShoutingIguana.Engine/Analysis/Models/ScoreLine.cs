namespace ShoutingIguana.Engine.Analysis.Models;

/// <summary>One check that found something, and what it takes off the score.</summary>
/// <param name="Severity">The check's severity when the analysis ran.</param>
/// <param name="Count">The URLs it flagged.</param>
/// <param name="Of">
/// What the count is a share of: the pages read or the URLs crawled, as <paramref name="Unit"/> says. For a site-wide
/// fault it is the URLs crawled, and isn't for showing: the fault is the whole site's.
/// </param>
/// <param name="Points">Points off the score. 0 for a notice.</param>
public sealed record ScoreLine(string CheckId, IssueSeverity Severity, long Count, long Of, ScoreUnit Unit, ScoreExtent Extent, int Points);
