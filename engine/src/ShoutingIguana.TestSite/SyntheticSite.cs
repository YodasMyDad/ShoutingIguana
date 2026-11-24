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

    /// <summary>The planted page custom extraction reads, and the three values its rules find there.</summary>
    public const string CUSTOM_EXTRACT_PATH = PLANTS_PATH + "custom-extract";

    public const string CUSTOM_EXTRACT_PRICE = "£12.50";
    public const string CUSTOM_EXTRACT_SKU = "SKU-4401";
    public const string CUSTOM_EXTRACT_CODE = "ALPHA-9";

    /// <summary>
    /// Plain HTTP gets a Cloudflare challenge (<c>internal-blocked</c>). A browser request gets the page, which has no
    /// title, and a link to <see cref="BROWSER_FALLBACK_NEXT_PATH"/>, which a crawl that follows links does not fetch
    /// until the browser has read this one.
    /// </summary>
    public const string BROWSER_FALLBACK_PATH = PLANTS_PATH + "browser-fallback";

    /// <inheritdoc cref="BROWSER_FALLBACK_PATH"/>
    public const string BROWSER_FALLBACK_NEXT_PATH = PLANTS_PATH + "browser-fallback-next";

    /// <summary>
    /// A page behind a check that clears by itself in a person's browser, as Cloudflare's "Just a moment..." does. Every
    /// request without the pass is a 403 challenge. The challenge's script sets the pass, a cookie made from the
    /// browser's user agent, and loads the page again, unless the browser says it is automated
    /// (<c>navigator.webdriver</c>), as the headless browser the crawl downloads does. Not linked from the site, and
    /// not a plant: only a crawl started here asks for it. It links to <see cref="HUMAN_CHECK_NEXT_PATH"/>.
    /// </summary>
    public const string HUMAN_CHECK_PATH = PLANTS_PATH + "human-check";

    /// <summary>
    /// The same, with a check that needs a person: the pass is set when the button is pressed. So a test can be the
    /// person, <c>?shown=1</c> on the address presses it once the window is on the screen in front of them.
    /// </summary>
    public const string HUMAN_CHECK_ASK_PATH = PLANTS_PATH + "human-check-ask";

    /// <summary>The next page on the host behind the check. It redirects to <see cref="HUMAN_CHECK_LAST_PATH"/>.</summary>
    public const string HUMAN_CHECK_NEXT_PATH = PLANTS_PATH + "human-check-next";

    /// <inheritdoc cref="HUMAN_CHECK_NEXT_PATH"/>
    public const string HUMAN_CHECK_LAST_PATH = PLANTS_PATH + "human-check-last";

    /// <summary>The cookie that carries the pass for <see cref="HUMAN_CHECK_PATH"/>.</summary>
    public const string HUMAN_CHECK_COOKIE = "si_clearance";

    /// <summary>The cookie that carries the pass for <see cref="HUMAN_CHECK_ASK_PATH"/>.</summary>
    public const string HUMAN_CHECK_ASK_COOKIE = "si_clearance_ask";

    /// <summary>Set by the redirect to itself that <see cref="HUMAN_CHECK_PATH"/> answers a browser with once it has the pass.</summary>
    public const string HUMAN_CHECK_HOP_COOKIE = "si_hop";

    /// <summary>The pass for a browser with this user agent. Another browser's pass is not this one's.</summary>
    public static string Clearance(string userAgent) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("synthetic-clearance:" + userAgent)))[..32].ToLowerInvariant();

    /// <summary>The planted page a list crawl fetches, and the page it links to, which that crawl records and does not fetch.</summary>
    public const string LIST_MODE_PATH = PLANTS_PATH + "list-mode";

    /// <inheritdoc cref="LIST_MODE_PATH"/>
    public const string LIST_MODE_TARGET_PATH = PLANTS_PATH + "list-mode-target";

    /// <summary>Answers 401 until the request carries HTTP Basic for <see cref="AUTH_USER"/>.</summary>
    public const string HTTP_AUTH_PATH = PLANTS_PATH + "http-auth";

    /// <summary>Answers 401 until the request carries HTTP Digest for <see cref="AUTH_USER"/>.</summary>
    public const string DIGEST_AUTH_PATH = PLANTS_PATH + "digest-auth";

    /// <summary>Answers 403 until the request carries the cookie a signed-in session has.</summary>
    public const string COOKIE_GATE_PATH = PLANTS_PATH + "cookie-gate";

    /// <summary>The sign-in form. A correct POST sets <see cref="AUTH_COOKIE"/> and redirects to <see cref="FORM_MEMBER_PATH"/>.</summary>
    public const string FORM_LOGIN_PATH = PLANTS_PATH + "form-login";

    /// <inheritdoc cref="FORM_LOGIN_PATH"/>
    public const string FORM_MEMBER_PATH = PLANTS_PATH + "form-member";

    /// <summary>The user name the auth plants accept.</summary>
    public const string AUTH_USER = "plant";

    /// <summary>The password the auth plants accept.</summary>
    public const string AUTH_PASSWORD = "plant-secret";

    /// <summary>The cookie the form sets and the cookie gate requires: <c>session=planted</c>.</summary>
    public const string AUTH_COOKIE = "session=planted";

    /// <summary>With <see cref="SyntheticSiteOptions.DeadTwin"/>, the dead twin's URLs the first orphan links: never checked.</summary>
    public const int DEAD_TWIN_UNCHECKED = 5;

    private const int PRODUCTS_PER_LISTING = 50;

    /// <summary>The analysis's default deepest a page may be, in clicks from the start (AnalysisSettings.MaxClickDepth).</summary>
    private const int MAX_CLICK_DEPTH = 4;
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

    /// <summary>The home page's: X-Content-Type-Options misspelt, which sets nothing (security-headers-weak).</summary>
    private static readonly Dictionary<string, string> HOME_HEADERS = new(SECURITY_HEADERS, StringComparer.OrdinalIgnoreCase)
    {
        ["X-Content-Type-Options"] = "no-sniff"
    };

    private static readonly string FILLER_STYLES = BuildFillerStyles();
    private static readonly byte[] IMAGE_BYTES = BuildImage(2_048, IMAGE_WIDTH, IMAGE_HEIGHT);

    /// <summary>The size in pixels every image of the site's says it is, in its frame header.</summary>
    public const int IMAGE_WIDTH = 640;

    /// <inheritdoc cref="IMAGE_WIDTH"/>
    public const int IMAGE_HEIGHT = 480;

    /// <summary>A 32 px PNG: a green disc on a pale square. Served as /favicon.ico under an icon file's media type, as sites do.</summary>
    private static readonly byte[] FAVICON_BYTES = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAIAAAD8GO2jAAAAZ0lEQVR42mP48vEFTRHDCLDAqtYFPyLfAoJGE2MNA1VMx2MHA7VMx2UHAxVNx2oHA3VNx7SDgeqmo9lBRwuoaDqyHaMWjFowasHwsmDoF3b0qA/oUaPRo06mR6uCHu0ierTshkbjFwCU+d/JYZdKeQAAAABJRU5ErkJggg==");

    /// <summary>The site's icon, as <c>/favicon.ico</c> serves it.</summary>
    public static IReadOnlyList<byte> Favicon => FAVICON_BYTES;

    private readonly SyntheticSiteOptions _options;
    private readonly string _base;
    private readonly string _external;
    private readonly IReadOnlyList<Plant> _plants;
    private readonly Dictionary<string, Plant> _plantsByPath;
    private readonly Dictionary<string, Plant> _partnerPlantsByPath;
    private readonly Dictionary<string, Plant> _plantsByUrl;

    public SyntheticSite(SyntheticSiteOptions options, Uri baseUri, Uri externalBaseUri)
    {
        if (options.PageCount < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "A synthetic site needs at least two pages.");
        }

        if (options.Revision is < 0 or > LATEST_REVISION)
        {
            throw new ArgumentOutOfRangeException(nameof(options), $"A synthetic site has revisions 0 to {LATEST_REVISION}.");
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
        _plantsByUrl = _plants.ToDictionary(PlantUrl, StringComparer.Ordinal);
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

            // Its rules keep crawlers out of one area, as many sites' do.
            if (path == "/robots.txt")
            {
                return new SyntheticResponse(200, "text/plain; charset=utf-8", "User-agent: *\nDisallow: /ext-private/\n", null, null);
            }

            if (path.StartsWith("/ext/img/", StringComparison.Ordinal))
            {
                return new SyntheticResponse(200, "image/jpeg", null, IMAGE_BYTES, null);
            }

            return path.StartsWith("/ext/", StringComparison.Ordinal)
                ? Html(RenderSimple("Partner site", "An external site linked from the shop.", path))
                : NotFound(path);
        }

        // Revision 1 redirects the HTTPS site's plain-HTTP home page to the HTTPS one, so the site has one version.
        if (path == "/" && IsHttps && plainHttp && Revision >= 1)
        {
            return Redirect(301, _base + "/");
        }

        if (path == "/")
        {
            return Html(RenderHome()) with { Headers = Revision == 0 ? HOME_HEADERS : IsHttps ? HOME_HEADERS_AFTER_HTTPS : HOME_HEADERS_AFTER };
        }

        // robots.txt names /sitemap.xml, which redirects to the index, as Yoast sites do. The HTTPS site's plain-HTTP side
        // drops the connection for it, though it still serves its page.
        // Revision 1 mends it: robots.txt answers on the plain-HTTP side too. The rest of that side still serves pages, as the
        // plain HTTP page shows.
        if (path == "/robots.txt" && IsHttps && plainHttp)
        {
            return Revision == 0
                ? new SyntheticResponse(0, HTML, null, null, null, Abort: true)
                : new SyntheticResponse(200, "text/plain; charset=utf-8", "User-agent: *\nDisallow: /private/\n", null, null);
        }

        // Revision 1 lets the crawler into one URL of the private area, the one robots.txt now allows.
        if (path == "/robots.txt")
        {
            var sitemap = _options is { Sitemaps: true, SitemapInRobots: true } ? $"\nSitemap: {_base}/sitemap.xml\n" : string.Empty;
            var allowed = Revision >= 1 ? $"Allow: {UNBLOCKED_PRIVATE}\n" : string.Empty;
            return new SyntheticResponse(200, "text/plain; charset=utf-8", $"User-agent: *\n{allowed}Disallow: /private/\n{sitemap}", null, null);
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

        // A sitemap the index lists that lists nothing: sitemap-empty.
        if (_options.Sitemaps && path == "/sitemaps/empty.xml")
        {
            return Xml("<?xml version=\"1.0\" encoding=\"UTF-8\"?><urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\"></urlset>");
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

        if (TryNumber(path, "/broken/", string.Empty, out var restored) && FateOfBrokenLink(restored) == BrokenLinkFate.Restored)
        {
            return Html(RenderSimple($"Restored {restored}", "A page that was missing, now back.", path));
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

        // Revision 1 replaces the oversized photo with one of a normal size, at the same address.
        if (path == "/img/large.jpg")
        {
            return new SyntheticResponse(200, "image/jpeg", null, Revision >= 1 ? IMAGE_BYTES : LARGE_IMAGE_BYTES, null);
        }

        // The site's icon, where browsers look for one: no page links it, so it's no URL of a crawl.
        if (path == "/favicon.ico")
        {
            return new SyntheticResponse(200, "image/x-icon", null, FAVICON_BYTES, null);
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
        HashSet<string> broken = [], errors = [], redirects = [], disallowed = [], hidden = [], externalOk = [], externalMissing = [], images = [], restored = [];
        var depths = ProductDepths();
        var linkedBy = ProductInlinks();
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

        List<(string Title, string Path)> titles = [];
        for (var product = 1; product < PageCount; product++)
        {
            var path = $"/p/{product}";
            foreach (var (href, _, _) in ContentLinks(product))
            {
                if (IsRestored(href))
                {
                    restored.Add(href);
                }
                else if (href.StartsWith("/broken/", StringComparison.Ordinal))
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
                    // A 302 to a 301: a chain, and a temporary redirect. The link to it isn't a link to a redirect: the
                    // temporary one keeps its URL the one to use.
                    redirects.Add(href);
                    redirects.Add("/r/" + href[4..]);
                    Flag("redirect-chain", href);
                    Flag("redirect-temporary", href);
                }
                else if (href.StartsWith("/private/", StringComparison.Ordinal) && href != UnblockedPrivate)
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

            if (IsNoindex(product) && HasProductMarkup(product))
            {
                Flag("structured-data-on-noindex", path);
            }

            if (IsNoindex(product))
            {
                Flag("noindex-page", path);

                // A noindex product one or two clicks from home, as every product a listing shows is.
                if (depths[product] <= 2)
                {
                    Flag("noindex-near-home", path);
                }

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

            if (IsIndexable(product) && depths[product] is > MAX_CLICK_DEPTH and < int.MaxValue)
            {
                Flag("deep-page", path);
            }

            if (IsIndexable(product) && linkedBy[product] is (true, false))
            {
                Flag("non-indexable-inlinks-only", path);
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

            // Content checks only look at indexable pages, so a shared title counts only between indexable ones.
            if (IsIndexable(product))
            {
                titles.Add((TitleFor(product), path));
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

        foreach (var shared in titles.GroupBy(page => page.Title, StringComparer.Ordinal).Where(group => group.Count() > 1))
        {
            foreach (var (_, page) in shared)
            {
                Flag("title-duplicate", page);
            }
        }

        // The home page of a site hidden from search is noindex itself, and a sitemap entry that can't be indexed.
        if (IsHomeNoindex)
        {
            Flag("noindex-page", "/");
            if (_options.Sitemaps)
            {
                Flag("sitemap-non-indexable", "/");
            }
        }

        // Site-wide findings go on the home page: plain HTTP, or over HTTPS the missing Strict-Transport-Security header
        // and the plain-HTTP robots.txt that doesn't answer. The HTTPS site's plain-HTTP side serves the home page rather
        // than redirect, a duplicate version of the site. Over plain HTTP, the https:// version doesn't answer, as on a
        // site without TLS, which is no duplicate.
        // Revision 1 drops a security header from the home page, and mends the plain-HTTP side of the HTTPS site.
        Flag(IsHttps ? "security-headers-missing" : "no-https", "/");

        // The home page's X-Content-Type-Options is misspelt ("no-sniff") until revision 1 mends it. Over HTTPS, revision 1
        // sends HSTS for a day, under a year.
        if (Revision == 0 || IsHttps)
        {
            Flag("security-headers-weak", "/");
        }

        // The tracked link's URL: the thin page, canonicalised to itself without the tag.
        Flag("url-tracking-parameters", PLANTS_PATH + "thin?utm_source=newsletter");
        Flag("canonicalised-page", PLANTS_PATH + "thin?utm_source=newsletter");
        if (!IsHttps && Revision >= 1)
        {
            Flag("security-headers-missing", "/");
        }

        if (IsHttps && Revision == 0)
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
            // The sitemap index lists a file that doesn't exist (until revision 1 drops it) and one that lists nothing, the
            // orphans are only in the extra sitemap, and the thin plant is in two sitemaps, one giving it a lastmod in the
            // year 3000.
            if (Revision == 0)
            {
                Flag("sitemap-error", "/");
            }

            Flag("sitemap-empty", "/");
            Flag("sitemap-lastmod-invalid", "/");
            Flag("sitemap-url-repeated", PLANTS_PATH + "thin");
            if (!_options.SitemapInRobots)
            {
                Flag("robots-txt-no-sitemap", "/");
            }

            for (var orphan = 0; orphan < OrphanCount; orphan++)
            {
                Flag("orphan-page", $"/orphan/{orphan}");
            }

            // Categories are linked from every page but listed in no sitemap, but for the first in revision 1.
            for (var category = 0; category < CATEGORY_COUNT; category++)
            {
                if (!IsCategoryListed(category))
                {
                    Flag("not-in-sitemap", $"/c/{category}");
                }
            }
        }
        else
        {
            Flag("sitemap-missing", "/");
        }

        // A plant's sitemap issues come from its sitemap entry, which a site without sitemaps doesn't have, and without
        // one no page can be missing from them.
        foreach (var plant in _plants)
        {
            foreach (var issue in plant.Issues.Where(issue => _options.Sitemaps || !(issue.StartsWith("sitemap-", StringComparison.Ordinal) || issue is "not-in-sitemap" or "orphan-page")))
            {
                Flag(issue, PlantUrl(plant));
            }
        }

        var products = PageCount - 1;
        var orphans = _options.Sitemaps ? OrphanCount : 0;
        var unblocked = UnblockedPrivate is null ? 0 : 1;
        // The tracked link's URL is the thin page again, at another URL (url-tracking-parameters).
        var htmlPages = 1 + products + CATEGORY_COUNT + STATIC_PAGES.Length + orphans + restored.Count + unblocked + 1 +
                        _plants.Count(plant => plant is { Status: 200, IsPage: true } && !plant.Path.Contains("/cdn-cgi/", StringComparison.Ordinal));
        // Cloudflare's /cdn-cgi/ endpoint is no page of the site's in any count (the engine's CrawlSchema.PAGE), so it's
        // out of both; a page the analysis doesn't audit for another reason would be out of this one alone.
        var analysedPages = htmlPages - _plants.Count(plant => plant is { Status: 200, IsPage: true, IsAnalysed: false } &&
                                                               !plant.Path.Contains("/cdn-cgi/", StringComparison.Ordinal));

        // A site whose home page is noindex, with more than half of its pages, is hidden as a whole, which is reported
        // once, on the home page. A canonicalised page is a variant of another, and no part of the count: no planted
        // canonical names its own page on another version of the site, so every one of them is out. Only a site with
        // HiddenFromSearch gets there: one with NoindexProducts has as many noindex pages less one, and a home page
        // that can be indexed.
        if (IsHomeNoindex && Count(issues, "noindex-page") * 2 > analysedPages - Canonicalised(issues))
        {
            Flag("site-hidden-from-search", "/");
        }

        return new SyntheticSiteExpectations(
            HtmlPages: htmlPages,
            AnalysedPages: analysedPages,
            Products: products,
            Orphans: orphans,
            BrokenUrls: broken.Count + _plants.Count(plant => plant is { Status: 404, OnPartnerSite: false }),
            ServerErrorUrls: errors.Count + _plants.Count(plant => plant is { Status: >= 500, OnPartnerSite: false }),
            // Over HTTPS, revision 1's plain-HTTP home page, which the crawler probes as another version of the site, redirects.
            Redirects: redirects.Count + _plants.Count(plant => plant.Status is >= 300 and < 400) + (IsHttps && Revision >= 1 ? 1 : 0),
            RobotsDisallowed: disallowed.Count + _plants.Count(plant => plant is { Status: Plant.REFUSED, OnPartnerSite: false }),
            // Over HTTPS, a nofollow link names a private address too.
            NofollowOnly: hidden.Count + NOFOLLOW_ONLY_FILES.Length + (IsHttps ? 1 : 0),
            ExternalOk: externalOk.Count,
            ExternalMissing: externalMissing.Count + _plants.Count(plant => plant is { Status: 404, OnPartnerSite: true }),
            Images: images.Count,
            Noindex: Count(issues, "noindex-page"),
            Canonicalised: Canonicalised(issues),
            DuplicateTitlePages: Count(issues, "title-duplicate") - _plants.Count(plant => plant.Issues.Contains("title-duplicate")),
            MissingDescriptions: Count(issues, "description-missing") - _plants.Count(plant => plant.Issues.Contains("description-missing")),
            ImagesMissingAlt: Count(issues, "image-missing-alt") - _plants.Count(plant => plant.Issues.Contains("image-missing-alt")),
            IssueUrls: issues.ToDictionary(pair => pair.Key, IReadOnlySet<string> (pair) => pair.Value, StringComparer.Ordinal))
        {
            NofollowOnlyFiles = NOFOLLOW_ONLY_FILES.Length + (IsHttps ? 1 : 0),
            PartnerRobotsDisallowed = _plants.Count(plant => plant is { Status: Plant.REFUSED, OnPartnerSite: true }),
            Unreachable = _plants.Count(plant => plant is { Status: 0, OnPartnerSite: false }),
            NotChecked = _options is { DeadTwin: true, Sitemaps: true } ? DEAD_TWIN_UNCHECKED : 0,
            SkippedChecks = _options.Sitemaps
                ? new HashSet<string>(StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal) { "orphan-page", "not-in-sitemap" }
        };
    }

    private static int Count(Dictionary<string, HashSet<string>> issues, string issue) =>
        issues.TryGetValue(issue, out var urls) ? urls.Count : 0;

    /// <summary>
    /// The pages canonicalised to another URL: those reported as such, and those whose canonical is reported as a fault of
    /// its own instead, to another site or from a later page of a series to its first.
    /// </summary>
    private static int Canonicalised(Dictionary<string, HashSet<string>> issues) =>
        Count(issues, "canonicalised-page") + Count(issues, "canonical-other-domain") + Count(issues, "canonical-to-first-page");

    /// <summary>The absolute URL a plant is served, linked and listed at.</summary>
    private string PlantUrl(Plant plant) => plant.Url ?? (plant.OnPartnerSite ? _external : _base) + plant.Path;

    /// <summary>Exact number of content-area links on a product page, for link-count assertions.</summary>
    public int ContentLinkCount(int product) => ContentLinks(product).Count();

    private static bool HasDuplicateTitle(int product) => product % 37 == 0;

    private static bool MissesDescription(int product) => product % 50 == 0;

    private bool IsHomeNoindex => _options.HiddenFromSearch;

    private bool IsNoindex(int product) => _options.HiddenFromSearch || _options.NoindexProducts || product % 101 == 0;

    private static bool IsCanonicalised(int product) => product % 73 == 0;

    private bool IsIndexable(int product) => !IsNoindex(product) && !IsCanonicalised(product);

    /// <summary>Every fifth product carries complete Product JSON-LD; a noindex one is structured data on a noindex page.</summary>
    private static bool HasProductMarkup(int product) => product % 5 == 0;

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

        // Revision 1 restores some of these pages, drops the link to others, and moves some links to another URL that
        // doesn't exist either.
        if (product % 29 == 0)
        {
            switch (FateOfBrokenLink(product))
            {
                case BrokenLinkFate.Broken or BrokenLinkFate.Restored:
                    yield return ($"/broken/{product}", false, "Broken link");
                    break;
                case BrokenLinkFate.Relinked:
                    yield return ($"/broken/{product}{RELINKED}", false, "Broken link");
                    break;
            }
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
            Title = TitleFor(product),
            Description = MissesDescription(product) ? null : $"Synthetic product {product}: {Words(product, 99, DESCRIPTION_WORDS)}.",
            H1 = $"Product {product}",
            Content = content.ToString(),
            Canonical = IsCanonicalised(product) ? $"{_base}/p/{product - 1}" : PageSpec.SELF,
            Robots = IsNoindex(product) ? "noindex" : null,
            JsonLd = HasProductMarkup(product)
                ? $$$"""{"@context":"https://schema.org","@type":"Product","name":"Product {{{product}}}","sku":"SKU-{{{product}}}","image":"{{{_base}}}/img/{{{product}}}.jpg","offers":{"@type":"Offer","price":"19.99","priceCurrency":"GBP","availability":"https://schema.org/InStock"}}"""
                : null
        });
    }

    /// <summary>The products the home page lists.</summary>
    private IEnumerable<int> HomeProducts() => Enumerable.Range(1, Math.Min(PRODUCTS_PER_LISTING, PageCount - 1));

    /// <summary>The products a category page lists: every twentieth from its number, up to a listing's worth.</summary>
    private IEnumerable<int> CategoryProducts(int category)
    {
        for (int product = category, listed = 0; product < PageCount && listed < PRODUCTS_PER_LISTING; product += CATEGORY_COUNT)
        {
            if (product < 1)
            {
                continue;
            }

            yield return product;
            listed++;
        }
    }

    /// <summary>
    /// Each product's depth as the crawl records it: the fewest links from the home page, any kind of link counting and a
    /// redirect counting as none, so a product a redirect leads to is as deep as the link to the redirect. The home page
    /// lists the first products, the categories (one click away) a listing's worth each, and each product links others.
    /// </summary>
    private int[] ProductDepths()
    {
        var depths = new int[PageCount];
        Array.Fill(depths, int.MaxValue);
        List<int> level = [.. HomeProducts()];
        level.ForEach(product => depths[product] = 1);
        for (var depth = 1; level.Count > 0; depth++)
        {
            List<int> next = [];
            void Reach(int product)
            {
                if (depths[product] > depth + 1)
                {
                    depths[product] = depth + 1;
                    next.Add(product);
                }
            }

            if (depth == 1)
            {
                Enumerable.Range(0, CATEGORY_COUNT).SelectMany(CategoryProducts).ToList().ForEach(Reach);
            }

            foreach (var product in level)
            {
                foreach (var (href, _, _) in ContentLinks(product))
                {
                    if (TryNumber(href, "/p/", string.Empty, out var target) || TryNumber(href, "/r/", string.Empty, out target) ||
                        TryNumber(href, "/r2/", string.Empty, out target))
                    {
                        Reach(target);
                    }
                }

                if (IsCanonicalised(product) && product > 1)
                {
                    Reach(product - 1);
                }
            }

            level = next;
        }

        return depths;
    }

    /// <summary>
    /// For each product, whether any page links to it with a followed link, and whether an indexable one does, crediting a
    /// link to a redirect to where it ends, as the analysis counts them: the home page and its categories list products, and
    /// products link each other, directly and through /r/ and /r2/.
    /// </summary>
    private (bool Any, bool Indexable)[] ProductInlinks()
    {
        var linked = new (bool Any, bool Indexable)[PageCount];
        void Link(int product, bool fromIndexable) => linked[product] = (true, linked[product].Indexable || fromIndexable);

        foreach (var product in HomeProducts())
        {
            Link(product, !IsHomeNoindex);
        }

        foreach (var product in Enumerable.Range(0, CATEGORY_COUNT).SelectMany(CategoryProducts))
        {
            Link(product, true);
        }

        for (var product = 1; product < PageCount; product++)
        {
            foreach (var (href, noFollow, _) in ContentLinks(product))
            {
                if (!noFollow && (TryNumber(href, "/p/", string.Empty, out var target) || TryNumber(href, "/r/", string.Empty, out target) ||
                                  TryNumber(href, "/r2/", string.Empty, out target)) && target != product)
                {
                    Link(target, IsIndexable(product));
                }
            }
        }

        return linked;
    }

    private string RenderHome()
    {
        var list = new StringBuilder();
        foreach (var product in HomeProducts())
        {
            list.Append("<li><a href=\"/p/").Append(product).Append("\">Product ").Append(product).Append("</a></li>");
        }

        return Render(new PageSpec
        {
            Path = "/",
            Title = "Synthetic Store | Everyday goods for crawler tests",
            Description = "The Synthetic Store home page: a deterministic shop with thousands of products, built to test SEO crawlers.",
            H1 = "Synthetic Store",
            Content = $"<p>{Text("home", INTRO_WORDS)}</p><ul>{list}</ul>",
            Robots = IsHomeNoindex ? "noindex" : null
        });
    }

    private string RenderCategory(int category)
    {
        var list = new StringBuilder();
        foreach (var product in CategoryProducts(category))
        {
            list.Append("<li><a href=\"/p/").Append(product).Append("\">Product ").Append(product).Append("</a></li>");
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
        html.Append("<!DOCTYPE html><html").Append(page.Lang ? $" lang=\"{page.LangValue}\"" : string.Empty).Append("><head>");
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
            html.Append("<meta name=\"viewport\" content=\"").Append(page.ViewportContent).Append("\">");
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

        if (page.TwitterCard)
        {
            html.Append("<meta name=\"twitter:card\" content=\"summary_large_image\">");
        }

        html.Append("<link rel=\"stylesheet\" href=\"/static/site.css\"><script src=\"/static/app.js\" defer></script>");
        html.Append("<style>").Append(FILLER_STYLES).Append("</style>");
        if (page.JsonLd is not null)
        {
            html.Append("<script type=\"application/ld+json\">").Append(page.JsonLd).Append("</script>");
        }

        html.Append(page.ExtraHead);
        html.Append("</head><body>");
        if (page.Chrome)
        {
            html.Append("<header class=\"site-header\"><a href=\"/\" class=\"logo\">Synthetic Store</a></header>");
            html.Append("<nav><ul><li><a href=\"/\">Home</a></li>");
            for (var category = 0; category < CATEGORY_COUNT; category++)
            {
                html.Append("<li><a href=\"/c/").Append(category).Append("\">Category ").Append(category).Append("</a></li>");
            }

            html.Append("</ul></nav>");
        }

        html.Append("<main>");
        if (page.H1 is not null)
        {
            html.Append("<h1>").Append(page.H1).Append("</h1>");
        }

        html.Append(page.Content).Append("</main>");
        if (page.Chrome)
        {
            html.Append("<footer><ul>");
            foreach (var staticPage in STATIC_PAGES)
            {
                html.Append("<li><a href=\"/").Append(staticPage).Append("\">").Append(staticPage).Append("</a></li>");
            }

            html.Append("</ul><p>&copy; Synthetic Store</p></footer>");
        }

        html.Append("</body></html>");
        return html.ToString();
    }

    private string RenderSitemapIndex()
    {
        var xml = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?><sitemapindex xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");
        for (var chunk = 0; chunk < ChunkCount; chunk++)
        {
            xml.Append("<sitemap><loc>").Append(_base).Append("/sitemaps/pages-").Append(chunk).Append(".xml</loc></sitemap>");
        }

        // The last entry doesn't exist: a planted sitemap error, which revision 1 mends. The one before it lists nothing.
        xml.Append("<sitemap><loc>").Append(_base).Append("/sitemaps/extra.xml</loc></sitemap>");
        xml.Append("<sitemap><loc>").Append(_base).Append("/sitemaps/empty.xml</loc></sitemap>");
        if (Revision == 0)
        {
            xml.Append("<sitemap><loc>").Append(_base).Append("/sitemaps/missing.xml</loc></sitemap>");
        }

        return xml.Append("</sitemapindex>").ToString();
    }

    private string RenderPagesSitemap(int chunk)
    {
        var xml = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?><urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");
        var end = Math.Min(PageCount, (chunk + 1) * SITEMAP_CHUNK_SIZE);
        for (var page = chunk * SITEMAP_CHUNK_SIZE; page < end; page++)
        {
            xml.Append("<url><loc>").Append(_base).Append(page == 0 ? "/" : $"/p/{page}").Append("</loc></url>");
        }

        // The thin plant is in the extra sitemap too (sitemap-url-repeated), listed here with a lastmod that hasn't happened
        // yet (sitemap-lastmod-invalid).
        if (chunk == 0)
        {
            xml.Append("<url><loc>").Append(_base).Append(PLANTS_PATH).Append("thin</loc><lastmod>3000-01-01</lastmod></url>");
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

        // Revision 1 lists what it adds and mends: the pages it restored, the private page robots.txt now allows, and the
        // first category.
        foreach (var path in RevisionListedPaths())
        {
            xml.Append("<url><loc>").Append(_base).Append(path).Append("</loc></url>");
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

    /// <summary>
    /// A JPEG of <paramref name="size"/> bytes whose frame header says it is <paramref name="width"/> by
    /// <paramref name="height"/> pixels: its start, a JFIF segment, a baseline frame header, then nothing up to its end.
    /// </summary>
    /// <summary>A PNG of the given size whose header gives its width and height, as the crawler reads image sizes.</summary>
    private static byte[] BuildPng(int size, int width, int height)
    {
        var bytes = new byte[size];
        byte[] header =
        [
            0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, (byte)'I', (byte)'H', (byte)'D', (byte)'R',
            (byte)(width >> 24), (byte)(width >> 16), (byte)(width >> 8), (byte)width, (byte)(height >> 24), (byte)(height >> 16), (byte)(height >> 8), (byte)height,
            0x08, 0x02, 0x00, 0x00, 0x00
        ];
        header.CopyTo(bytes, 0);
        return bytes;
    }

    private static byte[] BuildImage(int size, int width, int height)
    {
        var bytes = new byte[size];
        byte[] header =
        [
            0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00,
            0xFF, 0xC0, 0x00, 0x11, 0x08, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, 0x03
        ];
        header.CopyTo(bytes, 0);
        bytes[^2] = 0xFF;
        bytes[^1] = 0xD9;
        return bytes;
    }
}
