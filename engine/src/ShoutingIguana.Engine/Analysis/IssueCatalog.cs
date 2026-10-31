using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Engine.Analysis;

/// <summary>
/// Every issue the analysis can report. Ids are stable: they are stored in crawl files and used by the UI, so
/// change a title or description freely but never an id. Each issue is reported at most once per URL.
/// </summary>
public static class IssueCatalog
{
    // Response codes
    public static readonly IssueDefinition INTERNAL_CLIENT_ERROR = new("internal-client-error", IssueCategory.ResponseCodes, IssueSeverity.Error,
        "Internal URL returns 4xx",
        "The URL returns a client error such as 404 Not Found, so visitors and search engines that follow links to it hit a dead end.",
        "Restore the page, redirect it to the best replacement, or update the links that point to it.");

    public static readonly IssueDefinition INTERNAL_SERVER_ERROR = new("internal-server-error", IssueCategory.ResponseCodes, IssueSeverity.Error,
        "Internal URL returns 5xx",
        "The server failed to answer the request. Search engines slow down crawling and eventually drop pages that keep failing.",
        "Check the server logs for the URL and fix the error.");

    public static readonly IssueDefinition INTERNAL_UNREACHABLE = new("internal-unreachable", IssueCategory.ResponseCodes, IssueSeverity.Error,
        "Internal URL didn't respond",
        "The request timed out or the connection failed, even after retries.",
        "Check that the server answers this URL in a reasonable time and doesn't drop connections under load.");

    public static readonly IssueDefinition INTERNAL_BLOCKED = new("internal-blocked", IssueCategory.ResponseCodes, IssueSeverity.Warning,
        "Internal URL blocked by a firewall",
        "A firewall, bot protection or rate limit stopped the crawler, so the page could not be checked. Search engine crawlers may be blocked too.",
        "Allowlist the crawler (by user agent, IP or a secret header) or lower the crawl rate.");

    public static readonly IssueDefinition SOFT_404 = new("soft-404", IssueCategory.ResponseCodes, IssueSeverity.Warning,
        "Soft 404",
        "The page says it was not found but returns 200 OK, so search engines may index an error page.",
        "Return a real 404 or 410 status for missing pages, or redirect them.");

    // Redirects
    public static readonly IssueDefinition REDIRECT_CHAIN = new("redirect-chain", IssueCategory.Redirects, IssueSeverity.Warning,
        "Redirect chain",
        "The URL redirects more than once before reaching a page. Every hop adds latency and some crawlers stop following.",
        "Redirect straight to the final URL.");

    public static readonly IssueDefinition REDIRECT_LOOP = new("redirect-loop", IssueCategory.Redirects, IssueSeverity.Error,
        "Redirect loop",
        "The redirects never reach a page: they loop back on themselves, so browsers show an error.",
        "Point the redirect at a URL that returns 200.");

    public static readonly IssueDefinition REDIRECT_BROKEN = new("redirect-broken", IssueCategory.Redirects, IssueSeverity.Error,
        "Redirect leads to a broken URL",
        "The redirect ends at a URL that returns an error or doesn't respond.",
        "Point the redirect at a working page.");

    public static readonly IssueDefinition REDIRECT_TEMPORARY = new("redirect-temporary", IssueCategory.Redirects, IssueSeverity.Notice,
        "Temporary redirect",
        "A 302 or 307 tells search engines the move is temporary, so they may keep the old URL in results.",
        "Use a 301 or 308 if the move is permanent.");

    public static readonly IssueDefinition REDIRECT_DOWNGRADE = new("redirect-downgrade", IssueCategory.Redirects, IssueSeverity.Error,
        "Redirect from HTTPS to HTTP",
        "The redirect sends visitors from a secure URL to an insecure one.",
        "Redirect to the HTTPS version of the target.");

