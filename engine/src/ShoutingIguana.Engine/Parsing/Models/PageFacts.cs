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
    /// attributes, the page's next and prev links, a <c>Refresh</c> header, the links search engines can't follow, the head's
    /// tags in the body, the document's structure errors, every charset declaration and a separate mobile URL. 2 is batch
    /// 6's: whether an image is a data: URI and its size, and whether it's in a picture with other sources (and the page's
    /// Microdata, batch 5's, which only adds what an earlier page lacks). 3 is batch 7's: the page's forms, and its href
    /// and src values written without a scheme. 4 is batch 8's: whether a heading is an image's alt, the main text's
    /// placeholder, sentences and syllables, and the whole body's words. 6 is the fix loop's (2026-11-18): which links are
    /// written without a scheme (<see cref="LinkFlags.ProtocolRelative"/>), how many such values are in no link
    /// (<see cref="ProtocolRelativeFact.Unlinked"/>), and where on the page each link search engines can't follow is
    /// (<see cref="UncrawlableLinkFact.Position"/>).
    /// </summary>
    public const int CURRENT_COLLECTION = 6;

    /// <summary>
    /// Which collection of facts made these: <see cref="CURRENT_COLLECTION"/> for a page parsed now, 0 for every page parsed
    /// before the facts were versioned. A check that needs a fact a page's collection didn't have is skipped, rather than
    /// reading the fact's absence as the page's (<c>Analysis.CollectedFacts</c>).
    /// </summary>
    public int CollectedVersion { get; init; }

    // The lists the new checks added read as empty for a page an earlier version stored without them: the generated
    // deserialiser sets an init-only property it finds no value for to null, past its initialiser.
    private readonly IReadOnlyList<PaginationFact>? _pagination;
    private readonly IReadOnlyList<string>? _headTagsInBody;
    private readonly IReadOnlyList<string>? _structureErrors;
    private readonly IReadOnlyList<string>? _charsetDeclarations;
    private readonly IReadOnlyList<string>? _microdata;
    private readonly IReadOnlyList<FormFact>? _forms;

    public IReadOnlyList<string> Titles { get; init; } = [];
    public IReadOnlyList<string> MetaDescriptions { get; init; } = [];
    public IReadOnlyList<MetaTagFact> MetaTags { get; init; } = [];
    public IReadOnlyList<CanonicalFact> Canonicals { get; init; } = [];
    public IReadOnlyList<HreflangFact> Hreflangs { get; init; } = [];

    /// <summary>The page's <c>rel="next"</c> and <c>rel="prev"</c> links, in page order. Empty for a page crawled before they were noted.</summary>
    public IReadOnlyList<PaginationFact> Pagination { get => _pagination ?? []; init => _pagination = value; }

    /// <summary>The page's links search engines can't follow, or null with none, and for a page crawled before they were noted.</summary>
    public UncrawlableLinksFact? UncrawlableLinks { get; init; }

    /// <summary>
    /// Head-only tags the parser put in the body, in page order, at most 20: <c>title</c>, <c>meta description</c>,
    /// <c>meta robots</c>, <c>meta viewport</c>, <c>meta refresh</c>, <c>meta content-type</c>, an Open Graph or X tag by its
    /// property (<c>og:title</c>), <c>link alternate</c>, <c>link amphtml</c>. Canonical and hreflang links aren't listed:
    /// their own checks report them. Empty for a page crawled before they were noted.
    /// </summary>
    public IReadOnlyList<string> HeadTagsInBody { get => _headTagsInBody ?? []; init => _headTagsInBody = value; }

    /// <summary>
    /// The document's structure errors as the parser met them, each described: <c>2 &lt;head&gt; tags</c>, <c>2 &lt;body&gt;
    /// tags</c>, <c>&lt;html&gt; after content</c>. A body the parser made up for a stray element in the head, then the page's
    /// own &lt;body&gt; tag, is one body. Empty with none, and for a page crawled before they were noted.
    /// </summary>
    public IReadOnlyList<string> StructureErrors { get => _structureErrors ?? []; init => _structureErrors = value; }

    /// <summary>
    /// Every meta charset and http-equiv Content-Type charset the page declares, in page order, at most 5: the first is
    /// <see cref="DeclaredCharset"/>. Empty for a page crawled before they were noted.
    /// </summary>
    public IReadOnlyList<string> CharsetDeclarations { get => _charsetDeclarations ?? []; init => _charsetDeclarations = value; }

    /// <summary>The first <c>&lt;link rel="alternate" media="…"&gt;</c> without hreflang, resolved: a separate mobile version. Null with none.</summary>
    public string? MobileAlternate { get; init; }
    public IReadOnlyList<HeadingFact> Headings { get; init; } = [];
    public IReadOnlyList<ImageFact> Images { get; init; } = [];
    public IReadOnlyList<string> JsonLd { get; init; } = [];

    /// <summary>
    /// Each top-level Microdata item, typed, as a JSON-LD-shaped object (<see cref="MicrodataReader"/>), up to 50: the
    /// structured data checks read them as JSON-LD blocks. Empty for a page an earlier version parsed.
    /// </summary>
    public IReadOnlyList<string> Microdata { get => _microdata ?? []; init => _microdata = value; }

    /// <summary>The page's forms, up to 20 (collection 3; empty for a page parsed before).</summary>
    public IReadOnlyList<FormFact> Forms { get => _forms ?? []; init => _forms = value; }

    /// <summary>The page's href and src values that start //, counted, with the first (collection 3); null for none.</summary>
    public ProtocolRelativeFact? ProtocolRelativeUrls { get; init; }

    /// <summary>The first "lorem ipsum" in the main text, quoted from there and cut (collection 4); null for none.</summary>
    public string? PlaceholderText { get; init; }

    /// <summary>The main text's sentences and English syllables, for the Flesch reading ease (collection 4).</summary>
    public int SentenceCount { get; init; }

    public int SyllableCount { get; init; }

    /// <summary>Words of the whole body, menus, header and footer included (collection 4); null for a page parsed before.</summary>
    public int? TotalWordCount { get; init; }

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
    /// What ended the head before a canonical, an hreflang link or another head-only tag in the page: set only when one is
    /// outside the head, which is what the checks of these say. Null for every other page, and for a page crawled before the
    /// crawler noted it.
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

    /// <summary>
    /// What in the markup says the page is a Shopify store's (<c>Fetching.ShopifySignals</c>): <c>Shopify.theme</c>, the
    /// theme object only a Shopify storefront's inline script sets, or <c>cdn.shopify.com</c>, a script or stylesheet of
    /// the storefront's from Shopify's CDN. Null for neither. A fact of the crawl, which the writer keeps once for the
    /// site, not of the page: not stored with the rest.
    /// </summary>
    [JsonIgnore]
    public string? ShopifyMarkup { get; init; }
}
