using System.Text;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.TestSite;

/// <summary>
/// The planted test pages under /plants/: a clean page per check with one deliberate defect, plus whatever that
/// defect implies (a canonical to a 404 also makes the page canonicalised). The hub at /plants/ links them.
/// </summary>
public sealed partial class SyntheticSite
{
    private const int PLANT_WORDS = 250;
    private const int SLOW_PAGE_DELAY_MS = 1_200;
    private const int LONG_CHAIN_HOPS = 22;

    private const string SHARED_DESCRIPTION =
        "Two planted pages share this exact meta description, so the duplicate description check has to flag both of them.";

    private const string LONG_SLUG =
        "a-deliberately-long-address-that-keeps-on-going-so-the-full-url-ends-up-past-the-recommended-length";

    private static readonly string[] HINDI_WORDS = ["नमस्ते", "दुनिया", "किताब", "पानी", "घर", "बच्चे"];

    /// <summary>A page on the partner site its robots.txt disallows: the crawler never asks for it.</summary>
    private const string PARTNER_DISALLOWED = "/ext-private/alternate";

    /// <summary>Files only nofollow links point to, so never fetched: the full-size photo behind a gallery lightbox.</summary>
    private static readonly string[] NOFOLLOW_ONLY_FILES = ["/img/5.jpg?width=1400"];

    private static readonly byte[] LARGE_IMAGE_BYTES = BuildImage(300 * 1024, LARGE_IMAGE_WIDTH, LARGE_IMAGE_HEIGHT);

    /// <summary>The size in pixels the planted large image says it is.</summary>
    public const int LARGE_IMAGE_WIDTH = 4_000;

    /// <inheritdoc cref="LARGE_IMAGE_WIDTH"/>
    public const int LARGE_IMAGE_HEIGHT = 3_000;
    private static readonly string LONG_ALT = string.Join(' ', Enumerable.Repeat("a very long alt text", 8));