    public static readonly IssueDefinition META_REFRESH = new("meta-refresh", IssueCategory.Redirects, IssueSeverity.Warning,
        "Meta refresh",
        "The page uses a meta refresh to reload or move to another URL. Search engines handle it less reliably than a server redirect, and it's slower for visitors.",
        "Replace it with a 301 redirect.");

    // Links
    public static readonly IssueDefinition BROKEN_INTERNAL_LINKS = new("broken-internal-links", IssueCategory.Links, IssueSeverity.Error,
        "Links to broken internal URLs",
        "The page links to internal URLs that return an error or don't respond.",
        "Update or remove the links, or fix the target pages.");

    public static readonly IssueDefinition BROKEN_EXTERNAL_LINKS = new("broken-external-links", IssueCategory.Links, IssueSeverity.Warning,
        "Links to broken external URLs",
        "The page links to other sites' URLs that return an error or don't respond.",
        "Update the links to working URLs or remove them.");

    public static readonly IssueDefinition LINKS_TO_REDIRECTS = new("links-to-redirects", IssueCategory.Links, IssueSeverity.Notice,
        "Links to internal redirects",
        "The page links to internal URLs that redirect, which costs a hop for every visit and crawl.",
        "Link straight to the final URL.");

    public static readonly IssueDefinition INTERNAL_NOFOLLOW_LINKS = new("internal-nofollow-links", IssueCategory.Links, IssueSeverity.Warning,
        "Nofollow internal links",
        "The page marks links to its own site as nofollow, which stops them passing signals and can keep pages from being crawled.",
        "Remove rel=\"nofollow\" from internal links.");

    public static readonly IssueDefinition EMPTY_ANCHOR_TEXT = new("empty-anchor-text", IssueCategory.Links, IssueSeverity.Notice,
        "Internal links without anchor text",
        "The page has links with no text, alt text or aria-label, so search engines and screen readers can't tell what they point to.",
        "Add descriptive link text, or alt text to linked images.");

    public static readonly IssueDefinition BROKEN_FRAGMENT_LINKS = new("broken-fragment-links", IssueCategory.Links, IssueSeverity.Notice,
        "Links to missing page sections",
        "The page links to a #section that doesn't exist on the target page, so the link doesn't jump where it should.",
        "Fix the fragment or add the matching id to the target page.");

    public static readonly IssueDefinition ORPHAN_PAGE = new("orphan-page", IssueCategory.Links, IssueSeverity.Warning,
        "Orphan page",
        "The page is in a sitemap but no crawled page links to it, so it gets no internal link signals and visitors can't reach it.",
        "Link to it from relevant pages, or remove it from the sitemap if it shouldn't exist.");

    // Indexability
    public static readonly IssueDefinition NOINDEX_PAGE = new("noindex-page", IssueCategory.Indexability, IssueSeverity.Notice,
        "Noindex page",
        "A robots meta tag or X-Robots-Tag header keeps this page out of search results.",
        "Make sure that's intended; remove the noindex if the page should rank.");

    public static readonly IssueDefinition NOFOLLOW_PAGE = new("nofollow-page", IssueCategory.Indexability, IssueSeverity.Warning,
        "Nofollow page",
        "A robots nofollow directive tells search engines not to follow any link on the page.",
        "Remove the nofollow directive unless the page's links really shouldn't be followed.");

    public static readonly IssueDefinition BLOCKED_BY_ROBOTS = new("blocked-by-robots", IssueCategory.Indexability, IssueSeverity.Warning,
        "Blocked by robots.txt",
        "robots.txt stops crawlers from fetching this linked URL, so search engines can't see its content.",
        "Allow the URL in robots.txt if it should be crawled, or stop linking to it.");

    public static readonly IssueDefinition SNIPPET_BLOCKED = new("snippet-blocked", IssueCategory.Indexability, IssueSeverity.Notice,
        "Search snippet disabled",
        "A nosnippet or max-snippet:0 directive stops search engines showing a text snippet, which usually lowers clicks.",
        "Remove the directive unless hiding the snippet is intended.");

