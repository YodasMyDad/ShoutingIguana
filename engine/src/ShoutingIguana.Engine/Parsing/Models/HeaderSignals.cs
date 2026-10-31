namespace ShoutingIguana.Engine.Parsing.Models;

/// <summary>SEO signals carried in response headers: Link canonicals/hreflang and X-Robots-Tag.</summary>
internal sealed record HeaderSignals(
    IReadOnlyList<CanonicalFact> Canonicals,
    IReadOnlyList<HreflangFact> Hreflangs,
    IReadOnlyList<ExtractedLink> Links,
    IReadOnlyList<string> RobotsTagValues);
