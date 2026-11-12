using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Engine.Analysis;

/// <summary>
/// Every issue the analysis can report. Ids are stable: they are stored in crawl files and used by the UI, so
/// change a title, description, why or fix freely but never an id. Each issue is reported at most once per URL.
/// </summary>
public static class IssueCatalog
{
    // Response codes
    public static readonly IssueDefinition INTERNAL_CLIENT_ERROR = new("internal-client-error", IssueCategory.ResponseCodes, IssueSeverity.Error,
        "Internal URL returns 4xx",
        """This internal URL, a page or a resource such as an image or script, answered with a 4xx client error such as 404 Not Found, 410 Gone or 403 Forbidden.""",
        """If the URL should work, restore it or 301-redirect it to the closest replacement. If it's gone for good, keep the 404 or 410 and update or remove the links and sitemap entries that point to it.""")
    {
        Why = """Visitors who follow a link here get an error instead of the content. Google doesn't index URLs that return 4xx, and drops ones that were already indexed. A 404 or 410 is the right answer for something removed on purpose, and a 401 or 403 is normal behind a login: then the only problem is the links and sitemap entries that still lead here.""",
        LearnMore = "https://developers.google.com/crawling/docs/troubleshooting/http-status-codes#4xx-client-errors",
        Subject = IssueSubject.Url,
        Grouping = IssueGrouping.ResponseDetail
    };

    public static readonly IssueDefinition INTERNAL_SERVER_ERROR = new("internal-server-error", IssueCategory.ResponseCodes, IssueSeverity.Error,
        "Internal URL returns 5xx",
        """The server answered this internal URL with a 5xx error, such as 500 Internal Server Error or 503 Service Unavailable. Temporary errors (502, 503 and 504) were retried before being recorded.""",
        """Check the server and application logs for this URL at the time of the crawl, fix the cause and recrawl. If many URLs failed together, the server may have been overloaded: recrawl at a slower speed preset to see whether the errors stop.""")
    {
        Why = """Visitors get an error page instead of the content. Google slows down crawling when a site returns 5xx errors, and URLs that keep failing are eventually dropped from its index. A one-off error during a busy crawl can happen, so recheck the URL; errors that repeat or hit many URLs need fixing first.""",
        LearnMore = "https://developers.google.com/crawling/docs/troubleshooting/http-status-codes#5xx-server-errors",
        Subject = IssueSubject.Url,
        Grouping = IssueGrouping.ResponseDetail
    };

    public static readonly IssueDefinition INTERNAL_UNREACHABLE = new("internal-unreachable", IssueCategory.ResponseCodes, IssueSeverity.Error,
        "Internal URL didn't respond",
        """This internal URL gave no response: the request timed out (after 20 seconds by default) or the connection failed, even after retries (2 by default). Both can be changed in the crawl settings.""",
        """Check whether the URL loads from another network, then look for dropped or slow connections at the server, firewall, load balancer or DNS. If it only fails under load, the server needs more capacity or caching; a slower speed preset shows whether the crawl itself caused it.""")
    {
        Why = """Visitors get a browser error or a page that never loads. Google treats timeouts, connection resets and DNS errors like server errors: it slows crawling straight away, can't index URLs it gets nothing from, and removes indexed ones within days if the problem continues.""",
        LearnMore = "https://developers.google.com/crawling/docs/troubleshooting/dns-network-errors",
        Subject = IssueSubject.Url,
        Grouping = IssueGrouping.ResponseDetail
    };

    public static readonly IssueDefinition INTERNAL_BLOCKED = new("internal-blocked", IssueCategory.ResponseCodes, IssueSeverity.Warning,
        "Internal URL blocked by a firewall",
        """A firewall, bot protection service (such as Cloudflare, Akamai, AWS WAF, DataDome or Imperva) or a rate limit (HTTP 429) refused the crawler on this internal URL. The detail names the protection when it's recognised.""",
        """Allowlist the crawler in the firewall or bot protection (by user agent, IP address or a secret header), or crawl at a slower speed preset. Make sure the same rules let verified search engine crawlers through: verify Googlebot by reverse DNS or Google's published IP ranges rather than by user agent, which anyone can fake.""")
    {
        Why = """Nothing on this URL could be checked, so other checks can't see its problems. If the same rules challenge search engine crawlers, they can't crawl it either, and Google slows its crawling of sites that answer with 429. Many protections only challenge automated traffic, so visitors are usually unaffected.""",
        LearnMore = "https://developers.google.com/crawling/docs/crawlers-fetchers/verify-google-requests",
        Subject = IssueSubject.Url,
        Grouping = IssueGrouping.ResponseDetail
    };

    public static readonly IssueDefinition SOFT_404 = new("soft-404", IssueCategory.ResponseCodes, IssueSeverity.Warning,
        "Soft 404",
        """This indexable page returns 200 OK, but its title or H1 reads like an error message (such as "Page not found" or "no longer available") and it has fewer than 300 words.""",
        """Return 404 or 410 for content that's gone, or 301-redirect it to a real replacement. If the page should exist, change the wording that makes it look like an error, and check its content didn't fail to load.""")
    {
        Why = """Google detects pages like this as soft 404s and excludes them from search, but keeps crawling them, which wastes crawling on large sites. Serving an error with 200 also hides real breakage from monitoring that only looks at status codes. If the page is fine and only mentions "not found" in a heading, ignore this.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/troubleshoot-crawling-errors#soft-404-errors",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.None
    };


    // Redirects
    public static readonly IssueDefinition REDIRECT_CHAIN = new("redirect-chain", IssueCategory.Redirects, IssueSeverity.Warning,
        "Redirect chain",
        """This internal URL (linked from a crawled page, listed in a sitemap or the start URL) takes two or more redirects to reach its final URL. The detail gives the number of hops, and says when there are more than Googlebot follows.""",
        """Point each redirect straight at the final URL, and update links and sitemap entries to the final URL so they skip the redirects altogether.""")
    {
        Why = """Every hop is another round trip before the page starts to load, which visitors on slow connections notice. Google follows up to 10 hops and processes only the final URL's content, and its crawl budget guide says long chains have a negative effect on crawling, which matters mainly on large sites. Two hops is rarely a problem in itself, but chains grow with each site change.""",
        LearnMore = "https://developers.google.com/crawling/docs/troubleshooting/http-status-codes#3xx-redirection",
        Subject = IssueSubject.Url,
        Grouping = IssueGrouping.RelatedUrl
    };

    public static readonly IssueDefinition REDIRECT_LOOP = new("redirect-loop", IssueCategory.Redirects, IssueSeverity.Error,
        "Redirect loop",
        """Following this internal URL's redirects leads back to a URL already passed, so they never reach a page.""",
        """Follow the hops, find the rule that sends the URL back, and change it so the chain ends at a URL that returns 200.""")
    {
        Why = """Browsers give up with an error (Chrome's ERR_TOO_MANY_REDIRECTS), so visitors never see the content and search engines have nothing to index. Loops usually come from two rules undoing each other, such as one adding a trailing slash and another removing it, or an HTTPS redirect on a server behind a CDN or proxy that can't see the visitor is already on HTTPS. Fix this first.""",
        LearnMore = "https://developers.google.com/crawling/docs/troubleshooting/http-status-codes#3xx-redirection",
        Subject = IssueSubject.Url,
        Grouping = IssueGrouping.None
    };

    public static readonly IssueDefinition REDIRECT_BROKEN = new("redirect-broken", IssueCategory.Redirects, IssueSeverity.Error,
        "Redirect leads to a broken URL",
        """This internal URL redirects, but the redirects end at a URL that returns a 4xx or 5xx error or doesn't respond, or the redirect has no usable Location header.""",
        """Point the redirect at a working replacement or fix the target. If there's no replacement, let the old URL return 404 or 410 rather than redirect to an error.""")
    {
        Why = """Visitors land on an error instead of the page the redirect promised. Google ignores a redirecting URL's own content and processes only the final URL's, so there's nothing to index, and links to the old URL lead nowhere.""",
        LearnMore = "https://developers.google.com/crawling/docs/troubleshooting/http-status-codes#3xx-redirection",
        Subject = IssueSubject.Url,
        Grouping = IssueGrouping.ResponseDetail
    };

    public static readonly IssueDefinition REDIRECT_TEMPORARY = new("redirect-temporary", IssueCategory.Redirects, IssueSeverity.Notice,
        "Temporary redirect",
        "This internal URL redirects with a temporary status code: 302, 303 or 307.",
        "If the move is permanent, change the redirect to 301 or 308; if it's temporary, leave it.")
    {
        Why = """Google doesn't treat a temporary redirect as a sign that the target should replace the old URL (at most it's a weak signal), so the old URL can stay in search results. That's correct when the move really is temporary, such as a short-term campaign page or a login redirect. For a permanent move it delays the switch to the new URL.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/301-redirects#overview-of-redirect-types",
        Subject = IssueSubject.Url,
        Grouping = IssueGrouping.ResponseDetail
    };

    public static readonly IssueDefinition REDIRECT_DOWNGRADE = new("redirect-downgrade", IssueCategory.Redirects, IssueSeverity.Error,
        "Redirect from HTTPS to HTTP",
        "This internal HTTPS URL redirects to an HTTP URL.",
        """Redirect to the HTTPS version of the target. The cause is usually an old rule or a proxy that rewrites the scheme, so check the server and CDN redirect rules.""")
    {
        Why = """Visitors who started on a secure URL end on an insecure one that browsers label "Not secure". Google normally prefers HTTPS URLs as canonical, but says HTTPS-to-HTTP redirects make it prefer the HTTP version very strongly, which even HSTS can't override.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/consolidate-duplicate-urls#prefer-https-over-http-for-canonical-urls",
        Subject = IssueSubject.Url,
        Grouping = IssueGrouping.RelatedUrl
    };