    public static readonly IssueDefinition ROBOTS_TXT_UNREACHABLE = new("robots-txt-unreachable", IssueCategory.Indexability, IssueSeverity.Error,
        "robots.txt unavailable",
        "robots.txt returns a server error or doesn't respond. Google treats that as \"disallow everything\" and stops crawling the site.",
        "Make robots.txt return 200 (or 404 if you don't need one).");

    public static readonly IssueDefinition ROBOTS_TXT_MISSING = new("robots-txt-missing", IssueCategory.Indexability, IssueSeverity.Notice,
        "No robots.txt",
        "The site has no robots.txt. Everything may be crawled, which is fine, but there's nowhere to point crawlers at your sitemap.",
        "Add a robots.txt with a Sitemap: line.");

    // Canonicals
    public static readonly IssueDefinition CANONICALISED_PAGE = new("canonicalised-page", IssueCategory.Canonicals, IssueSeverity.Notice,
        "Canonicalised page",
        "The page names a different URL as its canonical, so search engines will usually index that URL instead.",
        "Make sure that's intended, and link to the canonical URL rather than this one.");

    public static readonly IssueDefinition CANONICAL_MISSING = new("canonical-missing", IssueCategory.Canonicals, IssueSeverity.Notice,
        "Missing canonical",
        "The page has no canonical, so search engines pick the preferred URL themselves when it's reachable at several addresses.",
        "Add a self-referencing <link rel=\"canonical\">.");

    public static readonly IssueDefinition CANONICAL_MULTIPLE = new("canonical-multiple", IssueCategory.Canonicals, IssueSeverity.Warning,
        "Conflicting canonicals",
        "The page declares more than one canonical URL (in the HTML, the Link header, or both). Google ignores them all when they disagree.",
        "Keep a single canonical.");

    public static readonly IssueDefinition CANONICAL_BROKEN = new("canonical-broken", IssueCategory.Canonicals, IssueSeverity.Error,
        "Canonical points to a broken URL",
        "The canonical URL returns an error or doesn't respond, directly or at the end of its redirects, so search engines ignore it and judge the page on its own.",
        "Point the canonical at a working, indexable page.");

    public static readonly IssueDefinition CANONICAL_TO_REDIRECT = new("canonical-to-redirect", IssueCategory.Canonicals, IssueSeverity.Warning,
        "Canonical points to a redirect",
        "The canonical URL redirects, a mixed signal search engines may ignore. When the redirect leads back to this page, the redirect wins and this page is the one indexed.",
        "Point the canonical at the final URL.");

    public static readonly IssueDefinition CANONICAL_TO_NON_INDEXABLE = new("canonical-to-non-indexable", IssueCategory.Canonicals, IssueSeverity.Warning,
        "Canonical points to a non-indexable page",
        "The canonical URL is itself noindex or canonicalised to another URL, so search engines get conflicting signals.",
        "Point the canonical at the final, indexable URL.");

    public static readonly IssueDefinition CANONICAL_OUTSIDE_HEAD = new("canonical-outside-head", IssueCategory.Canonicals, IssueSeverity.Warning,
        "Canonical outside <head>",
        "A canonical link sits in the page body, where search engines ignore it.",
        "Move the canonical into the <head>.");

    public static readonly IssueDefinition CANONICAL_RELATIVE = new("canonical-relative", IssueCategory.Canonicals, IssueSeverity.Notice,
        "Relative canonical URL",
        "The canonical is a relative URL. It works, but an absolute URL avoids mistakes when pages are copied or served on other hosts.",
        "Use the full URL, including https:// and the host.");

    public static readonly IssueDefinition CANONICAL_INVALID = new("canonical-invalid", IssueCategory.Canonicals, IssueSeverity.Warning,
        "Invalid canonical URL",
        "The canonical isn't a valid http(s) URL, so it's ignored.",
        "Fix the canonical URL.");

