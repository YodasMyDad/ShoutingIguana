using System.Text.Json.Serialization;

namespace ShoutingIguana.Engine.Parsing.Models;

/// <summary>
/// Everything extracted from one page in a single parse. Rules work from these facts, so the HTML never
/// has to be stored or parsed again. Links are kept separately in the links table.
/// </summary>
public sealed record PageFacts
{
    /// <summary>
    /// What <see cref="CollectedVersion"/> the parser writes now. 1 is the new checks' (step 3b): the canonical's other
    /// attributes, the page's next and prev links, a <c>Refresh</c> header.
    /// </summary>
    public const int CURRENT_COLLECTION = 1;

    /// <summary>
    /// Which collection of facts made these: <see cref="CURRENT_COLLECTION"/> for a page parsed now, 0 for every page parsed
    /// before the facts were versioned. A check that needs a fact a page's collection didn't have is skipped, rather than
    /// reading the fact's absence as the page's (<c>Analysis.CollectedFacts</c>).
    /// </summary>
    public int CollectedVersion { get; init; }

    public IReadOnlyList<string> Titles { get; init; } = [];
    public IReadOnlyList<string> MetaDescriptions { get; init; } = [];
    public IReadOnlyList<MetaTagFact> MetaTags { get; init; } = [];
    public IReadOnlyList<CanonicalFact> Canonicals { get; init; } = [];
    public IReadOnlyList<HreflangFact> Hreflangs { get; init; } = [];

    /// <summary>The page's <c>rel="next"</c> and <c>rel="prev"</c> links, in page order. Empty for a page crawled before they were noted.</summary>
    public IReadOnlyList<PaginationFact> Pagination { get; init; } = [];
    public IReadOnlyList<HeadingFact> Headings { get; init; } = [];
    public IReadOnlyList<ImageFact> Images { get; init; } = [];
    public IReadOnlyList<string> JsonLd { get; init; } = [];

    /// <summary>Element ids and named anchors, for checking <c>#fragment</c> links.</summary>
    public IReadOnlyList<string> AnchorIds { get; init; } = [];
    public MetaRefreshFact? MetaRefresh { get; init; }
    public RobotsDirectives Robots { get; init; } = RobotsDirectives.None;
    public string? Language { get; init; }
    public string? DeclaredCharset { get; init; }

    /// <summary>
    /// How far into the page's bytes the meta tag that declared <see cref="DeclaredCharset"/> ends, counted in the encoding
    /// it names. Browsers look for that tag in the first 1024 bytes only (the HTML standard's prescan), so one ending
    /// further in declares nothing on its own. Null with no such tag, and for a page parsed before the crawler recorded it.
    /// </summary>
    public int? DeclaredCharsetEnd { get; init; }

    /// <summary>
    /// The encoding a byte-order mark at the start of the page's bytes declared: <c>utf-8</c>, <c>utf-16be</c> or
    /// <c>utf-16le</c>. It beats every other declaration, so a page with one has declared its encoding. Null with none,
    /// and for a page fetched before the crawler kept it: the stored text has no mark to look for, so such a page reads
    /// as having none until it is crawled again.
    /// </summary>
    public string? ByteOrderMark { get; init; }
    public string? BaseHref { get; init; }

    /// <summary>
    /// What ended the head before a canonical or hreflang link in the page: set only when one is outside the head, which
    /// is what the checks of either say. Null for every other page, and for a page crawled before the crawler noted it.
    /// </summary>
    public HeadEndFact? HeadEnd { get; init; }
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