    private List<Plant> CreatePlants()
    {
        List<Plant> plants = [];

        // With an encoding, the page is sent as bytes in it, after its byte-order mark (the encoding's preamble).
        void Page(string slug, string name, string[] issues, Func<PageSpec, PageSpec>? change = null, bool inSitemap = true,
            string contentType = HTML, bool linked = true, Encoding? markedAs = null)
        {
            var page = PlantPage(slug, name);
            page = change?.Invoke(page) ?? page;
            plants.Add(new Plant(page.Path, 200, linked, inSitemap, issues, () => markedAs is null
                ? new SyntheticResponse(200, contentType, Render(page), null, null, SECURITY_HEADERS)
                : new SyntheticResponse(200, contentType, null, [.. markedAs.GetPreamble(), .. markedAs.GetBytes(Render(page))], null, SECURITY_HEADERS)));
        }

        void Other(string slug, int status, bool linked, string[] issues, Func<SyntheticResponse> respond) =>
            plants.Add(new Plant(PLANTS_PATH + slug, status, linked, InSitemap: false, issues, respond));

        var revised = Revision >= 1;
        Page("title-missing", "Missing title", ["title-missing"], page => page with { Title = null });
        Page("title-multiple", "Two titles", ["title-multiple"], page => page with { ExtraHead = "<title>A second title for the same planted page</title>" });

        // Revision 1 shortens the long title, removes the short-title page and the hub's link to it, and adds a page with
        // a long title.
        if (revised)
        {
            Page("title-long", "Long title", []);
            Page("title-long-new", "New long title", ["title-too-long"], page => page with
            {
                Title = "A page added since the last crawl, with a title that runs on past what search results show | Synthetic Store"
            });
        }
        else
        {
            Page("title-long", "Long title", ["title-too-long"], page => page with
            {
                Title = "This planted page has a title that runs on far past what search results can show | Synthetic Store"
            });
            Page("title-short", "Short title", ["title-too-short"], page => page with { Title = "Short page" });
        }

        // Full-width characters are one em wide in results: 21 of them make a normal title (420 px), 48 a long one (960 px).
        Page("title-wide", "Full-width title", [], page => page with { Title = "合成ストアの全角タイトル付き試験ページです" });
        Page("title-wide-long", "Long full-width title", ["title-too-long"], page => page with { Title = string.Concat(Enumerable.Repeat("日本語の題名", 8)) });

        // Titles are judged by their width in pixels, not their characters: capitals are wide and il1t narrow.
        // 59 characters, 736 px: too long. In revision 1 the page answers 500, so its title can't be judged, and the hub's
        // link to it and its sitemap entry are to an error.
        if (revised)
        {
            plants.Add(new Plant(PLANTS_PATH + "title-caps", 500, Linked: true, InSitemap: true, ["internal-server-error", "sitemap-non-indexable"],
                () => new SyntheticResponse(500, HTML, RenderSimple("Server error", "Something went wrong.", PLANTS_PATH + "title-caps"), null, null, SECURITY_HEADERS),
                IsPage: false));
        }
        else
        {
            Page("title-caps", "Title in capitals", ["title-too-long"], page => page with { Title = "WHOLESALE PATIO HEATERS: MODERN LANTERNS AND OUTDOOR LIGHTS" });
        }

        // 80 characters, 561 px: fits. Revision 1 gives it a second H1, as it takes the one the two-H1 page had.
        Page("title-narrow", "Title of narrow letters", revised ? ["h1-multiple"] : [], page => page with
        {
            Title = "little lilies, fritillaries and tulips: a field list of all the lilies in flower",
            Content = revised ? "<h1>Another heading at the top</h1>" + page.Content : page.Content
        });

        // 33 characters, 184 px: too short.
        Page("title-narrow-short", "Short title of narrow letters", ["title-too-short"], page => page with { Title = "lilies tilt; little jill fills it" });

        // 19 characters, 258 px: long enough.
        Page("title-caps-short", "Short title in capitals", [], page => page with { Title = "WOODEN GARDEN SHEDS" });

        Page("description-multiple", "Two descriptions", ["description-multiple"], page => page with
        {
            ExtraHead = "<meta name=\"description\" content=\"A second meta description on the same planted page, which search engines may ignore.\">"
        });
        Page("description-long", "Long description", ["description-too-long"], page => page with
        {
            Description = "This planted meta description keeps going well past the point where search results cut it off, " +
                          "adding clause after clause about nothing in particular until it is far too long to show."
        });
        Page("description-short", "Short description", ["description-too-short"], page => page with { Description = "Too short." });

        // Descriptions too: 132 characters in capitals are 1,054 px, too long; 172 of narrow letters are 826 px, and fit.
        Page("description-caps", "Description in capitals", ["description-too-long"], page => page with
        {
            Description = "PLANTED PAGE WITH A DESCRIPTION IN CAPITALS: EVERY LETTER IS WIDE, SO IT WILL BE CUT OFF IN SEARCH RESULTS LONG BEFORE ITS LAST WORD"
        });
        Page("description-narrow", "Description of narrow letters", [], page => page with
        {
            Description = "this little list of tiny lilies, frilly tulips, fritillaries and lilacs is still just right: " +
                          "it fills a full line, but its thin letters fit in the little space left for it."
        });
        Page("description-copy-a", "Shared description A", ["description-duplicate"], page => page with { Description = SHARED_DESCRIPTION });
        Page("description-copy-b", "Shared description B", ["description-duplicate"], page => page with { Description = SHARED_DESCRIPTION });

        Page("h1-missing", "Missing H1", ["h1-missing"], page => page with { H1 = null, Content = "<h2>Missing H1</h2>" + page.Content });
        if (revised)
        {
            Page("h1-multiple", "Two H1s", []);
        }
        else
        {
            Page("h1-multiple", "Two H1s", ["h1-multiple"], page => page with { Content = "<h1>A second H1 on the page</h1>" + page.Content });
        }

        Page("h1-copy-a", "Shared heading A", ["h1-duplicate"], page => page with { H1 = "Shared heading" });
        Page("h1-copy-b", "Shared heading B", ["h1-duplicate"], page => page with { H1 = "Shared heading" });

        // A line break separates words, so these two headings read the same.
        // The pages of a paginated series share their heading by design; their titles differ, so neither is flagged.
        Page("series", "Paginated series, first page", [], page => page with
        {
            H1 = "Paginated series", ExtraHead = $"<link rel=\"next\" href=\"{PLANTS_PATH}series-2\">"
        });
        Page("series-2", "Paginated series, second page", [], page => page with
        {
            H1 = "Paginated series", ExtraHead = $"<link rel=\"prev\" href=\"{PLANTS_PATH}series\">"
        });
        Page("h1-break-a", "Heading with a line break", ["h1-duplicate"], page => page with { H1 = "Welcome to<br>our planted store" });
        Page("h1-break-b", "Heading without a line break", ["h1-duplicate"], page => page with { H1 = "Welcome to our planted store" });

        // Identical main content means an identical H1 too, so the copies are also duplicate H1s.
        var copied = $"<p>{Text("plant-content-copy", 300)}</p>";
        Page("content-copy-a", "Copied content A", ["duplicate-content", "h1-duplicate"], page => page with { H1 = "Copied content", Content = copied });
        Page("content-copy-b", "Copied content B", ["duplicate-content", "h1-duplicate"], page => page with { H1 = "Copied content", Content = copied });
        var near = WordArray(KeySeed("plant-content-near"), 0, 600);
        var nearCopy = (string[])near.Clone();
        nearCopy[300] = "ocean";
        Page("content-near-a", "Near copy A", ["near-duplicate-content", "h1-duplicate"], page => page with
        {
            H1 = "Nearly copied content", Content = $"<p>{string.Join(' ', near)}</p>"
        });
        Page("content-near-b", "Near copy B", ["near-duplicate-content", "h1-duplicate"], page => page with
        {
            H1 = "Nearly copied content", Content = $"<p>{string.Join(' ', nearCopy)}</p>"
        });

        // Not near copies, though the first 64 bits of their fingerprints say so: text keys searched for (as about one
        // unrelated pair in 2×10^11 collides), then a pair sharing half its text (one in 10^5). Every size of the site
        // carries both, and the rest of the 256 bits must tell them apart.
        Page("collision-a", "Fingerprint collision A", [], page => page with { Content = $"<p>{Text("plant-collision-a-633038", PLANT_WORDS)}</p>" });
        Page("collision-b", "Fingerprint collision B", [], page => page with { Content = $"<p>{Text("plant-collision-b-1005823", PLANT_WORDS)}</p>" });
        var template = Text("plant-template", PLANT_WORDS / 2);
        Page("template-a", "Shared template A", [], page => page with { Content = $"<p>{template} {Text("plant-template-a-1772", PLANT_WORDS / 2)}</p>" });
        Page("template-b", "Shared template B", [], page => page with { Content = $"<p>{template} {Text("plant-template-b-3795", PLANT_WORDS / 2)}</p>" });

        Page("thin", "Thin page", ["low-word-count"], page => page with { Content = $"<p>{Text("plant-thin", 60)}</p>" });

        // 150 Hindi words: vowel signs and the virama are part of each word, so it's thin, not 300-odd words.
        Page("thin-hindi", "Thin Hindi page", ["low-word-count"], page => page with
        {
            Content = $"<p lang=\"hi\">{string.Join(' ', Enumerable.Range(0, 150).Select(word => HINDI_WORDS[word % HINDI_WORDS.Length]))}</p>"
        });
        Page("soft-404", "Soft 404", ["soft-404"], page => page with { Title = "Sorry, this page was not found | Synthetic Store", H1 = "Page not found" });

        Page("image-alt-long", "Long alt text", ["image-alt-too-long"], page => page with
        {
            Content = page.Content + $"<img src=\"/img/1.jpg\" alt=\"{LONG_ALT}\" width=\"400\" height=\"300\">"
        });
        Page("image-no-size", "Image without size", ["image-missing-dimensions"], page => page with
        {
            Content = page.Content + "<img src=\"/img/2.jpg\" alt=\"A product photo without a size\">"
        });
        Page("image-broken", "Broken image", ["image-broken"], page => page with
        {
            Content = page.Content + "<img src=\"/img/missing.jpg\" alt=\"A photo that is missing\" width=\"400\" height=\"300\">"
        });
        // Revision 1 serves a photo of a normal size at the large one's address.
        Page("image-large", "Large image", revised ? [] : ["image-too-large"], page => page with
        {
            Content = page.Content + "<img src=\"/img/large.jpg\" alt=\"A very large photo\" width=\"400\" height=\"300\">"
        });

        // Images without a src are still images: one with only srcset lacks alt text, a lazy one lacks a size.
        Page("image-srcset", "Images without src", ["image-missing-alt", "image-missing-dimensions"], page => page with
        {
            Content = page.Content + "<img srcset=\"/img/9.jpg 1x, /img/10.jpg 2x\" width=\"400\" height=\"300\">" +
                      "<img src=\"data:image/gif;base64,R0lGODlhAQABAAAAACw=\" data-original=\"/img/11.jpg\" alt=\"A lazily loaded photo\">"
        });

        Page("canonical-missing", "Missing canonical", ["canonical-missing"], page => page with { Canonical = null });
        Page("canonical-multiple", "Two canonicals", ["canonical-multiple"], page => page with
        {
            ExtraHead = $"<link rel=\"canonical\" href=\"{_base}{PLANTS_PATH}thin\">"
        });
        Page("canonical-relative", "Relative canonical", ["canonical-relative"], page => page with { Canonical = page.Path });
        Page("canonical-body", "Canonical in the body", ["canonical-outside-head"], page => page with
        {
            Canonical = null, Content = page.Content + $"<link rel=\"canonical\" href=\"{_base}{page.Path}\">"
        });
        Page("canonical-invalid", "Invalid canonical", ["canonical-invalid"], page => page with { Canonical = "http://[not a host]/" });
        // A canonical that leads nowhere is ignored, so the page stays indexable (and belongs in the sitemap).
        Page("canonical-broken", "Canonical to a 404", ["canonical-broken"], page => page with { Canonical = $"{_base}{PLANTS_PATH}gone" });
        Page("canonical-redirect", "Canonical to a redirect", ["canonicalised-page", "canonical-to-redirect"],
            page => page with { Canonical = $"{_base}{PLANTS_PATH}moved" }, inSitemap: false);

        // A canonical to a URL two redirects from where it ends: the canonical is a way into that chain, so the chain is
        // reported at the URL it names.
        Page("canonical-chain", "Canonical to a redirect chain", ["canonicalised-page", "canonical-to-redirect"],
            page => page with { Canonical = $"{_base}{PLANTS_PATH}moved-twice" }, inSitemap: false);
        Other("moved-twice", 301, linked: false, ["redirect-chain"], () => Redirect(301, PLANTS_PATH + "moved"));

        // /self-canonical/ names /self-canonical, which redirects back to it. The redirect is the stronger signal, so the
        // page is indexed and audited: its short title is reported, and it isn't canonicalised or out of place in the sitemap.
        Other("self-canonical", 301, linked: false, [], () => Redirect(301, PLANTS_PATH + "self-canonical/"));
        Page("self-canonical/", "Canonical back through a redirect", ["canonical-to-redirect", "title-too-short"],
            page => page with { Title = "Self canonical", Canonical = $"{_base}{PLANTS_PATH}self-canonical" });
        Page("canonical-noindex", "Canonical to a noindex page", ["canonicalised-page", "canonical-to-non-indexable"],
            page => page with { Canonical = $"{_base}{PLANTS_PATH}noindex" }, inSitemap: false);

        // A canonical to a URL that refused the crawler (a bot challenge here, a rate limit on a real shop) is unverified,
        // not non-indexable: the refusal says nothing about search engines, and the blocked URL is reported on its own.
        Page("canonical-blocked", "Canonical to a blocked URL", ["canonicalised-page"],
            page => page with { Canonical = $"{_base}{PLANTS_PATH}blocked" }, inSitemap: false);

        Page("noindex", "Noindex page", ["noindex-page"], page => page with { Robots = "noindex" }, inSitemap: false);
        Page("noindex-canonical", "Noindex with a canonical elsewhere", ["noindex-page", "noindex-with-canonical"], page => page with
        {
            Robots = "noindex", Canonical = $"{_base}{PLANTS_PATH}hreflang-lonely"
        }, inSitemap: false);
        Page("nofollow", "Nofollow page", ["nofollow-page"], page => page with { Robots = "nofollow" });
        // Revision 1 drops its noindex, and lists it in the sitemap, as a page that can be indexed belongs there.
        if (revised)
        {
            Page("robots-spaces", "Robots directives without commas", ["nofollow-page"], page => page with { Robots = "nofollow" });
        }
        else
        {
            Page("robots-spaces", "Robots directives without commas", ["noindex-page", "nofollow-page"], page => page with { Robots = "noindex nofollow" },
                inSitemap: false);
        }


        // Gallery lightboxes: nofollow on links to image files is harmless, both for a file that was fetched (it's also
        // shown) and for one that wasn't (the full-size version), unlike the nofollow links to /hidden/ pages on products.
        Page("nofollow-image", "Nofollow image links", [], page => page with
        {
            Content = page.Content +
                      $"<p><a href=\"{NOFOLLOW_ONLY_FILES[0]}\" rel=\"nofollow\" class=\"lightbox\"><img src=\"/img/5.jpg\" alt=\"A gallery photo\" width=\"400\" height=\"300\"></a> " +
                      "<a href=\"/img/6.jpg\" rel=\"nofollow\" class=\"lightbox\"><img src=\"/img/6.jpg\" alt=\"Another gallery photo\" width=\"400\" height=\"300\"></a></p>"
        });
        Page("nosnippet", "No snippet", ["snippet-blocked"], page => page with { Robots = "nosnippet" });
        Page("meta-refresh", "Meta refresh", ["meta-refresh"], page => page with
        {
            ExtraHead = $"<meta http-equiv=\"refresh\" content=\"30; url={PLANTS_PATH}thin\">"
        });

        // Google treats an instant meta refresh as a permanent redirect, as static hosts' redirect stubs rely on. The
        // stub isn't audited as a page (no title, H1 or text to speak of), the hub's link to it is a link to a redirect,
        // and the page it moves to counts as linked, so it isn't an orphan.
        Page("refresh-instant", "Instant meta refresh", ["meta-refresh"], page => page with
        {
            Title = null, Description = null, H1 = null, Content = "<p>Moved.</p>",
            ExtraHead = $"<meta http-equiv=\"refresh\" content=\"0; url={PLANTS_PATH}refresh-target\">"
        }, inSitemap: false);
        Page("refresh-target", "Meta refresh target", [], linked: false);

        // A link, a partner-site link and an image that only fail once their redirects are followed are broken, not just
        // redirects. The redirecting image is an internal redirect to a 404 itself.
        Page("redirects-to-errors", "Redirects to errors", ["broken-internal-links", "broken-external-links", "image-broken"], page => page with
        {
            Content = page.Content +
                      $"<p><a href=\"{PLANTS_PATH}redirect-broken\">Moved, then gone</a> <a href=\"{_external}/ext-moved\">Partner page, moved, then gone</a></p>" +
                      $"<img src=\"{PLANTS_PATH}image-moved.jpg\" alt=\"A photo that moved, then went\" width=\"400\" height=\"300\">"
        });
        Other("image-moved.jpg", 301, linked: false, ["redirect-broken"], () => Redirect(301, "/img/missing.jpg"));

        // The sitemap also lists both partner URLs, as one left behind after a move to another host would.
        plants.Add(new Plant("/ext-moved", 301, Linked: false, InSitemap: true, ["sitemap-url-other-host"], () => Redirect(301, "/ext-gone"), OnPartnerSite: true));
        plants.Add(new Plant("/ext-gone", 404, Linked: false, InSitemap: true, ["sitemap-url-other-host"], () => NotFound("/ext-gone"), OnPartnerSite: true));

        // And a URL robots.txt disallows, which only the sitemap leads to: listed, and never asked for. A crawl told to
        // ignore robots.txt finds a page there, as it does at every URL of the private area.
        if (_options.Sitemaps)
        {
            plants.Add(new Plant("/private/listed", Plant.REFUSED, Linked: false, InSitemap: true, ["blocked-by-robots", "sitemap-non-indexable"],
                () => Html(RenderSimple("Private listed", "robots.txt disallows this area.", "/private/listed")), IsPage: false));
        }

        Page("empty-anchor", "Link without text", ["empty-anchor-text"], page => page with
        {
            Content = page.Content + $"<p><a href=\"{PLANTS_PATH}thin\"></a></p>"
        });

        // A logo linking its own page has nothing to read all the same, as on a home page. Links written as a fragment
        // (a script's "#", a "back to top" arrow) are jumps within the page, not links to read. They come first, as a menu
        // toggle often does, so what reads the page's link to itself must pass them for the logo.
        Page("empty-self-link", "Image link to its own page", ["empty-anchor-text"], page => page with
        {
            Content = page.Content + "<p><a href=\"#\" class=\"menu-toggle\"><svg><path d=\"M0 0\"/></svg></a> <a href=\"#top\"><svg><path d=\"M0 0\"/></svg></a> " +
                      $"<a href=\"{PLANTS_PATH}empty-self-link\"><img src=\"/img/3.jpg\" alt=\"\" width=\"400\" height=\"300\"></a></p>"
        });

        // Icon links named the ways screen readers name them (a labelled SVG, a title behind an empty aria-label, a label
        // elsewhere on the page) have text, so none is flagged.
        Page("icon-links", "Icon links with names", [], page => page with
        {
            Content = page.Content +
                      $"<p><a href=\"{PLANTS_PATH}thin\"><svg aria-label=\"Thin page\"><path d=\"M0 0\"/></svg></a>" +
                      $"<a href=\"{PLANTS_PATH}thin\" aria-label=\"\" title=\"Thin page\"><svg><path d=\"M0 0\"/></svg></a>" +
                      $"<a href=\"{PLANTS_PATH}thin\" aria-labelledby=\"thin-label\"><svg><path d=\"M0 0\"/></svg></a>" +
                      "<span id=\"thin-label\">Thin page</span></p>"
        });
        Page("fragments", "Missing fragments", ["broken-fragment-links"], page => page with
        {
            Content = page.Content +
                      $"<p><a href=\"{PLANTS_PATH}fragment-target#present\">Present section</a> " +
                      $"<a href=\"{PLANTS_PATH}fragment-target#missing\">Missing section</a> " +
                      "<a href=\"#nowhere\">Nowhere on this page</a> <a href=\"#top\">Back to top</a></p>"
        });
        // Fragments can point at images, frames and SVG shapes, and an image map's area is labelled by its alt text.
        Page("fragment-elements", "Fragments to images and maps", [], page => page with
        {
            Content = page.Content +
                      "<img id=\"photo\" src=\"/img/7.jpg\" alt=\"A photo\" width=\"400\" height=\"300\">" +
                      $"<iframe id=\"map\" src=\"{PLANTS_PATH}embedded\" title=\"A map\"></iframe><svg width=\"10\" height=\"10\"><g id=\"chart\"></g></svg>" +
                      "<p><a href=\"#photo\">The photo</a> <a href=\"#map\">The map</a> <a href=\"#chart\">The chart</a></p>" +
                      "<img src=\"/img/8.jpg\" usemap=\"#areas\" alt=\"A clickable plan\" width=\"400\" height=\"300\">" +
                      $"<map name=\"areas\"><area href=\"{PLANTS_PATH}thin\" alt=\"The thin page\" shape=\"rect\" coords=\"0,0,10,10\"></map>"
        });

        // A base pushed into the body by a stray element in the head still applies, so "0" is /c/0, not /plants/0.
        Page("base-in-body", "Base in the body", [], page => page with
        {
            ExtraHead = "<img src=\"/img/12.jpg\" alt=\"\" width=\"1\" height=\"1\"><base href=\"/c/\">",
            Content = page.Content + "<p><a href=\"0\">Category zero</a></p>"
        });

        // Links that look like fragments but name no element: Cloudflare's email obfuscation and an Elementor popup.
        Page("fragment-target", "Fragment target", [], page => page with
        {
            Content = "<h2 id=\"present\">Present section</h2>" + page.Content +
                      "<p><a href=\"/cdn-cgi/l/email-protection#7a1f021b170a161f3a1f021b170a161f54191517\">[email&#160;protected]</a> " +
                      "<a href=\"#elementor-action%3Aaction%3Dpopup%3Aopen%26settings%3DeyJpZCI6IjEyMyJ9\">Open the popup</a></p>"
        });
        plants.Add(new Plant("/cdn-cgi/l/email-protection", 200, Linked: false, InSitemap: false, [],
            () => Html(Render(PlantPage("cdn-cgi-email", "Email Protection") with { Path = "/cdn-cgi/l/email-protection", Title = null })),
            IsAnalysed: false));

        // A page shown only in an iframe is status-checked, not crawled, so there's nothing to audit on it.
        Page("iframe", "Embedded frame", [], page => page with { Content = page.Content + $"<iframe src=\"{PLANTS_PATH}embedded\" title=\"Embedded\"></iframe>" });
        plants.Add(new Plant(PLANTS_PATH + "embedded", 200, Linked: false, InSitemap: false, [],
            () => Html(Render(PlantPage("embedded", "Embedded") with { Title = null, Content = "<p>Too short.</p>" })), IsPage: false, IsAnalysed: false));

        // The hub links the redirecting form; the page itself, in the sitemap, still counts as linked.
        Other("trailing", 301, linked: true, [], () => Redirect(301, PLANTS_PATH + "trailing/"));
        Page("trailing/", "Trailing slash page", [], linked: false);

        Page("Upper-Case", "Uppercase URL", ["url-uppercase"]);
        Page("under_score", "Underscore URL", ["url-underscores"]);
        Page(LONG_SLUG, "Long URL", ["url-too-long"]);

        // A clean English/French pair, then one page per hreflang mistake.
        var pair = Hreflang(("en", "hreflang-en"), ("fr", "hreflang-fr"), ("x-default", "hreflang-en"));
        Page("hreflang-en", "Hreflang English", [], page => page with { ExtraHead = pair });
        Page("hreflang-fr", "Hreflang French", [], page => page with { ExtraHead = pair });
        // Revision 1 gives the lonely alternate the set its partner names, so it links back.
        Page("hreflang-lonely", "Hreflang alternate", [], page => revised
            ? page with { ExtraHead = Hreflang(("en", "hreflang-no-return"), ("de", "hreflang-lonely"), ("x-default", "hreflang-no-return")) }
            : page);

        // A canonicalised variant repeating its canonical's set is what Google recommends, so only the canonical counts.
        Page("hreflang-variant", "Hreflang variant", ["canonicalised-page"],
            page => page with { Canonical = $"{_base}{PLANTS_PATH}hreflang-en", ExtraHead = pair }, inSitemap: false);
        Page("hreflang-no-return", "Hreflang without return", revised ? [] : ["hreflang-missing-return"], page => page with
        {
            ExtraHead = Hreflang(("en", "hreflang-no-return"), ("de", "hreflang-lonely"), ("x-default", "hreflang-no-return"))
        });
        Page("hreflang-no-self", "Hreflang without self", ["hreflang-missing-self", "hreflang-missing-return"], page => page with
        {
            ExtraHead = Hreflang(("en", "hreflang-en"), ("x-default", "hreflang-en"))
        });
        Page("hreflang-bad-code", "Hreflang bad code", ["hreflang-invalid-code"], page => page with
        {
            ExtraHead = Hreflang(("en-UK", "hreflang-bad-code"), ("x-default", "hreflang-bad-code"))
        });
        Page("hreflang-broken", "Hreflang to a 404", ["hreflang-broken-target"], page => page with
        {
            ExtraHead = Hreflang(("en", "hreflang-broken"), ("de", "gone"), ("x-default", "hreflang-broken"))
        });
        Page("hreflang-conflict", "Hreflang conflict", ["hreflang-duplicate-code", "hreflang-missing-return"], page => page with
        {
            ExtraHead = Hreflang(("en", "hreflang-conflict"), ("en", "hreflang-fr"), ("x-default", "hreflang-conflict"))
        });
        Page("hreflang-noindex", "Hreflang to a noindex page", ["hreflang-to-non-indexable"], page => page with
        {
            ExtraHead = Hreflang(("en", "hreflang-noindex"), ("fr", "noindex"), ("x-default", "hreflang-noindex"))
        });
        // Alternates the crawler couldn't judge aren't non-indexable: one that refused it, and one on another site whose
        // robots.txt disallows it, which says nothing about whether search engines may read it. Alternates are checked when
        // something else links them, as the language switcher here does (crawling hreflang is off by default).
        Page("hreflang-unverified", "Unverified hreflang", [], page => page with
        {
            ExtraHead = Hreflang(("en", "hreflang-unverified"), ("fr", "blocked"), ("x-default", "hreflang-unverified")) +
                        $"<link rel=\"alternate\" hreflang=\"de\" href=\"{_external}{PARTNER_DISALLOWED}\">",
            Content = page.Content + $"<p><a href=\"{PLANTS_PATH}blocked\">Français</a> <a href=\"{_external}{PARTNER_DISALLOWED}\">Deutsch</a></p>"
        });
        plants.Add(new Plant(PARTNER_DISALLOWED, Plant.REFUSED, Linked: false, InSitemap: false, [],
            () => Html(RenderSimple("Partner private page", "The partner site's robots.txt disallows this area.", PARTNER_DISALLOWED)),
            IsPage: false, OnPartnerSite: true));
        Page("hreflang-body", "Hreflang in the body", ["hreflang-outside-head"], page => page with
        {
            Content = page.Content + Hreflang(("en", "hreflang-body"), ("x-default", "hreflang-body"))
        });
        Page("hreflang-no-x-default", "Hreflang without x-default", ["hreflang-missing-x-default"], page => page with
        {
            ExtraHead = Hreflang(("en", "hreflang-no-x-default"))
        });

        Page("jsonld-invalid", "Broken JSON-LD", ["structured-data-invalid"], page => page with
        {
            JsonLd = """{"@context": "https://schema.org", "@type": "Product" "name": "Broken"}"""
        });
        Page("jsonld-incomplete", "Incomplete Product markup", ["structured-data-missing-required"], page => page with
        {
            JsonLd = """{"@context":"https://schema.org","@type":"Product","name":"Incomplete product"}"""
        });

        // A schema.org subtype is held to its parent's requirements: a Restaurant is a LocalBusiness, a MusicEvent an Event.
        Page("jsonld-subtypes", "Incomplete subtype markup", ["structured-data-missing-required"], page => page with
        {
            JsonLd = """[{"@context":"https://schema.org","@type":"Restaurant","name":"Planted bistro"},{"@context":"https://schema.org","@type":"MusicEvent","name":"Planted concert","startDate":"2026-12-01T19:00"}]"""
        });

        Page("large", "Large HTML", ["large-page"], page => page with { Content = page.Content + $"<!-- {new string('x', 1_100_000)} -->" });
        // Past Googlebot's 2 MB uncompressed HTML limit, and also over the 1 MB large-page threshold.
        Page("html-past-crawl-limit", "Huge HTML", ["large-page", "html-past-crawl-limit"],
            page => page with { Content = page.Content + $"<!-- {new string('x', 2_200_000)} -->" });
        Page("no-viewport", "Missing viewport", ["viewport-missing"], page => page with { Viewport = false });
        Page("no-lang", "Missing lang", ["lang-missing"], page => page with { Lang = false });
        Page("no-charset", "Missing charset", ["charset-missing"], page => page with { Charset = false }, contentType: "text/html");

        // With no charset in the header, a meta tag declares the encoding only within the first 1024 bytes, where browsers
        // look for it: first in the head it does, after the head's inline styles it doesn't.
        Page("charset-meta", "Charset in a meta tag", [], contentType: "text/html");
        Page("charset-late", "Charset declared too late", ["charset-missing"], page => page with
        {
            Charset = false, ExtraHead = "<meta charset=\"utf-8\">"
        }, contentType: "text/html");

        // A byte-order mark at the start of the file declares the encoding, with no meta tag and no charset in the
        // header: neither page is missing its character encoding.
        Page("charset-bom", "Byte-order mark", [], page => page with { Charset = false }, contentType: "text/html", markedAs: Encoding.UTF8);
        Page("charset-bom-utf16", "UTF-16 byte-order mark", [], page => page with { Charset = false }, contentType: "text/html",
            markedAs: Encoding.Unicode);
        Page("no-open-graph", "Missing Open Graph", ["open-graph-missing"], page => page with { OpenGraph = false });

        // One meta tag serving as both the description and og:description is both.
        Page("meta-name-and-property", "Shared description tag", [], page => page with
        {
            Description = null, OpenGraph = false,
            ExtraHead = $"<meta name=\"description\" property=\"og:description\" content=\"{page.Description}\">" +
                        $"<meta property=\"og:title\" content=\"{page.Title}\"><meta property=\"og:image\" content=\"{_base}/img/0.jpg\">"
        });

        var slow = PlantPage("slow", "Slow page");
        plants.Add(new Plant(slow.Path, 200, Linked: true, InSitemap: true, ["slow-response"], () => Html(Render(slow)) with { DelayMs = SLOW_PAGE_DELAY_MS }));

        Other("blocked", 403, linked: true, ["internal-blocked"], () => new SyntheticResponse(403, HTML,
            RenderSimple("Checking your browser", "A bot challenge stands in the way.", PLANTS_PATH + "blocked"), null, null,
            new Dictionary<string, string> { ["cf-mitigated"] = "challenge" }));
        Other("unreachable", 0, linked: true, ["internal-unreachable"], () => new SyntheticResponse(0, HTML, null, null, null, Abort: true));
        Other("loop-a", 301, linked: true, ["redirect-loop"], () => Redirect(301, PLANTS_PATH + "loop-b"));
        Other("loop-b", 301, linked: false, ["redirect-loop"], () => Redirect(301, PLANTS_PATH + "loop-a"));
        Other("redirect-broken", 301, linked: true, ["redirect-broken"], () => Redirect(301, PLANTS_PATH + "gone-redirect"));

        // A chain is reported where visitors enter it, chain-a, which the hub links, and not again at chain-b. Revision 1
        // sends chain-a straight to its end.
        if (revised)
        {
            Other("chain-a", 301, linked: true, [], () => Redirect(301, PLANTS_PATH + "thin"));
        }
        else
        {
            Other("chain-a", 301, linked: true, ["redirect-chain"], () => Redirect(301, PLANTS_PATH + "chain-b"));
            Other("chain-b", 301, linked: false, [], () => Redirect(301, PLANTS_PATH + "chain-c"));
            Other("chain-c", 301, linked: false, [], () => Redirect(301, PLANTS_PATH + "thin"));
        }

        // A very long chain is a chain, not a loop: no URL repeats. Browsers give up after 20 hops, so the hub's link to it is broken.
        for (var hop = 0; hop < LONG_CHAIN_HOPS; hop++)
        {
            var next = hop + 1 < LONG_CHAIN_HOPS ? $"{PLANTS_PATH}long-chain-{hop + 1}" : PLANTS_PATH + "thin";
            Other($"long-chain-{hop}", 301, linked: hop == 0, hop == 0 ? ["redirect-chain"] : [], () => Redirect(301, next));
        }
        Other("moved", 301, linked: false, [], () => Redirect(301, PLANTS_PATH + "thin"));

        // A link to a temporary redirect is right as it is: the linked URL stays the one to use, as with a shop's sign-in
        // link, which sends visitors on to its hosted login with a one-off token. A temporary redirect to another site is
        // such a hand-off, not a move, so only the one within the site is flagged (and, over HTTPS, the hand-off to the
        // plain-HTTP partner site is a downgrade).
        Page("temporary-links", "Links to temporary redirects", [], page => page with
        {
            Content = page.Content + $"<p><a href=\"{PLANTS_PATH}account-login\">Sign in</a> <a href=\"{PLANTS_PATH}sale\">This week's sale</a></p>"
        });
        Other("account-login", 302, linked: false, IsHttps ? ["redirect-downgrade"] : [], () => Redirect(302, $"{_external}/ext/account/login"));
        Other("sale", 302, linked: false, ["redirect-temporary"], () => Redirect(302, PLANTS_PATH + "thin"));
        Other("gone", 404, linked: false, ["internal-client-error"], () => NotFound(PLANTS_PATH + "gone"));
        Other("gone-redirect", 404, linked: false, ["internal-client-error"], () => NotFound(PLANTS_PATH + "gone-redirect"));
        plants.Add(new Plant("/img/missing.jpg", 404, Linked: false, InSitemap: false, ["internal-client-error"], () => NotFound("/img/missing.jpg")));

        // The site's dead https:// version (see SyntheticSiteOptions.DeadTwin): asked for, so unreachable. Its other URLs,
        // on the first orphan page, aren't asked for once it has been written off, so they aren't judged at all.
        if (_options.DeadTwin)
        {
            plants.Add(new Plant(PLANTS_PATH + "dead-twin", 0, Linked: true, InSitemap: false, ["internal-unreachable"],
                () => new SyntheticResponse(0, HTML, null, null, null, Abort: true), Url: DeadTwinBase + PLANTS_PATH + "dead-twin"));
        }

        if (IsHttps)
        {
            // A page still answering over plain HTTP on the same host, with no redirect to HTTPS. Its base element points
            // at the HTTPS site, so its links don't lead the crawler into a second, plain-HTTP copy of the shop.
            var insecure = PlantPage("http-page", "Plain HTTP page");
            var insecureUrl = "http" + _base["https".Length..] + insecure.Path;
            var insecureHtml = Render(insecure with { Canonical = insecureUrl, ExtraHead = $"<base href=\"{_base}/\">" });
            plants.Add(new Plant(insecure.Path, 200, Linked: true, InSitemap: true, ["http-page"],
                () => new SyntheticResponse(200, HTML, insecureHtml, null, null, SECURITY_HEADERS), Url: insecureUrl));

            Page("mixed-content", "Mixed content", ["mixed-content"], page => page with
            {
                Content = page.Content + $"<img src=\"{_external}/ext/img/1.jpg\" alt=\"A photo served over plain HTTP\" width=\"400\" height=\"300\">"
            });
            Other("downgrade", 301, linked: true, ["redirect-downgrade"], () => Redirect(301, $"{_external}/ext/plain-http"));
        }

        // The hub links to redirects, and to the unreachable URL, the redirect loop and the redirect to a 404, which are
        // all broken, so it carries those link issues itself.
        plants.Add(new Plant(PLANTS_PATH, 200, Linked: false, InSitemap: true, ["links-to-redirects", "broken-internal-links"], () => Html(RenderPlantsHub())));
        return plants;
    }