    public static readonly IssueDefinition NOINDEX_WITH_CANONICAL = new("noindex-with-canonical", IssueCategory.Canonicals, IssueSeverity.Warning,
        "Noindex page with a canonical to another URL",
        "The page is noindex and names another URL as its canonical. Google advises against combining them: the noindex drops the page from search, while the canonical says it's a duplicate whose signals belong to the other URL.",
        "Use the canonical alone to fold the page into the other URL, or the noindex alone, with a self-referencing canonical, to keep it out of search.");

    // Sitemaps
    public static readonly IssueDefinition SITEMAP_NON_INDEXABLE = new("sitemap-non-indexable", IssueCategory.Sitemaps, IssueSeverity.Warning,
        "Non-indexable URL in sitemap",
        "A sitemap lists a URL that redirects, fails, is blocked, noindex or canonicalised elsewhere. Sitemaps should list only pages you want indexed.",
        "Remove the URL from the sitemap or list its indexable version.");

    public static readonly IssueDefinition NOT_IN_SITEMAP = new("not-in-sitemap", IssueCategory.Sitemaps, IssueSeverity.Notice,
        "Indexable page not in a sitemap",
        "The page can be indexed but no sitemap lists it, so search engines rely on links alone to find it.",
        "Add the page to your XML sitemap.");

    public static readonly IssueDefinition SITEMAP_ERROR = new("sitemap-error", IssueCategory.Sitemaps, IssueSeverity.Error,
        "Sitemap can't be read",
        "A sitemap listed in robots.txt or a sitemap index returns an error or isn't valid XML.",
        "Fix the sitemap URL or file, or remove the reference to it.");

    public static readonly IssueDefinition SITEMAP_MISSING = new("sitemap-missing", IssueCategory.Sitemaps, IssueSeverity.Notice,
        "No XML sitemap found",
        "No sitemap is listed in robots.txt or found at /sitemap.xml.",
        "Publish an XML sitemap and reference it in robots.txt.");

    public static readonly IssueDefinition SITEMAP_URL_OTHER_HOST = new("sitemap-url-other-host", IssueCategory.Sitemaps, IssueSeverity.Warning,
        "Sitemap lists a URL on another host",
        "A sitemap lists a URL outside the site, often left over from a move to or from www. Search engines drop such entries unless that host's robots.txt points to the sitemap, so the page it was meant for isn't submitted.",
        "List the URL as it is on the site's own host now, or remove the entry.");

    // Titles
    public static readonly IssueDefinition TITLE_MISSING = new("title-missing", IssueCategory.Titles, IssueSeverity.Error,
        "Missing title",
        "The page has no <title> or it's empty. The title is the headline in search results and a strong relevance signal.",
        "Write a unique, descriptive title.");

    public static readonly IssueDefinition TITLE_MULTIPLE = new("title-multiple", IssueCategory.Titles, IssueSeverity.Warning,
        "Multiple titles",
        "The page has more than one <title> element; search engines may pick the wrong one.",
        "Keep one <title> in the <head>.");

    public static readonly IssueDefinition TITLE_TOO_LONG = new("title-too-long", IssueCategory.Titles, IssueSeverity.Notice,
        "Title too long",
        "Long titles get cut off in search results.",
        "Shorten the title, putting the important words first.");

    public static readonly IssueDefinition TITLE_TOO_SHORT = new("title-too-short", IssueCategory.Titles, IssueSeverity.Notice,
        "Title too short",
        "A very short title wastes space in search results and says little about the page.",
        "Expand the title to describe the page.");

    public static readonly IssueDefinition TITLE_DUPLICATE = new("title-duplicate", IssueCategory.Titles, IssueSeverity.Warning,
        "Duplicate title",
        "Another indexable page has the same title, which makes it hard for search engines and searchers to tell them apart.",
        "Give each page a unique title.");

    // Meta descriptions
    public static readonly IssueDefinition DESCRIPTION_MISSING = new("description-missing", IssueCategory.MetaDescriptions, IssueSeverity.Warning,
        "Missing meta description",
        "Without a description, search engines build a snippet from page text, which is often less compelling.",
        "Write a description that summarises the page and invites the click.");

