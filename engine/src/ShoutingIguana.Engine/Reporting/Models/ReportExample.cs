using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Reporting.Models;

/// <summary>One of a problem's first pages: the URL flagged, what was found on it, and the other URL involved.</summary>
/// <param name="FoundOn">
/// For a check whose URLs other URLs refer to (a 404, a redirect: <see cref="Analysis.Models.IssueReferences.Sources"/>),
/// where this one is found: the pages linking to it, the first of them, the sitemaps listing it. Null for other checks.
/// </param>
/// <param name="Targets">
/// For a check whose pages hold the bad references (<see cref="Analysis.Models.IssueReferences.Targets"/>), how many URLs
/// this page's issue names: the first is <paramref name="RelatedUrl"/>. Null for other checks.
/// </param>
/// <param name="Evidence">The check's evidence for the page, a value each, as a line of the report says it ("“Garden sofas…”", "noindex (Meta robots)").</param>
/// <param name="Named">For a check whose pages hold the bad references, the first URLs the page's issue names, each with what it is.</param>
internal sealed record ReportExample(string Url, string? Detail, string? RelatedUrl, UrlFoundOn? FoundOn = null, long? Targets = null)
{
    public IReadOnlyList<string> Evidence { get; init; } = [];

    public IReadOnlyList<IssueTarget> Named { get; init; } = [];
}
