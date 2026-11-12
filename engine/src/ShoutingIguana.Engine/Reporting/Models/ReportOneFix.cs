namespace ShoutingIguana.Engine.Reporting.Models;

/// <summary>
/// A problem most of whose pages share one cause, which one fix probably clears: the pages that share it, and what they
/// share (the other URL every one of them names, or the fault every one of them has).
/// </summary>
internal sealed record ReportOneFix(long Pages, string? RelatedUrl, string? Detail);