    public static readonly IssueDefinition DESCRIPTION_MULTIPLE = new("description-multiple", IssueCategory.MetaDescriptions, IssueSeverity.Warning,
        "Multiple meta descriptions",
        "The page has more than one meta description; search engines may ignore them.",
        "Keep a single meta description.");

    public static readonly IssueDefinition DESCRIPTION_TOO_LONG = new("description-too-long", IssueCategory.MetaDescriptions, IssueSeverity.Notice,
        "Meta description too long",
        "Long descriptions get cut off in search results.",
        "Shorten the description.");

    public static readonly IssueDefinition DESCRIPTION_TOO_SHORT = new("description-too-short", IssueCategory.MetaDescriptions, IssueSeverity.Notice,
        "Meta description too short",
        "A very short description leaves space unused and may be replaced by page text.",
        "Expand the description.");

    public static readonly IssueDefinition DESCRIPTION_DUPLICATE = new("description-duplicate", IssueCategory.MetaDescriptions, IssueSeverity.Warning,
        "Duplicate meta description",
        "Another indexable page has the same meta description.",
        "Write a unique description for each page.");

    // Headings
    public static readonly IssueDefinition H1_MISSING = new("h1-missing", IssueCategory.Headings, IssueSeverity.Warning,
        "Missing H1",
        "The page has no H1 heading, or it's empty. The H1 tells visitors and search engines what the page is about.",
        "Add one descriptive H1.");

    public static readonly IssueDefinition H1_MULTIPLE = new("h1-multiple", IssueCategory.Headings, IssueSeverity.Notice,
        "Multiple H1s",
        "The page has more than one H1, which can blur what the page is mainly about.",
        "Use one H1 and H2s for sections.");

    public static readonly IssueDefinition H1_DUPLICATE = new("h1-duplicate", IssueCategory.Headings, IssueSeverity.Notice,
        "Duplicate H1",
        "Another indexable page has the same H1.",
        "Give each page a unique H1.");

    // Content
    public static readonly IssueDefinition LOW_WORD_COUNT = new("low-word-count", IssueCategory.Content, IssueSeverity.Notice,
        "Low word count",
        "The page's main content has few words, which can look thin to search engines.",
        "Add useful content, or merge the page with a related one.");

    public static readonly IssueDefinition DUPLICATE_CONTENT = new("duplicate-content", IssueCategory.Content, IssueSeverity.Warning,
        "Duplicate content",
        "Another indexable page has exactly the same main content, so they compete with each other.",
        "Keep one version and redirect or canonicalise the others to it.");

    public static readonly IssueDefinition NEAR_DUPLICATE_CONTENT = new("near-duplicate-content", IssueCategory.Content, IssueSeverity.Notice,
        "Near-duplicate content",
        "Another indexable page has almost the same main content.",
        "Make each page distinct, or canonicalise the near copies to one version.");

    // Images
    public static readonly IssueDefinition IMAGE_MISSING_ALT = new("image-missing-alt", IssueCategory.Images, IssueSeverity.Warning,
        "Images without alt text",
        "Images have no alt attribute, so screen readers and image search can't tell what they show. An empty alt=\"\" is fine for decorative images.",
        "Add alt text that describes each image.");

    public static readonly IssueDefinition IMAGE_ALT_TOO_LONG = new("image-alt-too-long", IssueCategory.Images, IssueSeverity.Notice,
        "Alt text too long",
        "Very long alt text is tiring for screen reader users.",
        "Keep alt text to a short description.");

    public static readonly IssueDefinition IMAGE_MISSING_DIMENSIONS = new("image-missing-dimensions", IssueCategory.Images, IssueSeverity.Notice,
        "Images without width and height",
        "Without width and height the browser can't reserve space, so the layout jumps as images load (Cumulative Layout Shift).",
        "Set width and height attributes on images.");

