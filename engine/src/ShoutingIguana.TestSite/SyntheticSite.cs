using System.Globalization;
using System.Text;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.TestSite;

/// <summary>
/// A deterministic synthetic shop for tests and benchmarks. Every response is generated from its URL, so any size can
/// be served without storage. Pages pass every SEO check unless an issue is planted on them: products carry issues at
/// fixed intervals, and /plants/ holds a test page for each check. <see cref="GetExpectations"/> reports exactly what
/// a correct crawl and analysis must find.
/// </summary>
public sealed partial class SyntheticSite
{
    public const int CATEGORY_COUNT = 20;
    public const int IMAGE_COUNT = 100;
    public const int SITEMAP_CHUNK_SIZE = 10_000;
    public const string PLANTS_PATH = "/plants/";

    /// <summary>With <see cref="SyntheticSiteOptions.DeadTwin"/>, the dead twin's URLs the first orphan links: never checked.</summary>
    public const int DEAD_TWIN_UNCHECKED = 5;

    private const int PRODUCTS_PER_LISTING = 50;
    private const int PARAGRAPHS = 8;
    private const int WORDS_PER_PARAGRAPH = 60;
    private const int DESCRIPTION_WORDS = 14;
    private const int INTRO_WORDS = 220;
    private const string HTML = "text/html; charset=utf-8";
    private const string XML = "application/xml; charset=utf-8";

    private static readonly string[] STATIC_PAGES = ["about", "contact", "privacy"];

    private static readonly string[] WORDS =
    [
        "garden", "kettle", "river", "copper", "lantern", "meadow", "harbour", "velvet", "orchard", "pepper",
        "anchor", "biscuit", "candle", "dagger", "ember", "falcon", "glacier", "hammer", "island", "jacket",
        "kernel", "ladder", "marble", "needle", "oyster", "pillow", "quartz", "ribbon", "saddle", "timber",
        "umbrella", "valley", "walnut", "yarrow", "zephyr", "amber", "bramble", "cinder", "drizzle", "eclipse",
        "fennel", "granite", "hollow", "ivory", "juniper", "kestrel", "lagoon", "mosaic", "nectar", "obsidian",
        "pebble", "quiver", "russet", "sapphire", "thistle", "upland", "vessel", "willow", "xylem", "yonder",
        "azure", "basalt", "cobalt", "dune", "estuary", "fjord", "grove", "heather", "inlet", "jade",
        "knoll", "lichen", "moss", "nimbus", "ochre", "prairie", "quarry", "reef", "spruce", "tundra",
        "fresh", "durable", "handmade", "classic", "modern", "compact", "premium", "everyday", "seasonal", "rustic",
        "the", "and", "with", "for", "from", "under", "over", "between", "through", "beside"
    ];

    // Sent with every page so the home page passes the security headers check.
    private static readonly Dictionary<string, string> SECURITY_HEADERS = new(StringComparer.OrdinalIgnoreCase)
    {
        ["X-Content-Type-Options"] = "nosniff",
        ["X-Frame-Options"] = "SAMEORIGIN",
        ["Content-Security-Policy"] = "default-src 'self'",
        ["Referrer-Policy"] = "strict-origin-when-cross-origin"
    };

    private static readonly string FILLER_STYLES = BuildFillerStyles();
    private static readonly byte[] IMAGE_BYTES = BuildImage(2_048);

    private readonly SyntheticSiteOptions _options;
    private readonly string _base;
    private readonly string _external;
    private readonly IReadOnlyList<Plant> _plants;
    private readonly Dictionary<string, Plant> _plantsByPath;
    private readonly Dictionary<string, Plant> _partnerPlantsByPath;

    public SyntheticSite(SyntheticSiteOptions options, Uri baseUri, Uri externalBaseUri)
    {
        if (options.PageCount < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "A synthetic site needs at least two pages.");
        }

        _options = options;
        _base = baseUri.GetLeftPart(UriPartial.Authority);
        _external = externalBaseUri.GetLeftPart(UriPartial.Authority);
        if (options.DeadTwin && IsHttps)
        {
            throw new ArgumentException("The dead twin is the https:// version of a plain HTTP site.", nameof(options));
        }