    public static readonly IssueDefinition META_REFRESH = new("meta-refresh", IssueCategory.Redirects, IssueSeverity.Warning,
        "Meta refresh",
        """The page has a <meta http-equiv="refresh"> tag that sends visitors to another URL or reloads the page, at once or after a delay. The detail gives the target and the delay.""",
        """Replace it with a 301 redirect on the server (or 302 if the move is temporary). If the page reloads itself to show new content, update it with script instead, or let visitors refresh.""")
    {
        Why = """Google treats an instant meta refresh as a permanent redirect and a delayed one as temporary, but recommends a server-side redirect wherever you can set one up; that's faster too, as the browser doesn't load the page first. A timed redirect or reload also moves content away while people are reading, which WCAG counts as a failure. A refresh on old URLs where the server can't redirect is an acceptable fallback.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/301-redirects#metarefresh",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.PageDetail
    };


    // Links
    public static readonly IssueDefinition BROKEN_INTERNAL_LINKS = new("broken-internal-links", IssueCategory.Links, IssueSeverity.Error,
        "Links to broken internal URLs",
        """The page links to internal URLs that return a 4xx or 5xx error or don't respond, directly or at the end of their redirects (or whose redirects loop). The detail counts them and says where the first goes wrong.""",
        """Update each link to the working URL, remove it, or restore the target. For a target that moved, link to its new address rather than to a redirect.""")
    {
        Why = """Visitors who click hit an error, and search engines spend crawls on URLs they won't index, as Google doesn't index URLs that return 4xx. Links in a template, such as the menu or footer, repeat on every page, so fix those first.""",
        LearnMore = "https://developers.google.com/crawling/docs/troubleshooting/http-status-codes#4xx-client-errors",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.RelatedUrl
    };

    public static readonly IssueDefinition BROKEN_EXTERNAL_LINKS = new("broken-external-links", IssueCategory.Links, IssueSeverity.Warning,
        "Links to broken external URLs",
        """The page links to URLs on other sites that return a 4xx or 5xx error or don't respond, directly or after redirects. Statuses many sites send only to crawlers (401, 403, 405, 429, 503 and LinkedIn's 999) don't count.""",
        "Update each link to the resource's current address or an archived copy, or remove it.")
    {
        Why = """Visitors who click hit a dead end, which makes the page look neglected and its references unreliable. Google doesn't document broken outbound links as a ranking factor, so this is about visitors. Some sites refuse crawlers in other ways, so open a few in a browser before editing.""",
        LearnMore = null,
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.RelatedUrl
    };

    public static readonly IssueDefinition LINKS_TO_REDIRECTS = new("links-to-redirects", IssueCategory.Links, IssueSeverity.Notice,
        "Links to internal redirects",
        """The page links to internal URLs that redirect (by HTTP or an instant meta refresh) to a working page.""",
        """Change the links to point straight at the final URL. Keep the redirects for bookmarks and other sites' links.""")
    {
        Why = """Each click costs visitors an extra round trip before the page starts to load, and each crawl costs search engines an extra request. Google's advice is to link to the canonical URL within your site, which a redirecting URL isn't. A few are harmless; it's worth fixing in templates, where the same link repeats on every page.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/consolidate-duplicate-urls#best-practices",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.RelatedUrl
    };

    public static readonly IssueDefinition INTERNAL_NOFOLLOW_LINKS = new("internal-nofollow-links", IssueCategory.Links, IssueSeverity.Warning,
        "Nofollow internal links",
        """The page has rel="nofollow" on links to the site's own pages. Links to resources, such as images opened in a gallery lightbox, don't count.""",
        """Remove rel="nofollow" from internal links. Block URLs that shouldn't be crawled in robots.txt, or add noindex (without a robots.txt block) to pages that shouldn't appear in search.""")
    {
        Why = """Google treats nofollow as a hint not to crawl the target through that link or use the link for ranking, so pages linked only this way may be found late or not at all. For links within your own site, Google's advice is to use robots.txt to keep URLs from being crawled, and noindex to keep pages out of search, instead of nofollow. Nofollow on login or basket links is common and mostly harmless, but achieves little.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/qualify-outbound-links#nofollow",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.RelatedUrl
    };

    public static readonly IssueDefinition EMPTY_ANCHOR_TEXT = new("empty-anchor-text", IssueCategory.Links, IssueSeverity.Notice,
        "Internal links without anchor text",
        """The page has internal links with nothing to read: no text, no alt text on an image inside, and no aria-label, aria-labelledby or title. Links in the <head>, such as rel="amphtml", don't count.""",
        """Add visible text, alt text on the image inside the link, or an aria-label on icon links. For a logo that links home, alt text with the site name is enough.""")
    {
        Why = """Screen readers announce these as just "link" or read out the URL, so their users can't tell where they go. Google uses anchor text to understand the page a link points to, and the alt text of a linked image as its anchor text, so an empty link tells it nothing. Icon-only links in a template, such as a logo or social media icons, usually account for most of them.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/links-crawlable#anchor-text-placement",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.RelatedUrl
    };

    public static readonly IssueDefinition BROKEN_FRAGMENT_LINKS = new("broken-fragment-links", IssueCategory.Links, IssueSeverity.Notice,
        "Links to missing page sections",
        """The page links to a #fragment, on itself or another crawled page, that matches no id or named anchor on the target page. App routes (#/, #!), app state (fragments with =, : or &) and #top aren't checked.""",
        """Correct the fragment in the link, or add the matching id to the section on the target page; ids on headings are often lost when content is edited or regenerated.""")
    {
        Why = """Visitors land at the top of the page instead of the section the link promised. Google generally ignores URL fragments, so this is about visitors only. Ids added by JavaScript after the page loads can make a working link look broken, so check it in a browser first.""",
        LearnMore = "https://developer.mozilla.org/en-US/docs/Web/URI/Reference/Fragment",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.RelatedUrl
    };

    public static readonly IssueDefinition ORPHAN_PAGE = new("orphan-page", IssueCategory.Links, IssueSeverity.Warning,
        "Orphan page",
        """This indexable page is listed in a sitemap, but no crawled page links to it, not even with a nofollow link or through a redirect. It's only checked when the crawl finished.""",
        """If the page matters, link to it from relevant pages and from its section's listing. If it doesn't, remove it from the sitemap, then redirect or remove the page.""")
    {
        Why = """Visitors can't reach it by browsing, and search engines only know it from the sitemap. Google uses links to find pages and judge their relevance, and says every page you care about should have a link from at least one other page on your site. Often it's an old page left in the sitemap, or one linked only from places a crawler can't follow, such as forms or scripts.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/links-crawlable#internal-links",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.None
    };


    // Indexability
    public static readonly IssueDefinition NOINDEX_PAGE = new("noindex-page", IssueCategory.Indexability, IssueSeverity.Notice,
        "Noindex page",
        """The page has a noindex (or none) directive in a robots meta tag or an X-Robots-Tag header, which asks search engines to keep it out of their results.""",
        """Check that it's intended. If the page should appear in search, remove the noindex from the meta tag or header, and make sure robots.txt doesn't block the URL, so search engines can see the change.""")
    {
        Why = """Google drops a noindex page from its results entirely, even if other sites link to it. That's right for thank-you pages, internal search results, account pages and staging copies. On a page that should rank, a leftover noindex from a launch or a plugin setting is a common reason it vanishes from search.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/block-indexing",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.None
    };

