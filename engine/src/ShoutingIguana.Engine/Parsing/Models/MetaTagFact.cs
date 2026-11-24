namespace ShoutingIguana.Engine.Parsing.Models;

/// <param name="Name">Lowercase name or property, e.g. <c>description</c> or <c>og:title</c>.</param>
public sealed record MetaTagFact(string Name, string Content)
{
    /// <summary>
    /// The name a <c>&lt;meta http-equiv="Content-Security-Policy"&gt;</c> is kept by: a policy the page sets in its HTML,
    /// which browsers enforce as they do the header's (but for frame-ancestors, report-uri and sandbox, which only a
    /// header can set). Not noted in a crawl made before the check audit of 2026-11-24 (the third).
    /// </summary>
    public const string CONTENT_SECURITY_POLICY = "http-equiv:content-security-policy";

    /// <summary>
    /// Written only as <c>property=</c> (as Open Graph's RDFa is), not <c>name=</c>: X reads its own tags by name. False in a
    /// crawl made before it was noted (<see cref="PageFacts.CURRENT_COLLECTION"/> 5).
    /// </summary>
    public bool IsProperty { get; init; }
}
