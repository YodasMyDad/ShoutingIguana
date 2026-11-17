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
    LinkPosition? Position)
{
    /// <summary>What the URL is: the response it ends at, where its redirects end, whether it can be indexed. Null only for a target read without them.</summary>
    public UrlFacts? Facts { get; init; }

    /// <summary>The page's link to it: its <c>#fragment</c> as written after the <c>#</c>, or null.</summary>
    public string? Fragment { get; init; }

    /// <summary>The page's link to it: its flags (nofollow, an image link, from an HTTP header…).</summary>
    public LinkFlags Flags { get; init; }

    /// <summary>How many links of any kind the page has to it: a nav link and a footer link to the same 404 are 2.</summary>
    public int Links { get; init; }

    /// <summary>For a target the page shows as an image, the first &lt;img&gt; with its URL as the page writes it; null otherwise.</summary>
    public ImageOnPage? Img { get; init; }
}
