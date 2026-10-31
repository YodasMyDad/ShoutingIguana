namespace ShoutingIguana.Engine.Analysis.Models;

/// <summary>One entry in the issue catalog.</summary>
/// <param name="Id">Stable kebab-case id stored in crawl files, e.g. <c>title-too-long</c>.</param>
/// <param name="Description">What is wrong and why it matters, in one or two sentences.</param>
/// <param name="Fix">What to do about it, in one sentence.</param>
public sealed record IssueDefinition(
    string Id,
    IssueCategory Category,
    IssueSeverity Severity,
    string Title,
    string Description,
    string Fix);