        _plants = CreatePlants();
        _plantsByPath = _plants.Where(plant => !plant.OnPartnerSite).ToDictionary(plant => plant.Path, StringComparer.Ordinal);
        _partnerPlantsByPath = _plants.Where(plant => plant.OnPartnerSite).ToDictionary(plant => plant.Path, StringComparer.Ordinal);
    }

    public int PageCount => _options.PageCount;

    private bool IsHttps => _base.StartsWith("https:", StringComparison.Ordinal);

    public int OrphanCount => Math.Max(1, _options.PageCount / 100);

    /// <summary>With <see cref="SyntheticSiteOptions.DeadTwin"/>, where nothing answers: the site's https:// version on its port.</summary>
    private string DeadTwinBase => "https" + _base["http".Length..];

    private int ChunkCount => (PageCount + SITEMAP_CHUNK_SIZE - 1) / SITEMAP_CHUNK_SIZE;

    /// <param name="plainHttp">The request came over plain HTTP, which the HTTPS site answers on its own port too.</param>
    public SyntheticResponse Handle(string path, bool external, bool plainHttp = false)
    {
        if (external)
        {
            if (_partnerPlantsByPath.TryGetValue(path, out var partnerPlant))
            {
                return partnerPlant.Respond();
            }

            if (path.StartsWith("/ext/img/", StringComparison.Ordinal))
            {
                return new SyntheticResponse(200, "image/jpeg", null, IMAGE_BYTES, null);
            }

            return path.StartsWith("/ext/", StringComparison.Ordinal)
                ? Html(RenderSimple("Partner site", "An external site linked from the shop.", path))
                : NotFound(path);
        }

        if (path == "/")
        {
            return Html(RenderHome());
        }

        // robots.txt names /sitemap.xml, which redirects to the index, as Yoast sites do. The HTTPS site's plain-HTTP side
        // drops the connection for it, though it still serves its page.
        if (path == "/robots.txt" && IsHttps && plainHttp)
        {
            return new SyntheticResponse(0, HTML, null, null, null, Abort: true);
        }

        if (path == "/robots.txt")
        {
            var sitemap = _options.Sitemaps ? $"\nSitemap: {_base}/sitemap.xml\n" : string.Empty;
            return new SyntheticResponse(200, "text/plain; charset=utf-8", $"User-agent: *\nDisallow: /private/\n{sitemap}", null, null);
        }

        if (_options.Sitemaps && path == "/sitemap.xml")
        {
            return Redirect(301, "/sitemap_index.xml");
        }

        if (_options.Sitemaps && path == "/sitemap_index.xml")
        {
            return Xml(RenderSitemapIndex());
        }

        if (_options.Sitemaps && path == "/sitemaps/extra.xml")
        {
            return Xml(RenderExtraSitemap());
        }

        if (_options.Sitemaps && TryNumber(path, "/sitemaps/pages-", ".xml", out var chunk) && chunk < ChunkCount)
        {
            return Xml(RenderPagesSitemap(chunk));
        }

        if (TryNumber(path, "/p/", string.Empty, out var product) && product >= 1 && product < PageCount)
        {
            return Html(RenderProduct(product));
        }

        if (TryNumber(path, "/c/", string.Empty, out var category) && category < CATEGORY_COUNT)
        {
            return Html(RenderCategory(category));
        }

        if (TryNumber(path, "/orphan/", string.Empty, out var orphan) && orphan < OrphanCount)
        {
            // With a dead twin, the first orphan links the twin's URLs that are never asked for: only the sitemap leads
            // here, so the crawler reads it once no linked URL is in flight, by when it has written the twin off.
            var twinLinks = orphan == 0 && _options.DeadTwin
                ? $"<ul>{string.Concat(Enumerable.Range(1, DEAD_TWIN_UNCHECKED).Select(page => $"<li><a href=\"{DeadTwinBase}{PLANTS_PATH}dead-twin/{page}\">Twin page {page}</a></li>"))}</ul>"
                : string.Empty;
            return Html(RenderSimple($"Orphan {orphan}", "Only the sitemap lists this page.", path, twinLinks));
        }

        if (TryNumber(path, "/hidden/", string.Empty, out var hidden))
        {
            return Html(RenderSimple($"Hidden {hidden}", "Only nofollow links point here.", path));
        }

        if (TryNumber(path, "/private/", string.Empty, out var secret))
        {
            return Html(RenderSimple($"Private {secret}", "robots.txt disallows this area.", path));
        }

        if (TryNumber(path, "/r/", string.Empty, out var moved) && moved >= 1 && moved < PageCount)
        {
            return Redirect(301, $"/p/{moved}");
        }

        if (TryNumber(path, "/r2/", string.Empty, out var movedTwice) && movedTwice >= 1 && movedTwice < PageCount)
        {
            return Redirect(302, $"/r/{movedTwice}");
        }

        if (path.StartsWith("/err/", StringComparison.Ordinal))
        {
            return new SyntheticResponse(500, HTML, RenderSimple("Server error", "Something went wrong.", path), null, null, SECURITY_HEADERS);
        }

        if (TryNumber(path, "/img/", ".jpg", out _))
        {
            return new SyntheticResponse(200, "image/jpeg", null, IMAGE_BYTES, null);
        }

        if (path == "/img/large.jpg")
        {
            return new SyntheticResponse(200, "image/jpeg", null, LARGE_IMAGE_BYTES, null);
        }

        if (path == "/static/site.css")
        {
            return new SyntheticResponse(200, "text/css", FILLER_STYLES, null, null);
        }

        if (path == "/static/app.js")
        {
            return new SyntheticResponse(200, "application/javascript", "document.documentElement.classList.add('js');", null, null);
        }

        if (STATIC_PAGES.Any(page => path == "/" + page))
        {
            return Html(RenderSimple(CultureInfo.InvariantCulture.TextInfo.ToTitleCase(path[1..]), "A static information page.", path));
        }

        return _plantsByPath.TryGetValue(path, out var plant) ? plant.Respond() : NotFound(path);
    }

    public SyntheticSiteExpectations GetExpectations()
    {
        HashSet<string> broken = [], errors = [], redirects = [], disallowed = [], hidden = [], externalOk = [], externalMissing = [], images = [];
        Dictionary<string, HashSet<string>> issues = new(StringComparer.Ordinal);

        // Paths are on this site; links to the external site are already absolute.
        void Flag(string issue, string pathOrUrl)
        {
            if (!issues.TryGetValue(issue, out var urls))
            {
                issues[issue] = urls = new HashSet<string>(StringComparer.Ordinal);
            }

            urls.Add(pathOrUrl.StartsWith('/') ? _base + pathOrUrl : pathOrUrl);
        }

        for (var product = 1; product < PageCount; product++)
        {
            var path = $"/p/{product}";
            foreach (var (href, _, _) in ContentLinks(product))
            {
                if (href.StartsWith("/broken/", StringComparison.Ordinal))
                {
                    broken.Add(href);
                    Flag("internal-client-error", href);
                    Flag("broken-internal-links", path);
                }
                else if (href.StartsWith("/err/", StringComparison.Ordinal))
                {
                    errors.Add(href);
                    Flag("internal-server-error", href);
                    Flag("broken-internal-links", path);
                }
                else if (href.StartsWith("/r/", StringComparison.Ordinal))
                {
                    redirects.Add(href);
                    Flag("links-to-redirects", path);
                }
                else if (href.StartsWith("/r2/", StringComparison.Ordinal))
                {
                    // A 302 to a 301: a chain, and a temporary redirect.
                    redirects.Add(href);
                    redirects.Add("/r/" + href[4..]);
                    Flag("redirect-chain", href);
                    Flag("redirect-temporary", href);
                    Flag("links-to-redirects", path);
                }
                else if (href.StartsWith("/private/", StringComparison.Ordinal))
                {
                    disallowed.Add(href);
                    Flag("blocked-by-robots", href);
                }
                else if (href.StartsWith("/hidden/", StringComparison.Ordinal))
                {
                    hidden.Add(href);
                    Flag("internal-nofollow-links", path);
                }
                else if (href.Contains("/ext-missing/", StringComparison.Ordinal))
                {
                    externalMissing.Add(href);
                    Flag("broken-external-links", path);
                }
                else if (href.Contains("/ext/", StringComparison.Ordinal))
                {
                    externalOk.Add(href);
                }
            }

            for (var image = 0; image < 3; image++)
            {
                images.Add(ImagePath(product, image));
            }

            if (IsNoindex(product))
            {
                Flag("noindex-page", path);

                // noindex decides the page, so a canonical elsewhere is a mixed signal rather than a canonicalised page.
                if (IsCanonicalised(product))
                {
                    Flag("noindex-with-canonical", path);
                }
            }
            else if (IsCanonicalised(product))
            {
                Flag("canonicalised-page", path);
            }

            // Every product is in a sitemap, so each non-indexable one is a sitemap issue.
            if (_options.Sitemaps && !IsIndexable(product))
            {
                Flag("sitemap-non-indexable", path);
            }

            if (IsCanonicalised(product) && !IsIndexable(product - 1))
            {
                Flag("canonical-to-non-indexable", path);
            }

            // Content checks only look at indexable pages, so a pair counts only when both are indexable.
            if (HasDuplicateTitle(product) && IsIndexable(product) && IsIndexable(product - 1))
            {
                Flag("title-duplicate", path);
                Flag("title-duplicate", $"/p/{product - 1}");
            }

            if (MissesDescription(product) && IsIndexable(product))
            {
                Flag("description-missing", path);
            }

            if (MissesAlt(product))
            {
                Flag("image-missing-alt", path);
            }
        }

        // Site-wide findings go on the home page: plain HTTP, or over HTTPS the missing Strict-Transport-Security header
        // and the plain-HTTP robots.txt that doesn't answer. The HTTPS site's plain-HTTP side serves the home page rather
        // than redirect, a duplicate version of the site. Over plain HTTP, the https:// version doesn't answer, as on a
        // site without TLS, which is no duplicate.
        Flag(IsHttps ? "security-headers-missing" : "no-https", "/");
        if (IsHttps)
        {
            Flag("robots-txt-unreachable-other-origin", "/");
            Flag("duplicate-site-version", "http" + _base["https".Length..] + "/");
        }

        // The site links its dead https:// version, so that version's robots.txt, which doesn't answer, is the site's.
        if (_options.DeadTwin)
        {
            Flag("robots-txt-unreachable-other-origin", "/");
        }

        if (_options.Sitemaps)
        {
            // The sitemap index lists a file that doesn't exist, and the orphans are only in the extra sitemap.
            Flag("sitemap-error", "/");
            for (var orphan = 0; orphan < OrphanCount; orphan++)
            {
                Flag("orphan-page", $"/orphan/{orphan}");
            }

            // Categories are linked from every page but listed in no sitemap.
            for (var category = 0; category < CATEGORY_COUNT; category++)
            {
                Flag("not-in-sitemap", $"/c/{category}");
            }
        }
        else
        {
            Flag("sitemap-missing", "/");
        }

        // A plant's sitemap issues come from its sitemap entry, which a site without sitemaps doesn't have.
        foreach (var plant in _plants)
        {
            foreach (var issue in plant.Issues.Where(issue => _options.Sitemaps || !issue.StartsWith("sitemap-", StringComparison.Ordinal)))
            {
                Flag(issue, PlantUrl(plant));
            }
        }

        var products = PageCount - 1;
        var orphans = _options.Sitemaps ? OrphanCount : 0;
        var htmlPages = 1 + products + CATEGORY_COUNT + STATIC_PAGES.Length + orphans + _plants.Count(plant => plant is { Status: 200, IsPage: true });
        var analysedPages = htmlPages - _plants.Count(plant => plant is { Status: 200, IsPage: true, IsAnalysed: false });

        // A site more than half of whose pages keep themselves out of search is hidden as a whole, which is reported once,
        // on the home page. Every noindex page counts, and every canonicalised one: no planted canonical names its own
        // page on another version of the site. Only a site with HiddenFromSearch gets there.
        if ((Count(issues, "noindex-page") + Count(issues, "canonicalised-page")) * 2 > analysedPages)
        {
            Flag("site-hidden-from-search", "/");
        }

        return new SyntheticSiteExpectations(
            HtmlPages: htmlPages,
            AnalysedPages: analysedPages,
            Products: products,
            Orphans: orphans,
            BrokenUrls: broken.Count + _plants.Count(plant => plant is { Status: 404, OnPartnerSite: false }),
            ServerErrorUrls: errors.Count,
            Redirects: redirects.Count + _plants.Count(plant => plant.Status is >= 300 and < 400),
            RobotsDisallowed: disallowed.Count,
            NofollowOnly: hidden.Count + NOFOLLOW_ONLY_FILES.Length,
            ExternalOk: externalOk.Count,
            ExternalMissing: externalMissing.Count + _plants.Count(plant => plant is { Status: 404, OnPartnerSite: true }),
            Images: images.Count,
            Noindex: Count(issues, "noindex-page"),
            Canonicalised: Count(issues, "canonicalised-page"),
            DuplicateTitlePages: Count(issues, "title-duplicate") - _plants.Count(plant => plant.Issues.Contains("title-duplicate")),
            MissingDescriptions: Count(issues, "description-missing") - _plants.Count(plant => plant.Issues.Contains("description-missing")),
            ImagesMissingAlt: Count(issues, "image-missing-alt") - _plants.Count(plant => plant.Issues.Contains("image-missing-alt")),
            IssueUrls: issues.ToDictionary(pair => pair.Key, IReadOnlySet<string> (pair) => pair.Value, StringComparer.Ordinal))
        {
            NofollowOnlyFiles = NOFOLLOW_ONLY_FILES.Length,
            Unreachable = _plants.Count(plant => plant is { Status: 0, OnPartnerSite: false }),
            NotChecked = _options is { DeadTwin: true, Sitemaps: true } ? DEAD_TWIN_UNCHECKED : 0,
            SkippedChecks = _options.Sitemaps
                ? new HashSet<string>(StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal) { "orphan-page", "not-in-sitemap" }
        };
    }

    private static int Count(Dictionary<string, HashSet<string>> issues, string issue) =>
        issues.TryGetValue(issue, out var urls) ? urls.Count : 0;

    /// <summary>The absolute URL a plant is served, linked and listed at.</summary>
    private string PlantUrl(Plant plant) => plant.Url ?? (plant.OnPartnerSite ? _external : _base) + plant.Path;

    /// <summary>Exact number of content-area links on a product page, for link-count assertions.</summary>
    public int ContentLinkCount(int product) => ContentLinks(product).Count();

    private static bool HasDuplicateTitle(int product) => product % 37 == 0;

    private static bool MissesDescription(int product) => product % 50 == 0;

    private bool IsNoindex(int product) => _options.HiddenFromSearch || product % 101 == 0;

    private static bool IsCanonicalised(int product) => product % 73 == 0;

    private bool IsIndexable(int product) => !IsNoindex(product) && !IsCanonicalised(product);

    private static bool MissesAlt(int product) => product % 31 == 0;

    private IEnumerable<(string Href, bool NoFollow, string Label)> ContentLinks(int product)
    {
        if (product + 1 < PageCount)
        {
            yield return ($"/p/{product + 1}", false, $"Product {product + 1}");
        }

        if (product - 1 >= 1)
        {
            yield return ($"/p/{product - 1}", false, $"Product {product - 1}");
        }

        if (product * 2 < PageCount)
        {
            yield return ($"/p/{product * 2}", false, $"Product {product * 2}");
        }

        if (product * 2 + 1 < PageCount)
        {
            yield return ($"/p/{product * 2 + 1}", false, $"Product {product * 2 + 1}");
        }

        for (var slot = 0; slot < _options.ContentLinksPerPage - 4; slot++)
        {
            var target = RandomProduct(product, slot);
            yield return ($"/p/{target}", false, $"Product {target}");
        }

        if (product % 29 == 0)
        {
            yield return ($"/broken/{product}", false, "Broken link");
        }

        if (product % 211 == 0)
        {
            yield return ($"/err/{product}", false, "Failing page");
        }

        if (product % 53 == 0)
        {
            yield return ($"/r/{RandomProduct(product, 1_000)}", false, "Moved product");
        }

        if (product % 97 == 0)
        {
            yield return ($"/r2/{RandomProduct(product, 2_000)}", false, "Product moved twice");
        }

        if (product % 61 == 0)
        {
            yield return ($"/private/{product % 10}", false, "Private area");
        }

        if (product % 43 == 0)
        {
            yield return ($"/hidden/{product}", true, "Hidden page");
        }

        if (product % 17 == 0)
        {
            yield return ($"{_external}/ext/{product % 50}", false, "Partner site");
        }

        if (product % 89 == 0)
        {
            yield return ($"{_external}/ext-missing/{product}", false, "Dead partner link");
        }
    }

    private string RenderProduct(int product)
    {
        var content = new StringBuilder(8 * 1024);
        content.Append("<h2 id=\"details\">Details</h2>");
        for (var paragraph = 0; paragraph < PARAGRAPHS; paragraph++)
        {
            content.Append("<p>").Append(Words(product, paragraph, WORDS_PER_PARAGRAPH)).Append("</p>");
        }

        content.Append("<h2>Related products</h2><ul class=\"related\">");
        foreach (var (href, noFollow, label) in ContentLinks(product))
        {
            content.Append("<li><a href=\"").Append(href).Append(noFollow ? "\" rel=\"nofollow\">" : "\">").Append(label).Append("</a></li>");
        }

        content.Append("</ul><div class=\"gallery\">");
        for (var image = 0; image < 3; image++)
        {
            var alt = MissesAlt(product) && image == 0 ? string.Empty : $" alt=\"Product {product} photo {image + 1}\"";
            content.Append("<img src=\"").Append(ImagePath(product, image)).Append('"').Append(alt).Append(" width=\"400\" height=\"300\">");
        }

        content.Append("</div>");
        return Render(new PageSpec
        {
            Path = $"/p/{product}",
            Title = TitleOf(HasDuplicateTitle(product) ? product - 1 : product),
            Description = MissesDescription(product) ? null : $"Synthetic product {product}: {Words(product, 99, DESCRIPTION_WORDS)}.",
            H1 = $"Product {product}",
            Content = content.ToString(),
            Canonical = IsCanonicalised(product) ? $"{_base}/p/{product - 1}" : PageSpec.SELF,
            Robots = IsNoindex(product) ? "noindex" : null,
            JsonLd = product % 5 == 0
                ? $$$"""{"@context":"https://schema.org","@type":"Product","name":"Product {{{product}}}","sku":"SKU-{{{product}}}","offers":{"@type":"Offer","price":"19.99","priceCurrency":"GBP"}}"""
                : null
        });
    }

    private string RenderHome()
    {
        var list = new StringBuilder();
        for (var product = 1; product <= Math.Min(PRODUCTS_PER_LISTING, PageCount - 1); product++)
        {
            list.Append("<li><a href=\"/p/").Append(product).Append("\">Product ").Append(product).Append("</a></li>");
        }

        return Render(new PageSpec
        {
            Path = "/",
            Title = "Synthetic Store | Everyday goods for crawler tests",
            Description = "The Synthetic Store home page: a deterministic shop with thousands of products, built to test SEO crawlers.",
            H1 = "Synthetic Store",
            Content = $"<p>{Text("home", INTRO_WORDS)}</p><ul>{list}</ul>"
        });
    }

    private string RenderCategory(int category)
    {
        var list = new StringBuilder();
        for (int product = category, listed = 0; product < PageCount && listed < PRODUCTS_PER_LISTING; product += CATEGORY_COUNT)
        {
            if (product < 1)
            {
                continue;
            }

            list.Append("<li><a href=\"/p/").Append(product).Append("\">Product ").Append(product).Append("</a></li>");
            listed++;
        }

        // The plants hub hangs off the first category, two clicks from home, so depth-limited crawls stay unchanged.
        var plantsLink = category == 0 ? $"<p><a href=\"{PLANTS_PATH}\">Planted test pages</a></p>" : string.Empty;
        return Render(new PageSpec
        {
            Path = $"/c/{category}",
            Title = $"Category {category} products | Synthetic Store",
            Description = $"Every product filed under category {category} in the Synthetic Store, a deterministic shop built to test SEO crawlers.",
            H1 = $"Category {category}",
            Content = $"<p>{Text($"category-{category}", INTRO_WORDS)}</p><ul>{list}</ul>{plantsLink}"
        });
    }

    private string RenderSimple(string heading, string text, string path, string extraContent = "") => Render(new PageSpec
    {
        Path = path,
        Title = $"{heading} | Synthetic Store, a shop for tests",
        Description = $"{heading}: {text} Part of the Synthetic Store, a deterministic shop built for crawler tests.",
        H1 = heading,
        Content = $"<p>{text}</p><p>{Text(heading, INTRO_WORDS)}</p>{extraContent}"
    });

    private string Render(PageSpec page)
    {
        var html = new StringBuilder(32 * 1024);
        html.Append("<!DOCTYPE html><html").Append(page.Lang ? " lang=\"en\"" : string.Empty).Append("><head>");
        if (page.Charset)
        {
            html.Append("<meta charset=\"utf-8\">");
        }

        // A tracking pixel's noscript fallback high in the head, as most sites have. It must not end the head early.
        html.Append("<noscript><img height=\"1\" width=\"1\" src=\"/img/0.jpg\" alt=\"\"></noscript>");

        if (page.Title is not null)
        {
            html.Append("<title>").Append(page.Title).Append("</title>");
        }

        if (page.Description is not null)
        {
            html.Append("<meta name=\"description\" content=\"").Append(page.Description).Append("\">");
        }

        if (page.Viewport)
        {
            html.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        }

        if (page.Canonical is not null)
        {
            html.Append("<link rel=\"canonical\" href=\"").Append(page.Canonical == PageSpec.SELF ? _base + page.Path : page.Canonical).Append("\">");
        }

        if (page.Robots is not null)
        {
            html.Append("<meta name=\"robots\" content=\"").Append(page.Robots).Append("\">");
        }

        if (page.OpenGraph)
        {
            html.Append("<meta property=\"og:title\" content=\"").Append(page.Title ?? page.H1 ?? "Synthetic Store").Append("\">")
                .Append("<meta property=\"og:description\" content=\"").Append(page.Description ?? page.H1 ?? "Synthetic Store").Append("\">")
                .Append("<meta property=\"og:image\" content=\"").Append(_base).Append("/img/0.jpg\">");
        }

        html.Append("<link rel=\"stylesheet\" href=\"/static/site.css\"><script src=\"/static/app.js\" defer></script>");
        html.Append("<style>").Append(FILLER_STYLES).Append("</style>");
        if (page.JsonLd is not null)
        {
            html.Append("<script type=\"application/ld+json\">").Append(page.JsonLd).Append("</script>");
        }

        html.Append(page.ExtraHead);
        html.Append("</head><body><header class=\"site-header\"><a href=\"/\" class=\"logo\">Synthetic Store</a></header>");
        html.Append("<nav><ul><li><a href=\"/\">Home</a></li>");
        for (var category = 0; category < CATEGORY_COUNT; category++)
        {
            html.Append("<li><a href=\"/c/").Append(category).Append("\">Category ").Append(category).Append("</a></li>");
        }

        html.Append("</ul></nav><main>");
        if (page.H1 is not null)
        {
            html.Append("<h1>").Append(page.H1).Append("</h1>");
        }

        html.Append(page.Content).Append("</main><footer><ul>");
        foreach (var staticPage in STATIC_PAGES)
        {
            html.Append("<li><a href=\"/").Append(staticPage).Append("\">").Append(staticPage).Append("</a></li>");
        }

        html.Append("</ul><p>&copy; Synthetic Store</p></footer></body></html>");
        return html.ToString();
    }

    private string RenderSitemapIndex()
    {
        var xml = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?><sitemapindex xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");
        for (var chunk = 0; chunk < ChunkCount; chunk++)
        {
            xml.Append("<sitemap><loc>").Append(_base).Append("/sitemaps/pages-").Append(chunk).Append(".xml</loc></sitemap>");
        }

        // The last entry doesn't exist: a planted sitemap error.
        xml.Append("<sitemap><loc>").Append(_base).Append("/sitemaps/extra.xml</loc></sitemap>");
        xml.Append("<sitemap><loc>").Append(_base).Append("/sitemaps/missing.xml</loc></sitemap></sitemapindex>");
        return xml.ToString();
    }

    private string RenderPagesSitemap(int chunk)
    {
        var xml = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?><urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");
        var end = Math.Min(PageCount, (chunk + 1) * SITEMAP_CHUNK_SIZE);
        for (var page = chunk * SITEMAP_CHUNK_SIZE; page < end; page++)
        {
            xml.Append("<url><loc>").Append(_base).Append(page == 0 ? "/" : $"/p/{page}").Append("</loc></url>");
        }

        return xml.Append("</urlset>").ToString();
    }

    private string RenderExtraSitemap()
    {
        var xml = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?><urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");
        for (var orphan = 0; orphan < OrphanCount; orphan++)
        {
            xml.Append("<url><loc>").Append(_base).Append("/orphan/").Append(orphan).Append("</loc></url>");
        }

        foreach (var page in STATIC_PAGES)
        {
            xml.Append("<url><loc>").Append(_base).Append('/').Append(page).Append("</loc></url>");
        }

        foreach (var plant in _plants.Where(plant => plant.InSitemap))
        {
            xml.Append("<url><loc>").Append(PlantUrl(plant)).Append("</loc></url>");
        }

        return xml.Append("</urlset>").ToString();
    }

    private string TitleOf(int product) => $"Product {product}: {Words(product, 98, 2)} | Synthetic Store";

    private static string ImagePath(int product, int image) => $"/img/{(product * 3 + image) % IMAGE_COUNT}.jpg";

    private int RandomProduct(int product, int slot) => 1 + (int)(Mix(_options.Seed, product, slot) % (uint)(PageCount - 1));

    private string Words(int product, int paragraph, int count) => string.Join(' ', WordArray(product, paragraph, count));

    private string[] WordArray(int product, int paragraph, int count)
    {
        var words = new string[count];
        for (var word = 0; word < count; word++)
        {
            words[word] = WORDS[Mix(_options.Seed, product, paragraph * 1_000 + word) % (uint)WORDS.Length];
        }

        return words;
    }

    /// <summary>Deterministic text for a page that isn't a product, seeded from a key so every page reads differently.</summary>
    private string Text(string key, int count) => Words(KeySeed(key), 0, count);

    /// <summary>FNV-1a of the key with the top bit set, so it never matches a product number.</summary>
    private static int KeySeed(string key)
    {
        var hash = 2166136261u;
        foreach (var character in key)
        {
            hash = (hash ^ character) * 16777619u;
        }

        return (int)(hash | 0x80000000u);
    }

    /// <summary>SplitMix64-style mixing: deterministic across processes, unlike HashCode.Combine.</summary>
    private static uint Mix(int seed, int first, int second)
    {
        var value = (ulong)(uint)seed * 0x9E3779B97F4A7C15UL ^ (ulong)(uint)first * 0xBF58476D1CE4E5B9UL ^ (ulong)(uint)second * 0x94D049BB133111EBUL;
        value ^= value >> 30;
        value *= 0xBF58476D1CE4E5B9UL;
        value ^= value >> 27;
        value *= 0x94D049BB133111EBUL;
        value ^= value >> 31;
        return (uint)value;
    }

    private static bool TryNumber(string path, string prefix, string suffix, out int number)
    {
        number = 0;
        if (!path.StartsWith(prefix, StringComparison.Ordinal) || !path.EndsWith(suffix, StringComparison.Ordinal) ||
            path.Length <= prefix.Length + suffix.Length)
        {
            return false;
        }

        return int.TryParse(path.AsSpan(prefix.Length, path.Length - prefix.Length - suffix.Length), NumberStyles.None,
            CultureInfo.InvariantCulture, out number);
    }

    private static SyntheticResponse Html(string text) => new(200, HTML, text, null, null, SECURITY_HEADERS);

    private static SyntheticResponse Xml(string text) => new(200, XML, text, null, null);

    private static SyntheticResponse Redirect(int status, string location) => new(status, HTML, null, null, location);

    private SyntheticResponse NotFound(string path) =>
        new(404, HTML, RenderSimple("Not found", "This page does not exist.", path), null, null, SECURITY_HEADERS);

    private static string BuildFillerStyles()
    {
        var css = new StringBuilder();
        for (var rule = 0; rule < 120; rule++)
        {
            css.Append(CultureInfo.InvariantCulture, $".c{rule}{{margin:{rule % 7}px;padding:{rule % 5}px;color:#{rule * 2_113 % 0xFFFFFF:x6}}}\n");
        }

        return css.ToString();
    }

    private static byte[] BuildImage(int size)
    {
        var bytes = new byte[size];
        byte[] header = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0x00];
        header.CopyTo(bytes, 0);
        bytes[^2] = 0xFF;
        bytes[^1] = 0xD9;
        return bytes;
    }
}