    private PageSpec PlantPage(string slug, string name) => new()
    {
        Path = PLANTS_PATH + slug,
        Title = $"{name} | Synthetic Store planted page",
        Description = $"{name}: a planted test page with one deliberate SEO problem, part of the Synthetic Store crawler tests.",
        H1 = name,
        Content = $"<p>{Text("plant-" + slug, PLANT_WORDS)}</p>"
    };

    private string RenderPlantsHub()
    {
        var links = string.Concat(_plants.Where(plant => plant.Linked)
            .Select(plant => $"<li><a href=\"{plant.Url ?? plant.Path}\">Planted page {plant.Path[PLANTS_PATH.Length..]}</a></li>"));
        return Render(new PageSpec
        {
            Path = PLANTS_PATH,
            Title = "Planted test pages | Synthetic Store, a shop for tests",
            Description = "Index of the planted test pages: each one carries a known SEO problem so the crawler's checks can be proven exactly.",
            H1 = "Planted test pages",
            Content = $"<p>{Text("plants", INTRO_WORDS)}</p><ul>{links}</ul>"
        });
    }

    private string Hreflang(params (string Code, string Slug)[] alternates) => string.Concat(alternates.Select(alternate =>
        $"<link rel=\"alternate\" hreflang=\"{alternate.Code}\" href=\"{_base}{PLANTS_PATH}{alternate.Slug}\">"));
}
