namespace ShoutingIguana.Engine.Reporting.Models;

/// <summary>
/// A problem most of whose pages share one cause, which one fix probably clears: the pages that share it, and what they
/// share (the other URL every one of them names, or the fault every one of them has).
/// </summary>
/// <param name="NearlyEveryPage">A check that doesn't group flagged nearly every page audited: it's probably the site's template.</param>
internal sealed record ReportOneFix(long Pages, string? RelatedUrl, string? Detail, bool NearlyEveryPage = false);
