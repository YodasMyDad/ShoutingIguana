namespace ShoutingIguana.Engine.Parsing.Models;

/// <summary>
/// Everything extracted from one page in a single parse. Rules work from these facts, so the HTML never
/// has to be stored or parsed again. Links are kept separately in the links table.
/// </summary>
public sealed record PageFacts
{
    public IReadOnlyList<string> Titles { get; init; } = [];
    public IReadOnlyList<string> MetaDescriptions { get; init; } = [];
    public IReadOnlyList<MetaTagFact> MetaTags { get; init; } = [];
    public IReadOnlyList<CanonicalFact> Canonicals { get; init; } = [];
    public IReadOnlyList<HreflangFact> Hreflangs { get; init; } = [];
    public IReadOnlyList<HeadingFact> Headings { get; init; } = [];
    public IReadOnlyList<ImageFact> Images { get; init; } = [];
    public IReadOnlyList<string> JsonLd { get; init; } = [];

    /// <summary>Element ids and named anchors, for checking <c>#fragment</c> links.</summary>
    public IReadOnlyList<string> AnchorIds { get; init; } = [];
    public MetaRefreshFact? MetaRefresh { get; init; }
    public RobotsDirectives Robots { get; init; } = RobotsDirectives.None;
    public string? Language { get; init; }
    public string? DeclaredCharset { get; init; }
    public string? BaseHref { get; init; }
    public int WordCount { get; init; }
    public ulong ContentHash { get; init; }
    public ulong SimHash { get; init; }
}
