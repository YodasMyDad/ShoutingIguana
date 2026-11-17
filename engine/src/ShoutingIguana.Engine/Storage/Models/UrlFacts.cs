using ShoutingIguana.Engine.Parsing.Models;

namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// What a URL an issue names is, as the URL grid's columns of these names have it: its status, and the response it ends at
/// through its redirects (an image that redirects is the image at the end), where those end, whether it can be indexed, and
/// what it's canonicalised to. A group's related URL (<see cref="IssueGroup.Related"/>), and each target's own facts.
/// </summary>
/// <param name="ContentType">The response it ends at: its own, or its redirects' end's.</param>
/// <param name="SizeBytes">The response it ends at, in bytes on the wire.</param>
/// <param name="PixelWidth">The response it ends at, for an image whose header gave its size.</param>
/// <param name="Final">Where its redirects end; null when it doesn't redirect.</param>
/// <param name="Indexability">As the URL grid's column: <see cref="IndexabilityReason.None"/> for indexable, null when not judged.</param>
/// <param name="Canonical">The URL it's canonicalised to, when its canonical names another; null otherwise.</param>
/// <param name="ResponseMs">Its own response, not its redirects' end's: how long the whole answer took, as the URL grid's column.</param>
public sealed record UrlFacts(
    int? StatusCode,
    UrlOutcome? Outcome,
    string? ContentType,
    long? SizeBytes,
    int? PixelWidth,
    int? PixelHeight,
    RedirectEnd? Final,
    IndexabilityReason? Indexability,
    UrlTarget? Canonical,
    int? ResponseMs = null);