    public static readonly IssueDefinition IMAGE_BROKEN = new("image-broken", IssueCategory.Images, IssueSeverity.Error,
        "Broken images",
        "Images on the page return an error or don't load.",
        "Fix the image URLs or remove the images.");

    public static readonly IssueDefinition IMAGE_TOO_LARGE = new("image-too-large", IssueCategory.Images, IssueSeverity.Warning,
        "Large images",
        "Image files are big enough to slow the page down, especially on mobile.",
        "Compress the images, resize them to the size shown, or use WebP/AVIF.");

    // Hreflang
    public static readonly IssueDefinition HREFLANG_INVALID_CODE = new("hreflang-invalid-code", IssueCategory.Hreflang, IssueSeverity.Error,
        "Invalid hreflang code",
        "An hreflang value isn't a valid ISO 639-1 language with an optional ISO 3166-1 region (e.g. en-GB, not en-UK), so it's ignored.",
        "Fix the language and region codes.");

    public static readonly IssueDefinition HREFLANG_MISSING_SELF = new("hreflang-missing-self", IssueCategory.Hreflang, IssueSeverity.Warning,
        "Hreflang without a self-reference",
        "The page lists alternates in other languages but doesn't include itself.",
        "Add an hreflang entry for the page's own URL.");

    public static readonly IssueDefinition HREFLANG_MISSING_RETURN = new("hreflang-missing-return", IssueCategory.Hreflang, IssueSeverity.Warning,
        "Hreflang without return links",
        "An alternate page doesn't link back with hreflang. Google ignores hreflang pairs that aren't confirmed from both sides.",
        "Add the matching hreflang entries to the alternate pages.");

    public static readonly IssueDefinition HREFLANG_BROKEN_TARGET = new("hreflang-broken-target", IssueCategory.Hreflang, IssueSeverity.Error,
        "Hreflang points to a broken URL",
        "An hreflang alternate returns an error or doesn't respond.",
        "Point the hreflang entry at the working alternate page.");

    public static readonly IssueDefinition HREFLANG_TO_NON_INDEXABLE = new("hreflang-to-non-indexable", IssueCategory.Hreflang, IssueSeverity.Warning,
        "Hreflang points to a non-indexable URL",
        "An hreflang alternate redirects, is noindex or is canonicalised elsewhere. Alternates should be the indexable, canonical pages.",
        "Point the hreflang entry at the canonical, indexable alternate.");

    public static readonly IssueDefinition HREFLANG_DUPLICATE_CODE = new("hreflang-duplicate-code", IssueCategory.Hreflang, IssueSeverity.Warning,
        "Conflicting hreflang entries",
        "The same language code points to different URLs, so search engines can't tell which one to show.",
        "Keep one URL per language code.");

    public static readonly IssueDefinition HREFLANG_MISSING_X_DEFAULT = new("hreflang-missing-x-default", IssueCategory.Hreflang, IssueSeverity.Notice,
        "Hreflang without x-default",
        "No x-default entry says which page to show searchers whose language isn't listed.",
        "Add an x-default entry, usually pointing at the language selector or main version.");

    public static readonly IssueDefinition HREFLANG_OUTSIDE_HEAD = new("hreflang-outside-head", IssueCategory.Hreflang, IssueSeverity.Warning,
        "Hreflang outside <head>",
        "Hreflang links sit in the page body, where search engines ignore them.",
        "Move the hreflang links into the <head>.");

    // Structured data
    public static readonly IssueDefinition STRUCTURED_DATA_INVALID = new("structured-data-invalid", IssueCategory.StructuredData, IssueSeverity.Error,
        "Invalid JSON-LD",
        "A JSON-LD block isn't valid JSON, so search engines ignore all of it.",
        "Fix the JSON syntax; Google's Rich Results Test shows where.");