    public static readonly IssueDefinition NOFOLLOW_PAGE = new("nofollow-page", IssueCategory.Indexability, IssueSeverity.Warning,
        "Nofollow page",
        """The page has a nofollow (or none) directive in a robots meta tag or an X-Robots-Tag header, which asks search engines not to follow any of its links.""",
        """Remove nofollow from the robots meta tag or X-Robots-Tag header. To qualify individual links to other sites, use rel="nofollow", "sponsored" or "ugc" on those links instead.""")
    {
        Why = """Search engines that honour it won't use the page's links to discover the pages they point to, so pages linked only from here may go unfound. A page-wide nofollow is rarely intended on a normal page; it's usually left over from a staging setup or set by a plugin.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/robots-meta-tag#nofollow",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.None
    };

    public static readonly IssueDefinition BLOCKED_BY_ROBOTS = new("blocked-by-robots", IssueCategory.Indexability, IssueSeverity.Notice,
        "Blocked by robots.txt",
        """robots.txt disallows this internal URL, so the crawler didn't fetch it. If it was refused because robots.txt itself couldn't be read, the detail says so.""",
        """If the URL should be crawled, change or remove the Disallow rule that matches it. If it's meant to be blocked, avoid linking to it from crawlable pages where you can, and if it must stay out of search, allow crawling and use noindex instead.""")
    {
        Why = """Search engines that obey robots.txt can't read the page, so its content, links and any noindex on it go unseen, yet Google can still index the URL without its content, with no description, if links point to it. Blocking is right for URLs with no search value, such as baskets, internal search or filter combinations. It's a problem when a broad rule catches pages you want in search, or the CSS and JavaScript that pages need to render.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/robots/intro",
        Subject = IssueSubject.Url,
        Grouping = IssueGrouping.ResponseDetail
    };

    public static readonly IssueDefinition SNIPPET_BLOCKED = new("snippet-blocked", IssueCategory.Indexability, IssueSeverity.Notice,
        "Search snippet disabled",
        """This indexable page has a nosnippet or max-snippet:0 directive in a robots meta tag or X-Robots-Tag header.""",
        """Remove the directive if it isn't intended. To keep only part of the page out of snippets, use the data-nosnippet attribute on that part, or a max-snippet length instead of 0.""")
    {
        Why = """Google shows the page in results without a text snippet or video preview, and doesn't use its content as direct input for AI Overviews or AI Mode. That can be intended, for example for sensitive content, but a result with no description tells searchers less about the page.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/robots-meta-tag#nosnippet",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.PageDetail
    };

    public static readonly IssueDefinition ROBOTS_TXT_UNREACHABLE = new("robots-txt-unreachable", IssueCategory.Indexability, IssueSeverity.Error,
        "robots.txt unavailable",
        """robots.txt on the site's main origin (the start URL's, or the home page's after redirects) returned a 5xx error or 429, or didn't respond. Reported once, on the home page.""",
        """Make /robots.txt answer quickly with 200 and your rules, or with 404 if you don't need any. Check that the firewall, CDN or rate limiting doesn't treat requests for it differently from pages.""")
    {
        Why = """Google treats a robots.txt it can't fetch as a server error: it stops crawling the whole site for the first 12 hours, then uses its last good copy for up to 30 days (or assumes no restrictions if it has none). If it's still failing after that and the site is otherwise up, Google acts as if there were no robots.txt. Until it's fixed, new and changed pages aren't crawled, so fix this first.""",
        LearnMore = "https://developers.google.com/crawling/docs/robots-txt/robots-txt-spec#http-status-codes",
        Subject = IssueSubject.Url,
        Grouping = IssueGrouping.None,
        SiteWide = true,
        ReportedOnce = true
    };

    public static readonly IssueDefinition ROBOTS_TXT_UNREACHABLE_OTHER_ORIGIN = new("robots-txt-unreachable-other-origin", IssueCategory.Indexability, IssueSeverity.Warning,
        "robots.txt unavailable on another of the site's origins",
        """robots.txt on another of the site's origins, such as a subdomain or the http:// version, returned a 5xx error or 429, or didn't respond; the detail names the origin. Origins reached only by the crawler's own probe of the site's other versions aren't included.""",
        """Make robots.txt on that origin return 200, or 404 if it needs no rules. If the origin shouldn't exist, stop linking to it.""")
    {
        Why = """Each origin (scheme, host and port) has its own robots.txt, so only that origin is affected: Google stops crawling it for 12 hours, then relies on its last good copy for up to 30 days. The main site carries on as normal. It matters if pages or files you want in search live there, or if that origin's redirects to the main site haven't been picked up yet.""",
        LearnMore = "https://developers.google.com/crawling/docs/robots-txt/robots-txt-spec#file-location-and-range-of-validity",
        Subject = IssueSubject.Url,
        Grouping = IssueGrouping.None,
        SiteWide = true,
        ReportedOnce = true
    };

    public static readonly IssueDefinition ROBOTS_TXT_MISSING = new("robots-txt-missing", IssueCategory.Indexability, IssueSeverity.Notice,
        "No robots.txt",
        """robots.txt returned a 4xx error other than 429 (usually 404, sometimes 401 or 403 from a firewall), or redirected too many times, on the main origin or another of the site's origins. Reported on the home page; the detail lists the origins.""",
        """Add a robots.txt at the origin's root, even if it only holds a Sitemap: line with your XML sitemap's full URL. If a firewall answers it with 401 or 403, let it through.""")
    {
        Why = """Crawling isn't affected: Google treats a missing robots.txt as having no restrictions, so everything can be crawled. What's missing is a Sitemap: line, which Google, Bing and other search engines read to find your sitemaps, and a way to keep crawlers out of URLs with no search value should you need one. A 401 or 403 meant to keep crawlers out has the opposite effect.""",
        LearnMore = "https://developers.google.com/crawling/docs/robots-txt/create-robots-txt",
        Subject = IssueSubject.Url,
        Grouping = IssueGrouping.None,
        ReportedOnce = true
    };

    public static readonly IssueDefinition SITE_HIDDEN_FROM_SEARCH = new("site-hidden-from-search", IssueCategory.Indexability, IssueSeverity.Error,
        "Most pages hidden from search",
        """The home page is noindex, and so are more than half of the site's pages, so most of the site can't appear in search results. The home page is the page the start URL leads to, after its redirects; while it can be indexed, nothing is reported, however many other pages are noindex. The detail gives the numbers, as in "212 of 240 pages are noindex": the noindex pages, out of the pages the crawl read less the canonicalised ones. A canonicalised page (one that isn't noindex and whose canonical names another URL) is a variant of the page it names, so it isn't in the total. A canonical that leads back to the page, to a broken URL, or to the same page on another version of the site (www or not, http or https) doesn't make a variant. Reported once, on the home page.""",
        """Find what sets noindex on every page: a "discourage search engines" setting or a staging configuration left on, a robots meta tag in the template, or an X-Robots-Tag header from the server or CDN. Remove it from the pages that should appear in search, then crawl again.""")
    {
        Why = """Google drops a noindex page from its results, so most of this site can't be found in search. It's nearly always a launch mistake: a staging site's noindex or a "discourage search engines" setting left on. If the pages are meant to stay out, as on a private site or a staging copy, check the noindex pages and ignore this.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/block-indexing",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.None,
        SiteWide = true,
        ReportedOnce = true
    };


    // Canonicals
    public static readonly IssueDefinition CANONICALISED_PAGE = new("canonicalised-page", IssueCategory.Canonicals, IssueSeverity.Notice,
        "Canonicalised page",
        """The page's canonical (in the <head> or an HTTP Link header) names a different URL, so the page asks to be treated as a duplicate of it. Canonicals that lead back to this page or to a broken URL don't count.""",
        """Check the canonical is intended. If this page should rank, make its canonical point to itself; if it's a true duplicate, link to the canonical URL rather than this one within the site.""")
    {
        Why = """Google treats a canonical as a strong hint, not a rule: it usually indexes the named URL and folds this page's signals into it, but can choose differently if other signals disagree. That's right for parameter, sorting and print versions of a page. It's a problem when the page has its own content and should rank, as when a template points every canonical at the home page or the first page of a series.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/canonicalization",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.RelatedUrl
    };

    public static readonly IssueDefinition CANONICAL_MISSING = new("canonical-missing", IssueCategory.Canonicals, IssueSeverity.Notice,
        "Missing canonical",
        """This indexable page has no canonical, neither a <link rel="canonical"> in the <head> nor an HTTP Link header.""",
        """Add <link rel="canonical" href="..."> to the <head> with the page's own absolute URL; most CMSs and SEO plugins can do this for every page.""")
    {
        Why = """Google doesn't require one: without it, Google picks the best URL itself when the same content is reachable at several addresses, such as with tracking parameters or with and without a trailing slash. A canonical pointing to the page itself makes your preference explicit, and Google recommends one on canonical pages. Low priority on its own, but it helps most on sites with many URL variants.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/consolidate-duplicate-urls#rel-canonical-link-method",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.None
    };

    public static readonly IssueDefinition CANONICAL_MULTIPLE = new("canonical-multiple", IssueCategory.Canonicals, IssueSeverity.Warning,
        "Conflicting canonicals",
        """The page declares two or more different canonical URLs, in the <head>, the HTTP Link header, or across both.""",
        """Keep one canonical: find which plugin, theme or server rule adds each and turn off the wrong one. A Link header usually comes from the server, CDN or CMS, not the template.""")
    {
        Why = """Google has said that when a page specifies more than one canonical, it will likely ignore all of them, so the page effectively has none and Google chooses on its own. It usually comes from two systems, such as a theme and an SEO plugin, or the HTML and a server or CDN header, each adding its own.""",
        LearnMore = "https://developers.google.com/search/blog/2013/04/5-common-mistakes-with-relcanonical",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.None
    };

    public static readonly IssueDefinition CANONICAL_BROKEN = new("canonical-broken", IssueCategory.Canonicals, IssueSeverity.Error,
        "Canonical points to a broken URL",
        """The page's canonical names a URL that returns a 4xx or 5xx error or doesn't respond, directly or at the end of its redirects (or its redirects loop).""",
        """Point the canonical at the working URL you want indexed, usually this page's own URL, and check templates that build canonicals from a hard-coded host or path.""")
    {
        Why = """A canonical has to name a working page: Google's own checklist is to make sure the target exists and isn't an error or soft 404. One that leads to an error can't take this page's place, so expect search engines to disregard it and judge this page on its own. The cause is usually a stale URL, such as a staging host, an old URL structure or a mistyped path.""",
        LearnMore = "https://developers.google.com/search/blog/2013/04/5-common-mistakes-with-relcanonical",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.RelatedUrl
    };

    public static readonly IssueDefinition CANONICAL_TO_REDIRECT = new("canonical-to-redirect", IssueCategory.Canonicals, IssueSeverity.Warning,
        "Canonical points to a redirect",
        """The page's canonical names a URL that redirects. The detail gives the URL the redirects end at, or says that they lead back to this page.""",
        """Change the canonical to the redirect's final URL, which the detail gives; when the redirect leads back here, that's this page's own URL.""")
    {
        Why = """A canonical should name the final URL you want indexed. Naming a redirect sends search engines two signals that disagree: the canonical says to index the named URL, and its redirect says to index another. When the redirect leads back to this page, such as a canonical to /page on /page/ when /page redirects to /page/, this page most likely stays the indexed one, but the canonical is still wrong.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/consolidate-duplicate-urls",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.RelatedUrl
    };

    public static readonly IssueDefinition CANONICAL_TO_NON_INDEXABLE = new("canonical-to-non-indexable", IssueCategory.Canonicals, IssueSeverity.Warning,
        "Canonical points to a non-indexable page",
        """The page's canonical names another URL on the site that can't be indexed itself: it's noindex, canonicalised to yet another URL, or blocked by robots.txt or a firewall.""",
        """Point the canonical at the final, indexable URL. If the target isn't meant to be indexed, this page probably shouldn't name it as canonical.""")
    {
        Why = """A canonical asks search engines to index the named URL instead of this one, so if that URL is noindex, blocked or points elsewhere in turn, the signals contradict each other and the outcome is up to Google. Google's own checklist includes making sure the canonical target isn't noindex. Canonical chains (A to B to C) usually come from content that moved more than once.""",
        LearnMore = "https://developers.google.com/search/blog/2013/04/5-common-mistakes-with-relcanonical",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.RelatedUrl
    };

    public static readonly IssueDefinition CANONICAL_OUTSIDE_HEAD = new("canonical-outside-head", IssueCategory.Canonicals, IssueSeverity.Warning,
        "Canonical outside <head>",
        """The page's only canonical links are in the <body> as browsers parse it, and there's no canonical HTTP Link header.""",
        """Move the canonical into the <head>, as early as you can, and remove the element that ends the head early (or move it into the body).""")
    {
        Why = """Google only accepts rel="canonical" in the <head>, so the page has no canonical in effect. Often the tag is in the head in the source, but an element that doesn't belong there, such as an <img>, a <div> or stray text from a script or plugin, ends the head early and pushes everything after it into the body. The same break can push other head tags, such as hreflang, out too.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/consolidate-duplicate-urls#rel-canonical-link-method",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.None
    };

    public static readonly IssueDefinition CANONICAL_RELATIVE = new("canonical-relative", IssueCategory.Canonicals, IssueSeverity.Notice,
        "Relative canonical URL",
        """The canonical in the HTML is a relative URL, such as /page or //example.com/page, instead of a full URL with https:// and the host.""",
        "Write the canonical as a full URL, such as https://www.example.com/page.")
    {
        Why = """Google supports relative canonicals, but recommends absolute URLs because relative ones can cause problems later, for example when a testing copy of the site gets crawled and its canonicals point at itself. A mistyped relative path (page instead of /page) also resolves against the current folder. Low priority if the canonicals resolve to the right URLs.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/consolidate-duplicate-urls#rel-canonical-link-method",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.PageDetail, FaultOf = IssueFaults.RelativeCanonical
    };

    public static readonly IssueDefinition CANONICAL_INVALID = new("canonical-invalid", IssueCategory.Canonicals, IssueSeverity.Warning,
        "Invalid canonical URL",
        """A canonical in the <head> or an HTTP Link header has a value that isn't a valid http or https URL, such as an empty href, a template placeholder or a malformed address.""",
        "Fix the template so the canonical holds the page's absolute URL, or remove the broken tag.")
    {
        Why = """Search engines can't resolve it, so it's ignored: if it's the page's only canonical, the page has none in effect. It's usually a template variable that didn't fill in.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/consolidate-duplicate-urls#rel-canonical-link-method",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.PageDetail, FaultOf = IssueFaults.InvalidCanonical
    };

    public static readonly IssueDefinition NOINDEX_WITH_CANONICAL = new("noindex-with-canonical", IssueCategory.Canonicals, IssueSeverity.Warning,
        "Noindex page with a canonical to another URL",
        """The page is noindex and its canonical names a different URL, one that doesn't lead back to this page or to a broken URL.""",
        """If the page duplicates the other URL, remove the noindex and keep the canonical. If it should stay out of search, keep the noindex and make the canonical point to the page itself, or remove it.""")
    {
        Why = """The two signals say different things: noindex asks for the page to be dropped from search, while the canonical says it's a duplicate whose signals belong to the other URL. Google hasn't documented which wins, and its guidance is to use rel="canonical", not noindex, to choose between duplicates on one site, since noindex removes the page from search entirely.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/consolidate-duplicate-urls#best-practices",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.RelatedUrl
    };


    // Sitemaps
    public static readonly IssueDefinition SITEMAP_NON_INDEXABLE = new("sitemap-non-indexable", IssueCategory.Sitemaps, IssueSeverity.Warning,
        "Non-indexable URL in sitemap",
        """A sitemap lists this internal URL, but it can't be indexed as listed: it redirects, returns a 4xx or 5xx error, is blocked by robots.txt, is noindex or names another URL as canonical. URLs the crawler couldn't check, because of timeouts or firewall blocks, aren't included.""",
        """Remove the URL from the sitemap, or replace it with the final, indexable URL, such as the redirect target or the canonical. If the sitemap is generated, fix its filter rather than editing the file.""")
    {
        Why = """A sitemap should list the URLs you want in search results, and Google treats every URL in it as a suggested canonical. Listing URLs that redirect, fail or ask not to be indexed contradicts your other signals and spends crawls on URLs that won't be indexed. It's usually a sitemap generator that doesn't filter by status, noindex or canonical.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/sitemaps/build-sitemap#general-guidelines",
        Subject = IssueSubject.Url,
        Grouping = IssueGrouping.ResponseDetail
    };

    public static readonly IssueDefinition NOT_IN_SITEMAP = new("not-in-sitemap", IssueCategory.Sitemaps, IssueSeverity.Notice,
        "Indexable page not in a sitemap",
        """This indexable page isn't in any sitemap the crawl read. It's only checked when the crawl finished and every declared sitemap could be read.""",
        """Add the page to your XML sitemap, ideally by making sure the generator includes every indexable page type.""")
    {
        Why = """Search engines can still find it through links, and on a small, well-linked site that's enough: Google says sites of about 500 pages or fewer that are comprehensively linked may not need a sitemap. On large or fast-changing sites, a complete sitemap helps new and updated pages get discovered sooner. Leaving out some page types, such as filtered or paginated listings, can be deliberate.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/sitemaps/overview#do-i-need-a-sitemap",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.None
    };

    public static readonly IssueDefinition SITEMAP_ERROR = new("sitemap-error", IssueCategory.Sitemaps, IssueSeverity.Error,
        "Sitemap can't be read",
        """A sitemap listed in robots.txt, a sitemap index or the crawl settings returned an error (including 404), didn't respond, or isn't valid sitemap XML. Reported on the home page; the detail lists up to three, with their errors.""",
        """Open the sitemap URL to see the error, then fix the file or the server: it must be valid XML, at most 50 MB uncompressed and 50,000 URLs. If the sitemap no longer exists, remove its reference from robots.txt or the sitemap index.""")
    {
        Why = """Search engines get no URLs from a sitemap they can't fetch or parse: Google keeps retrying for a few days, then stops trying that URL. Well-linked pages are still found, but what the sitemap adds, such as quicker discovery of new and deep pages, is lost. A referenced sitemap that returns 404 is usually stale and only needs removing.""",
        LearnMore = "https://support.google.com/webmasters/answer/7451001",
        Subject = IssueSubject.Url,
        Grouping = IssueGrouping.None,
        ReportedOnce = true
    };

    public static readonly IssueDefinition SITEMAP_MISSING = new("sitemap-missing", IssueCategory.Sitemaps, IssueSeverity.Notice,
        "No XML sitemap found",
        """The crawl found no sitemap: none is listed in robots.txt or the crawl settings, and there's none at /sitemap.xml. Reported on the home page, only when sitemap discovery was on and the crawl finished.""",
        """Publish an XML sitemap of your indexable pages (most CMSs can generate one) and add a Sitemap: line with its full URL to robots.txt.""")
    {
        Why = """A sitemap isn't required: Google finds pages through links, and a small, well-linked site may not need one. It helps most on large sites, new sites with few links from elsewhere, and sites with pages that links don't reach. A sitemap at another path that's only submitted in Search Console isn't visible to this crawl, so this may not be a real gap.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/sitemaps/overview#do-i-need-a-sitemap",
        Subject = IssueSubject.Url,
        Grouping = IssueGrouping.None,
        ReportedOnce = true
    };

    public static readonly IssueDefinition SITEMAP_URL_OTHER_HOST = new("sitemap-url-other-host", IssueCategory.Sitemaps, IssueSeverity.Warning,
        "Sitemap lists a URL on another host",
        """A sitemap lists this URL, but it's on a different host from the sitemap: another site, or another of this site's hosts, such as www in a sitemap on the bare domain. The detail says how the URL answered.""",
        """List the URL as it is on the sitemap's own host now (usually where it redirects), or remove the entry. For a sitemap that deliberately covers several hosts, set up cross-submission through Search Console or robots.txt.""")
    {
        Why = """The sitemaps protocol only accepts URLs on the sitemap's own host and drops the rest, so the page it was meant for isn't submitted. Google makes an exception for cross-submission: when every site involved is verified in Search Console and the sitemap is submitted there, or the other host's robots.txt references the sitemap. It's common after a move to or from www.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/sitemaps/build-sitemap#cross-submit",
        Subject = IssueSubject.Url,
        Grouping = IssueGrouping.ResponseDetail
    };


    // Titles
    public static readonly IssueDefinition TITLE_MISSING = new("title-missing", IssueCategory.Titles, IssueSeverity.Error,
        "Missing title",
        "This indexable page has no <title> element, or its first one is empty.",
        """Add a unique, descriptive <title> in the <head>. If a template sets it, check the field that fills it isn't empty for this page type.""")
    {
        Why = """Google uses the <title> as one of its main sources for the page's title link in search results, and browsers show it in tabs and bookmarks. Without one, Google builds the title link from headings, anchor text and other text on the page, which may not say what you'd choose. Google asks for every page to have one, so fix this first.""",
        LearnMore = "https://developers.google.com/search/docs/appearance/title-link#page-titles",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.None
    };

    public static readonly IssueDefinition TITLE_MULTIPLE = new("title-multiple", IssueCategory.Titles, IssueSeverity.Warning,
        "Multiple titles",
        """This indexable page has more than one <title> element (titles inside SVG images don't count). The detail says how many.""",
        "Keep a single <title> in the <head> and turn off the source of the others.")
    {
        Why = """HTML allows only one per page, and browsers use the first. Which one search engines use isn't documented, so the title you wrote may not be the one considered. A second title usually means two systems, such as a theme and an SEO plugin, are both writing the page's metadata.""",
        LearnMore = "https://html.spec.whatwg.org/multipage/semantics.html#the-title-element",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.None
    };

    public static readonly IssueDefinition TITLE_TOO_LONG = new("title-too-long", IssueCategory.Titles, IssueSeverity.Notice,
        "Title too long",
        """The page's title is longer than 60 characters (the default, which can be changed in the analysis settings), counting full-width characters such as Chinese, Japanese and Korean, and emoji, as two.""",
        """Shorten the title or move the important words to the front; cut repeated words and long brand suffixes.""")
    {
        Why = """Google sets no length limit for titles, but truncates title links to fit the device width, so the end of a long title may not show; 60 characters is a rough guide to what fits, not a rule. Google may also rewrite titles that are long-winded or stuffed with keywords. With the important words first, a cut-off rarely matters.""",
        LearnMore = "https://developers.google.com/search/docs/appearance/title-link#page-titles",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.None
    };

    public static readonly IssueDefinition TITLE_TOO_SHORT = new("title-too-short", IssueCategory.Titles, IssueSeverity.Notice,
        "Title too short",
        """The page's title is shorter than 30 characters (the default, which can be changed in the analysis settings), counting full-width characters and emoji as two.""",
        """Describe the page in the title: add the specific topic, product or place and, if you like, the site name.""")
    {
        Why = """Short isn't wrong: a brand or product name can be exactly right. But very short titles are often vague, such as "Home", "Products" or "Page 2", which Google's guidance warns against because they don't tell searchers what the page is or set it apart from similar pages.""",
        LearnMore = "https://developers.google.com/search/docs/appearance/title-link#page-titles",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.None
    };

    public static readonly IssueDefinition TITLE_DUPLICATE = new("title-duplicate", IssueCategory.Titles, IssueSeverity.Warning,
        "Duplicate title",
        """Another indexable page has the same title, ignoring case and spacing. The detail says how many other pages share it.""",
        """Give each page a title that says what's specific about it. If the pages really are the same, consolidate them with a redirect or canonical instead.""")
    {
        Why = """Google asks for distinct titles on every page, because identical ones make it impossible for searchers to tell pages apart, and it may rewrite title links that repeat boilerplate. Duplicate titles often mean duplicate pages, or a template that leaves out the distinguishing detail, such as the product, place or page number.""",
        LearnMore = "https://developers.google.com/search/docs/appearance/title-link#page-titles",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.RelatedUrl
    };


    // Meta descriptions
    public static readonly IssueDefinition DESCRIPTION_MISSING = new("description-missing", IssueCategory.MetaDescriptions, IssueSeverity.Notice,
        "Missing meta description",
        "This indexable page has no meta description, or its first one is empty.",
        """Write a one- or two-sentence summary specific to the page in <meta name="description" content="...">. On large sites, generate descriptions from each page's data, such as product name, price and key features.""")
    {
        Why = """Google writes most snippets from the page's content, but uses the meta description when it describes the page better, and says high-quality descriptions can improve the quality and quantity of search traffic. Without one, the snippet is whatever text Google picks. Descriptions don't affect ranking, so prioritise important pages, such as the home page and key landing pages.""",
        LearnMore = "https://developers.google.com/search/docs/appearance/snippet#meta-descriptions",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.None
    };

    public static readonly IssueDefinition DESCRIPTION_MULTIPLE = new("description-multiple", IssueCategory.MetaDescriptions, IssueSeverity.Warning,
        "Multiple meta descriptions",
        "This indexable page has more than one meta description. The detail says how many.",
        "Keep a single meta description and turn off the other source.")
    {
        Why = """HTML allows only one per page, and which one search engines use isn't documented, so you can't be sure the one you wrote is the one considered for the snippet. It usually comes from a theme and an SEO plugin both adding one.""",
        LearnMore = "https://html.spec.whatwg.org/multipage/semantics.html#meta-description",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.None
    };

    public static readonly IssueDefinition DESCRIPTION_TOO_LONG = new("description-too-long", IssueCategory.MetaDescriptions, IssueSeverity.Notice,
        "Meta description too long",
        """The page's meta description is longer than 155 characters (the default, which can be changed in the analysis settings), counting full-width characters and emoji as two.""",
        """Put what matters in the first sentence and trim the rest, so the description still works if it's cut.""")
    {
        Why = """There's no length limit, but Google truncates snippets to fit the device width, so the end of a long description may not show, and it often builds the snippet from page text anyway. With the key point first, the cut rarely matters.""",
        LearnMore = "https://developers.google.com/search/docs/appearance/snippet#meta-descriptions",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.None
    };

    public static readonly IssueDefinition DESCRIPTION_TOO_SHORT = new("description-too-short", IssueCategory.MetaDescriptions, IssueSeverity.Notice,
        "Meta description too short",
        """The page's meta description is shorter than 70 characters (the default, which can be changed in the analysis settings), counting full-width characters and emoji as two.""",
        "Expand it into a specific one- or two-sentence summary of what the page offers and for whom.")
    {
        Why = """There's no minimum, but a very short description rarely summarises the page, and Google uses the meta description only when it describes the page better than the page's own text. A short one is fine if it says everything that matters.""",
        LearnMore = "https://developers.google.com/search/docs/appearance/snippet#use-quality-descriptions",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.None
    };

    public static readonly IssueDefinition DESCRIPTION_DUPLICATE = new("description-duplicate", IssueCategory.MetaDescriptions, IssueSeverity.Warning,
        "Duplicate meta description",
        """Another indexable page has the same meta description, ignoring case and spacing. The detail says how many other pages share it.""",
        "Write a page-specific description, or generate one from each page's own data.")
    {
        Why = """Google says identical descriptions aren't helpful when individual pages appear in search, and recommends a unique one per page, with site-level wording only on the home page and other aggregation pages. Repeated descriptions usually come from a template default.""",
        LearnMore = "https://developers.google.com/search/docs/appearance/snippet#create-unique-descriptions-for-each-page-on-your-site",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.RelatedUrl
    };


    // Headings
    public static readonly IssueDefinition H1_MISSING = new("h1-missing", IssueCategory.Headings, IssueSeverity.Notice,
        "Missing H1",
        """This indexable page has no H1 heading, or its H1s are empty (the alt text of an image inside an H1, such as a logo, counts as text).""",
        "Mark up the page's visible main heading as a single <h1> that describes the page.")
    {
        Why = """The H1 is the page's main heading, and screen reader users jump between headings to find their way, so a page without one is harder to scan. Google lists headings such as the H1 among its sources for title links, but says it can rarely rely on HTML semantics, so this matters mostly for readers. Often the visible main heading is just styled text.""",
        LearnMore = "https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/Heading_Elements#navigation",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.PageDetail
    };

    public static readonly IssueDefinition H1_MULTIPLE = new("h1-multiple", IssueCategory.Headings, IssueSeverity.Notice,
        "Multiple H1s",
        "This indexable page has more than one H1 element. The detail says how many.",
        """Keep one <h1> for the main heading and change the others to <h2> or lower, or to plain styled text if they aren't headings.""")
    {
        Why = """Google says the number and order of headings don't matter for Search, so this isn't a search problem. For readers and screen reader users, one H1 naming the page with H2s for its sections gives the clearest outline, which is why MDN advises against multiple H1s. Often a logo or a sidebar title is marked up as an H1 by the theme.""",
        LearnMore = "https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/Heading_Elements#avoid_using_multiple_h1_elements_on_one_page",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.None
    };

    public static readonly IssueDefinition H1_DUPLICATE = new("h1-duplicate", IssueCategory.Headings, IssueSeverity.Notice,
        "Duplicate H1",
        """Another indexable page has the same H1, ignoring case and spacing. Pages in the same paginated series (joined by rel="next" and rel="prev") don't count against each other.""",
        "Make each H1 say what's specific about its page, such as the product, place or topic.")
    {
        Why = """Identical main headings make similar pages hard to tell apart for visitors arriving from search, and often signal duplicate pages or a template whose heading lacks the distinguishing detail. Google doesn't document duplicate headings as a problem in themselves, so it's low priority unless the pages are duplicates too.""",
        LearnMore = null,
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.RelatedUrl
    };


    // Content
    public static readonly IssueDefinition LOW_WORD_COUNT = new("low-word-count", IssueCategory.Content, IssueSeverity.Notice,
        "Low word count",
        """This indexable page's main content has fewer than 200 words (the default, which can be changed in the analysis settings). Navigation menus and footers aren't counted.""",
        """Check whether the page is meant to be this short. If content is missing, restore it; if the page has little to offer, merge it into a related page with a redirect, or noindex it.""")
    {
        Why = """Google says content length alone doesn't matter for ranking: there's no minimum word count. Plenty of good pages are short, such as contact, product and category pages. Use this to find pages that are empty by mistake, such as an unfilled template, a placeholder or content that only appears with JavaScript, not as a reason to pad pages out.""",
        LearnMore = "https://developers.google.com/search/docs/fundamentals/seo-starter-guide#focusing",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.None
    };

    public static readonly IssueDefinition DUPLICATE_CONTENT = new("duplicate-content", IssueCategory.Content, IssueSeverity.Warning,
        "Duplicate content",
        """Another indexable page has exactly the same main content: the same words in the same order, ignoring case and punctuation. Pages with fewer than 50 words (the default, which can be changed in the analysis settings) aren't compared.""",
        """Decide which URL should be indexed, then 301-redirect the others to it or give them a canonical pointing to it. If the pages should differ, give each its own content.""")
    {
        Why = """Some duplication is normal and isn't against Google's spam policies. Google groups duplicates and shows one URL from the group, which may not be the one you'd choose, and crawls the others less often. The usual cause is the same page at several URLs, through parameters, trailing slashes or print versions.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/canonicalization",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.RelatedUrl
    };

    public static readonly IssueDefinition NEAR_DUPLICATE_CONTENT = new("near-duplicate-content", IssueCategory.Content, IssueSeverity.Notice,
        "Near-duplicate content",
        """Another indexable page has almost the same main content, roughly 96% or more alike at the default of 24 differing bits out of 256 in a content fingerprint (the default, which can be changed in the analysis settings). Exact copies are reported as duplicate content instead.""",
        """Give each page main content that is clearly its own, canonicalise the variants to one version, or merge them into one page with options.""")
    {
        Why = """Pages that differ in only a few words, such as location pages with the town swapped or products that differ by size, may be grouped by Google as duplicates, with only one shown. That's fine for variants you don't need to rank separately. Pages that should rank on their own need differences that are, in Google's words, clear and significant.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/canonicalization-troubleshooting",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.RelatedUrl
    };

    public static readonly IssueDefinition DUPLICATE_SITE_VERSION = new("duplicate-site-version", IssueCategory.Content, IssueSeverity.Warning,
        "Duplicate version of the site",
        """This version of the site (the bare domain or www, over http or https) answers with a page, directly or after redirecting to another non-main version, instead of redirecting to the main version: the home page's.""",
        """301-redirect every URL on the other versions to the same path on the main version, in one hop, at the server or CDN.""")
    {
        Why = """Every page is then reachable at two or more addresses. Google treats the versions as duplicate URLs and picks one, preferring HTTPS, which may not be the version you want, and it crawls the copies less. If the duplicate is the http version, visitors who land on it browse unencrypted. The other version is only probed, not parsed, so a canonical on it isn't seen; a redirect is what makes the choice yours.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/consolidate-duplicate-urls#reasons-to-specify-a-canonical-url",
        Subject = IssueSubject.Url,
        Grouping = IssueGrouping.ResponseDetail,
        SiteWide = true,
        ReportedOnce = true
    };


    // Images
    public static readonly IssueDefinition IMAGE_MISSING_ALT = new("image-missing-alt", IssueCategory.Images, IssueSeverity.Warning,
        "Images without alt text",
        """The page has <img> elements with no alt attribute at all. Images with an empty alt="" (marked as decorative) and 1×1 tracking pixels don't count.""",
        """Add alt text that says what each image shows or does in context. For purely decorative images, add an empty alt="" so screen readers skip them.""")
    {
        Why = """Some screen readers read out the image's file name when there's no alt, and WCAG requires a text alternative for every image that conveys something. Google uses alt text, with computer vision and the page's content, to understand images for Google Images, and as the anchor text when an image is a link.""",
        LearnMore = "https://developers.google.com/search/docs/appearance/google-images",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.None
    };

    public static readonly IssueDefinition IMAGE_ALT_TOO_LONG = new("image-alt-too-long", IssueCategory.Images, IssueSeverity.Notice,
        "Alt text too long",
        """The page has images whose alt text is longer than 125 characters (the default, which can be changed in the analysis settings).""",
        """Cut the alt text to a short description of what the image conveys, and move longer descriptions, such as a chart's data, into a caption or the page text.""")
    {
        Why = """W3C's guidance is that alt text should be the most concise description of the image's purpose, and anything longer than a short phrase or sentence belongs in a caption or the surrounding text. Screen reader users hear alt text in one go and can't skim it, and Google warns that alt text stuffed with keywords can make a site look spammy. 125 characters is a common convention, not a standard.""",
        LearnMore = "https://www.w3.org/WAI/tutorials/images/tips/",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.None
    };

    public static readonly IssueDefinition IMAGE_MISSING_DIMENSIONS = new("image-missing-dimensions", IssueCategory.Images, IssueSeverity.Notice,
        "Images without width and height attributes",
        """The page has <img> elements missing a width or height attribute, or both; 1×1 tracking pixels don't count. Only the HTML is read, so images sized in CSS still appear here.""",
        """Add width and height attributes with the image's intrinsic size (CSS can still scale it), or give the image box an aspect-ratio or fixed size in CSS.""")
    {
        Why = """Without both attributes, and unless CSS gives the image box a size or aspect-ratio, the browser can't reserve space before the image loads, so the content below jumps when it arrives. That layout shift annoys readers and counts towards Cumulative Layout Shift, one of Google's Core Web Vitals. If the CSS already reserves the space, nothing needs doing.""",
        LearnMore = "https://web.dev/articles/optimize-cls#images-without-dimensions",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.None
    };

    public static readonly IssueDefinition IMAGE_BROKEN = new("image-broken", IssueCategory.Images, IssueSeverity.Error,
        "Broken images",
        """The page embeds images (<img>) whose URLs return a 4xx or 5xx error or don't respond, directly or after redirects. For images on other sites, statuses often sent only to crawlers (401, 403, 405, 429, 503, 999) don't count.""",
        "Fix the image URL or restore the file. If the image is gone, replace it or remove the <img>.")
    {
        Why = """Visitors see a broken-image icon or a gap, which makes the page look unfinished, and an image that can't be fetched can't appear in Google Images. A broken image in a template shows up on every page that uses it, so fix those first.""",
        LearnMore = null,
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.RelatedUrl
    };

    public static readonly IssueDefinition IMAGE_TOO_LARGE = new("image-too-large", IssueCategory.Images, IssueSeverity.Warning,
        "Large images",
        """The page embeds images whose files, after any redirects, are over 200 KB as transferred (the default, which can be changed in the analysis settings).""",
        """Resize images to the largest size they're displayed at (with srcset for different screens), compress them, and use WebP or AVIF where you can.""")
    {
        Why = """Images are often the largest part of a page's weight, and a big one costs visitors time and data, especially on mobile connections. It matters most above the fold, such as a hero image that's the page's Largest Contentful Paint; a large image lazy-loaded far down the page costs much less. The usual causes are photos uploaded straight from a camera and images far larger than the size they're shown at.""",
        LearnMore = "https://web.dev/articles/optimize-lcp#reduce-size",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.RelatedUrl
    };


    // Hreflang
    public static readonly IssueDefinition HREFLANG_INVALID_CODE = new("hreflang-invalid-code", IssueCategory.Hreflang, IssueSeverity.Error,
        "Invalid hreflang code",
        """An hreflang value on this indexable page isn't a valid code. Valid codes are an ISO 639-1 language, optionally followed by a four-letter script and an ISO 3166-1 alpha-2 region (en, en-GB, zh-Hant-TW), or x-default. UN M.49 numeric regions such as es-419 aren't supported.""",
        "Correct the code to the language-REGION form, such as en-GB or de-AT, on every page in the set.")
    {
        Why = """Google supports only ISO 639-1 languages and ISO 3166-1 alpha-2 regions, and says other codes, such as es-419, aren't supported, so the entry is ignored. An unknown language makes it useless. A reserved region such as UK, EU or UN is different: Google ignores the region and the code still works as the language alone, so en-UK only works as plain en and doesn't target the United Kingdom (whose code is GB). A country code on its own isn't valid either: hreflang="be" means Belarusian, not Belgium.""",
        LearnMore = "https://developers.google.com/search/docs/specialty/international/localized-versions#language-codes",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.PageDetail
    };

    public static readonly IssueDefinition HREFLANG_MISSING_SELF = new("hreflang-missing-self", IssueCategory.Hreflang, IssueSeverity.Warning,
        "Hreflang without a self-reference",
        """This indexable page lists hreflang alternates, but none of them is the page's own URL exactly as crawled.""",
        """Add an hreflang entry for the page's own URL, written exactly as its canonical URL (scheme, host, path and trailing slash), alongside the entries for the other versions.""")
    {
        Why = """Google's rule is that each language version must list itself as well as all the other versions, with the same set of links on every page. Without the self-reference the set breaks that rule and may not be used as intended. Often the self-reference is there but in a different form of the URL, such as http instead of https, without the trailing slash or with a tracking parameter.""",
        LearnMore = "https://developers.google.com/search/docs/specialty/international/localized-versions#all-method-guidelines",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.None
    };

    public static readonly IssueDefinition HREFLANG_MISSING_RETURN = new("hreflang-missing-return", IssueCategory.Hreflang, IssueSeverity.Warning,
        "Hreflang without return links",
        """This indexable page names alternates on the site that are indexable but don't list it in their own hreflang annotations. The detail says how many.""",
        """Give every page in the set the same complete list of hreflang entries, including one that points back to this page with exactly this URL.""")
    {
        Why = """Google requires hreflang to be confirmed from both sides: if page X lists page Y, Y must list X. Where it isn't, Google says the annotations may be ignored or not interpreted correctly, so searchers may be shown the wrong language version. It usually means the sets differ between pages, for example after a new version was added to some templates but not others.""",
        LearnMore = "https://developers.google.com/search/docs/specialty/international/localized-versions#common-mistakes",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.RelatedUrl
    };

    public static readonly IssueDefinition HREFLANG_BROKEN_TARGET = new("hreflang-broken-target", IssueCategory.Hreflang, IssueSeverity.Error,
        "Hreflang points to a broken URL",
        "This indexable page has hreflang entries whose URLs return a 4xx or 5xx error or don't respond.",
        """Point the entry at the working page for that language, or remove it from every page in the set if that version no longer exists.""")
    {
        Why = """An alternate that doesn't load can't be shown to anyone and can't confirm the return link, so the entry for that language does nothing, and anyone sent there gets an error. Usually it's a version that was removed or moved without the annotations being updated.""",
        LearnMore = "https://developers.google.com/search/docs/specialty/international/localized-versions",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.RelatedUrl
    };

    public static readonly IssueDefinition HREFLANG_TO_NON_INDEXABLE = new("hreflang-to-non-indexable", IssueCategory.Hreflang, IssueSeverity.Warning,
        "Hreflang points to a non-indexable URL",
        """This indexable page has hreflang entries whose URLs redirect, are noindex, name another URL as canonical, or are blocked by robots.txt (or, on this site, by a firewall).""",
        """Replace each entry with the alternate's final, canonical, indexable URL, and keep the list identical on every page in the set.""")
    {
        Why = """Google generally shows each version's canonical, indexable URL in results, so hreflang should name exactly those URLs. An alternate that redirects or is canonicalised elsewhere isn't the URL that gets indexed, and one that's noindex or blocked can't be shown at all, so the entry for that language is unreliable. Google specifically warns against listing HTTP URLs in hreflang when the pages are on HTTPS.""",
        LearnMore = "https://developers.google.com/search/docs/specialty/international/localized-versions",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.RelatedUrl
    };

    public static readonly IssueDefinition HREFLANG_DUPLICATE_CODE = new("hreflang-duplicate-code", IssueCategory.Hreflang, IssueSeverity.Warning,
        "Conflicting hreflang entries",
        """This indexable page has the same hreflang code (compared ignoring case) pointing to two or more different URLs. The detail lists the codes.""",
        """Keep one URL per code. If two pages really target the same audience, choose one, or give them different regions.""")
    {
        Why = """Each language or language-region code should name exactly one URL; when a code names several, search engines can't tell which to show for that audience. It's usually two systems adding hreflang, such as a plugin and the theme, or the HTML and an HTTP header, or a copy-paste mistake in the set.""",
        LearnMore = "https://developers.google.com/search/docs/specialty/international/localized-versions#html",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.PageDetail
    };

    public static readonly IssueDefinition HREFLANG_MISSING_X_DEFAULT = new("hreflang-missing-x-default", IssueCategory.Hreflang, IssueSeverity.Notice,
        "Hreflang without x-default",
        "This indexable page has hreflang annotations but no x-default entry.",
        """Add <link rel="alternate" hreflang="x-default" href="..."> pointing at your selector page or default version, on every page in the set.""")
    {
        Why = """x-default names the page for searchers whose language or region isn't in your list, usually a language selector or the main international version. Google suggests adding one, especially on language or country selectors and home pages that redirect by locale, but it's optional.""",
        LearnMore = "https://developers.google.com/search/docs/specialty/international/localized-versions#xdefault",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.None
    };

    public static readonly IssueDefinition HREFLANG_OUTSIDE_HEAD = new("hreflang-outside-head", IssueCategory.Hreflang, IssueSeverity.Warning,
        "Hreflang outside <head>",
        "Some of this indexable page's hreflang links are in the <body> as browsers parse it.",
        """Move the hreflang links into the <head> above anything that could close it early, or declare them in HTTP Link headers or the XML sitemap instead.""")
    {
        Why = """Google requires hreflang <link> elements to be inside a well-formed <head>, so these entries don't count and the set is incomplete, which can also break return links from the other versions. Usually the tags are in the head in the source, but an element that doesn't belong there, such as an <img>, a <div> or stray text, ends the head early.""",
        LearnMore = "https://developers.google.com/search/docs/specialty/international/localized-versions#html",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.None
    };


    // Structured data
    public static readonly IssueDefinition STRUCTURED_DATA_INVALID = new("structured-data-invalid", IssueCategory.StructuredData, IssueSeverity.Error,
        "Invalid JSON-LD",
        """A JSON-LD block (<script type="application/ld+json">) on the page isn't valid JSON. The detail shows the parser's first error.""",
        """Fix the syntax where the detail points (often an unescaped quote, a trailing or missing comma, or a comment left in by a template), then test the page with Google's Rich Results Test.""")
    {
        Why = """JSON-LD has to be valid JSON: a trailing comma or a comment is enough for Google to drop the whole block, and its Unparsable structured data report lists the error a trailing comma produces. Nothing in the block is used, so it can't produce a rich result. Other valid blocks on the page still work. A single template error usually breaks the same block on every page that uses it.""",
        LearnMore = "https://support.google.com/webmasters/answer/9166415",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.PageDetail, FaultOf = IssueFaults.JsonError
    };

    public static readonly IssueDefinition STRUCTURED_DATA_MISSING_REQUIRED = new("structured-data-missing-required", IssueCategory.StructuredData, IssueSeverity.Warning,
        "Structured data missing required properties",
        """A top-level schema.org item on this indexable page lacks a property Google requires for that type's rich result. The detail names the type and the missing properties.""",
        """Add the missing properties with values that match what's visible on the page, then test it with the Rich Results Test.""")
    {
        Why = """Google says items missing required properties aren't eligible for rich results, so the markup does nothing for that feature. Recommended properties aren't checked, since they don't affect eligibility. FAQPage isn't checked either: Google stopped showing FAQ rich results in May 2026. Even complete markup only makes a rich result possible: Google decides whether to show one.""",
        LearnMore = "https://developers.google.com/search/docs/appearance/structured-data/sd-policies#completeness",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.PageDetail
    };


    // Security
    public static readonly IssueDefinition NO_HTTPS = new("no-https", IssueCategory.Security, IssueSeverity.Error,
        "Site not on HTTPS",
        """The home page (the start URL after its redirects) is served over plain HTTP. Reported once, on the home page.""",
        """Install a TLS certificate (free from Let's Encrypt or most hosts), serve every page over HTTPS and 301-redirect each HTTP URL to its HTTPS equivalent. Then update internal links, canonicals and sitemaps, and add HSTS.""")
    {
        Why = """Browsers label HTTP pages "Not secure", and anything visitors send, such as form data, can be read or changed on the way. Google has used HTTPS as a lightweight ranking signal since 2014 and prefers HTTPS URLs as canonical when both versions exist. Fix this first.""",
        LearnMore = "https://web.dev/articles/enable-https",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.None,
        SiteWide = true,
        ReportedOnce = true
    };

    public static readonly IssueDefinition HTTP_PAGE = new("http-page", IssueCategory.Security, IssueSeverity.Warning,
        "HTTP page on an HTTPS site",
        """This internal page answers over plain HTTP with a page of its own, while the site's home page is on HTTPS.""",
        """301-redirect the HTTP URL to its HTTPS version, ideally for the whole http:// origin at once, and update the links that point at http://.""")
    {
        Why = """Visitors who reach it get an unencrypted page that browsers label "Not secure", and if an HTTPS version also exists, the two are duplicates for Google to sort out. Usually internal links or a sitemap still point at the http:// URL and the server doesn't redirect it.""",
        LearnMore = "https://web.dev/articles/enable-https#redirect-to-https",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.None
    };

    public static readonly IssueDefinition MIXED_CONTENT = new("mixed-content", IssueCategory.Security, IssueSeverity.Error,
        "Mixed content",
        "This HTTPS page loads images, scripts, stylesheets, iframes, audio or video from http:// URLs.",
        """Change the resource URLs to https:// or relative paths, and host copies of resources whose servers don't support HTTPS. A Content-Security-Policy with upgrade-insecure-requests is a useful safety net.""")
    {
        Why = """Browsers block insecure scripts, stylesheets and frames on HTTPS pages, which can break layout and features, and upgrade images, audio and video to HTTPS, which then fail to load if the server doesn't offer them that way. Google also lists insecure dependencies other than images among the things that can stop it preferring a page's HTTPS version.""",
        LearnMore = "https://developer.mozilla.org/en-US/docs/Web/Security/Defenses/Mixed_content",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.RelatedUrl
    };

    public static readonly IssueDefinition SECURITY_HEADERS_MISSING = new("security-headers-missing", IssueCategory.Security, IssueSeverity.Notice,
        "Security headers missing",
        """The home page's response lacks one or more of Strict-Transport-Security (checked on HTTPS only), X-Content-Type-Options, Content-Security-Policy, X-Frame-Options (or a CSP frame-ancestors rule) and Referrer-Policy. The detail lists which.""",
        """Add the missing headers in the server, CDN or framework configuration, starting with X-Content-Type-Options and Referrer-Policy, which rarely break anything. Roll out CSP in report-only mode first, and enable HSTS only once HTTPS works everywhere on the site.""")
    {
        Why = """These headers protect visitors: HSTS keeps browsers on HTTPS, CSP limits what injected scripts can do, X-Frame-Options or frame-ancestors stops clickjacking, X-Content-Type-Options stops MIME sniffing, and Referrer-Policy limits which URLs leak to other sites. Google doesn't document them as a ranking factor. Only the home page is checked, as they're normally set site-wide.""",
        LearnMore = "https://web.dev/articles/security-headers",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.None,
        ReportedOnce = true
    };


    // URLs
    public static readonly IssueDefinition URL_UPPERCASE = new("url-uppercase", IssueCategory.Urls, IssueSeverity.Notice,
        "Uppercase characters in URL",
        """This indexable page's URL path contains uppercase letters (percent-escapes such as %2F don't count).""",
        """Use lowercase for new URLs. Change an existing one only if it causes duplicates or broken links, with a 301 redirect to the lowercase version and updated internal links.""")
    {
        Why = """Google treats URLs as case-sensitive, so /Page and /page are different URLs: if the server serves both, mixed-case links create duplicates, and if it doesn't, links typed in lowercase break. Lowercase URLs avoid both. An established URL that works and is linked consistently is fine to leave, since changing it needs redirects.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/url-structure#be-aware-that-urls-are-case-sensitive",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.None
    };

    public static readonly IssueDefinition URL_UNDERSCORES = new("url-underscores", IssueCategory.Urls, IssueSeverity.Notice,
        "Underscores in URL",
        "This indexable page's URL path contains underscores.",
        """Use hyphens between words in new URLs. Rename existing ones only as part of a wider URL change, with 301 redirects.""")
    {
        Why = """Google recommends hyphens to separate words in URLs, because underscores are commonly read as joining words (as in format_date) rather than separating them, and hyphens help users and search engines identify the words. The effect is small, so don't change established URLs for this alone.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/url-structure#use-hyphens-to-separate-words",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.None
    };

    public static readonly IssueDefinition URL_TOO_LONG = new("url-too-long", IssueCategory.Urls, IssueSeverity.Notice,
        "Long URL",
        """This indexable page's URL is longer than 115 characters (the default, which can be changed in the analysis settings), counted as people read it: escapes decoded and an internationalised host in its own script.""",
        """Use short, descriptive paths for new pages and keep tracking parameters out of internal links. Shorten existing URLs only with 301 redirects.""")
    {
        Why = """Google doesn't document a length limit that affects search, and results usually show the domain and a breadcrumb made from parts of the URL rather than the whole thing. Long URLs are mainly harder to read, share and type, and they often point to deep folder structures, keyword-stuffed slugs or tracking parameters worth cleaning up.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/url-structure#use-descriptive-urls",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.None
    };


    // Performance
    public static readonly IssueDefinition SLOW_RESPONSE = new("slow-response", IssueCategory.Performance, IssueSeverity.Warning,
        "Slow response",
        """The crawler took more than 1,000 ms (the default, which can be changed in the analysis settings) to receive this page's HTML, from request to last byte. The detail also gives the time to first byte.""",
        """If the first byte is slow, find what the server waits on: add caching, speed up database queries and back-end calls, and serve from a CDN near your visitors. If the download after it is slow, reduce the HTML size.""")
    {
        Why = """Nothing on the page can render until its HTML arrives, so a slow response delays everything visitors see; web.dev's rough guide is a time to first byte of 0.8 seconds or less. Google also crawls less when a site's response times get longer, which matters mainly for large sites. Times depend on the crawler's location and speed preset, so recheck a few slow pages before acting.""",
        LearnMore = "https://web.dev/articles/ttfb",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.None
    };

    public static readonly IssueDefinition LARGE_PAGE = new("large-page", IssueCategory.Performance, IssueSeverity.Notice,
        "Large HTML",
        """This page's HTML is larger than 1 MB once decompressed (the default, which can be changed in the analysis settings). Images, scripts and stylesheets loaded as separate files don't count.""",
        """Move inline scripts, styles, framework data and base64 images into separate cacheable files, paginate very long lists, and keep the main content and links early in the HTML.""")
    {
        Why = """Large HTML takes longer to download and parse, especially on phones. Googlebot reads only the first 2 MB of an HTML file, measured uncompressed, for Google Search, and ignores the rest; a page past that limit is reported on its own. The bulk is usually inline scripts, framework state data, inline SVG or base64 images.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/googlebot#how-googlebot-accesses-your-site",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.None
    };

    public static readonly IssueDefinition HTML_PAST_CRAWL_LIMIT = new("html-past-crawl-limit", IssueCategory.Performance, IssueSeverity.Warning,
        "HTML past Google's crawl limit",
        """This page's HTML is larger than 2 MB once decompressed, which is as far as Googlebot reads a file for Google Search. Images, scripts and stylesheets loaded as separate files don't count. The limit is Google's, not an analysis setting.""",
        """Move inline scripts, styles, framework data and base64 images into separate files, and keep the main content and links within the first 2 MB.""")
    {
        Why = """Googlebot stops at the first 2 MB of the uncompressed file and ignores everything after it, so content and links past that point aren't indexed or followed for Google Search. Other Google fetchers use different limits. The usual cause is inline scripts, framework data, inline SVG or base64 images pushing the real content past the cut-off.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/googlebot#how-googlebot-accesses-your-site",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.None
    };


    // HTML
    public static readonly IssueDefinition VIEWPORT_MISSING = new("viewport-missing", IssueCategory.Html, IssueSeverity.Warning,
        "Missing viewport",
        """The page has no viewport meta tag (<meta name="viewport">) with content.""",
        """Add <meta name="viewport" content="width=device-width, initial-scale=1"> to the <head>, then check the layout really adapts to small screens.""")
    {
        Why = """Without it, mobile browsers lay the page out at a desktop width (about 980 pixels) and shrink it to fit, so text is tiny and visitors have to zoom and pan. Google indexes pages as its smartphone crawler sees them, and its page experience guidance asks whether content displays well on mobile devices.""",
        LearnMore = "https://web.dev/articles/responsive-web-design-basics#viewport",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.None
    };

    public static readonly IssueDefinition LANG_MISSING = new("lang-missing", IssueCategory.Html, IssueSeverity.Notice,
        "Missing lang attribute",
        "The page's <html> element has no lang attribute, or it's empty.",
        """Add the page's main language to the <html> element, such as <html lang="en-GB">, and mark passages in other languages with their own lang.""")
    {
        Why = """Screen readers use lang to choose the right voice and pronunciation, and WCAG requires the page's language to be set this way; browsers use it too, for example for hyphenation. Google doesn't: it detects the language from the visible content, so this is about accessibility, not search.""",
        LearnMore = "https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Global_attributes/lang#accessibility_concerns",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.None
    };

    public static readonly IssueDefinition CHARSET_MISSING = new("charset-missing", IssueCategory.Html, IssueSeverity.Notice,
        "Missing character encoding",
        """Nothing declares the page's character encoding: no byte-order mark at the start of the file, no meta tag (<meta charset> or http-equiv Content-Type) and no charset in the Content-Type header.""",
        """Add <meta charset="utf-8"> as the first element in the <head>, within the first 1024 bytes of the file, or send Content-Type: text/html; charset=utf-8 from the server.""")
    {
        Why = """Without a declaration, browsers have to guess the encoding, and a wrong guess turns accented letters, currency symbols and curly quotes into garbled characters. W3C advises always declaring it, even when a page happens to display correctly. A page that starts with a byte-order mark has declared it, and isn't reported; a crawl made before the crawler recorded the mark still reports such a page, until it's crawled again.""",
        LearnMore = "https://www.w3.org/International/questions/qa-html-encoding-declarations",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.None
    };

    public static readonly IssueDefinition OPEN_GRAPH_MISSING = new("open-graph-missing", IssueCategory.Html, IssueSeverity.Notice,
        "Open Graph tags missing",
        """This indexable page lacks og:title, og:description or og:image, or has them empty. The detail says which are missing and which are empty.""",
        """Add og:title, og:description and og:image (an absolute URL to a representative image, not the site logo) to the <head>; most SEO plugins can fill them from the page's title, description and featured image.""")
    {
        Why = """Social networks and chat apps build link previews from Open Graph tags; without them they guess the title and image or show a bare link. Google also lists og:title among its sources for title links and accepts og:image as a way to suggest a page's preferred image. Low priority for pages nobody shares.""",
        LearnMore = "https://ogp.me/",
        Subject = IssueSubject.Page,
        Grouping = IssueGrouping.PageDetail
    };

    public static IReadOnlyList<IssueDefinition> All { get; } =
    [
        INTERNAL_CLIENT_ERROR, INTERNAL_SERVER_ERROR, INTERNAL_UNREACHABLE, INTERNAL_BLOCKED, SOFT_404,
        REDIRECT_CHAIN, REDIRECT_LOOP, REDIRECT_BROKEN, REDIRECT_TEMPORARY, REDIRECT_DOWNGRADE, META_REFRESH,
        BROKEN_INTERNAL_LINKS, BROKEN_EXTERNAL_LINKS, LINKS_TO_REDIRECTS, INTERNAL_NOFOLLOW_LINKS, EMPTY_ANCHOR_TEXT,
        BROKEN_FRAGMENT_LINKS, ORPHAN_PAGE,
        NOINDEX_PAGE, NOFOLLOW_PAGE, BLOCKED_BY_ROBOTS, SNIPPET_BLOCKED, ROBOTS_TXT_UNREACHABLE,
        ROBOTS_TXT_UNREACHABLE_OTHER_ORIGIN, ROBOTS_TXT_MISSING, SITE_HIDDEN_FROM_SEARCH,
        CANONICALISED_PAGE, CANONICAL_MISSING, CANONICAL_MULTIPLE, CANONICAL_BROKEN, CANONICAL_TO_REDIRECT,
        CANONICAL_TO_NON_INDEXABLE, CANONICAL_OUTSIDE_HEAD, CANONICAL_RELATIVE, CANONICAL_INVALID,
        NOINDEX_WITH_CANONICAL,
        SITEMAP_NON_INDEXABLE, NOT_IN_SITEMAP, SITEMAP_ERROR, SITEMAP_MISSING, SITEMAP_URL_OTHER_HOST,
        TITLE_MISSING, TITLE_MULTIPLE, TITLE_TOO_LONG, TITLE_TOO_SHORT, TITLE_DUPLICATE,
        DESCRIPTION_MISSING, DESCRIPTION_MULTIPLE, DESCRIPTION_TOO_LONG, DESCRIPTION_TOO_SHORT, DESCRIPTION_DUPLICATE,
        H1_MISSING, H1_MULTIPLE, H1_DUPLICATE,
        LOW_WORD_COUNT, DUPLICATE_CONTENT, NEAR_DUPLICATE_CONTENT, DUPLICATE_SITE_VERSION,
        IMAGE_MISSING_ALT, IMAGE_ALT_TOO_LONG, IMAGE_MISSING_DIMENSIONS, IMAGE_BROKEN, IMAGE_TOO_LARGE,
        HREFLANG_INVALID_CODE, HREFLANG_MISSING_SELF, HREFLANG_MISSING_RETURN, HREFLANG_BROKEN_TARGET,
        HREFLANG_TO_NON_INDEXABLE, HREFLANG_DUPLICATE_CODE, HREFLANG_MISSING_X_DEFAULT, HREFLANG_OUTSIDE_HEAD,
        STRUCTURED_DATA_INVALID, STRUCTURED_DATA_MISSING_REQUIRED,
        NO_HTTPS, HTTP_PAGE, MIXED_CONTENT, SECURITY_HEADERS_MISSING,
        URL_UPPERCASE, URL_UNDERSCORES, URL_TOO_LONG,
        SLOW_RESPONSE, LARGE_PAGE, HTML_PAST_CRAWL_LIMIT,
        VIEWPORT_MISSING, LANG_MISSING, CHARSET_MISSING, OPEN_GRAPH_MISSING
    ];

    private static readonly Dictionary<string, IssueDefinition> BY_ID = All.ToDictionary(issue => issue.Id, StringComparer.Ordinal);

    public static IssueDefinition? Find(string id) => BY_ID.GetValueOrDefault(id);
}
