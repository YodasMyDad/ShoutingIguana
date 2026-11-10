using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <param name="Description">What is wrong.</param>
/// <param name="Why">Why it matters.</param>
/// <param name="Fix">What to do about it.</param>
/// <param name="LearnMore">A page that explains the check, or null.</param>
/// <param name="Grouping">How the check's issues group (E3a): <c>detail</c>, <c>relatedUrl</c> or <c>none</c>.</param>
/// <param name="Subject">What the URLs it flags are (E3b): <c>page</c> when always pages, <c>url</c> when any URL.</param>
internal sealed record CheckDto(
    string Id,
    IssueCategory Category,
    IssueSeverity Severity,
    string Title,
    string Description,
    string Fix,
    string Why,
    string? LearnMore,
    string Grouping,
    IssueSubject Subject);
