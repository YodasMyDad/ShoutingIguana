using System.Text.Json.Serialization;

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

    /// <summary>
    /// Lanes 1 to 3 of the content fingerprint, <see cref="SimHash"/> being lane 0. They are stored in columns of their own
    /// (<c>responses.simhash_1</c> to <c>_3</c>), not with the rest of the facts, and the analysis reads them back from
    /// there. Null when the crawl didn't record them: it was made, or the page fetched, by an earlier version.
    /// </summary>
    [JsonIgnore]
    public SimHashLanes? SimHashLanes { get; init; }

    /// <summary>
    /// The page's <c>&lt;link rel="icon"&gt;</c> elements, in page order: what the crawl chooses the site's icon from
    /// (<c>Favicons.FaviconFetcher</c>), on the first page it reads. They aren't stored with the rest of the facts: the
    /// links table has each as a link of kind icon, and nothing reads them back.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<IconFact> Icons { get; init; } = [];
}
