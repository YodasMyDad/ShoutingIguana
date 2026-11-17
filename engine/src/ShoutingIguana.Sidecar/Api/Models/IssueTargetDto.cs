using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>
/// A URL a page's issue names (evidence): what it is (its status, the response it ends at, where its redirects end, whether
/// it can be indexed, what it's canonicalised to), how the page links to it, and for an image, the &lt;img&gt; as the page
/// writes it. Flat, as <c>/issues/{checkId}/targets</c> gives it.
/// </summary>
/// <param name="Flags">The page's link's flags, by name: <c>noFollow</c>, <c>imageLink</c>, <c>fromHttpHeader</c>…</param>
/// <param name="Links">How many links of any kind the page has to it.</param>
internal sealed record IssueTargetDto(
    int Id,
    string Url,
    int? StatusCode,
    bool Fetched,
    UrlOutcome? Outcome,
    string? ContentType,
    long? SizeBytes,
    int? PixelWidth,
    int? PixelHeight,
    int? ResponseMs,
    string? RedirectKind,
    RedirectEnd? Final,
    IndexabilityReason? Indexability,
    UrlTarget? Canonical,
    LinkKind? Kind,
    string? Text,
    LinkPosition? Position,
    string? Fragment,
    IReadOnlyList<string> Flags,
    int Links,
    ImageOnPage? Img)
{
    public static IssueTargetDto From(IssueTarget target) => new(
        target.Id, target.Url, target.StatusCode, target.Fetched, target.Outcome,
        target.Facts?.ContentType, target.Facts?.SizeBytes, target.Facts?.PixelWidth, target.Facts?.PixelHeight, target.Facts?.ResponseMs, target.Facts?.RedirectKind, target.Facts?.Final,
        target.Facts?.Indexability, target.Facts?.Canonical,
        target.Kind, target.Text, target.Position, target.Fragment, FlagNames(target.Flags), target.Links, target.Img);

    /// <summary>A link's flags by name, as a link's row gives them: the ones this engine knows.</summary>
    public static IReadOnlyList<string> FlagNames(LinkFlags flags) =>
        [.. Enum.GetValues<LinkFlags>().Where(flag => flag != LinkFlags.None && flags.HasFlag(flag)).Select(flag => char.ToLowerInvariant(flag.ToString()[0]) + flag.ToString()[1..])];
}
