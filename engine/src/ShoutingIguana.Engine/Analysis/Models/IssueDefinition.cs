namespace ShoutingIguana.Engine.Analysis.Models;

/// <summary>One entry in the issue catalog.</summary>
/// <param name="Id">Stable kebab-case id stored in crawl files, e.g. <c>title-too-long</c>.</param>
/// <param name="Description">What is wrong. The catalog text's "what".</param>
/// <param name="Fix">What to do about it. The catalog text's "fix".</param>
public sealed record IssueDefinition(
    string Id,
    IssueCategory Category,
    IssueSeverity Severity,
    string Title,
    string Description,
    string Fix)
{
    /// <summary>
    /// Why it matters. Shown with the check, and not part of <see cref="AnalysisFreshness.ChecksFingerprint"/>.
    /// </summary>
    public required string Why { get; init; }

    /// <summary>
    /// A page that explains the check, or null when no authoritative source exists. Not part of
    /// <see cref="AnalysisFreshness.ChecksFingerprint"/>.
    /// </summary>
    public string? LearnMore { get; init; }

    /// <summary>
    /// How the check's issues group, as its rule writes their details. Only how they're shown: it isn't stored with an
    /// analysis, nor part of <see cref="AnalysisFreshness.ChecksFingerprint"/>.
    /// </summary>
    public required IssueGrouping Grouping { get; init; }

    /// <summary>
    /// What the URLs the check flags are: always pages, or any URL the crawl has, as its rule can flag them. Only how its
    /// count is worded: it isn't stored with an analysis, nor part of <see cref="AnalysisFreshness.ChecksFingerprint"/>.
    /// </summary>
    public required IssueSubject Subject { get; init; }

    /// <summary>
    /// For a check grouped by detail whose detail is each page's own instance of a fault (the href of its relative canonical,
    /// a JSON parser's message saying where in the block it failed): the fault the detail shows, which its groups are by
    /// instead, so a fault in the site's template is one group. The analysis stores it beside each issue, and the issue keeps
    /// its own detail, so a change to one comes with a new <see cref="AnalysisFreshness.VERSION"/>. Null groups by the
    /// detail itself.
    /// </summary>
    internal Func<string, string>? FaultOf { get; init; }
}
