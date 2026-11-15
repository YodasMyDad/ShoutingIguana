using ShoutingIguana.Engine.Parsing.Models;

namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// A URL a page's issue names (a broken target, the canonical, a redirecting link's target), with its status, and what the
/// page's link to it is: its kind, text (anchor, alt or language) and position, null where the page has no stored link
/// to it (a target only an earlier engine's analysis named).
/// </summary>
/// <param name="Outcome">What became of the request for it, as <see cref="LinkRow.Outcome"/>.</param>
public sealed record IssueTarget(
    int Id,
    string Url,
    int? StatusCode,
    bool Fetched,
    UrlOutcome? Outcome,
    LinkKind? Kind,
    string? Text,
    LinkPosition? Position);