    public static readonly IssueDefinition STRUCTURED_DATA_MISSING_REQUIRED = new("structured-data-missing-required", IssueCategory.StructuredData, IssueSeverity.Warning,
        "Structured data missing required properties",
        "Markup for a rich result type lacks properties Google requires, so the page isn't eligible for that rich result.",
        "Add the missing properties.");

    // Security
    public static readonly IssueDefinition NO_HTTPS = new("no-https", IssueCategory.Security, IssueSeverity.Error,
        "Site not on HTTPS",
        "The site is served over plain HTTP. Browsers mark it \"Not secure\" and HTTPS is a ranking signal.",
        "Serve the site over HTTPS and redirect HTTP to it.");

    public static readonly IssueDefinition HTTP_PAGE = new("http-page", IssueCategory.Security, IssueSeverity.Warning,
        "HTTP page on an HTTPS site",
        "This page is still served over plain HTTP while the rest of the site uses HTTPS.",
        "Redirect it to its HTTPS version.");

    public static readonly IssueDefinition MIXED_CONTENT = new("mixed-content", IssueCategory.Security, IssueSeverity.Error,
        "Mixed content",
        "The HTTPS page loads images, scripts, styles or frames over HTTP. Browsers block or warn about them.",
        "Load every resource over HTTPS.");

    public static readonly IssueDefinition SECURITY_HEADERS_MISSING = new("security-headers-missing", IssueCategory.Security, IssueSeverity.Notice,
        "Security headers missing",
        "The home page response lacks common security headers. They don't affect rankings directly but protect visitors.",
        "Add the missing headers in the server or CDN configuration.");

    // URLs
    public static readonly IssueDefinition URL_UPPERCASE = new("url-uppercase", IssueCategory.Urls, IssueSeverity.Notice,
        "Uppercase characters in URL",
        "URLs are case-sensitive, so mixed case invites duplicate URLs and broken links typed by hand.",
        "Use lowercase URLs and redirect the mixed-case versions.");

    public static readonly IssueDefinition URL_UNDERSCORES = new("url-underscores", IssueCategory.Urls, IssueSeverity.Notice,
        "Underscores in URL",
        "Search engines treat hyphens as word separators but not always underscores.",
        "Use hyphens between words.");

    public static readonly IssueDefinition URL_TOO_LONG = new("url-too-long", IssueCategory.Urls, IssueSeverity.Notice,
        "Long URL",
        "Long URLs are hard to read and share and get cut off in search results.",
        "Use a shorter, descriptive URL.");

    // Performance
    public static readonly IssueDefinition SLOW_RESPONSE = new("slow-response", IssueCategory.Performance, IssueSeverity.Warning,
        "Slow page",
        "The server took a long time to deliver the page, which hurts visitors and crawling.",
        "Speed up the server: caching, database queries, a CDN.");

    public static readonly IssueDefinition LARGE_PAGE = new("large-page", IssueCategory.Performance, IssueSeverity.Notice,
        "Large HTML",
        "The HTML alone is very large, which slows downloading and parsing, especially on mobile.",
        "Move inline scripts, styles and data out of the HTML and trim markup.");

