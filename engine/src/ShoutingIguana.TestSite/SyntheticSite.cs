using System.Globalization;
using System.Text;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.TestSite;

/// <summary>
/// A deterministic synthetic shop for tests and benchmarks. Every response is generated from its URL, so
/// any size can be served without storage. Known SEO issues are planted at fixed intervals, and
/// <see cref="GetExpectations"/> reports exactly what a correct crawl must find.
/// </summary>
public sealed class SyntheticSite
{
    public const int CATEGORY_COUNT = 20;
    public const int IMAGE_COUNT = 100;
    public const int SITEMAP_CHUNK_SIZE = 10_000;

    private const int PRODUCTS_PER_LISTING = 50;
    private const int PARAGRAPHS = 8;
    private const int WORDS_PER_PARAGRAPH = 60;
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

    private static readonly string FILLER_STYLES = BuildFillerStyles();
    private static readonly byte[] IMAGE_BYTES = BuildImage();

    private readonly SyntheticSiteOptions _options;
    private readonly string _base;
    private readonly string _external;

    public SyntheticSite(SyntheticSiteOptions options, Uri baseUri, Uri externalBaseUri)
    {
        if (options.PageCount < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "A synthetic site needs at least two pages.");
        }

        _options = options;
        _base = baseUri.GetLeftPart(UriPartial.Authority);
        _external = externalBaseUri.GetLeftPart(UriPartial.Authority);
    }

    public int PageCount => _options.PageCount;

    public int OrphanCount => Math.Max(1, _options.PageCount / 100);

    private int ChunkCount => (PageCount + SITEMAP_CHUNK_SIZE - 1) / SITEMAP_CHUNK_SIZE;

    public SyntheticResponse Handle(string path, bool external)
    {
        if (external)
        {
            return path.StartsWith("/ext/", StringComparison.Ordinal)
                ? Html(RenderSimple("Partner site", "An external site linked from the shop."))
                : NotFound();
        }

        if (path == "/")
        {
            return Html(RenderHome());
        }

        if (path == "/robots.txt")
        {
            return new SyntheticResponse(200, "text/plain; charset=utf-8", $"User-agent: *\nDisallow: /private/\n\nSitemap: {_base}/sitemap_index.xml\n", null, null);
        }

        if (path == "/sitemap_index.xml")
        {
            return Xml(RenderSitemapIndex());
        }

        if (path == "/sitemaps/extra.xml")
        {
            return Xml(RenderExtraSitemap());
        }

        if (TryNumber(path, "/sitemaps/pages-", ".xml", out var chunk) && chunk < ChunkCount)
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
            return Html(RenderSimple($"Orphan {orphan}", "Only the sitemap lists this page."));
        }

        if (TryNumber(path, "/hidden/", string.Empty, out var hidden))
        {
            return Html(RenderSimple($"Hidden {hidden}", "Only nofollow links point here."));
        }

        if (TryNumber(path, "/private/", string.Empty, out var secret))
        {
            return Html(RenderSimple($"Private {secret}", "robots.txt disallows this area."));
        }

        if (TryNumber(path, "/r/", string.Empty, out var moved) && moved >= 1 && moved < PageCount)
        {
            return new SyntheticResponse(301, HTML, null, null, $"/p/{moved}");
        }

        if (TryNumber(path, "/r2/", string.Empty, out var movedTwice) && movedTwice >= 1 && movedTwice < PageCount)
        {
            return new SyntheticResponse(302, HTML, null, null, $"/r/{movedTwice}");
        }

        if (path.StartsWith("/err/", StringComparison.Ordinal))
        {
            return new SyntheticResponse(500, HTML, RenderSimple("Server error", "Something went wrong."), null, null);
        }

        if (TryNumber(path, "/img/", ".jpg", out _))
        {
            return new SyntheticResponse(200, "image/jpeg", null, IMAGE_BYTES, null);
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
            return Html(RenderSimple(CultureInfo.InvariantCulture.TextInfo.ToTitleCase(path[1..]), "A static information page."));
        }

        return NotFound();
    }

    public SyntheticSiteExpectations GetExpectations()
    {
        HashSet<string> broken = [], errors = [], redirects = [], disallowed = [], hidden = [], externalOk = [], externalMissing = [], images = [];
        int noindex = 0, canonicalised = 0, duplicateTitles = 0, missingDescriptions = 0, missingAlt = 0;

        for (var product = 1; product < PageCount; product++)
        {
            foreach (var (href, _, _) in ContentLinks(product))
            {
                if (href.StartsWith("/broken/", StringComparison.Ordinal))
                {
                    broken.Add(href);
                }
                else if (href.StartsWith("/err/", StringComparison.Ordinal))
                {
                    errors.Add(href);
                }
                else if (href.StartsWith("/r/", StringComparison.Ordinal))
                {
                    redirects.Add(href);
                }
                else if (href.StartsWith("/r2/", StringComparison.Ordinal))
                {
                    redirects.Add(href);
                    redirects.Add("/r/" + href[4..]);
                }
                else if (href.StartsWith("/private/", StringComparison.Ordinal))
                {
                    disallowed.Add(href);
                }
                else if (href.StartsWith("/hidden/", StringComparison.Ordinal))
                {
                    hidden.Add(href);
                }
                else if (href.Contains("/ext-missing/", StringComparison.Ordinal))
                {
                    externalMissing.Add(href);
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
                noindex++;
            }
            else if (IsCanonicalised(product))
            {
                canonicalised++;
            }

            if (HasDuplicateTitle(product))
            {
                duplicateTitles += 2;
            }

            if (MissesDescription(product))
            {
                missingDescriptions++;
            }

            if (MissesAlt(product))
            {
                missingAlt++;
            }
        }

        var products = PageCount - 1;
        return new SyntheticSiteExpectations(
            HtmlPages: 1 + products + CATEGORY_COUNT + STATIC_PAGES.Length + OrphanCount,
            Products: products,
            Orphans: OrphanCount,
            BrokenUrls: broken.Count,
            ServerErrorUrls: errors.Count,
            Redirects: redirects.Count,
            RobotsDisallowed: disallowed.Count,
            NofollowOnly: hidden.Count,
            ExternalOk: externalOk.Count,
            ExternalMissing: externalMissing.Count,
            Images: images.Count,
            Noindex: noindex,
            Canonicalised: canonicalised,
            DuplicateTitlePages: duplicateTitles,
            MissingDescriptions: missingDescriptions,
            ImagesMissingAlt: missingAlt);
    }

    /// <summary>Exact number of content-area links on a product page, for link-count assertions.</summary>
    public int ContentLinkCount(int product) => ContentLinks(product).Count();

    private static bool HasDuplicateTitle(int product) => product % 37 == 0;

    private static bool MissesDescription(int product) => product % 50 == 0;

    private static bool IsNoindex(int product) => product % 101 == 0;

    private static bool IsCanonicalised(int product) => product % 73 == 0;

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
        var title = TitleOf(HasDuplicateTitle(product) ? product - 1 : product);
        var description = MissesDescription(product) ? null : $"Synthetic product {product}: {Words(product, 99, 12)}.";
        var canonical = IsCanonicalised(product) ? $"/p/{product - 1}" : $"/p/{product}";
        var jsonLd = product % 5 == 0
            ? $$"""{"@context":"https://schema.org","@type":"Product","name":"Product {{product}}","sku":"SKU-{{product}}"}"""
            : null;

        var html = StartPage(title, description, canonical, IsNoindex(product), jsonLd);
        html.Append("<main><h1>Product ").Append(product).Append("</h1>");
        for (var paragraph = 0; paragraph < PARAGRAPHS; paragraph++)
        {
            html.Append("<p>").Append(Words(product, paragraph, WORDS_PER_PARAGRAPH)).Append("</p>");
        }

        html.Append("<h2>Related products</h2><ul class=\"related\">");
        foreach (var (href, noFollow, label) in ContentLinks(product))
        {
            html.Append("<li><a href=\"").Append(href).Append(noFollow ? "\" rel=\"nofollow\">" : "\">").Append(label).Append("</a></li>");
        }

        html.Append("</ul><div class=\"gallery\">");
        for (var image = 0; image < 3; image++)
        {
            var alt = MissesAlt(product) && image == 0 ? string.Empty : $" alt=\"Product {product} photo {image + 1}\"";
            html.Append("<img src=\"").Append(ImagePath(product, image)).Append('"').Append(alt).Append(" width=\"400\" height=\"300\">");
        }

        html.Append("</div></main>");
        return EndPage(html);
    }

    private string RenderHome()
    {
        var html = StartPage("Synthetic Store", "A synthetic shop for crawler tests.", "/", false, null);
        html.Append("<main><h1>Synthetic Store</h1><ul>");
        for (var product = 1; product <= Math.Min(PRODUCTS_PER_LISTING, PageCount - 1); product++)
        {
            html.Append("<li><a href=\"/p/").Append(product).Append("\">Product ").Append(product).Append("</a></li>");
        }

        html.Append("</ul></main>");
        return EndPage(html);
    }

    private string RenderCategory(int category)
    {
        var html = StartPage($"Category {category} | Synthetic Store", $"Products in category {category}.", $"/c/{category}", false, null);
        html.Append("<main><h1>Category ").Append(category).Append("</h1><ul>");
        for (int product = category, listed = 0; product < PageCount && listed < PRODUCTS_PER_LISTING; product += CATEGORY_COUNT)
        {
            if (product < 1)
            {
                continue;
            }

            html.Append("<li><a href=\"/p/").Append(product).Append("\">Product ").Append(product).Append("</a></li>");
            listed++;
        }

        html.Append("</ul></main>");
        return EndPage(html);
    }

    private string RenderSimple(string heading, string text)
    {
        var html = StartPage($"{heading} | Synthetic Store", text, null, false, null);
        html.Append("<main><h1>").Append(heading).Append("</h1><p>").Append(text).Append("</p></main>");
        return EndPage(html);
    }

    private StringBuilder StartPage(string title, string? description, string? canonicalPath, bool noindex, string? jsonLd)
    {
        var html = new StringBuilder(32 * 1024);
        html.Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>").Append(title).Append("</title>");
        if (description is not null)
        {
            html.Append("<meta name=\"description\" content=\"").Append(description).Append("\">");
        }

        html.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        if (canonicalPath is not null)
        {
            html.Append("<link rel=\"canonical\" href=\"").Append(_base).Append(canonicalPath).Append("\">");
        }

        if (noindex)
        {
            html.Append("<meta name=\"robots\" content=\"noindex\">");
        }

        html.Append("<link rel=\"stylesheet\" href=\"/static/site.css\"><script src=\"/static/app.js\" defer></script>");
        html.Append("<style>").Append(FILLER_STYLES).Append("</style>");
        if (jsonLd is not null)
        {
            html.Append("<script type=\"application/ld+json\">").Append(jsonLd).Append("</script>");
        }

        html.Append("</head><body><header class=\"site-header\"><a href=\"/\" class=\"logo\">Synthetic Store</a></header>");
        html.Append("<nav><ul><li><a href=\"/\">Home</a></li>");
        for (var category = 0; category < CATEGORY_COUNT; category++)
        {
            html.Append("<li><a href=\"/c/").Append(category).Append("\">Category ").Append(category).Append("</a></li>");
        }

        html.Append("</ul></nav>");
        return html;
    }

    private static string EndPage(StringBuilder html)
    {
        html.Append("<footer><ul>");
        foreach (var page in STATIC_PAGES)
        {
            html.Append("<li><a href=\"/").Append(page).Append("\">").Append(page).Append("</a></li>");
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

        xml.Append("<sitemap><loc>").Append(_base).Append("/sitemaps/extra.xml</loc></sitemap></sitemapindex>");
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

        return xml.Append("</urlset>").ToString();
    }

    private static string TitleOf(int product) => $"Product {product} | Synthetic Store";

    private static string ImagePath(int product, int image) => $"/img/{(product * 3 + image) % IMAGE_COUNT}.jpg";

    private int RandomProduct(int product, int slot) => 1 + (int)(Mix(_options.Seed, product, slot) % (uint)(PageCount - 1));

    private string Words(int product, int paragraph, int count)
    {
        var text = new StringBuilder(count * 8);
        for (var word = 0; word < count; word++)
        {
            if (word > 0)
            {
                text.Append(' ');
            }

            text.Append(WORDS[Mix(_options.Seed, product, paragraph * 1_000 + word) % (uint)WORDS.Length]);
        }

        return text.ToString();
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

    private static SyntheticResponse Html(string text) => new(200, HTML, text, null, null);

    private static SyntheticResponse Xml(string text) => new(200, XML, text, null, null);

    private SyntheticResponse NotFound() => new(404, HTML, RenderSimple("Not found", "This page does not exist."), null, null);

    private static string BuildFillerStyles()
    {
        var css = new StringBuilder();
        for (var rule = 0; rule < 120; rule++)
        {
            css.Append(CultureInfo.InvariantCulture, $".c{rule}{{margin:{rule % 7}px;padding:{rule % 5}px;color:#{rule * 2_113 % 0xFFFFFF:x6}}}\n");
        }

        return css.ToString();
    }

    private static byte[] BuildImage()
    {
        var bytes = new byte[2_048];
        byte[] header = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0x00];
        header.CopyTo(bytes, 0);
        bytes[^2] = 0xFF;
        bytes[^1] = 0xD9;
        return bytes;
    }
}
