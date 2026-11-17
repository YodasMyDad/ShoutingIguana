using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <param name="Description">What is wrong.</param>
/// <param name="Why">Why it matters.</param>
/// <param name="Fix">What to do about it.</param>
/// <param name="LearnMore">A page that explains the check, or null.</param>
/// <param name="Grouping">How the check's issues group (E3a): <c>detail</c>, <c>relatedUrl</c> or <c>none</c>.</param>
/// <param name="Subject">What the URLs it flags are (E3b): <c>page</c> when always pages, <c>url</c> when any URL.</param>
/// <param name="References">
/// Where to fix what it flags (found-on): <c>sources</c> when each URL is something other URLs refer to (show where it's found,
/// <c>/urls/found-on</c>), <c>targets</c> when each page holds the bad references (show what its issue names,
/// <c>/issues/{checkId}/targets</c>), <c>none</c> otherwise.
/// </param>
/// <param name="SiteWide">The check finds one fault of the whole site, which the score counts as the whole site whatever its count.</param>
/// <param name="ReportedOnce">
/// The check reports one issue for the whole site (or one for each other version of it), not one a page: its count isn't
/// a share of the pages. Only for how the count is worded: the score doesn't read it.
/// </param>
/// <param name="PlainTitle">
/// The check in plain words: its title. Null, with every other plain field, while the check has no plain words. Every
/// plain text comes with its limits filled in ("60 characters") from the analysis settings the request is for.
/// </param>
/// <param name="PlainWhat">What's wrong and why it matters, a sentence a string; the first stands alone.</param>
/// <param name="PlainFix">How to fix it, a step a string.</param>
/// <param name="Fixer">Who usually makes the fix: <c>you</c>, <c>developer</c> or <c>either</c>.</param>
/// <param name="PlainPassed">The good news when the check finds nothing.</param>
/// <param name="PlainOneFix">What a fault in the site's template usually is. Null for most checks, plain words or not.</param>
/// <param name="PlainLooksFor">What the check looks for, as a short noun phrase ("A missing sitemap"). Optional, as <paramref name="PlainOneFix"/> is.</param>
/// <param name="Evidence">The URL columns that show what the check found on each row, in order (evidence).</param>
/// <param name="TargetFacts">The fields of a target, and of a group's related URL, to say about each URL the check names, in order (evidence).</param>
/// <param name="Clears">
/// The checks a fix of this one also clears, by id (related checks): this check flags URLs others refer to, each of those
/// the pages that refer to them. What a fix clears on a crawl is its analysis's <c>counts[].clears</c>.
/// </param>
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
    IssueSubject Subject,
    IssueReferences References,
    bool SiteWide,
    bool ReportedOnce,
    string? PlainTitle,
    IReadOnlyList<string>? PlainWhat,
    IReadOnlyList<string>? PlainFix,
    IssueFixer? Fixer,
    string? PlainPassed,
    string? PlainOneFix,
    string? PlainLooksFor,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<string> TargetFacts,
    IReadOnlyList<string> Clears)
{
    /// <param name="settings">The thresholds the plain words' limits are filled from.</param>
    public static CheckDto From(IssueDefinition check, AnalysisSettings settings)
    {
        var plain = PlainLimits.Fill(check.Plain, settings);
        return new CheckDto(check.Id, check.Category, check.Severity, check.Title, check.Description, check.Fix, check.Why, check.LearnMore,
            GridRequests.Grouping(check.Grouping), check.Subject, check.References, check.SiteWide, check.ReportedOnce,
            plain?.Title, plain?.What, plain?.Fix, plain?.Fixer, plain?.Passed, plain?.OneFix, plain?.LooksFor, check.Evidence, check.TargetFacts, check.Clears);
    }
}