    // HTML
    public static readonly IssueDefinition VIEWPORT_MISSING = new("viewport-missing", IssueCategory.Html, IssueSeverity.Warning,
        "Missing viewport",
        "Without a viewport meta tag, mobile browsers render the page zoomed out, and Google indexes the mobile version.",
        "Add <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">.");

    public static readonly IssueDefinition LANG_MISSING = new("lang-missing", IssueCategory.Html, IssueSeverity.Notice,
        "Missing lang attribute",
        "The <html> element has no lang attribute, which screen readers and translation tools rely on.",
        "Add lang to <html>, e.g. lang=\"en\".");

    public static readonly IssueDefinition CHARSET_MISSING = new("charset-missing", IssueCategory.Html, IssueSeverity.Notice,
        "Missing character encoding",
        "Neither the Content-Type header nor a meta tag declares the encoding, so browsers may show garbled characters.",
        "Add <meta charset=\"utf-8\"> at the top of the <head>.");

    public static readonly IssueDefinition OPEN_GRAPH_MISSING = new("open-graph-missing", IssueCategory.Html, IssueSeverity.Notice,
        "Open Graph tags missing",
        "The page lacks og:title, og:description or og:image, so shares on social networks and chat apps get a poor preview.",
        "Add the missing Open Graph meta tags.");

    public static IReadOnlyList<IssueDefinition> All { get; } =
    [
        INTERNAL_CLIENT_ERROR, INTERNAL_SERVER_ERROR, INTERNAL_UNREACHABLE, INTERNAL_BLOCKED, SOFT_404,
        REDIRECT_CHAIN, REDIRECT_LOOP, REDIRECT_BROKEN, REDIRECT_TEMPORARY, REDIRECT_DOWNGRADE, META_REFRESH,
        BROKEN_INTERNAL_LINKS, BROKEN_EXTERNAL_LINKS, LINKS_TO_REDIRECTS, INTERNAL_NOFOLLOW_LINKS, EMPTY_ANCHOR_TEXT,
        BROKEN_FRAGMENT_LINKS, ORPHAN_PAGE,
        NOINDEX_PAGE, NOFOLLOW_PAGE, BLOCKED_BY_ROBOTS, SNIPPET_BLOCKED, ROBOTS_TXT_UNREACHABLE, ROBOTS_TXT_MISSING,
        CANONICALISED_PAGE, CANONICAL_MISSING, CANONICAL_MULTIPLE, CANONICAL_BROKEN, CANONICAL_TO_REDIRECT,
        CANONICAL_TO_NON_INDEXABLE, CANONICAL_OUTSIDE_HEAD, CANONICAL_RELATIVE, CANONICAL_INVALID, NOINDEX_WITH_CANONICAL,
        SITEMAP_NON_INDEXABLE, NOT_IN_SITEMAP, SITEMAP_ERROR, SITEMAP_MISSING, SITEMAP_URL_OTHER_HOST,
        TITLE_MISSING, TITLE_MULTIPLE, TITLE_TOO_LONG, TITLE_TOO_SHORT, TITLE_DUPLICATE,
        DESCRIPTION_MISSING, DESCRIPTION_MULTIPLE, DESCRIPTION_TOO_LONG, DESCRIPTION_TOO_SHORT, DESCRIPTION_DUPLICATE,
        H1_MISSING, H1_MULTIPLE, H1_DUPLICATE,
        LOW_WORD_COUNT, DUPLICATE_CONTENT, NEAR_DUPLICATE_CONTENT,
        IMAGE_MISSING_ALT, IMAGE_ALT_TOO_LONG, IMAGE_MISSING_DIMENSIONS, IMAGE_BROKEN, IMAGE_TOO_LARGE,
        HREFLANG_INVALID_CODE, HREFLANG_MISSING_SELF, HREFLANG_MISSING_RETURN, HREFLANG_BROKEN_TARGET,
        HREFLANG_TO_NON_INDEXABLE, HREFLANG_DUPLICATE_CODE, HREFLANG_MISSING_X_DEFAULT, HREFLANG_OUTSIDE_HEAD,
        STRUCTURED_DATA_INVALID, STRUCTURED_DATA_MISSING_REQUIRED,
        NO_HTTPS, HTTP_PAGE, MIXED_CONTENT, SECURITY_HEADERS_MISSING,
        URL_UPPERCASE, URL_UNDERSCORES, URL_TOO_LONG,
        SLOW_RESPONSE, LARGE_PAGE,
        VIEWPORT_MISSING, LANG_MISSING, CHARSET_MISSING, OPEN_GRAPH_MISSING
    ];

    private static readonly Dictionary<string, IssueDefinition> BY_ID = All.ToDictionary(issue => issue.Id, StringComparer.Ordinal);

    public static IssueDefinition? Find(string id) => BY_ID.GetValueOrDefault(id);
}
