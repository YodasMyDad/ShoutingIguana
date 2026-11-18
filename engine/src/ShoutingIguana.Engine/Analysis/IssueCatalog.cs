using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Engine.Analysis;

/// <summary>
/// Every issue the analysis can report. Ids are stable: they are stored in crawl files and used by the UI, so
/// change a title, description, why or fix freely but never an id. Each issue is reported at most once per URL.
/// </summary>
public static class IssueCatalog
{
    // What restoring a URL that doesn't work clears: the links, images, files, canonicals, hreflang entries and pagination
    // links naming it. Ids, not the definitions, which are made in the order written and so would be null here. First, as
    // the checks use it.
    private static readonly string[] BROKEN_TARGETS =
        ["broken-internal-links", "image-broken", "broken-resources", "canonical-broken", "hreflang-broken-target", "pagination-broken", "social-image-broken"];

    // Response codes
    public static readonly IssueDefinition INTERNAL_CLIENT_ERROR = new("internal-client-error", IssueCategory.ResponseCodes, IssueSeverity.Error,
        "Internal URL returns 4xx",
        """This internal URL, a page or a resource such as an image or script, answered with a 4xx client error such as 404 Not Found, 410 Gone or 403 Forbidden.""",
        """If the URL should work, restore it or 301-redirect it to the closest replacement. If it's gone for good, keep the 404 or 410 and update or remove the links and sitemap entries that point to it.""")
    {
        Why = """Visitors who follow a link here get an error instead of the content. Google doesn't index URLs that return 4xx, and drops ones that were already indexed. A 404 or 410 is the right answer for something removed on purpose, and a 401 or 403 is normal behind a login: then the only problem is the links and sitemap entries that still lead here.""",
        LearnMore = "https://developers.google.com/crawling/docs/troubleshooting/http-status-codes#4xx-client-errors",
        References = IssueReferences.Sources,
        Subject = IssueSubject.Url,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.ResponseDetail,
        Evidence = ["type"],
        Clears = BROKEN_TARGETS
    };

    public static readonly IssueDefinition INTERNAL_SERVER_ERROR = new("internal-server-error", IssueCategory.ResponseCodes, IssueSeverity.Error,
        "Internal URL returns 5xx",
        """The server answered this internal URL with a 5xx error, such as 500 Internal Server Error or 503 Service Unavailable. Temporary errors (502, 503 and 504) were retried before being recorded.""",
        """Check the server and application logs for this URL at the time of the crawl, fix the cause and recrawl. If many URLs failed together, the server may have been overloaded: recrawl at a slower speed preset to see whether the errors stop.""")
    {
        Why = """Visitors get an error page instead of the content. Google slows down crawling when a site returns 5xx errors, and URLs that keep failing are eventually dropped from its index. A one-off error during a busy crawl can happen, so recheck the URL; errors that repeat or hit many URLs need fixing first.""",
        LearnMore = "https://developers.google.com/crawling/docs/troubleshooting/http-status-codes#5xx-server-errors",
        References = IssueReferences.Sources,
        Subject = IssueSubject.Url,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.ResponseDetail,
        Evidence = ["type", "responseMs"],
        Clears = [.. BROKEN_TARGETS, "sitemap-non-indexable"]
    };

    public static readonly IssueDefinition INTERNAL_UNREACHABLE = new("internal-unreachable", IssueCategory.ResponseCodes, IssueSeverity.Error,
        "Internal URL didn't respond",
        """This internal URL gave no response: the request timed out (after 20 seconds by default) or the connection failed, even after retries (2 by default). Both can be changed in the crawl settings.""",
        """Check whether the URL loads from another network, then look for dropped or slow connections at the server, firewall, load balancer or DNS. If it only fails under load, the server needs more capacity or caching; a slower speed preset shows whether the crawl itself caused it.""")
    {
        Why = """Visitors get a browser error or a page that never loads. Google treats timeouts, connection resets and DNS errors like server errors: it slows crawling straight away, can't index URLs it gets nothing from, and removes indexed ones within days if the problem continues.""",
        LearnMore = "https://developers.google.com/crawling/docs/troubleshooting/dns-network-errors",
        References = IssueReferences.Sources,
        Subject = IssueSubject.Url,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.ResponseDetail,
        Evidence = ["type", "fetchError", "responseMs"],
        Clears = BROKEN_TARGETS
    };

    // A 429 is not one of these: the crawler slows down and asks again, and a URL the site still answers with 429 at the
    // end of the crawl is stored as not checked, which no check judges.
    public static readonly IssueDefinition INTERNAL_BLOCKED = new("internal-blocked", IssueCategory.ResponseCodes, IssueSeverity.Notice,
        "Internal URL refused the crawler",
        """A firewall or bot protection service (such as Cloudflare, Akamai, AWS WAF, DataDome or Imperva) refused the crawler on this internal URL with a block page or a challenge. The detail names the protection when it's recognised.""",
        """Allowlist the crawler in the firewall or bot protection (by user agent, IP address or a secret header), or crawl at a slower speed preset. Make sure the same rules let verified search engine crawlers through: verify Googlebot by reverse DNS or Google's published IP ranges rather than by user agent, which anyone can fake.""")
    {
        Why = """Nothing on this URL could be checked, so other checks can't see its problems. If the same rules challenge search engine crawlers, they can't crawl it either. Many protections only challenge automated traffic, so visitors are usually unaffected.""",
        LearnMore = "https://developers.google.com/crawling/docs/crawlers-fetchers/verify-google-requests",
        References = IssueReferences.Sources,
        Subject = IssueSubject.Url,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.ResponseDetail,
        Evidence = ["type"]
    };

    public static readonly IssueDefinition CONTENT_TYPE_MISMATCH = new("content-type-mismatch", IssueCategory.ResponseCodes, IssueSeverity.Warning,
        "File served with the wrong content type",
        """A page uses this internal URL as a script, a stylesheet or an image, but the server sends it with a Content-Type that isn't one (a script not sent as JavaScript, a stylesheet not as text/css, an image not as image/*), or with none or a malformed one. A URL used two ways is judged for each. The detail says how it's used and what it's sent as.""",
        """Configure the server or CDN to send the right Content-Type for the file's type, such as text/javascript for .js and text/css for .css. If the URL answers with a page instead, such as an error or login page sent with 200, fix or replace the link to the file.""")
    {
        Why = """With X-Content-Type-Options: nosniff, as most sites send, browsers refuse to run a script or apply a stylesheet sent as the wrong type, so the page can break for visitors and for Google's rendering. An "image" that answers with a web page is usually an error page in disguise.""",
        LearnMore = "https://developer.mozilla.org/en-US/docs/Web/HTTP/Reference/Headers/X-Content-Type-Options",
        References = IssueReferences.Sources,
        Subject = IssueSubject.Url,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.ResponseDetail,
        Evidence = ["type", "contentType"]
    };

    public static readonly IssueDefinition SOFT_404 = new("soft-404", IssueCategory.ResponseCodes, IssueSeverity.Warning,
        "Soft 404",
        """This indexable page returns 200 OK, but its title or H1 reads like an error message (such as "Page not found" or "no longer available") and it has fewer than 300 words.""",
        """Return 404 or 410 for content that's gone, or 301-redirect it to a real replacement. If the page should exist, change the wording that makes it look like an error, and check its content didn't fail to load.""")
    {
        Why = """Google detects pages like this as soft 404s and excludes them from search, but keeps crawling them, which wastes crawling on large sites. Serving an error with 200 also hides real breakage from monitoring that only looks at status codes. If the page is fine and only mentions "not found" in a heading, ignore this.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/troubleshoot-crawling-errors#soft-404-errors",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.None,
        Evidence = ["title", "h1", "wordCount"]
    };


    // Redirects
    public static readonly IssueDefinition REDIRECT_CHAIN = new("redirect-chain", IssueCategory.Redirects, IssueSeverity.Warning,
        "Redirect chain",
        """This internal URL (linked from a crawled page, listed in a sitemap or the start URL) takes two or more redirects to reach its final URL. The detail gives the number of hops, and says when there are more than Googlebot follows.""",
        """Point each redirect straight at the final URL, and update links and sitemap entries to the final URL so they skip the redirects altogether.""")
    {
        Why = """Every hop is another round trip before the page starts to load, which visitors on slow connections notice. Google follows up to 10 hops and processes only the final URL's content, and its crawl budget guide says long chains have a negative effect on crawling, which matters mainly on large sites. Two hops is rarely a problem in itself, but chains grow with each site change.""",
        LearnMore = "https://developers.google.com/crawling/docs/troubleshooting/http-status-codes#3xx-redirection",
        References = IssueReferences.Sources,
        Subject = IssueSubject.Url,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.RelatedUrl,
        Evidence = ["redirectChain", "redirectKind"],
        TargetFacts = ["statusCode", "indexability"],
        Clears = ["links-to-redirects", "pagination-broken"]
    };

    public static readonly IssueDefinition REDIRECT_LOOP = new("redirect-loop", IssueCategory.Redirects, IssueSeverity.Error,
        "Redirect loop",
        """Following this internal URL's redirects leads back to a URL already passed, so they never reach a page.""",
        """Follow the hops, find the rule that sends the URL back, and change it so the chain ends at a URL that returns 200.""")
    {
        Why = """Browsers give up with an error (Chrome's ERR_TOO_MANY_REDIRECTS), so visitors never see the content and search engines have nothing to index. Loops usually come from two rules undoing each other, such as one adding a trailing slash and another removing it, or an HTTPS redirect on a server behind a CDN or proxy that can't see the visitor is already on HTTPS. Fix this first.""",
        LearnMore = "https://developers.google.com/crawling/docs/troubleshooting/http-status-codes#3xx-redirection",
        References = IssueReferences.Sources,
        Subject = IssueSubject.Url,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.None,
        Evidence = ["redirectChain"],
        Clears = ["broken-internal-links", "canonical-broken"]
    };

    public static readonly IssueDefinition REDIRECT_BROKEN = new("redirect-broken", IssueCategory.Redirects, IssueSeverity.Error,
        "Broken redirect",
        """This internal URL redirects, but the redirects end at a URL that returns a 4xx or 5xx error or doesn't respond, or the redirect has no usable Location header.""",
        """Point the redirect at a working replacement or fix the target. If there's no replacement, let the old URL return 404 or 410 rather than redirect to an error.""")
    {
        Why = """Visitors land on an error instead of the page the redirect promised. Google ignores a redirecting URL's own content and processes only the final URL's, so there's nothing to index, and links to the old URL lead nowhere.""",
        LearnMore = "https://developers.google.com/crawling/docs/troubleshooting/http-status-codes#3xx-redirection",
        References = IssueReferences.Sources,
        Subject = IssueSubject.Url,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.ResponseDetail,
        Evidence = ["redirectChain", "redirectKind"],
        TargetFacts = ["statusCode"],
        Clears = ["broken-internal-links", "image-broken", "broken-resources", "canonical-broken", "social-image-broken"]
    };

    public static readonly IssueDefinition REDIRECT_TEMPORARY = new("redirect-temporary", IssueCategory.Redirects, IssueSeverity.Notice,
        "Temporary redirect",
        """This internal URL redirects to another URL on the site with a temporary status code: 302, 303 or 307. Temporary redirects to other sites, such as a shop's hosted login or checkout, don't count: they hand visitors over rather than move the page.""",
        "If the move is permanent, change the redirect to 301 or 308; if it's temporary, leave it.")
    {
        Why = """Google doesn't treat a temporary redirect as a sign that the target should replace the old URL (at most it's a weak signal), so the old URL can stay in search results. That's correct when the move really is temporary, such as a short-term campaign page or a login redirect. For a permanent move it delays the switch to the new URL.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/301-redirects#overview-of-redirect-types",
        References = IssueReferences.Sources,
        Subject = IssueSubject.Url,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.ResponseDetail,
        Evidence = ["redirectChain", "redirectKind"],
        TargetFacts = ["statusCode"]
    };

    public static readonly IssueDefinition REDIRECT_DOWNGRADE = new("redirect-downgrade", IssueCategory.Redirects, IssueSeverity.Error,
        "Redirect from HTTPS to HTTP",
        "This internal HTTPS URL redirects to an HTTP URL.",
        """Redirect to the HTTPS version of the target. The cause is usually an old rule or a proxy that rewrites the scheme, so check the server and CDN redirect rules.""")
    {
        Why = """Visitors who started on a secure URL end on an insecure one that browsers label "Not secure". Google normally prefers HTTPS URLs as canonical, but says HTTPS-to-HTTP redirects make it prefer the HTTP version very strongly, which even HSTS can't override.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/consolidate-duplicate-urls#prefer-https-over-http-for-canonical-urls",
        References = IssueReferences.Sources,
        Subject = IssueSubject.Url,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.RelatedUrl,
        Evidence = ["redirectChain"],
        TargetFacts = ["statusCode", "final"]
    };

    public static readonly IssueDefinition REDIRECT_TEMPORARY_CACHED = new("redirect-temporary-cached", IssueCategory.Redirects, IssueSeverity.Notice,
        "Temporary redirect cached for long",
        """This internal URL redirects to another URL on the site with a temporary status code (302, 303 or 307), but its response lets browsers and proxies keep the redirect for more than a day: a Cache-Control max-age or s-maxage over 86,400 seconds, or an Expires more than a day after its Date, without no-store or no-cache. The detail gives how long.""",
        """If the move is permanent, make it a 301 or 308. If it really is temporary, send the redirect with Cache-Control: no-store, or a max-age of an hour or so, so visitors' browsers ask again once it ends.""")
    {
        Why = """A temporary redirect is meant to be checked again each time, but a long cache lifetime lets each visitor's browser keep sending them to the old target for days or weeks after you change or remove it. Search engines treat it as temporary all the same, which is a separate signal to fix if the move is for good.""",
        LearnMore = "https://developer.mozilla.org/en-US/docs/Web/HTTP/Reference/Headers/Cache-Control",
        References = IssueReferences.Sources,
        Subject = IssueSubject.Url,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.ResponseDetail,
        Evidence = ["redirectChain", "redirectKind"]
    };

    public static readonly IssueDefinition META_REFRESH = new("meta-refresh", IssueCategory.Redirects, IssueSeverity.Warning,
        "Meta refresh",
        """The page has a <meta http-equiv="refresh"> tag, or its response a Refresh header, that sends visitors to another URL or reloads the page, at once or after a delay. The detail gives the target and the delay, and says when it's the header.""",
        """Replace it with a 301 redirect on the server (or 302 if the move is temporary). If the page reloads itself to show new content, update it with script instead, or let visitors refresh.""")
    {
        Why = """Google treats an instant meta refresh as a permanent redirect and a delayed one as temporary, but recommends a server-side redirect wherever you can set one up; that's faster too, as the browser doesn't load the page first. A timed redirect or reload also moves content away while people are reading, which WCAG counts as a failure. A refresh on old URLs where the server can't redirect is an acceptable fallback.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/301-redirects#metarefresh",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
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
        References = IssueReferences.Targets,
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.RelatedUrl,
        TargetFacts = ["statusCode", "final", "text", "position", "fragment", "flags", "links"]
    };

    public static readonly IssueDefinition BROKEN_EXTERNAL_LINKS = new("broken-external-links", IssueCategory.Links, IssueSeverity.Warning,
        "Links to broken external URLs",
        """The page links to URLs on other sites that return a 4xx or 5xx error or don't respond, directly or after redirects. Statuses many sites send only to crawlers (401, 403, 405, 429, 503 and LinkedIn's 999) don't count, and nor do links that the other site's bot protection (such as Cloudflare) refused, or that its robots.txt kept the crawler from checking.""",
        "Update each link to the resource's current address or an archived copy, or remove it.")
    {
        Why = """Visitors who click hit a dead end, which makes the page look neglected and its references unreliable. Google doesn't document broken outbound links as a ranking factor, so this is about visitors. Some sites refuse crawlers in other ways, so open a few in a browser before editing.""",
        LearnMore = null,
        References = IssueReferences.Targets,
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.RelatedUrl,
        TargetFacts = ["statusCode", "final", "text", "position", "fragment", "flags", "links"]
    };

    public static readonly IssueDefinition LINKS_TO_REDIRECTS = new("links-to-redirects", IssueCategory.Links, IssueSeverity.Notice,
        "Links to internal redirects",
        """The page links to internal URLs that redirect permanently (301 or 308, or an instant meta refresh) to a working page. Links to temporary redirects (302, 303 or 307), such as a sign-in link that sends visitors on to a login page, don't count: the linked URL is still the one to use.""",
        """Change the links to point straight at the final URL. Keep the redirects for bookmarks and other sites' links.""")
    {
        Why = """Each click costs visitors an extra round trip before the page starts to load, and each crawl costs search engines an extra request. Google's advice is to link to the canonical URL within your site, which a redirecting URL isn't. A few are harmless; it's worth fixing in templates, where the same link repeats on every page.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/consolidate-duplicate-urls#best-practices",
        References = IssueReferences.Targets,
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.RelatedUrl,
        TargetFacts = ["statusCode", "final", "redirectKind", "text", "position", "links"]
    };

    public static readonly IssueDefinition INTERNAL_NOFOLLOW_LINKS = new("internal-nofollow-links", IssueCategory.Links, IssueSeverity.Warning,
        "Nofollow internal links",
        """The page has rel="nofollow" on links to the site's own pages. Links to resources, such as images opened in a gallery lightbox, don't count.""",
        """Remove rel="nofollow" from internal links. Block URLs that shouldn't be crawled in robots.txt, or add noindex (without a robots.txt block) to pages that shouldn't appear in search.""")
    {
        Why = """Google treats nofollow as a hint not to crawl the target through that link or use the link for ranking, so pages linked only this way may be found late or not at all. For links within your own site, Google's advice is to use robots.txt to keep URLs from being crawled, and noindex to keep pages out of search, instead of nofollow. Nofollow on login or basket links is common and mostly harmless, but achieves little.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/qualify-outbound-links#nofollow",
        References = IssueReferences.Targets,
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.RelatedUrl,
        TargetFacts = ["statusCode", "text", "position", "flags", "links"]
    };

    public static readonly IssueDefinition EMPTY_ANCHOR_TEXT = new("empty-anchor-text", IssueCategory.Links, IssueSeverity.Notice,
        "Internal links without anchor text",
        """The page has internal links with nothing to read: no text, no alt text on an image inside, and no aria-label, aria-labelledby or title. A link to the page itself counts too, as a logo on the home page is; links written as a fragment (#, #top) and links in the <head>, such as rel="amphtml", don't.""",
        """Add visible text, alt text on the image inside the link, or an aria-label on icon links. For a logo that links home, alt text with the site name is enough.""")
    {
        Why = """Screen readers announce these as just "link" or read out the URL, so their users can't tell where they go. Google uses anchor text to understand the page a link points to, and the alt text of a linked image as its anchor text, so an empty link tells it nothing. Icon-only links in a template, such as a logo, a basket or a search icon, usually account for most of them.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/links-crawlable#anchor-text-placement",
        References = IssueReferences.Targets,
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.RelatedUrl,
        TargetFacts = ["statusCode", "position", "flags", "links"]
    };

    public static readonly IssueDefinition BROKEN_FRAGMENT_LINKS = new("broken-fragment-links", IssueCategory.Links, IssueSeverity.Notice,
        "Links to missing page sections",
        """The page links to a #fragment, on itself or another crawled page, that matches no id or named anchor on the target page. App routes (#/, #!), app state (fragments with =, : or &) and #top aren't checked.""",
        """Correct the fragment in the link, or add the matching id to the section on the target page; ids on headings are often lost when content is edited or regenerated.""")
    {
        Why = """Visitors land at the top of the page instead of the section the link promised. Google generally ignores URL fragments, so this is about visitors only. Ids added by JavaScript after the page loads can make a working link look broken, so check it in a browser first.""",
        LearnMore = "https://developer.mozilla.org/en-US/docs/Web/URI/Reference/Fragment",
        References = IssueReferences.Targets,
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.RelatedUrl,
        TargetFacts = ["statusCode", "fragment", "text", "position", "links"]
    };

    public static readonly IssueDefinition ORPHAN_PAGE = new("orphan-page", IssueCategory.Links, IssueSeverity.Warning,
        "Orphan page in a sitemap",
        """This indexable page is listed in a sitemap, but no crawled page links to it with a followed link, even through a redirect. The detail says whether only nofollow links lead to it, or nothing. It's only checked when the crawl finished, and not when 1 in 20 or more of the pages it tried to read refused the crawler or didn't answer, as their links are unknown.""",
        """If the page matters, link to it from relevant pages and from its section's listing. If it doesn't, remove it from the sitemap, then redirect or remove the page.""")
    {
        Why = """Visitors can't reach it by browsing, and search engines only know it from the sitemap. Google uses links to find pages and judge their relevance, and says every page you care about should have a link from at least one other page on your site. Often it's an old page left in the sitemap, or one linked only from places a crawler can't follow, such as forms or scripts.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/links-crawlable#internal-links",
        References = IssueReferences.Sources,
        Subject = IssueSubject.Page,
        Recheck = [],
        Grouping = IssueGrouping.None
    };


    public static readonly IssueDefinition UNCRAWLABLE_LINKS = new("uncrawlable-links", IssueCategory.Links, IssueSeverity.Warning,
        "Links search engines can't follow",
        """The page has links that lead to another page in a way a search engine can't follow: an <a> whose href is a javascript: URL that navigates, an <a> that navigates only through onclick or a routerLink, or another element, such as a <span> or <div>, given an href with a URL. Script that leads nowhere, such as a menu toggle or javascript:void(0), an empty href (the page itself) and a named anchor don't count. The detail gives up to three, with what makes each one uncrawlable. A crawl made before the crawler noted them isn't checked.""",
        """Make each one an <a> element with a real URL in its href, and keep any script on top of it, so search engines, and browsers without the script, can follow it. A control that only opens something on the page belongs in a <button>.""")
    {
        Why = """Google follows only <a> elements with an href it can resolve, so a page reached only through such links isn't found by crawling and gets no credit from them. Menus, "load more" controls and single-page-app routers are the usual cause.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/links-crawlable",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.None
    };

    public static readonly IssueDefinition BROKEN_RESOURCES = new("broken-resources", IssueCategory.Links, IssueSeverity.Warning,
        "Page uses a broken file",
        """The page loads a script, stylesheet, iframe, video or audio file whose URL returns a 4xx or 5xx error or doesn't respond, directly or at the end of its redirects. Images are the broken image check's. On another site, statuses often sent only to crawlers (401, 403, 429, 503) don't count. The detail gives the count, and where the first goes wrong.""",
        """Restore each missing file, or correct the URL that loads it in the page or its template; remove references to anything no longer used, such as an old tracking script.""")
    {
        Why = """A missing stylesheet or script can leave the page unstyled or broken for visitors, and for Google, which renders pages with their files to see what visitors see. A broken third-party tag often changes nothing anyone sees, which is why this is a warning, not an error like a broken image.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/javascript/javascript-seo-basics",
        References = IssueReferences.Targets,
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.RelatedUrl,
        TargetFacts = ["statusCode", "final", "kind", "position", "links"]
    };

    public static readonly IssueDefinition LINKS_TO_LOCALHOST = new("links-to-localhost", IssueCategory.Links, IssueSeverity.Warning,
        "Links to localhost or a private address",
        """The page links to, or loads something from, localhost, a .localhost name, or a loopback or private network address (127.x, ::1, 10.x, 172.16 to 172.31, 192.168.x, fc00::/7). On a crawl that started at such an address, as a development copy does, that machine's own addresses aren't counted. The detail gives the count and the first.""",
        """Replace each with the live site's URL, or remove it. They usually come from content or settings copied over from a development or staging copy of the site.""")
    {
        Why = """Such an address points at the visitor's own computer or network, so a link to one is dead for everyone, and a file loaded from one is missing. Browsers may also warn about, or block, a public page reaching into a private network.""",
        References = IssueReferences.Targets,
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.RelatedUrl,
        TargetFacts = ["statusCode", "text", "position"]
    };

    public static readonly IssueDefinition FEW_INTERNAL_OUTLINKS = new("few-internal-outlinks", IssueCategory.Links, IssueSeverity.Notice,
        "Page with few or no internal links",
        """This indexable page links with <a href> to fewer than 3 other URLs on the site (the default, which can be changed in the analysis settings), followed or not. Links to the page itself, and to sections of it, don't count.""",
        """Link the page to related pages and to its section, or give it the site's usual header, menu and footer if it's missing them.""")
    {
        Why = """A page with nowhere else to go is a dead end for visitors, and gives search engines no path onward from it. It's common on landing pages built without the site's template. The page itself is fine; only the links from it are missing.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/links-crawlable#internal-links",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.None,
        Evidence = ["outlinks"]
    };

    public static readonly IssueDefinition GENERIC_ANCHOR_TEXT = new("generic-anchor-text", IssueCategory.Links, IssueSeverity.Notice,
        "Links with generic text",
        """The page has internal links whose whole text is generic, such as "click here", "read more", "learn more", "here" or "more", rather than saying where they go. Image links aren't counted. The detail gives each text, and how often.""",
        """Rewrite each link's text to name what's at the other end, such as "Read the delivery policy" instead of "Read more".""")
    {
        Why = """Google uses link text to understand the page linked to, and calls texts like "click here" and "read more" too generic. Screen reader users often list a page's links on their own, where "read more" says nothing; WCAG accepts it only where the text around the link makes its purpose clear.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/links-crawlable#anchor-text-placement",
        References = IssueReferences.Targets,
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.None,
        TargetFacts = ["text", "position", "statusCode"]
    };

    public static readonly IssueDefinition NON_INDEXABLE_INLINKS_ONLY = new("non-indexable-inlinks-only", IssueCategory.Links, IssueSeverity.Notice,
        "Linked only from non-indexable pages",
        """Every page that links to this indexable page with a followed link is itself not indexable: noindex, canonicalised to another URL, or a redirect by an instant meta refresh. Links reaching it through a redirect count. The start and home pages aren't reported, and it's only checked when the crawl read every page that could link to it.""",
        """Link to the page from at least one indexable page where it belongs, such as its category or a related page.""")
    {
        Why = """Search engines find and value pages largely through links from pages they index. Links only from pages they're told to drop, or to treat as copies, still let them reach this page but count for little, and if those pages go the page is left with no links. It's often a page linked only from tag, filter or paginated pages that are noindex.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/links-crawlable#internal-links",
        References = IssueReferences.Sources,
        Subject = IssueSubject.Page,
        Recheck = [],
        Grouping = IssueGrouping.None,
        Evidence = ["inlinks", "linkingPages"]
    };

    public static readonly IssueDefinition DEEP_PAGE = new("deep-page", IssueCategory.Links, IssueSeverity.Notice,
        "Page many clicks from the home page",
        """This indexable page is more than 4 clicks from where the crawl started by the shortest path of links (the default, which can be changed in the analysis settings), as the crawl recorded it. Pages found only through a sitemap aren't measured: the orphan page check covers them.""",
        """Link to the page from higher up: its category, a hub page, the home page or the menu; or add levels of navigation, such as subcategories, so lists don't run deep.""")
    {
        Why = """The further a page is from the home page, the less often search engines tend to crawl it and the less weight they give it, and visitors rarely click that far. No standard sets a depth; 4 is a common rule of thumb. Large shops have deep pages by design, so look at which pages are deep rather than how many.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/links-crawlable#internal-links",
        Subject = IssueSubject.Page,
        Recheck = [],
        Grouping = IssueGrouping.None,
        Evidence = ["depth", "inlinks"]
    };

    public static readonly IssueDefinition TOO_MANY_LINKS = new("too-many-links", IssueCategory.Links, IssueSeverity.Notice,
        "Page with a very high number of links",
        """This indexable page has more than 1,000 internal links or more than 100 links to other sites (the defaults, which can be changed in the analysis settings), counting every <a href>, repeats included. A page's links are read up to 10,000.""",
        """Cut the links down to the ones visitors use: trim mega-menus and footers, split long lists into pages, and drop repeated links to the same page.""")
    {
        Why = """Each link's share of the page's weight shrinks as links are added, and huge lists of links are hard for visitors to use. Google no longer sets a limit, so treat this as a prompt to look, not a fault in itself.""",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.None,
        Evidence = ["outlinks"]
    };

    public static readonly IssueDefinition SLOW_EXTERNAL_LINKS = new("slow-external-links", IssueCategory.Links, IssueSeverity.Notice,
        "Links to slow external pages",
        """The page links to pages on other sites that took more than 3 seconds to answer when the crawl checked them (the default, which can be changed in the analysis settings), whether they returned a page or a redirect. It isn't checked when links to other sites weren't. The detail gives the count and the slowest time.""",
        """Open the slowest links to see whether they're slow for you too; replace a link to a site that stays slow with a better source, or remove it.""")
    {
        Why = """Visitors who follow the link wait, and may blame your site for it. The other site's server isn't yours to fix, only the link, and one slow answer during a crawl can be a blip, so check before changing anything.""",
        References = IssueReferences.Targets,
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.RelatedUrl,
        TargetFacts = ["statusCode", "responseMs", "text", "position"]
    };

    public static readonly IssueDefinition PAGINATION_NOT_LINKED = new("pagination-not-linked", IssueCategory.Links, IssueSeverity.Notice,
        "Pagination URL not linked with an <a>",
        """The page has a rel="next" or rel="prev" link to another of the site's pages but no ordinary <a href> link to it. The detail names the first.""",
        """Link each page of the series to the next and previous ones with ordinary <a href> links, such as "Next" and page numbers, as well as or instead of rel="next" and rel="prev".""")
    {
        Why = """Google no longer uses rel="next" and rel="prev", and finds later pages of a series only through ordinary links, so pages reached only through these tags may never be crawled. Other search engines and browsers may still read them, so they do no harm beside real links.""",
        LearnMore = "https://developers.google.com/search/docs/specialty/ecommerce/pagination-and-incremental-page-loading",
        References = IssueReferences.Targets,
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.RelatedUrl,
        TargetFacts = ["statusCode", "indexability"]
    };

    public static readonly IssueDefinition PAGINATION_BROKEN = new("pagination-broken", IssueCategory.Links, IssueSeverity.Notice,
        "Pagination URL broken or redirected",
        """A rel="next" or rel="prev" link of the page names a URL that returns an error, doesn't respond, or redirects. The detail says which and how.""",
        """Point rel="next" and rel="prev" at the working, final URLs of the series' pages, or remove the ones that lead nowhere.""")
    {
        Why = """Browsers and search engines that read these links expect them to name the series' pages themselves. One that fails or redirects usually means the series' URLs changed and the template didn't follow.""",
        LearnMore = "https://developers.google.com/search/docs/specialty/ecommerce/pagination-and-incremental-page-loading",
        References = IssueReferences.Targets,
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.RelatedUrl,
        TargetFacts = ["statusCode", "final"]
    };

    public static readonly IssueDefinition PAGINATION_TO_NON_INDEXABLE = new("pagination-to-non-indexable", IssueCategory.Links, IssueSeverity.Notice,
        "Pagination URL not indexable",
        """A rel="next" or rel="prev" link of the page names a page that isn't indexable: noindex, or canonicalised to another URL. The detail says which.""",
        """Make the series' pages indexable, each with a canonical to itself, or point rel="next" and rel="prev" at the pages that are.""")
    {
        Why = """The pages of a series list what's on the site; if they're noindex or canonicalised away, what's listed only on them is found only through pages search engines are told to drop. It's right for filtered or sorted copies of a list, not for the list's own pages.""",
        LearnMore = "https://developers.google.com/search/docs/specialty/ecommerce/pagination-and-incremental-page-loading#use-urls-correctly",
        References = IssueReferences.Targets,
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.RelatedUrl,
        TargetFacts = ["statusCode", "indexability", "canonical"]
    };

    public static readonly IssueDefinition PAGINATION_SEQUENCE_ERROR = new("pagination-sequence-error", IssueCategory.Links, IssueSeverity.Notice,
        "Pagination sequence error",
        """The page's rel="next" and rel="prev" links don't make a sequence: it has more than one of either, one names the page itself, the page its rel="next" names doesn't name it back with rel="prev", or following rel="next" comes back to a page already passed. The detail says which.""",
        """Give each page of the series one rel="next" to the page after it and one rel="prev" to the page before it, and none on the last and first pages respectively.""")
    {
        Why = """Tools and browsers that read these links follow them as a chain, and a chain that branches, loops or doesn't link back is usually a template bug that also shows in the series' visible links. Google no longer uses them, so this is about tidiness more than search.""",
        LearnMore = "https://developers.google.com/search/docs/specialty/ecommerce/pagination-and-incremental-page-loading",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.PageDetail, FaultOf = IssueFaults.PaginationFault
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
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.None,
        Evidence = ["metaRobots", "xRobotsTag"]
    };

    public static readonly IssueDefinition NOFOLLOW_PAGE = new("nofollow-page", IssueCategory.Indexability, IssueSeverity.Warning,
        "Nofollow page",
        """The page has a nofollow (or none) directive in a robots meta tag or an X-Robots-Tag header, which asks search engines not to follow any of its links.""",
        """Remove nofollow from the robots meta tag or X-Robots-Tag header. To qualify individual links to other sites, use rel="nofollow", "sponsored" or "ugc" on those links instead.""")
    {
        Why = """Search engines that honour it won't use the page's links to discover the pages they point to, so pages linked only from here may go unfound. A page-wide nofollow is rarely intended on a normal page; it's usually left over from a staging setup or set by a plugin.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/robots-meta-tag#nofollow",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.None,
        Evidence = ["metaRobots", "xRobotsTag"]
    };

    public static readonly IssueDefinition BLOCKED_BY_ROBOTS = new("blocked-by-robots", IssueCategory.Indexability, IssueSeverity.Notice,
        "Blocked by robots.txt",
        """The crawler didn't fetch this internal URL, because robots.txt disallows it, or because robots.txt itself couldn't be read, which crawlers treat as disallowing everything. The detail names the rule that refused it and its line, or says robots.txt couldn't be read. Not checked on a crawl that was told to ignore robots.txt.""",
        """If the URL should be crawled, change or remove the Disallow rule that matches it. If it's meant to be blocked, avoid linking to it from crawlable pages where you can, and if it must stay out of search, allow crawling and use noindex instead.""")
    {
        Why = """Search engines that obey robots.txt can't read the page, so its content, links and any noindex on it go unseen, yet Google can still index the URL without its content, with no description, if links point to it. Blocking is right for URLs with no search value, such as baskets, internal search or filter combinations. It's a problem when a broad rule catches pages you want in search, or the CSS and JavaScript that pages need to render.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/robots/intro",
        References = IssueReferences.Sources,
        Subject = IssueSubject.Url,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.ResponseDetail,
        Evidence = ["robotsRule"]
    };

    public static readonly IssueDefinition SNIPPET_BLOCKED = new("snippet-blocked", IssueCategory.Indexability, IssueSeverity.Notice,
        "Search snippet disabled",
        """This indexable page has a nosnippet or max-snippet:0 directive in a robots meta tag or X-Robots-Tag header.""",
        """Remove the directive if it isn't intended. To keep only part of the page out of snippets, use the data-nosnippet attribute on that part, or a max-snippet length instead of 0.""")
    {
        Why = """Google shows the page in results without a text snippet or video preview, and doesn't use its content as direct input for AI Overviews or AI Mode. That can be intended, for example for sensitive content, but a result with no description tells searchers less about the page.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/robots-meta-tag#nosnippet",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.PageDetail,
        Evidence = ["metaRobots", "xRobotsTag"]
    };

    public static readonly IssueDefinition ROBOTS_TXT_UNREACHABLE = new("robots-txt-unreachable", IssueCategory.Indexability, IssueSeverity.Error,
        "robots.txt unavailable",
        """robots.txt on the site's main origin (the start URL's, or the home page's after redirects) returned a 5xx error or 429, or didn't respond. Reported once, on the home page. Not checked on a crawl told to ignore robots.txt that never read it.""",
        """Make /robots.txt answer quickly with 200 and your rules, or with 404 if you don't need any. Check that the firewall, CDN or rate limiting doesn't treat requests for it differently from pages.""")
    {
        Why = """Google treats a robots.txt it can't fetch as a server error: it stops crawling the whole site for the first 12 hours, then uses its last good copy for up to 30 days (or assumes no restrictions if it has none). If it's still failing after that and the site is otherwise up, Google acts as if there were no robots.txt. Until it's fixed, new and changed pages aren't crawled, so fix this first.""",
        LearnMore = "https://developers.google.com/crawling/docs/robots-txt/robots-txt-spec#http-status-codes",
        Subject = IssueSubject.Url,
        Recheck = [RecheckKind.RobotsTxt],
        Grouping = IssueGrouping.None,
        SiteWide = true,
        ReportedOnce = true, About = IssueAbout.RobotsTxt
    };

    public static readonly IssueDefinition ROBOTS_TXT_UNREACHABLE_OTHER_ORIGIN = new("robots-txt-unreachable-other-origin", IssueCategory.Indexability, IssueSeverity.Warning,
        "robots.txt unavailable on another of the site's origins",
        """robots.txt on another of the site's origins, such as a subdomain or the http:// version, returned a 5xx error or 429, or didn't respond; the detail names the origin. Origins reached only by the crawler's own probe of the site's other versions aren't included. Not checked on a crawl told to ignore robots.txt, which doesn't read it there.""",
        """Make robots.txt on that origin return 200, or 404 if it needs no rules. If the origin shouldn't exist, stop linking to it.""")
    {
        Why = """Each origin (scheme, host and port) has its own robots.txt, so only that origin is affected: Google stops crawling it for 12 hours, then relies on its last good copy for up to 30 days. The main site carries on as normal. It matters if pages or files you want in search live there, or if that origin's redirects to the main site haven't been picked up yet.""",
        LearnMore = "https://developers.google.com/crawling/docs/robots-txt/robots-txt-spec#file-location-and-range-of-validity",
        Subject = IssueSubject.Url,
        Recheck = [RecheckKind.RobotsTxt],
        Grouping = IssueGrouping.None,
        SiteWide = true,
        ReportedOnce = true, About = IssueAbout.RobotsTxt
    };

    public static readonly IssueDefinition ROBOTS_TXT_MISSING = new("robots-txt-missing", IssueCategory.Indexability, IssueSeverity.Notice,
        "robots.txt missing or refused",
        """robots.txt returned a 4xx error other than 429 (usually 404, sometimes 401 or 403 from a firewall), or redirected too many times, on the main origin or another of the site's origins. Reported on the home page; the detail lists the origins. A crawl told to ignore robots.txt judges the start URL's origin alone, and not at all when it never read robots.txt.""",
        """Add a robots.txt at the origin's root, even if it only holds a Sitemap: line with your XML sitemap's full URL. If a firewall answers it with 401 or 403, let it through.""")
    {
        Why = """Crawling isn't affected: Google treats a missing robots.txt as having no restrictions, so everything can be crawled. What's missing is a Sitemap: line, which Google, Bing and other search engines read to find your sitemaps, and a way to keep crawlers out of URLs with no search value should you need one. A 401 or 403 meant to keep crawlers out has the opposite effect.""",
        LearnMore = "https://developers.google.com/crawling/docs/robots-txt/create-robots-txt",
        Subject = IssueSubject.Url,
        Recheck = [RecheckKind.RobotsTxt],
        Grouping = IssueGrouping.None,
        ReportedOnce = true, About = IssueAbout.RobotsTxt
    };

    public static readonly IssueDefinition SITE_HIDDEN_FROM_SEARCH = new("site-hidden-from-search", IssueCategory.Indexability, IssueSeverity.Error,
        "Most pages hidden from search",
        """The home page is noindex, and so are more than half of the site's pages, so most of the site can't appear in search results. The home page is the page the start URL leads to, after its redirects; while it can be indexed, nothing is reported, however many other pages are noindex. The detail gives the numbers, as in "212 of 240 pages are noindex": the noindex pages, out of the pages the crawl read less the canonicalised ones. A canonicalised page (one that isn't noindex and whose canonical names another URL) is a variant of the page it names, so it isn't in the total. A canonical that leads back to the page, to a broken URL, or to the same page on another version of the site (www or not, http or https) doesn't make a variant. Not checked on a crawl that didn't start at the site's root. Reported once, on the home page.""",
        """Find what sets noindex on every page: a "discourage search engines" setting or a staging configuration left on, a robots meta tag in the template, or an X-Robots-Tag header from the server or CDN. Remove it from the pages that should appear in search, then crawl again.""")
    {
        Why = """Google drops a noindex page from its results, so most of this site can't be found in search. It's nearly always a launch mistake: a staging site's noindex or a "discourage search engines" setting left on. If the pages are meant to stay out, as on a private site or a staging copy, check the noindex pages and ignore this.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/block-indexing",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.None,
        SiteWide = true,
        ReportedOnce = true,
        Evidence = ["metaRobots", "xRobotsTag"]
    };


    public static readonly IssueDefinition ROBOTS_DIRECTIVES_CONFLICT = new("robots-directives-conflict", IssueCategory.Indexability, IssueSeverity.Warning,
        "Meta robots and X-Robots-Tag disagree",
        """The page's robots meta tags and its X-Robots-Tag header give opposite rules: one says noindex (or none) where the other says index (or all), or one says nofollow where the other says follow. Only rules for all crawlers, Googlebot and this crawler count. The detail quotes both.""",
        """Decide which rule the page should have, then make both say it, or remove one. The meta tag comes from the page's template or SEO plugin; the header from the server, CDN or CMS.""")
    {
        Why = """Search engines combine the two, and where their rules conflict Google applies the more restrictive one, so a page whose HTML says index is dropped from search if its header says noindex. It usually means two systems each set robots rules, and whoever checks only one of them sees the wrong answer. A rule only one of them mentions isn't a conflict.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/robots-meta-tag#combining-robots-meta-tag-directives",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.PageDetail,
        Evidence = ["metaRobots", "xRobotsTag"]
    };

    public static readonly IssueDefinition NOINDEX_NEAR_HOME = new("noindex-near-home", IssueCategory.Indexability, IssueSeverity.Warning,
        "Noindex page close to the home page",
        """This noindex page is one or two clicks from where the crawl started, where a site's important sections usually are. The home page isn't reported, nor a page whose address names a private area: cart, basket, checkout, account, login, register, logout, wishlist, search, thank-you, order-received or password pages. The page is in the noindex check too.""",
        """If the page should be in search, remove the noindex from its robots meta tag or X-Robots-Tag header. If it's meant to stay out, such as a members' or legal page, leave it: this is a prompt to check.""")
    {
        Why = """Pages linked from the home page or its menus are usually ones a site wants found, so a noindex there is more often a leftover from a launch, a staging setting or a plugin than a choice. Google drops noindex pages from its results entirely. Sites do keep a few such pages out of search on purpose, which is fine.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/block-indexing",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.None,
        Evidence = ["metaRobots", "xRobotsTag", "depth", "inlinks"]
    };

    public static readonly IssueDefinition UNAVAILABLE_AFTER_PASSED = new("unavailable-after-passed", IssueCategory.Indexability, IssueSeverity.Warning,
        "unavailable_after date has passed",
        """The page's robots meta tag or X-Robots-Tag header has an unavailable_after rule whose date was already past when the crawl fetched the page. A date in a format search engines can't read is ignored, as Google ignores it, and a page that's noindex anyway isn't reported.""",
        """If the page should stay in search, remove the unavailable_after rule or move its date on. If the content really has expired, redirect the page to its replacement or let it return 404 or 410.""")
    {
        Why = """Google stops showing a page in its results after its unavailable_after date, as if it were noindex. The rule is meant for event and offer pages, and its date is easily forgotten when the page is reused or kept. Ignore this if the page was meant to drop out.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/robots-meta-tag#unavailable_after",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.None,
        Evidence = ["metaRobots", "xRobotsTag"]
    };

    public static readonly IssueDefinition MEDIA_PREVIEW_BLOCKED = new("media-preview-blocked", IssueCategory.Indexability, IssueSeverity.Notice,
        "Image or video previews blocked",
        """This indexable page has a robots rule that keeps its images or video out of search: noimageindex, max-image-preview:none or max-video-preview:0. The detail names the rules found.""",
        """Remove the rule if the page's pictures or video should appear in search; max-image-preview:large allows the largest previews. If they're meant to stay out, leave it.""")
    {
        Why = """Google won't show the page's images in its results or image search, or a preview of its video, so the result stands out less. That's often deliberate, for photos licensed for one use or private media, but some plugins set it for a whole site without anyone noticing.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/robots-meta-tag#max-image-preview",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.PageDetail,
        Evidence = ["metaRobots", "xRobotsTag"]
    };

    public static readonly IssueDefinition TRANSLATION_BLOCKED = new("translation-blocked", IssueCategory.Indexability, IssueSeverity.Notice,
        "Translation blocked (notranslate)",
        """This indexable page has a notranslate robots rule, so Google won't offer a translation of it in search results.""",
        """Remove notranslate if people searching in other languages should be offered the page; leave it where a translation could mislead, such as on legal text.""")
    {
        Why = """Without it, Google can show the result's title and snippet in the searcher's language and link to a machine translation of the page, which reaches visitors the page couldn't otherwise. It's a choice, not a fault, but it's sometimes left in a template by mistake.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/robots-meta-tag#notranslate",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.None,
        Evidence = ["metaRobots", "xRobotsTag"]
    };

    public static readonly IssueDefinition INDEXIFEMBEDDED_WITHOUT_NOINDEX = new("indexifembedded-without-noindex", IssueCategory.Indexability, IssueSeverity.Notice,
        "indexifembedded without noindex",
        """The page's robots rules include indexifembedded but not noindex, so the rule does nothing.""",
        """If the page's content should be indexed only where other pages embed it in an iframe, add noindex beside indexifembedded. If not, remove indexifembedded.""")
    {
        Why = """indexifembedded lets Google index a page's content where another page embeds it, though the page itself is noindex; without noindex it has no effect. It harms nothing, but it shows the robots rules aren't doing what someone meant.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/robots-meta-tag#indexifembedded",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.None,
        Evidence = ["metaRobots", "xRobotsTag"]
    };

    // Canonicals
    public static readonly IssueDefinition CANONICALISED_PAGE = new("canonicalised-page", IssueCategory.Canonicals, IssueSeverity.Notice,
        "Canonicalised page",
        """The page's canonical (in the <head> or an HTTP Link header) names a different URL, so the page asks to be treated as a duplicate of it. Canonicals that lead back to this page or to a broken URL don't count.""",
        """Check the canonical is intended. If this page should rank, make its canonical point to itself; if it's a true duplicate, link to the canonical URL rather than this one within the site.""")
    {
        Why = """Google treats a canonical as a strong hint, not a rule: it usually indexes the named URL and folds this page's signals into it, but can choose differently if other signals disagree. That's right for parameter, sorting and print versions of a page. It's a problem when the page has its own content and should rank, as when a template points every canonical at the home page or the first page of a series.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/canonicalization",
        References = IssueReferences.Targets,
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.RelatedUrl,
        TargetFacts = ["statusCode", "final", "indexability", "canonical", "flags"]
    };

    public static readonly IssueDefinition CANONICAL_MISSING = new("canonical-missing", IssueCategory.Canonicals, IssueSeverity.Notice,
        "Missing canonical",
        """This indexable page has no canonical, neither a <link rel="canonical"> in the <head> nor an HTTP Link header.""",
        """Add <link rel="canonical" href="..."> to the <head> with the page's own absolute URL; most CMSs and SEO plugins can do this for every page.""")
    {
        Why = """Google doesn't require one: without it, Google picks the best URL itself when the same content is reachable at several addresses, such as with tracking parameters or with and without a trailing slash. A canonical pointing to the page itself makes your preference explicit, and Google recommends one on canonical pages. Low priority on its own, but it helps most on sites with many URL variants.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/consolidate-duplicate-urls#rel-canonical-link-method",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
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
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.None
    };

    public static readonly IssueDefinition CANONICAL_BROKEN = new("canonical-broken", IssueCategory.Canonicals, IssueSeverity.Error,
        "Canonical points to a broken URL",
        """The page's canonical names a URL that returns a 4xx or 5xx error or doesn't respond, directly or at the end of its redirects (or its redirects loop).""",
        """Point the canonical at the working URL you want indexed, usually this page's own URL, and check templates that build canonicals from a hard-coded host or path.""")
    {
        Why = """A canonical has to name a working page: Google's own checklist is to make sure the target exists and isn't an error or soft 404. One that leads to an error can't take this page's place, so expect search engines to disregard it and judge this page on its own. The cause is usually a stale URL, such as a staging host, an old URL structure or a mistyped path.""",
        LearnMore = "https://developers.google.com/search/blog/2013/04/5-common-mistakes-with-relcanonical",
        References = IssueReferences.Targets,
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.RelatedUrl,
        TargetFacts = ["statusCode", "final", "flags"]
    };

    public static readonly IssueDefinition CANONICAL_TO_REDIRECT = new("canonical-to-redirect", IssueCategory.Canonicals, IssueSeverity.Warning,
        "Canonical points to a redirect",
        """The page's canonical names a URL that redirects. The detail gives the URL the redirects end at, or says that they lead back to this page. Where the crawl didn't check that URL, or was refused there, the detail says so: it is where the redirects lead, and may not be where they end.""",
        """Change the canonical to the redirect's final URL, which the detail gives; when the redirect leads back here, that's this page's own URL. If the detail says the URL wasn't checked, or was refused, open it first and use the address you end up at: it may redirect again.""")
    {
        Why = """A canonical should name the final URL you want indexed. Naming a redirect sends search engines two signals that disagree: the canonical says to index the named URL, and its redirect says to index another. When the redirect leads back to this page, such as a canonical to /page on /page/ when /page redirects to /page/, this page most likely stays the indexed one, but the canonical is still wrong.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/consolidate-duplicate-urls",
        References = IssueReferences.Targets,
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.RelatedUrl,
        TargetFacts = ["statusCode", "final", "flags"]
    };

    public static readonly IssueDefinition CANONICAL_TO_NON_INDEXABLE = new("canonical-to-non-indexable", IssueCategory.Canonicals, IssueSeverity.Warning,
        "Canonical points to a non-indexable page",
        """The page's canonical names another URL on the site that can't be indexed itself: it's noindex, canonicalised to yet another URL, or blocked by robots.txt. A URL that refused the crawler (a firewall, a bot challenge or a rate limit) isn't judged, as that says nothing about search engines; it's reported as blocked itself.""",
        """Point the canonical at the final, indexable URL. If the target isn't meant to be indexed, this page probably shouldn't name it as canonical.""")
    {
        Why = """A canonical asks search engines to index the named URL instead of this one, so if that URL is noindex, blocked or points elsewhere in turn, the signals contradict each other and the outcome is up to Google. Google's own checklist includes making sure the canonical target isn't noindex. Canonical chains (A to B to C) usually come from content that moved more than once.""",
        LearnMore = "https://developers.google.com/search/blog/2013/04/5-common-mistakes-with-relcanonical",
        References = IssueReferences.Targets,
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.RelatedUrl,
        TargetFacts = ["statusCode", "indexability", "canonical", "final", "flags"]
    };

    public static readonly IssueDefinition CANONICAL_OUTSIDE_HEAD = new("canonical-outside-head", IssueCategory.Canonicals, IssueSeverity.Warning,
        "Canonical outside <head>",
        """The page's only canonical links are in the <body> as browsers parse it, and there's no canonical HTTP Link header.""",
        """Move the canonical into the <head>, as early as you can, and remove the element that ends the head early (or move it into the body).""")
    {
        Why = """Google only accepts rel="canonical" in the <head>, so the page has no canonical in effect. Often the tag is in the head in the source, but an element that doesn't belong there, such as an <img>, a <div> or stray text from a script or plugin, ends the head early and pushes everything after it into the body. The same break can push other head tags, such as hreflang, out too.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/consolidate-duplicate-urls#rel-canonical-link-method",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
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
        Recheck = [RecheckKind.Urls],
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
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.PageDetail, FaultOf = IssueFaults.InvalidCanonical
    };

    public static readonly IssueDefinition NOINDEX_WITH_CANONICAL = new("noindex-with-canonical", IssueCategory.Canonicals, IssueSeverity.Warning,
        "Noindex page with a canonical to another URL",
        """The page is noindex and its canonical names a different URL, one that doesn't lead back to this page or to a broken URL.""",
        """If the page duplicates the other URL, remove the noindex and keep the canonical. If it should stay out of search, keep the noindex and make the canonical point to the page itself, or remove it.""")
    {
        Why = """The two signals say different things: noindex asks for the page to be dropped from search, while the canonical says it's a duplicate whose signals belong to the other URL. Google hasn't documented which wins, and its guidance is to use rel="canonical", not noindex, to choose between duplicates on one site, since noindex removes the page from search entirely.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/consolidate-duplicate-urls#best-practices",
        References = IssueReferences.Targets,
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.RelatedUrl,
        Evidence = ["metaRobots", "xRobotsTag"],
        TargetFacts = ["statusCode", "indexability", "flags"]
    };


    public static readonly IssueDefinition CANONICAL_TO_FIRST_PAGE = new("canonical-to-first-page", IssueCategory.Canonicals, IssueSeverity.Warning,
        "Paginated page canonicalised to page 1",
        """This later page of a paginated series, page 2 onward by its rel="prev" link or a page number in its URL (?page=2, ?p=2, /page/2/), names the series' first page as its canonical. It isn't reported as canonicalised too.""",
        """Give each page of the series a canonical to itself, and keep ordinary links between the pages so every one can be reached.""")
    {
        Why = """The canonical asks search engines to treat page 2 as a copy of page 1, which it isn't. What's listed only on later pages is then found only through pages that ask to be dropped, so it may be crawled less, or not at all. Google's pagination guidance says not to use the first page as the canonical; it's a common setting in SEO plugins and shop themes.""",
        LearnMore = "https://developers.google.com/search/docs/specialty/ecommerce/pagination-and-incremental-page-loading#use-urls-correctly",
        References = IssueReferences.Targets,
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.RelatedUrl,
        TargetFacts = ["statusCode", "indexability", "canonical", "flags"]
    };

    public static readonly IssueDefinition CANONICAL_OTHER_DOMAIN = new("canonical-other-domain", IssueCategory.Canonicals, IssueSeverity.Warning,
        "Canonical points to another site",
        """The page's canonical names a URL on another host, outside the crawl, so the page asks to be treated as a copy of a page on another site. It isn't reported as canonicalised too. The detail names the host.""",
        """If this page is a copy of an article first published elsewhere, that's right: leave it. Otherwise point the canonical at this site's own URL for the page; a staging, old or development host in it usually comes from a template or a setting.""")
    {
        Why = """Search engines may index the other site's page instead of this one and fold this page's signals into it, so it can disappear from search. That's correct for content republished from another site, and a disaster when a template or a migration leaves every canonical pointing at a staging host or the old domain.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/consolidate-duplicate-urls",
        References = IssueReferences.Targets,
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.RelatedUrl,
        TargetFacts = ["statusCode", "final", "indexability", "canonical"]
    };

    public static readonly IssueDefinition CANONICAL_LOOP = new("canonical-loop", IssueCategory.Canonicals, IssueSeverity.Warning,
        "Canonical loop",
        """Following this page's canonical, page to page and through any redirects, comes back to this page: two or more pages name each other as their canonical. The detail lists the loop. Its pages aren't reported as canonicalised to a non-indexable page too.""",
        """Choose the one page of the loop that should be indexed, give it a canonical to itself, and point the others' canonicals at it.""")
    {
        Why = """Each page asks to be dropped for another that asks the same, so the canonicals cancel out and Google picks a page itself, which may not be the one you meant. It usually comes from two pages each set to the other by hand, or from one rule that rewrites canonicals undoing another.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/consolidate-duplicate-urls",
        References = IssueReferences.Targets,
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.RelatedUrl,
        TargetFacts = ["statusCode", "indexability", "canonical"]
    };

    public static readonly IssueDefinition CANONICAL_ON_REDIRECT = new("canonical-on-redirect", IssueCategory.Canonicals, IssueSeverity.Notice,
        "Canonical header on a redirect",
        """This internal URL redirects, and its response also has a Link header naming, as canonical, a different URL from the one its redirects end at. The detail gives both.""",
        """Remove the Link header from the redirect, or make it name the redirect's final URL. It usually comes from a server or CDN rule that adds the header to every response.""")
    {
        Why = """Search engines follow the redirect and ignore the rest of the response, so the header has no effect on search. It misleads only people and tools that read the headers, but it can show that a rule meant for pages is applied to every response.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/consolidate-duplicate-urls#rel-canonical-header-method",
        References = IssueReferences.Sources,
        Subject = IssueSubject.Url,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.None,
        Evidence = ["redirectChain"]
    };

    public static readonly IssueDefinition CANONICAL_FRAGMENT = new("canonical-fragment", IssueCategory.Canonicals, IssueSeverity.Notice,
        "Canonical contains a fragment (#)",
        """A canonical in the <head> or an HTTP Link header has a fragment, a part after # such as #reviews. The detail gives the fragment.""",
        "Remove the # part from the canonical, so it's the page's plain URL.")
    {
        Why = """Google says not to use a fragment in a canonical, as it generally doesn't support them: it drops the fragment, so the canonical usually works as meant all the same. The markup is still wrong, and a fragment can be a sign the canonical is built from the browser's address rather than set on purpose.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/consolidate-duplicate-urls#rel-canonical-link-method",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.PageDetail
    };

    public static readonly IssueDefinition CANONICAL_INVALID_ATTRIBUTE = new("canonical-invalid-attribute", IssueCategory.Canonicals, IssueSeverity.Notice,
        "Canonical link with hreflang, lang, media or type",
        """A canonical <link> in the <head> also has an hreflang, lang, media or type attribute. The detail names them. A crawl made before the crawler noted these attributes isn't checked.""",
        """Remove those attributes from the canonical link. To point at another language or a mobile version, add a separate <link rel="alternate"> with hreflang or media instead.""")
    {
        Why = """Those attributes describe an alternate version of a page, not the page's own preferred URL, so search engines may not take the link as a canonical at all. The usual cause is an alternate link written as rel="canonical" by mistake. No Google page says such a canonical is ignored, so treat this as tidying up.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/consolidate-duplicate-urls#rel-canonical-link-method",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.PageDetail
    };

    public static readonly IssueDefinition CANONICAL_UNLINKED = new("canonical-unlinked", IssueCategory.Canonicals, IssueSeverity.Notice,
        "Canonical URL that no page links to",
        """Another page names this indexable page as its canonical, but no crawled page links to it, not even with a nofollow link or through a redirect, and no sitemap lists it (a sitemap page nothing links to is an orphan page). It's only checked when the crawl read every page that could link to it.""",
        """Link to this page wherever its duplicates are linked, such as menus and listings, or change those links to point here. If it shouldn't be the canonical, point the other pages' canonicals at one that is linked.""")
    {
        Why = """A canonical tells search engines which URL to index, and links tell them it matters: a canonical page nothing links to sends mixed signals, and Google may choose a linked duplicate instead. Visitors browsing the site only ever reach the duplicates.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/links-crawlable#internal-links",
        References = IssueReferences.Sources,
        Subject = IssueSubject.Page,
        Recheck = [],
        Grouping = IssueGrouping.None,
        Evidence = ["inlinks", "linkingPages"]
    };

    // Sitemaps
    public static readonly IssueDefinition SITEMAP_NON_INDEXABLE = new("sitemap-non-indexable", IssueCategory.Sitemaps, IssueSeverity.Warning,
        "Non-indexable URL in sitemap",
        """A sitemap lists this internal URL, but it can't be indexed as listed: it redirects, returns a 4xx or 5xx error, is blocked by robots.txt, is noindex or names another URL as canonical. URLs the crawler couldn't check, because of timeouts or firewall blocks, aren't included.""",
        """Remove the URL from the sitemap, or replace it with the final, indexable URL, such as the redirect target or the canonical. If the sitemap is generated, fix its filter rather than editing the file.""")
    {
        Why = """A sitemap should list the URLs you want in search results, and Google treats every URL in it as a suggested canonical. Listing URLs that redirect, fail or ask not to be indexed contradicts your other signals and spends crawls on URLs that won't be indexed. It's usually a sitemap generator that doesn't filter by status, noindex or canonical.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/sitemaps/build-sitemap#general-guidelines",
        References = IssueReferences.Sources,
        Subject = IssueSubject.Url,
        Recheck = [RecheckKind.Urls, RecheckKind.Sitemaps],
        Grouping = IssueGrouping.ResponseDetail,
        Evidence = ["indexability", "finalUrl", "canonicalUrl"]
    };

    public static readonly IssueDefinition NOT_IN_SITEMAP = new("not-in-sitemap", IssueCategory.Sitemaps, IssueSeverity.Notice,
        "Indexable page not in a sitemap",
        """This indexable page isn't in any sitemap the crawl read. It's only checked when the crawl finished and every declared sitemap could be read.""",
        """Add the page to your XML sitemap, ideally by making sure the generator includes every indexable page type.""")
    {
        Why = """Search engines can still find it through links, and on a small, well-linked site that's enough: Google says sites of about 500 pages or fewer that are comprehensively linked may not need a sitemap. On large or fast-changing sites, a complete sitemap helps new and updated pages get discovered sooner. Leaving out some page types, such as filtered or paginated listings, can be deliberate. Login, account and basket pages don't belong in a sitemap: they're better kept out of search with noindex.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/sitemaps/overview#do-i-need-a-sitemap",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls, RecheckKind.Sitemaps],
        Grouping = IssueGrouping.None,
        Evidence = ["title", "depth"], RowOrder = IssueRowOrder.FewestLinksFirst
    };

    public static readonly IssueDefinition SITEMAP_ERROR = new("sitemap-error", IssueCategory.Sitemaps, IssueSeverity.Warning,
        "Sitemap can't be read",
        """A sitemap listed in robots.txt, a sitemap index or the crawl settings returned an error (including 404), didn't respond, or isn't valid sitemap XML. Reported on the home page; the detail lists up to three, with their errors.""",
        """Open the sitemap URL to see the error, then fix the file or the server: it must be valid XML, at most 50 MB uncompressed and 50,000 URLs. If the sitemap no longer exists, remove its reference from robots.txt or the sitemap index.""")
    {
        Why = """Search engines get no URLs from a sitemap they can't fetch or parse: Google keeps retrying for a few days, then stops trying that URL. Well-linked pages are still found, but what the sitemap adds, such as quicker discovery of new and deep pages, is lost. A referenced sitemap that returns 404 is usually stale and only needs removing.""",
        LearnMore = "https://support.google.com/webmasters/answer/7451001",
        Subject = IssueSubject.Url,
        Recheck = [RecheckKind.Sitemaps],
        Grouping = IssueGrouping.None,
        ReportedOnce = true, About = IssueAbout.Sitemap
    };

    public static readonly IssueDefinition SITEMAP_MISSING = new("sitemap-missing", IssueCategory.Sitemaps, IssueSeverity.Notice,
        "No XML sitemap found",
        """The crawl found no sitemap: none is listed in robots.txt or the crawl settings, and there's none at /sitemap.xml. Reported on the home page, only when sitemap discovery was on and the crawl finished.""",
        """Publish an XML sitemap of your indexable pages (most CMSs can generate one) and add a Sitemap: line with its full URL to robots.txt.""")
    {
        Why = """A sitemap isn't required: Google finds pages through links, and a small, well-linked site may not need one. It helps most on large sites, new sites with few links from elsewhere, and sites with pages that links don't reach. A sitemap at another path that's only submitted in Search Console isn't visible to this crawl, so this may not be a real gap.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/sitemaps/overview#do-i-need-a-sitemap",
        Subject = IssueSubject.Url,
        Recheck = [RecheckKind.Sitemaps],
        Grouping = IssueGrouping.None,
        ReportedOnce = true, About = IssueAbout.Sitemap
    };

    public static readonly IssueDefinition SITEMAP_URL_OTHER_HOST = new("sitemap-url-other-host", IssueCategory.Sitemaps, IssueSeverity.Warning,
        "Sitemap lists a URL on another host",
        """A sitemap lists this URL, but it's on a different host from the sitemap: another site, or another of this site's hosts, such as www in a sitemap on the bare domain; or on another scheme, such as an http:// URL in an https:// sitemap. The detail says how the URL answered, or which schemes differ.""",
        """List the URL as it is on the sitemap's own host now (usually where it redirects), or remove the entry. For a sitemap that deliberately covers several hosts, set up cross-submission through Search Console or robots.txt.""")
    {
        Why = """The sitemaps protocol only accepts URLs on the sitemap's own host and drops the rest, so the page it was meant for isn't submitted. Google makes an exception for cross-submission: when every site involved is verified in Search Console and the sitemap is submitted there, or the other host's robots.txt references the sitemap. It's common after a move to or from www.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/sitemaps/build-sitemap#cross-submit",
        References = IssueReferences.Sources,
        Subject = IssueSubject.Url,
        Recheck = [RecheckKind.Urls, RecheckKind.Sitemaps],
        Grouping = IssueGrouping.ResponseDetail,
        Evidence = ["finalUrl"]
    };

    public static readonly IssueDefinition ROBOTS_TXT_NO_SITEMAP = new("robots-txt-no-sitemap", IssueCategory.Sitemaps, IssueSeverity.Notice,
        "robots.txt doesn't name the sitemap",
        """The site's robots.txt has no Sitemap: line, though the crawl found a sitemap anyway: at /sitemap.xml, where crawlers look by convention, or in the crawl settings. Reported once, on the home page, when sitemaps were read and robots.txt answered. The detail names the robots.txt and where the sitemap was found.""",
        """Add a line such as Sitemap: https://www.example.com/sitemap.xml to robots.txt, with the sitemap's full URL. It can go anywhere in the file, and there can be one for each sitemap.""")
    {
        Why = """Search engines read robots.txt before anything else, and its Sitemap: line is how all of them learn where the sitemap is, not only the ones it was submitted to. A sitemap at /sitemap.xml is usually found without it; one anywhere else may not be.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/sitemaps/build-sitemap#addsitemap",
        Subject = IssueSubject.Url,
        Recheck = [RecheckKind.RobotsTxt, RecheckKind.Sitemaps],
        Grouping = IssueGrouping.None,
        ReportedOnce = true, About = IssueAbout.RobotsTxt
    };

    public static readonly IssueDefinition SITEMAP_OVER_LIMIT = new("sitemap-over-limit", IssueCategory.Sitemaps, IssueSeverity.Warning,
        "Sitemap over 50,000 URLs or 50 MB",
        """A sitemap lists more than 50,000 URLs, a sitemap index lists more than 50,000 sitemaps, or a sitemap file is bigger than 50 MB uncompressed: the limits Google and the sitemaps protocol set for one file. Reported once, on the home page; the detail lists up to three, with what each is over.""",
        """Split the file into sitemaps of at most 50,000 URLs and 50 MB each, and list them in a sitemap index; an index over 50,000 sitemaps needs splitting into several indexes. Most CMS sitemap plugins can be set to fewer URLs per file.""")
    {
        Why = """Search engines don't read past a sitemap's limits, and Search Console reports such a file as an error, so the URLs past the limit, or all of the file's, may not reach them through the sitemap.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/sitemaps/build-sitemap#sitemap-best-practices",
        Subject = IssueSubject.Url,
        Recheck = [RecheckKind.Sitemaps],
        Grouping = IssueGrouping.None,
        ReportedOnce = true, About = IssueAbout.Sitemap
    };

    public static readonly IssueDefinition SITEMAP_NEAR_LIMIT = new("sitemap-near-limit", IssueCategory.Sitemaps, IssueSeverity.Notice,
        "Sitemap near the 50,000-URL or 50 MB limit",
        """A sitemap lists 45,000 to 50,000 URLs, a sitemap index 45,000 to 50,000 sitemaps, or a file is between 45 and 50 MB uncompressed: within a tenth of what one file can hold. Reported once, on the home page; the detail lists up to three.""",
        """Split the file before it grows past the limit: several sitemaps, listed in a sitemap index. Most CMS sitemap plugins can be set to fewer URLs per file.""")
    {
        Why = """Nothing is lost yet, but a site that keeps adding pages will pass the limit, and the URLs past it may then not reach search engines through the sitemap.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/sitemaps/build-sitemap#sitemap-best-practices",
        Subject = IssueSubject.Url,
        Recheck = [RecheckKind.Sitemaps],
        Grouping = IssueGrouping.None,
        ReportedOnce = true, About = IssueAbout.Sitemap
    };

    public static readonly IssueDefinition SITEMAP_EMPTY = new("sitemap-empty", IssueCategory.Sitemaps, IssueSeverity.Notice,
        "Empty sitemap",
        """A sitemap that loaded without an error lists no URLs and no sitemaps. Reported once, on the home page; the detail lists up to three.""",
        """Find out why it's empty: a sitemap plugin set to include nothing, a section with no pages yet, or a file left behind. If it isn't needed, remove it and its reference in robots.txt or the sitemap index.""")
    {
        Why = """An empty sitemap does no harm by itself, but it usually means a sitemap that should list pages doesn't, or one left over that search engines keep fetching for nothing.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/sitemaps/build-sitemap",
        Subject = IssueSubject.Url,
        Recheck = [RecheckKind.Sitemaps],
        Grouping = IssueGrouping.None,
        ReportedOnce = true, About = IssueAbout.Sitemap
    };

    public static readonly IssueDefinition SITEMAP_URL_REPEATED = new("sitemap-url-repeated", IssueCategory.Sitemaps, IssueSeverity.Notice,
        "URL listed in more than one sitemap",
        """More than one of the site's sitemap files lists this URL. The detail names the sitemaps that list it.""",
        """Keep each URL in one sitemap, usually the one for its kind of page. Repeats often come from two sitemap plugins at once, or a page listed under two sections.""")
    {
        Why = """The sitemaps protocol allows it and search engines cope, so this is housekeeping: repeated entries make sitemaps bigger and harder to keep right, and can give one URL two different lastmod dates.""",
        LearnMore = "https://www.sitemaps.org/protocol.html",
        References = IssueReferences.Sources,
        Subject = IssueSubject.Url,
        Recheck = [RecheckKind.Sitemaps],
        Grouping = IssueGrouping.None
    };

    public static readonly IssueDefinition SITEMAP_LASTMOD_INVALID = new("sitemap-lastmod-invalid", IssueCategory.Sitemaps, IssueSeverity.Notice,
        "Sitemap lastmod invalid or in the future",
        """Sitemap entries have a <lastmod> that isn't a date (sitemaps use W3C Datetime, such as 2026-11-17 or 2026-11-17T09:30:00+00:00) or a date more than a day after the crawl. Reported once, on the home page; the detail counts each kind, names the sitemaps, and gives the first wrong entry with what its lastmod says. A crawl made before the crawler read lastmod isn't checked.""",
        """Have the sitemap give each URL's real last change in W3C Datetime, as most CMS sitemap plugins do when left to themselves. Don't set every entry to today's date, or any to a date in the future.""")
    {
        Why = """Google uses lastmod only when it's consistently and verifiably accurate. Dates it can't read, or that haven't happened yet, show it they aren't, so it may ignore the sitemap's dates and find changed pages later.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/sitemaps/build-sitemap#additional-notes-about-xml-sitemaps",
        Subject = IssueSubject.Url,
        Recheck = [RecheckKind.Sitemaps],
        Grouping = IssueGrouping.None,
        ReportedOnce = true, About = IssueAbout.Sitemap
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
        Recheck = [RecheckKind.Urls],
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
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.None
    };

    public static readonly IssueDefinition TITLE_TOO_LONG = new("title-too-long", IssueCategory.Titles, IssueSeverity.Notice,
        "Title too long",
        """The page's title is wider than 580 pixels (the default, which can be changed in the analysis settings), about 60 characters of ordinary text, measured as Google's desktop results show titles: Arial at 20 px, with full-width characters such as Chinese, Japanese and Korean, and emoji, one em wide.""",
        """Shorten the title or move the important words to the front; cut repeated words and long brand suffixes.""")
    {
        Why = """Google sets no length limit for titles, but truncates title links to fit the device width, so the end of a long title may not show. Width depends on the letters ("WWW" is more than four times as wide as "iii"), so pixels predict the cut better than characters: 580 px is about where Google's desktop results cut a title, after the last whole word that fits, and a rough guide, not a rule. Google may also rewrite titles that are long-winded or stuffed with keywords. With the important words first, a cut-off rarely matters.""",
        LearnMore = "https://developers.google.com/search/docs/appearance/title-link#page-titles",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.None,
        Evidence = ["title", "titleInResults", "titleWidth", "titleLength"]
    };

    public static readonly IssueDefinition TITLE_TOO_SHORT = new("title-too-short", IssueCategory.Titles, IssueSeverity.Notice,
        "Title too short",
        """The page's title is narrower than 200 pixels (the default, which can be changed in the analysis settings), about 20 characters of ordinary text, measured as Google's desktop results show titles: Arial at 20 px, with full-width characters and emoji one em wide.""",
        """Describe the page in the title: add the specific topic, product or place and, if you like, the site name.""")
    {
        Why = """Short isn't wrong: a brand or product name can be exactly right. But very short titles are often vague, such as "Home", "Products" or "Page 2", which Google's guidance warns against because they don't tell searchers what the page is or set it apart from similar pages.""",
        LearnMore = "https://developers.google.com/search/docs/appearance/title-link#page-titles",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.None,
        Evidence = ["title", "titleWidth", "titleLength"]
    };

    public static readonly IssueDefinition TITLE_DUPLICATE = new("title-duplicate", IssueCategory.Titles, IssueSeverity.Warning,
        "Duplicate title",
        """Another indexable page has the same title, ignoring case and spacing. The detail says how many other pages share it.""",
        """Give each page a title that says what's specific about it. If the pages really are the same, consolidate them with a redirect or canonical instead.""")
    {
        Why = """Google asks for distinct titles on every page, because identical ones make it impossible for searchers to tell pages apart, and it may rewrite title links that repeat boilerplate. Duplicate titles often mean duplicate pages, or a template that leaves out the distinguishing detail, such as the product, place or page number.""",
        LearnMore = "https://developers.google.com/search/docs/appearance/title-link#page-titles",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.RelatedUrl,
        Evidence = ["title"]
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
        Recheck = [RecheckKind.Urls],
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
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.None
    };

    public static readonly IssueDefinition DESCRIPTION_TOO_LONG = new("description-too-long", IssueCategory.MetaDescriptions, IssueSeverity.Notice,
        "Meta description too long",
        """The page's meta description is wider than 985 pixels (the default, which can be changed in the analysis settings), about 160 characters of ordinary text, measured as Google's desktop results show snippets: Arial at 14 px, with full-width characters and emoji one em wide.""",
        """Put what matters in the first sentence and trim the rest, so the description still works if it's cut.""")
    {
        Why = """There's no length limit, but Google truncates snippets to fit the device width, about two lines on a desktop, so the end of a long description may not show, and it often builds the snippet from page text anyway. Width depends on the letters, so pixels predict the cut better than characters; 985 px is Screaming Frog's default. With the key point first, the cut rarely matters.""",
        LearnMore = "https://developers.google.com/search/docs/appearance/snippet#meta-descriptions",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.None,
        Evidence = ["description", "descriptionInResults", "descriptionWidth", "descriptionLength"]
    };

    public static readonly IssueDefinition DESCRIPTION_TOO_SHORT = new("description-too-short", IssueCategory.MetaDescriptions, IssueSeverity.Notice,
        "Meta description too short",
        """The page's meta description is narrower than 400 pixels (the default, which can be changed in the analysis settings), about 65 characters of ordinary text, measured as Google's desktop results show snippets: Arial at 14 px, with full-width characters and emoji one em wide.""",
        "Expand it into a specific one- or two-sentence summary of what the page offers and for whom.")
    {
        Why = """There's no minimum, but a very short description rarely summarises the page, and Google uses the meta description only when it describes the page better than the page's own text. A short one is fine if it says everything that matters.""",
        LearnMore = "https://developers.google.com/search/docs/appearance/snippet#use-quality-descriptions",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.None,
        Evidence = ["description", "descriptionWidth", "descriptionLength"]
    };

    public static readonly IssueDefinition DESCRIPTION_DUPLICATE = new("description-duplicate", IssueCategory.MetaDescriptions, IssueSeverity.Notice,
        "Duplicate meta description",
        """Another indexable page has the same meta description, ignoring case and spacing. The detail says how many other pages share it.""",
        "Write a page-specific description, or generate one from each page's own data.")
    {
        Why = """Google says identical descriptions aren't helpful when individual pages appear in search, and recommends a unique one per page, with site-level wording only on the home page and other aggregation pages. Repeated descriptions usually come from a template default.""",
        LearnMore = "https://developers.google.com/search/docs/appearance/snippet#create-unique-descriptions-for-each-page-on-your-site",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.RelatedUrl,
        Evidence = ["description"]
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
        Recheck = [RecheckKind.Urls],
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
        Recheck = [RecheckKind.Urls],
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
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.RelatedUrl,
        Evidence = ["h1"]
    };
    public static readonly IssueDefinition HEADING_LEVELS_SKIPPED = new("heading-levels-skipped", IssueCategory.Headings, IssueSeverity.Notice,
        "Heading levels skipped",
        """On this indexable page, which has an H1, a heading after the H1 is more than one level below the one before it (an H2, then an H4). Headings in menus and footers after the H1 count, as screen readers list them all; headings before the H1, such as a theme's hidden cart or a menu's column titles, aren't judged. The detail gives the first such step.""",
        """Use the next level down for each subsection (H2 under the H1, H3 under an H2), and style a heading by CSS, not by picking a smaller level.""")
    {
        Why = """Screen reader users move through a page by its headings, and a skipped level suggests a missing section. HTML asks for each heading to be at most one level below the one before. Search engines cope either way, so this is about structure and accessibility.""",
        LearnMore = "https://www.w3.org/WAI/tutorials/page-structure/headings/",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.PageDetail
    };

    public static readonly IssueDefinition H1_IMAGE_ONLY = new("h1-image-only", IssueCategory.Headings, IssueSeverity.Notice,
        "H1 is an image",
        """This indexable page's first H1 has no text of its own: its text is an image's alt, as with a logo in the H1. The detail quotes the alt. A crawl made before the crawler noted this isn't checked.""",
        """Give the page a text H1 that says what this page is about, and keep the logo as an image outside it, or in it beside the text.""")
    {
        Why = """An H1 that's a logo says the same on every page, the site's name, not what this page is about. Search engines read the alt, but a heading in text is clearer to them and to screen reader users.""",
        LearnMore = "https://developers.google.com/search/docs/appearance/title-link#page-titles",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.None,
        Evidence = ["h1"]
    };

    public static readonly IssueDefinition HEADING_TOO_LONG = new("heading-too-long", IssueCategory.Headings, IssueSeverity.Notice,
        "H1 or H2 over 70 characters",
        """This indexable page's first H1, or one of its H2s, is longer than 70 characters (the default, which can be changed in the analysis settings). The detail gives the first, its level and its length.""",
        """Shorten the heading to what the section is about, and move the rest into the text below it.""")
    {
        Why = """A heading is read as a label: a long one is harder to scan, and on a phone it wraps over several lines. Search engines read it whole, so this is about readers, not ranking.""",
        LearnMore = "https://www.w3.org/WAI/tutorials/page-structure/headings/",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.None,
        Evidence = ["h1"]
    };

    public static readonly IssueDefinition H2_MISSING = new("h2-missing", IssueCategory.Headings, IssueSeverity.Notice,
        "Long page without an H2",
        """This indexable page has 500 or more words of its own (the default, which can be changed in the analysis settings) and no H2 to divide them. A short page doesn't need one, so it isn't asked of one.""",
        """Break the text into sections, each under an H2 that says what it covers.""")
    {
        Why = """Subheadings let readers scan a long page and jump to what they need, and they help search engines see what each part is about.""",
        LearnMore = "https://www.w3.org/WAI/tutorials/page-structure/headings/",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.None,
        Evidence = ["wordCount"]
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
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.None
    };
    public static readonly IssueDefinition PLACEHOLDER_TEXT = new("placeholder-text", IssueCategory.Content, IssueSeverity.Warning,
        "Placeholder text (lorem ipsum)",
        """This indexable page's text, title or meta description has the placeholder "lorem ipsum" in it. The detail quotes it. A crawl made before the crawler looked for it isn't checked.""",
        """Replace the placeholder with the page's real text, or remove the block it's in, then check other pages made from the same template.""")
    {
        Why = """Every visitor sees it, and it reads as an unfinished site. Search engines index it as the page's text, and it can end up in the snippet under the page's title.""",
        LearnMore = "https://developers.google.com/search/docs/fundamentals/creating-helpful-content",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.None
    };

    public static readonly IssueDefinition READABILITY_LOW = new("readability-low", IssueCategory.Content, IssueSeverity.Notice,
        "Text that's very hard to read",
        """This indexable English page (its <html lang> is en or en-…) has a Flesch reading ease under 30 (the default, which can be changed in the analysis settings): long sentences of long words. Only prose is judged, 100 words and 5 sentences or more; menus and product grids without sentence ends aren't. A crawl made before the crawler counted sentences isn't checked.""",
        """Shorten long sentences, split ones that carry several ideas, and prefer short everyday words to long ones where they say the same.""")
    {
        Why = """Text that's very hard to read loses readers before they reach the point, on phones above all. Flesch's scale puts under 30 at university level. Search engines don't score readability, so this is about the people reading.""",
        LearnMore = "https://en.wikipedia.org/wiki/Flesch%E2%80%93Kincaid_readability_tests#Flesch_reading_ease",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.None
    };

    public static readonly IssueDefinition BOILERPLATE_HEAVY = new("boilerplate-heavy", IssueCategory.Content, IssueSeverity.Notice,
        "Page mostly template text",
        """Under a quarter of this indexable page's words are its own: the rest are menus, header, footer and sidebars. Pages with too few words of their own are another check's (low word count). The detail gives the page's own words of all its words. A crawl made before the crawler counted them isn't checked.""",
        """Add text of the page's own, or trim menus and footers that repeat long lists of links on every page.""")
    {
        Why = """When most of a page is the same as every other page, search engines see little that's particular to it, and pages made from the template look alike to them.""",
        LearnMore = "https://developers.google.com/search/docs/fundamentals/creating-helpful-content",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
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
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.RelatedUrl,
        Evidence = ["indexability", "canonicalUrl", "inlinks"]
    };

    public static readonly IssueDefinition NEAR_DUPLICATE_CONTENT = new("near-duplicate-content", IssueCategory.Content, IssueSeverity.Notice,
        "Near-duplicate content",
        """Another indexable page has almost the same main content, roughly 96% or more alike (the default, which can be changed in the analysis settings). Exact copies are reported as duplicate content instead, and pages under that check's minimum word count aren't compared.""",
        """Give each page main content that is clearly its own, canonicalise the variants to one version, or merge them into one page with options.""")
    {
        Why = """Pages that differ in only a few words, such as location pages with the town swapped or products that differ by size, may be grouped by Google as duplicates, with only one shown. That's fine for variants you don't need to rank separately. Pages that should rank on their own need differences that are, in Google's words, clear and significant.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/canonicalization-troubleshooting",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.RelatedUrl,
        Evidence = ["indexability", "canonicalUrl", "inlinks", "wordCount"]
    };

    public static readonly IssueDefinition DUPLICATE_SITE_VERSION = new("duplicate-site-version", IssueCategory.Content, IssueSeverity.Warning,
        "Duplicate version of the site",
        """This version of the site (the bare domain or www, over http or https) answers with a page, directly or after redirecting to another non-main version, instead of redirecting to the main version: the home page's.""",
        """301-redirect every URL on the other versions to the same path on the main version, in one hop, at the server or CDN.""")
    {
        Why = """Every page is then reachable at two or more addresses. Google treats the versions as duplicate URLs and picks one, preferring HTTPS, which may not be the version you want, and it crawls the copies less. If the duplicate is the http version, visitors who land on it browse unencrypted. The other version is only probed, not parsed, so a canonical on it isn't seen; a redirect is what makes the choice yours.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/consolidate-duplicate-urls#reasons-to-specify-a-canonical-url",
        Subject = IssueSubject.Url,
        Recheck = [RecheckKind.SiteVersions],
        Grouping = IssueGrouping.ResponseDetail,
        SiteWide = true,
        ReportedOnce = true
    };


    // Images
    public static readonly IssueDefinition IMAGE_MISSING_ALT = new("image-missing-alt", IssueCategory.Images, IssueSeverity.Warning,
        "Images without an alt attribute",
        """The page has <img> elements with no alt attribute at all. Images with an empty alt="" (marked as decorative) and 1×1 tracking pixels don't count.""",
        """Add alt text that says what each image shows or does in context. For purely decorative images, add an empty alt="" so screen readers skip them.""")
    {
        Why = """Some screen readers read out the image's file name when there's no alt, and WCAG requires a text alternative for every image that conveys something. Google uses alt text, with computer vision and the page's content, to understand images for Google Images, and as the anchor text when an image is a link.""",
        LearnMore = "https://developers.google.com/search/docs/appearance/google-images",
        References = IssueReferences.Targets,
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.RelatedUrl,
        TargetFacts = ["img", "position", "links"]
    };

    public static readonly IssueDefinition IMAGE_ALT_TOO_LONG = new("image-alt-too-long", IssueCategory.Images, IssueSeverity.Notice,
        "Alt text too long",
        """The page has images whose alt text is longer than 125 characters (the default, which can be changed in the analysis settings).""",
        """Cut the alt text to a short description of what the image conveys, and move longer descriptions, such as a chart's data, into a caption or the page text.""")
    {
        Why = """W3C's guidance is that alt text should be the most concise description of the image's purpose, and anything longer than a short phrase or sentence belongs in a caption or the surrounding text. Screen reader users hear alt text in one go and can't skim it, and Google warns that alt text stuffed with keywords can make a site look spammy. 125 characters is a common convention, not a standard.""",
        LearnMore = "https://www.w3.org/WAI/tutorials/images/tips/",
        References = IssueReferences.Targets,
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.RelatedUrl,
        TargetFacts = ["img", "position"]
    };

    public static readonly IssueDefinition IMAGE_MISSING_DIMENSIONS = new("image-missing-dimensions", IssueCategory.Images, IssueSeverity.Notice,
        "Images without width and height attributes",
        """The page has <img> elements missing a width or height attribute, or both; 1×1 tracking pixels don't count. Only the HTML is read, so images sized in CSS still appear here.""",
        """Add width and height attributes with the image's intrinsic size (CSS can still scale it), or give the image box an aspect-ratio or fixed size in CSS.""")
    {
        Why = """Without both attributes, and unless CSS gives the image box a size or aspect-ratio, the browser can't reserve space before the image loads, so the content below jumps when it arrives. That layout shift annoys readers and counts towards Cumulative Layout Shift, one of Google's Core Web Vitals. If the CSS already reserves the space, nothing needs doing.""",
        LearnMore = "https://web.dev/articles/optimize-cls#images-without-dimensions",
        References = IssueReferences.Targets,
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.RelatedUrl,
        TargetFacts = ["img", "pixelWidth", "pixelHeight", "position"]
    };

    public static readonly IssueDefinition IMAGE_BROKEN = new("image-broken", IssueCategory.Images, IssueSeverity.Error,
        "Broken images",
        """The page embeds images (<img>) whose URLs return a 4xx or 5xx error or don't respond, directly or after redirects. For images on other sites, statuses often sent only to crawlers (401, 403, 405, 429, 503, 999) don't count.""",
        "Fix the image URL or restore the file. If the image is gone, replace it or remove the <img>.")
    {
        Why = """Visitors see a broken-image icon or a gap, which makes the page look unfinished, and an image that can't be fetched can't appear in Google Images. A broken image in a template shows up on every page that uses it, so fix those first.""",
        LearnMore = null,
        References = IssueReferences.Targets,
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.RelatedUrl,
        TargetFacts = ["statusCode", "final", "text", "position", "links"]
    };

    public static readonly IssueDefinition IMAGE_TOO_LARGE = new("image-too-large", IssueCategory.Images, IssueSeverity.Warning,
        "Large images",
        """The page embeds images whose files, after any redirects, are over 200 KB as transferred (the default, which can be changed in the analysis settings).""",
        """Resize images to the largest size they're displayed at (with srcset for different screens), compress them, and use WebP or AVIF where you can.""")
    {
        Why = """Images are often the largest part of a page's weight, and a big one costs visitors time and data, especially on mobile connections. It matters most above the fold, such as a hero image that's the page's Largest Contentful Paint; a large image lazy-loaded far down the page costs much less. The usual causes are photos uploaded straight from a camera and images far larger than the size they're shown at.""",
        LearnMore = "https://web.dev/articles/optimize-lcp#reduce-size",
        References = IssueReferences.Targets,
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.RelatedUrl,
        TargetFacts = ["sizeBytes", "contentType", "pixelWidth", "pixelHeight", "img", "statusCode", "final", "position", "links"]
    };

    public static readonly IssueDefinition IMAGE_MISSING_SRC = new("image-missing-src", IssueCategory.Images, IssueSeverity.Warning,
        "Image without a source",
        """The page has <img> elements with no file to show: no src, none of the lazy-loading attributes the crawler reads (data-src and the like), no srcset, and not a data: image. The detail counts them. A crawl made before the crawler told data: images apart isn't checked.""",
        """Give each image its file in src. If a lazy-loading script fills it in, check that it uses a standard attribute such as data-src or loading="lazy", and that the image shows without the script.""")
    {
        Why = """An image with no source never shows, for visitors or for Google Images. It's usually a template slot left empty, or a lazy-loading script keeping the file in an attribute only it knows, which search engines may not read either. Not an error: a script the crawler doesn't know can look the same.""",
        LearnMore = "https://developers.google.com/search/docs/appearance/google-images#use-html-image-elements",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.None
    };

    public static readonly IssueDefinition IMAGE_NO_SRCSET = new("image-no-srcset", IssueCategory.Images, IssueSeverity.Notice,
        "Large image without srcset",
        """The page shows image files at least 800 pixels wide (the default, which can be changed in the analysis settings) with no srcset and not in a <picture> with other sources, so phones download the whole file. It needs the files' pixel sizes, read when the crawl fetched them, so it isn't checked when images weren't fetched. The detail counts them and gives the widest.""",
        """Offer smaller versions with srcset and sizes, such as 480, 800 and 1200 pixels wide, or a <picture> with sources; most CMSs do this for images uploaded through them.""")
    {
        Why = """A phone shows a 2,000-pixel image at a few hundred pixels: without other sizes to choose from, it downloads all of it, which costs visitors time and data and slows the page's largest paint. Google Images reads srcset and <picture> too.""",
        LearnMore = "https://web.dev/learn/design/responsive-images",
        References = IssueReferences.Targets,
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.RelatedUrl,
        TargetFacts = ["img", "pixelWidth", "pixelHeight", "sizeBytes"]
    };

    public static readonly IssueDefinition IMAGE_LEGACY_FORMAT = new("image-legacy-format", IssueCategory.Images, IssueSeverity.Notice,
        "Image in an older format",
        """The page shows JPEG, PNG, GIF or BMP files of 50 KB or more (the default, which can be changed in the analysis settings), as transferred. WebP or AVIF would usually make them smaller. It isn't checked when images weren't fetched. The detail counts them and gives the largest.""",
        """Serve these images as WebP or AVIF: most CMSs and image CDNs can convert them, keeping the old format as a fallback in a <picture> if you need it.""")
    {
        Why = """WebP and AVIF usually make photos a quarter to a half smaller at the same quality, and every browser in use shows them, as Google Images does. Smaller images load sooner, above all on phones. 50 KB is about where the newer formats save a noticeable amount.""",
        LearnMore = "https://developer.chrome.com/docs/lighthouse/performance/uses-webp-images",
        References = IssueReferences.Targets,
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.RelatedUrl,
        TargetFacts = ["contentType", "sizeBytes", "pixelWidth", "pixelHeight"]
    };

    public static readonly IssueDefinition IMAGE_ALT_UNHELPFUL = new("image-alt-unhelpful", IssueCategory.Images, IssueSeverity.Notice,
        "Alt text that's a file name or too short",
        """The page has images whose alt text describes nothing: one or two characters (in an alphabet; in Chinese or Japanese that can be a word), no letter or digit at all, an image file name such as photo.jpg, a camera's or upload's name such as IMG_1234 or image-3, or the image file's own name. An empty alt is right for a decorative image and isn't counted. The detail counts them.""",
        """Write alt text that says what the image shows or does, in a few words, as you'd describe it to someone who can't see it. Leave alt empty for an image that's only decoration.""")
    {
        Why = """Screen readers read the alt text aloud, and a file name or a stray character tells their users nothing. Google Images uses alt text to understand an image, so a file name is a chance missed.""",
        LearnMore = "https://developers.google.com/search/docs/appearance/google-images#use-descriptive-alt-text",
        References = IssueReferences.Targets,
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.RelatedUrl,
        TargetFacts = ["img", "position"]
    };

    public static readonly IssueDefinition IMAGE_HOTLINKED = new("image-hotlinked", IssueCategory.Images, IssueSeverity.Notice,
        "Image loaded from another website",
        """The page shows images straight from another website: not the site's own host or domain, nor an image CDN sites serve their own images from (Shopify's, CloudFront, Cloudinary, imgix, WordPress's i0.wp.com and others). On a crawl started at a private address, other private addresses aren't counted. The detail counts the images and the sites.""",
        """Copy the images you have the right to use onto your own site, or a CDN of yours, and show them from there.""")
    {
        Why = """An image shown from someone else's server can vanish or change whenever they move or block it, and it uses their bandwidth, which many sites refuse. Google Images credits the image to the site it's on, not to yours.""",
        LearnMore = "https://developers.google.com/search/docs/appearance/google-images",
        References = IssueReferences.Targets,
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.RelatedUrl,
        TargetFacts = ["statusCode", "img", "position"]
    };

    public static readonly IssueDefinition IMAGE_INLINE_LARGE = new("image-inline-large", IssueCategory.Images, IssueSeverity.Notice,
        "Large inline (data:) image",
        """The page builds images into its HTML as data: URIs larger than 10 KB once decoded (the default, which can be changed in the analysis settings). A crawl made before the crawler measured them isn't checked. The detail counts them and gives the largest.""",
        """Save each as an image file and show it with an ordinary src, so browsers can cache it and download it alongside the page.""")
    {
        Why = """An inline image comes with the page every time, can't be cached on its own, and in base64 is a third bigger than the file. A small icon is fine; a photo makes every view of the page slower.""",
        LearnMore = "https://developer.mozilla.org/en-US/docs/Web/URI/Reference/Schemes/data",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.None
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
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.PageDetail
    };

    public static readonly IssueDefinition HREFLANG_MISSING_SELF = new("hreflang-missing-self", IssueCategory.Hreflang, IssueSeverity.Warning,
        "Hreflang without a self-reference",
        """This indexable page lists hreflang alternates, but none of them is the page's own URL exactly as crawled.""",
        """Add an hreflang entry for the page's own URL, written exactly as the page is served (scheme, host, path, trailing slash and any query string), alongside the entries for the other versions.""")
    {
        Why = """Google's rule is that each language version must list itself as well as all the other versions, with the same set of links on every page. Without the self-reference the set breaks that rule and may not be used as intended. Often the self-reference is there but in a different form of the URL, such as http instead of https, without the trailing slash or with a tracking parameter.""",
        LearnMore = "https://developers.google.com/search/docs/specialty/international/localized-versions#all-method-guidelines",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.None
    };

    public static readonly IssueDefinition HREFLANG_MISSING_RETURN = new("hreflang-missing-return", IssueCategory.Hreflang, IssueSeverity.Warning,
        "Hreflang without return links",
        """This indexable page names alternates on the site that are indexable but don't list it in their own hreflang annotations. The detail says how many.""",
        """Give every page in the set the same complete list of hreflang entries, including one that points back to this page with exactly this URL.""")
    {
        Why = """Google requires hreflang to be confirmed from both sides: if page X lists page Y, Y must list X. Where it isn't, Google says the annotations may be ignored or not interpreted correctly, so searchers may be shown the wrong language version. It usually means the sets differ between pages, for example after a new version was added to some templates but not others.""",
        LearnMore = "https://developers.google.com/search/docs/specialty/international/localized-versions#common-mistakes",
        References = IssueReferences.Targets,
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.RelatedUrl,
        TargetFacts = ["statusCode", "text", "indexability"]
    };

    public static readonly IssueDefinition HREFLANG_BROKEN_TARGET = new("hreflang-broken-target", IssueCategory.Hreflang, IssueSeverity.Error,
        "Hreflang points to a broken URL",
        "This indexable page has hreflang entries whose URLs return a 4xx or 5xx error or don't respond.",
        """Point the entry at the working page for that language, or remove it from every page in the set if that version no longer exists.""")
    {
        Why = """An alternate that doesn't load can't be shown to anyone and can't confirm the return link, so the entry for that language does nothing, and anyone sent there gets an error. Usually it's a version that was removed or moved without the annotations being updated.""",
        LearnMore = "https://developers.google.com/search/docs/specialty/international/localized-versions",
        References = IssueReferences.Targets,
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.RelatedUrl,
        TargetFacts = ["statusCode", "final", "text"]
    };

    public static readonly IssueDefinition HREFLANG_TO_NON_INDEXABLE = new("hreflang-to-non-indexable", IssueCategory.Hreflang, IssueSeverity.Warning,
        "Hreflang points to a non-indexable URL",
        """This indexable page has hreflang entries whose URLs redirect, are noindex, name another URL as canonical, or are blocked by your robots.txt. Alternates that refused the crawler (a firewall, a bot challenge or a rate limit), and ones on other sites whose robots.txt kept the crawler out, aren't judged: neither says whether search engines can index them.""",
        """Replace each entry with the alternate's final, canonical, indexable URL, and keep the list identical on every page in the set.""")
    {
        Why = """Google generally shows each version's canonical, indexable URL in results, so hreflang should name exactly those URLs. An alternate that redirects or is canonicalised elsewhere isn't the URL that gets indexed, and one that's noindex or blocked can't be shown at all, so the entry for that language is unreliable. Google specifically warns against listing HTTP URLs in hreflang when the pages are on HTTPS.""",
        LearnMore = "https://developers.google.com/search/docs/specialty/international/localized-versions",
        References = IssueReferences.Targets,
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.RelatedUrl,
        TargetFacts = ["statusCode", "indexability", "final", "canonical", "text"]
    };

    public static readonly IssueDefinition HREFLANG_DUPLICATE_CODE = new("hreflang-duplicate-code", IssueCategory.Hreflang, IssueSeverity.Warning,
        "Conflicting hreflang entries",
        """This indexable page has the same hreflang code (compared ignoring case) pointing to two or more different URLs. The detail lists the codes.""",
        """Keep one URL per code. If two pages really target the same audience, choose one, or give them different regions.""")
    {
        Why = """Each language or language-region code should name exactly one URL; when a code names several, search engines can't tell which to show for that audience. It's usually two systems adding hreflang, such as a plugin and the theme, or the HTML and an HTTP header, or a copy-paste mistake in the set.""",
        LearnMore = "https://developers.google.com/search/docs/specialty/international/localized-versions#html",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.PageDetail, FaultOf = IssueFaults.HreflangCodes
    };

    public static readonly IssueDefinition HREFLANG_MISSING_X_DEFAULT = new("hreflang-missing-x-default", IssueCategory.Hreflang, IssueSeverity.Notice,
        "Hreflang without x-default",
        "This indexable page has hreflang annotations but no x-default entry.",
        """Add <link rel="alternate" hreflang="x-default" href="..."> pointing at your selector page or default version, on every page in the set.""")
    {
        Why = """x-default names the page for searchers whose language or region isn't in your list, usually a language selector or the main international version. Google suggests adding one, especially on language or country selectors and home pages that redirect by locale, but it's optional.""",
        LearnMore = "https://developers.google.com/search/docs/specialty/international/localized-versions#xdefault",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
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
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.None
    };
    public static readonly IssueDefinition HREFLANG_INCONSISTENT_CODE = new("hreflang-inconsistent-code", IssueCategory.Hreflang, IssueSeverity.Warning,
        "Hreflang return link uses another code",
        """This indexable page names an alternate under one language code, and the alternate links back but calls itself by another (the page says fr, the alternate fr-ca). A missing return link is another check's. The detail names the first.""",
        """Make every page in the set use the same code for each version: the one the version uses for itself.""")
    {
        Why = """Google asks every version to list itself and all the others; versions that disagree about which language each one is may be ignored, and the wrong version shown to searchers.""",
        LearnMore = "https://developers.google.com/search/docs/specialty/international/localized-versions",
        References = IssueReferences.Targets,
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.RelatedUrl,
        TargetFacts = ["statusCode", "text", "indexability"]
    };

    public static readonly IssueDefinition HREFLANG_HEADER_MISMATCH = new("hreflang-header-mismatch", IssueCategory.Hreflang, IssueSeverity.Notice,
        "Hreflang in the HTML and the HTTP header disagree",
        """This page gives its language versions both in its HTML and in a Link HTTP header, and the two name different sets. The detail counts each and names what only one has.""",
        """Give the versions in one place, or keep both identical: usually the HTML, from your SEO plugin, with the header removed from the server.""")
    {
        Why = """Google reads both as the same declaration, so sets that differ leave it to guess which is right.""",
        LearnMore = "https://developers.google.com/search/docs/specialty/international/localized-versions#methods-for-indicating-your-alternate-pages",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.None
    };

    public static readonly IssueDefinition HREFLANG_UNLINKED = new("hreflang-unlinked", IssueCategory.Hreflang, IssueSeverity.Notice,
        "Hreflang URL that no page links to",
        """An indexable page another page names as a language version, but that no crawled page links to with an ordinary link and no sitemap lists. It's only checked when the crawl read every page that could link to it, and every version hreflang names: the crawl doesn't follow hreflang unless told to, so a version only hreflang leads to is left unread. Where it's found lists the pages naming it.""",
        """Link to each language version from the others, such as with a language switcher, and list them in the sitemap.""")
    {
        Why = """Hreflang says which version to show, not that a page matters: a version only hreflang leads to gets no link signals, and visitors can't reach it from the site.""",
        LearnMore = "https://developers.google.com/search/docs/specialty/international/localized-versions",
        References = IssueReferences.Sources,
        Subject = IssueSubject.Page,
        // As an orphan: only a full crawl shows that something now links to it.
        Recheck = [],
        Grouping = IssueGrouping.None,
        Evidence = ["inlinks", "linkingPages"]
    };

    public static readonly IssueDefinition HREFLANG_LANG_MISMATCH = new("hreflang-lang-mismatch", IssueCategory.Hreflang, IssueSeverity.Notice,
        "Hreflang code and lang attribute differ",
        """This indexable page's hreflang entry for itself and its <html lang> name different languages, both written correctly (en against fr; en-GB against en-US is the same language). x-default isn't compared. The detail gives both.""",
        """Correct whichever is wrong: the lang attribute in the page's template, or the code your SEO plugin gives this version.""")
    {
        Why = """Google reads neither to decide a page's language, but screen readers use lang, and two that disagree usually mean one was copied from another version unchanged.""",
        LearnMore = "https://developers.google.com/search/docs/specialty/international/localized-versions",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.PageDetail,
        Evidence = ["language"]
    };

    public static readonly IssueDefinition HREFLANG_SELF_ONLY = new("hreflang-self-only", IssueCategory.Hreflang, IssueSeverity.Notice,
        "Hreflang that names only the page itself",
        """Every hreflang entry this indexable page has names the page itself (x-default included), so it lists no other language version.""",
        """If the site has other language versions, list them all on every version. If it has none, the hreflang entries can go.""")
    {
        Why = """Hreflang only helps when it links versions of a page in other languages or regions; on its own it does nothing, and it often means the other versions were left out by mistake.""",
        LearnMore = "https://developers.google.com/search/docs/specialty/international/localized-versions",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.None
    };


    // Structured data
    public static readonly IssueDefinition STRUCTURED_DATA_INVALID = new("structured-data-invalid", IssueCategory.StructuredData, IssueSeverity.Warning,
        "Invalid JSON-LD",
        """A JSON-LD block (<script type="application/ld+json">) on the page isn't valid JSON. The detail shows the parser's first error.""",
        """Fix the syntax where the detail points (often an unescaped quote, a trailing or missing comma, or a comment left in by a template), then test the page with Google's Rich Results Test.""")
    {
        Why = """JSON-LD has to be valid JSON: a trailing comma or a comment is enough for Google to drop the whole block, and its Unparsable structured data report lists the error a trailing comma produces. Nothing in the block is used, so it can't produce a rich result. Other valid blocks on the page still work. A single template error usually breaks the same block on every page that uses it.""",
        LearnMore = "https://support.google.com/webmasters/answer/9166415",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.PageDetail, FaultOf = IssueFaults.JsonError
    };

    public static readonly IssueDefinition STRUCTURED_DATA_MISSING_REQUIRED = new("structured-data-missing-required", IssueCategory.StructuredData, IssueSeverity.Warning,
        "Structured data missing required properties",
        """A top-level schema.org item on this indexable page, in JSON-LD or Microdata, lacks a property Google requires for that type's rich result. A subtype is held to its parent type's requirements, as a Restaurant is to a LocalBusiness's and a MusicEvent to an Event's. The detail names the type and the missing properties.""",
        """Add the missing properties with values that match what's visible on the page, then test it with the Rich Results Test.""")
    {
        Why = """Google says items missing required properties aren't eligible for rich results, so the markup does nothing for that feature. Recommended properties don't affect eligibility, so they're another check's (Structured data missing recommended properties). FAQPage isn't checked: Google stopped showing FAQ rich results in May 2026. Even complete markup only makes a rich result possible: Google decides whether to show one.""",
        LearnMore = "https://developers.google.com/search/docs/appearance/structured-data/sd-policies#completeness",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.PageDetail
    };

    public static readonly IssueDefinition STRUCTURED_DATA_SCHEMA_ERRORS = new("structured-data-schema-errors", IssueCategory.StructuredData, IssueSeverity.Warning,
        "Structured data that isn't valid schema.org",
        """A JSON-LD block or Microdata item on the page has no @context or one that isn't schema.org, a @type schema.org doesn't have, a property schema.org doesn't define for that type or the types it descends from, a date property whose value isn't an ISO 8601 date, or a priceCurrency that isn't an ISO 4217 code. Types and properties written in another vocabulary aren't judged. The detail gives the first three faults.""",
        """Correct each fault the detail names: the type's or property's spelling as schema.org writes it (Organization, not Organisation), dates as 2026-11-17 or 2026-11-17T09:30:00+00:00, and currencies as three-letter codes such as GBP. Then test the page with the Schema Markup Validator.""")
    {
        Why = """Search engines read structured data by schema.org's vocabulary: a type or property they don't know, or a date or currency they can't read, is ignored without a word, so the markup may do nothing for the page. Google's Rich Results Test only reports what its own features use, so these faults often go unnoticed.""",
        LearnMore = "https://validator.schema.org/",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.PageDetail
    };

    public static readonly IssueDefinition STRUCTURED_DATA_MISSING_RECOMMENDED = new("structured-data-missing-recommended", IssueCategory.StructuredData, IssueSeverity.Notice,
        "Structured data missing recommended properties",
        """A top-level item on this indexable page lacks properties Google recommends for its type: an Article, NewsArticle or BlogPosting its headline, image, datePublished or author; an Organization its url or logo; a Product its image, or its Offer a priceCurrency or availability. The detail names the type and what it lacks.""",
        """Add the properties the detail names, with values that match what's visible on the page. Most SEO and shop plugins have a setting for each, such as the article's featured image or the product's stock status.""")
    {
        Why = """The markup works without them, but Google uses them for richer results: an article's image and date, a product's price and stock in merchant listings, an organization's logo in knowledge panels. A product needs an image to appear in merchant listings at all.""",
        LearnMore = "https://developers.google.com/search/docs/appearance/structured-data/article#structured-data-type-definitions",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.PageDetail
    };

    public static readonly IssueDefinition STRUCTURED_DATA_ON_NOINDEX = new("structured-data-on-noindex", IssueCategory.StructuredData, IssueSeverity.Notice,
        "Structured data on a noindex page",
        """The page tells search engines not to index it (noindex, in a meta tag or the X-Robots-Tag header) yet has structured data of a type with rich results, such as a Product, Article, Organization or BreadcrumbList, in JSON-LD or Microdata. The detail names the types.""",
        """If the page should be in search results, remove the noindex. If it shouldn't, the structured data does nothing there and can stay or go; make sure the indexable page it describes has it instead.""")
    {
        Why = """Google can't show a rich result for a page it doesn't index, so the markup can't do anything there. It's harmless, but it often means the noindex is a mistake on a page meant to rank.""",
        LearnMore = "https://developers.google.com/search/docs/appearance/structured-data/sd-policies#technical-guidelines",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.PageDetail,
        Evidence = ["metaRobots", "xRobotsTag"]
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
        Recheck = [],
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
        References = IssueReferences.Sources,
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.None
    };

    public static readonly IssueDefinition MIXED_CONTENT = new("mixed-content", IssueCategory.Security, IssueSeverity.Error,
        "Mixed content",
        "This HTTPS page loads images, scripts, stylesheets, iframes, audio or video from http:// URLs.",
        """Change the resource URLs to https:// or relative paths, and host copies of resources whose servers don't support HTTPS. A Content-Security-Policy with upgrade-insecure-requests is a useful safety net.""")
    {
        Why = """Browsers block insecure scripts, stylesheets and frames on HTTPS pages, which can break layout and features, and upgrade images, audio and video to HTTPS, which then fail to load if the server doesn't offer them that way. Google also lists insecure dependencies other than images among the things that can stop it preferring a page's HTTPS version.""",
        LearnMore = "https://developer.mozilla.org/en-US/docs/Web/Security/Defenses/Mixed_content",
        References = IssueReferences.Targets,
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.RelatedUrl,
        TargetFacts = ["kind", "statusCode", "contentType", "position", "links"]
    };

    public static readonly IssueDefinition SECURITY_HEADERS_MISSING = new("security-headers-missing", IssueCategory.Security, IssueSeverity.Notice,
        "Security headers missing",
        """The home page's response lacks one or more of Strict-Transport-Security (checked on HTTPS only), X-Content-Type-Options, Content-Security-Policy, X-Frame-Options (or a CSP frame-ancestors rule) and Referrer-Policy. The detail lists which.""",
        """Add the missing headers in the server, CDN or framework configuration, starting with X-Content-Type-Options and Referrer-Policy, which rarely break anything. Roll out CSP in report-only mode first, and enable HSTS only once HTTPS works everywhere on the site.""")
    {
        Why = """These headers protect visitors: HSTS keeps browsers on HTTPS, CSP limits what injected scripts can do, X-Frame-Options or frame-ancestors stops clickjacking, X-Content-Type-Options stops MIME sniffing, and Referrer-Policy limits which URLs leak to other sites (browsers already default to a safe policy, so it matters least). Google doesn't document them as a ranking factor. Only the home page is checked, as they're normally set site-wide.""",
        LearnMore = "https://web.dev/articles/security-headers",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.None,
        ReportedOnce = true
    };
    public static readonly IssueDefinition SECURITY_HEADERS_WEAK = new("security-headers-weak", IssueCategory.Security, IssueSeverity.Notice,
        "Security headers set too weakly",
        """The home page sends security headers set too weakly: Strict-Transport-Security (on HTTPS) whose max-age is missing, unreadable or under a year (31,536,000 seconds), or X-Content-Type-Options set to anything but nosniff. Checked once, for the whole site, as missing security headers are. The detail names each.""",
        """Send Strict-Transport-Security: max-age=31536000 (a year) or more, once the whole site works over HTTPS, and X-Content-Type-Options: nosniff. Your host or CDN usually sets them.""")
    {
        Why = """HSTS keeps browsers on HTTPS between visits only for as long as its max-age, and the preload list browsers ship asks for a year; a short one lapses between visits. X-Content-Type-Options has one value, nosniff: anything else does nothing.""",
        LearnMore = "https://hstspreload.org/#deployment-recommendations",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.None,
        ReportedOnce = true
    };

    public static readonly IssueDefinition COOKIES_INSECURE = new("cookies-insecure", IssueCategory.Security, IssueSeverity.Notice,
        "Cookies without Secure or HttpOnly",
        """The page's response sets cookies without basic protection: on HTTPS, without Secure; with SameSite=None but no Secure, which browsers refuse; or a session or login cookie (its name has sess, sid, auth, token, login or jwt) without HttpOnly. Only cookies the server sets are seen, not those a script sets. The detail names each cookie and what it lacks.""",
        """Add Secure to every cookie on an HTTPS site, and HttpOnly to session and login cookies, in the server's or framework's cookie settings.""")
    {
        Why = """A cookie without Secure can be sent over plain HTTP, where it can be read in transit; one without HttpOnly can be read by a script injected into the page, which is how sessions are stolen. Browsers drop a SameSite=None cookie without Secure altogether.""",
        LearnMore = "https://developer.mozilla.org/en-US/docs/Web/HTTP/Reference/Headers/Set-Cookie",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.PageDetail
    };

    public static readonly IssueDefinition FORM_NOT_SECURE = new("form-not-secure", IssueCategory.Security, IssueSeverity.Warning,
        "Form sends data over HTTP",
        """On an HTTPS page, a form sends what's typed to an http:// address; or on an HTTP page, a form asks for a password. The detail says where it sends. A crawl made before the crawler read forms isn't checked.""",
        """Point the form's action at the https:// address, or a relative one, and serve every page with a form over HTTPS.""")
    {
        Why = """What people type travels unencrypted, where others on the network can read or change it, and browsers warn them before sending it, or mark the page not secure.""",
        LearnMore = "https://developer.mozilla.org/en-US/docs/Web/Security/Mixed_content",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.PageDetail
    };

    public static readonly IssueDefinition PROTOCOL_RELATIVE_LINKS = new("protocol-relative-links", IssueCategory.Security, IssueSeverity.Notice,
        "Protocol-relative (//) URLs",
        """The page has href or src values written without their scheme, starting // (such as //cdn.example.com/app.js), which take the page's own: on an HTTP copy of the page they load over HTTP. The detail counts them and gives the first. A crawl made before the crawler noted them isn't checked.""",
        """Write each with https:// in front, now that every browser and most hosts support it.""")
    {
        Why = """They were a way to load the same file over HTTP and HTTPS. On an HTTPS page they load securely anyway, so the risk is only an HTTP copy of the page, but they're an old habit worth replacing, and some tools misread them.""",
        LearnMore = "https://www.paulirish.com/2010/the-protocol-relative-url/",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.None
    };


    // URLs
    public static readonly IssueDefinition URL_UPPERCASE = new("url-uppercase", IssueCategory.Urls, IssueSeverity.Notice,
        "Uppercase characters in URL",
        """This indexable page's URL path contains uppercase letters (percent-escapes such as %2F don't count).""",
        """Use lowercase for new URLs. Change an existing one only if it causes duplicates or broken links, with a 301 redirect to the lowercase version and updated internal links.""")
    {
        Why = """Google treats URLs as case-sensitive, so /Page and /page are different URLs: if the server serves both, mixed-case links create duplicates, and if it doesn't, links typed in lowercase break. Lowercase URLs avoid both. An established URL that works and is linked consistently is fine to leave, since changing it needs redirects.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/url-structure#be-aware-that-urls-are-case-sensitive",
        References = IssueReferences.Sources,
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.None
    };

    public static readonly IssueDefinition URL_UNDERSCORES = new("url-underscores", IssueCategory.Urls, IssueSeverity.Notice,
        "Underscores in URL",
        "This indexable page's URL path contains underscores.",
        """Use hyphens between words in new URLs. Rename existing ones only as part of a wider URL change, with 301 redirects.""")
    {
        Why = """Google recommends hyphens to separate words in URLs, because underscores are commonly read as joining words (as in format_date) rather than separating them, and hyphens help users and search engines identify the words. The effect is small, so don't change established URLs for this alone.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/url-structure#use-hyphens-to-separate-words",
        References = IssueReferences.Sources,
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
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
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.None
    };
    public static readonly IssueDefinition URL_TRACKING_PARAMETERS = new("url-tracking-parameters", IssueCategory.Urls, IssueSeverity.Notice,
        "Internal link with tracking parameters",
        """Pages on the site link to this URL of the site's with ad or campaign tracking parameters in its query: utm_source and the other utm_ parameters, gclid, fbclid, msclkid, mc_cid or _ga. The detail names them; where it's found lists the pages linking to it.""",
        """Link to the URL without its tracking parameters: they belong on links from emails, ads and other sites, not between your own pages.""")
    {
        Why = """Each tagged link is another URL for search engines to crawl and fold into the page they belong to, and analytics counts the visitor as arriving from a new campaign, which breaks up their visit.""",
        LearnMore = "https://support.google.com/analytics/answer/10917952",
        References = IssueReferences.Sources,
        Subject = IssueSubject.Url,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.None
    };

    public static readonly IssueDefinition URL_MULTIPLE_SLASHES = new("url-multiple-slashes", IssueCategory.Urls, IssueSeverity.Notice,
        "URL with multiple slashes",
        """This indexable page's path has two slashes in a row (//), which makes it a different URL from the one with a single slash. Where it's found lists the pages linking to it.""",
        """Link to the single-slash address, and redirect the double-slash one to it. The usual cause is a template joining a path and a folder that both end or start with a slash.""")
    {
        Why = """Most servers answer both with the same page, so the site has two URLs for it, which search engines must crawl and fold together.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/url-structure",
        References = IssueReferences.Sources,
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.None
    };

    public static readonly IssueDefinition URL_SPACE = new("url-space", IssueCategory.Urls, IssueSeverity.Notice,
        "URL with a space",
        """This indexable page's path has a space in it (written %20). Where it's found lists the pages linking to it.""",
        """Rename the page with hyphens in place of spaces, redirect the old address to it, and update the links.""")
    {
        Why = """A space isn't allowed in a URL as written, so it's escaped as %20, which reads badly and breaks when the address is copied into emails or documents.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/url-structure",
        References = IssueReferences.Sources,
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.None
    };

    public static readonly IssueDefinition URL_PARAMETERS = new("url-parameters", IssueCategory.Urls, IssueSeverity.Notice,
        "Indexable URL with parameters",
        """This indexable page's URL has a query (a ? part), and it isn't the site's search, which is its own check. The detail names the parameters; where it's found lists the pages linking to it.""",
        """If the parameters only sort, filter or track, point the page's canonical at the address without them. If they make a page worth finding, give it a plain address instead.""")
    {
        Why = """Every combination of parameters is another URL for search engines to crawl, and the same content under many of them divides its signals. Often fine, as for a page of a series, so it's for review.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/url-structure",
        References = IssueReferences.Sources,
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.None
    };

    public static readonly IssueDefinition URL_NON_ASCII = new("url-non-ascii", IssueCategory.Urls, IssueSeverity.Notice,
        "URL with non-ASCII characters",
        """This indexable page's path or query, decoded, has characters outside ASCII, such as accented or non-Latin letters. Where it's found lists the pages linking to it.""",
        """Nothing is broken: search engines read them. Use ASCII letters, hyphens and digits in new addresses if people will type or share them, and don't rename pages that already rank.""")
    {
        Why = """URLs are sent as ASCII, so other characters travel percent-encoded (café becomes caf%C3%A9), which some tools, emails and people get wrong when copying.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/url-structure",
        References = IssueReferences.Sources,
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.None
    };

    public static readonly IssueDefinition URL_REPETITIVE_PATH = new("url-repetitive-path", IssueCategory.Urls, IssueSeverity.Notice,
        "URL with a repeating path",
        """This URL of the site's has a path that repeats itself: one segment three times or more, or the same run of segments twice in a row (/shop/shop/, /a/b/a/b/). Where it's found lists the pages linking to it.""",
        """Find the link that builds it, usually a relative link such as href="shop/" that should be "/shop/", and fix it; redirect the URL to the right one.""")
    {
        Why = """A relative link resolved from the wrong place builds ever-longer URLs, which crawlers can follow without end, each answering with the same page.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/url-structure",
        References = IssueReferences.Sources,
        Subject = IssueSubject.Url,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.None
    };

    public static readonly IssueDefinition URL_INTERNAL_SEARCH = new("url-internal-search", IssueCategory.Urls, IssueSeverity.Notice,
        "Internal search results page",
        """This indexable page is the site's own search: its path has a segment "search", or its query asks something (s, q, query, search, keyword or keywords). The detail names the parameters; where it's found lists the pages linking to it.""",
        """Keep search results out of search engines: add noindex to them, or point their canonical at a real page, and avoid linking to searches from the site's pages.""")
    {
        Why = """Every search is another page of results that rarely helps anyone who lands on it from a search engine, and there's no end to them for a crawler.""",
        LearnMore = "https://developers.google.com/search/docs/essentials/spam-policies#thin-affiliate-pages",
        References = IssueReferences.Sources,
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
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
        Recheck = [RecheckKind.Urls],
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
        Recheck = [RecheckKind.Urls],
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
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.None
    };


    public static readonly IssueDefinition RESOURCE_PAST_CRAWL_LIMIT = new("resource-past-crawl-limit", IssueCategory.Performance, IssueSeverity.Warning,
        "Script or stylesheet over 2 MB",
        """A page uses this script or stylesheet, here or on another site, and it's larger than 2 MB, as far as Googlebot reads a file. It's measured as the crawl received it, compressed if the server compressed it, so a compressed file over the limit is larger still once decompressed, and one just under it may be over. The limit is Google's, not an analysis setting.""",
        """Split the file, or remove what pages don't use (unused code, inlined data, source maps), and compress it on the server; load what isn't needed at first later.""")
    {
        Why = """Google fetches each script and stylesheet a page uses separately, with the same 2 MB limit as the page, and ignores the rest of a larger one, so the page as Google renders it can be missing styles or behaviour, or break. A file that size also slows the page down for visitors on mobile.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/googlebot#how-googlebot-accesses-your-site",
        References = IssueReferences.Sources,
        Subject = IssueSubject.Url,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.None,
        Evidence = ["sizeBytes", "contentType"]
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
        Recheck = [RecheckKind.Urls],
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
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.None
    };

    public static readonly IssueDefinition CHARSET_MISSING = new("charset-missing", IssueCategory.Html, IssueSeverity.Notice,
        "Missing character encoding",
        """Nothing declares the page's character encoding where browsers look for it: no byte-order mark at the start of the file, no charset in the Content-Type header, and no meta tag (<meta charset> or http-equiv Content-Type) within the first 1024 bytes. The detail says where a meta tag further in ends.""",
        """Add <meta charset="utf-8"> as the first element in the <head>, within the first 1024 bytes of the file, or send Content-Type: text/html; charset=utf-8 from the server.""")
    {
        Why = """Without a declaration, browsers have to guess the encoding, and a wrong guess turns accented letters, currency symbols and curly quotes into garbled characters. W3C advises always declaring it, even when a page happens to display correctly. A page that starts with a byte-order mark has declared it, and isn't reported; a crawl made before the crawler recorded the mark still reports such a page, until it's crawled again. Likewise a crawl made before the crawler recorded where the meta tag ends passes one past the first 1024 bytes.""",
        LearnMore = "https://www.w3.org/International/questions/qa-html-encoding-declarations",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
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
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.PageDetail
    };

    public static readonly IssueDefinition X_CARD_MISSING = new("x-card-missing", IssueCategory.Html, IssueSeverity.Notice,
        "X (Twitter) card missing",
        """This indexable page has no twitter:card meta tag (by name or property), so X shows its link without a card.""",
        """Add <meta name="twitter:card" content="summary_large_image"> to the <head>; most SEO plugins do it with one setting. X takes the title, description and image from the Open Graph tags when there are no twitter: ones.""")
    {
        Why = """X reads twitter:card to choose a card; without it, a shared link gets no image or a small one. It falls back to Open Graph for everything else, so the one tag is usually all a page needs.""",
        LearnMore = "https://developer.x.com/en/docs/x-for-websites/cards/overview/markup",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.None
    };

    public static readonly IssueDefinition X_CARD_INCOMPLETE = new("x-card-incomplete", IssueCategory.Html, IssueSeverity.Notice,
        "X (Twitter) card incomplete",
        """This indexable page asks for an X card that can't be built: its type isn't summary, summary_large_image, app or player; it has no title (twitter:title, else og:title); a summary card has no image (twitter:image, else og:image); a player card has no twitter:player; or its twitter: tags are written only with property=, which X doesn't read. The detail gives each fault. A crawl made before the crawler told name= from property= isn't checked.""",
        """Fill in what the detail says is missing, or write the twitter: tags with name= rather than property=.""")
    {
        Why = """X builds a card only from the tags it reads; with a part missing it shows a bare link, so the card tag does nothing.""",
        LearnMore = "https://developer.x.com/en/docs/x-for-websites/cards/overview/markup",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.PageDetail
    };

    public static readonly IssueDefinition SOCIAL_IMAGE_RELATIVE = new("social-image-relative", IssueCategory.Html, IssueSeverity.Notice,
        "Open Graph or X image URL isn't absolute",
        """This indexable page's og:image (og:image:url, og:image:secure_url) or twitter:image isn't a full http:// or https:// URL: a path such as /img/share.jpg, or one starting with //. The detail gives the first.""",
        """Give the image as a full URL, starting with https:// and your domain.""")
    {
        Why = """The Open Graph protocol gives its URLs with http:// or https://, and networks fetch the image on their own: a relative path may be read from the wrong place or not at all, and the share shows no image.""",
        LearnMore = "https://ogp.me/",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.PageDetail
    };

    public static readonly IssueDefinition OG_URL_NOT_CANONICAL = new("og-url-not-canonical", IssueCategory.Html, IssueSeverity.Notice,
        "og:url isn't the page's address",
        """This indexable page's og:url, normalised, is another URL than its own: often every page's points at the home page. An indexable page's canonical is itself, so that's what og:url should give. The detail gives it, grouped by it, so a template's is one group.""",
        """Make og:url the page's own URL (most SEO plugins use the canonical), or remove it: networks then use the URL that was shared.""")
    {
        Why = """The Open Graph protocol defines og:url as the canonical URL of the object. Networks gather shares, likes and the preview under it, so one that names another page credits that page and sends clicks there.""",
        LearnMore = "https://ogp.me/",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.PageDetail
    };

    public static readonly IssueDefinition SOCIAL_TAGS_MULTIPLE = new("social-tags-multiple", IssueCategory.Html, IssueSeverity.Notice,
        "Open Graph or X tag repeated",
        """This indexable page gives og:title, og:description, og:url, og:type, twitter:card, twitter:title or twitter:description more than once, with different content. og:image and the other properties that take several values may repeat. The detail counts each, as "2 og:title".""",
        """Keep one of each: usually two plugins, or a plugin and the theme, both write them, and one can be turned off.""")
    {
        Why = """Each of these says one thing about the page; given twice with different values, each network picks one its own way, and it may not be the one you meant.""",
        LearnMore = "https://ogp.me/",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.PageDetail
    };

    public static readonly IssueDefinition OG_TITLE_DUPLICATE = new("og-title-duplicate", IssueCategory.Html, IssueSeverity.Notice,
        "Duplicate og:title",
        """Indexable pages share an og:title (or the twitter:title where it has none) while their <title>s differ: pages whose <title>s are the same are title-duplicate's, and their og:titles usually follow. A page whose og:title is its own title isn't flagged: it's the one the others copied. Grouped by the shared text.""",
        """Give each page its own og:title, usually by letting the SEO plugin fill it from the page's title.""")
    {
        Why = """Shared on a network, pages with one og:title look the same, however different they are: a template's fixed og:title is the usual cause.""",
        LearnMore = "https://ogp.me/",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.RelatedUrl,
        Evidence = ["title"]
    };

    public static readonly IssueDefinition OG_DESCRIPTION_DUPLICATE = new("og-description-duplicate", IssueCategory.Html, IssueSeverity.Notice,
        "Duplicate og:description",
        """Indexable pages share an og:description (or the twitter:description where it has none) while their meta descriptions differ. A page whose og:description is its own meta description isn't flagged: it's the one the others copied, as with a platform that gives every page without a description the home page's. Grouped by the shared text.""",
        """Give each page its own og:description, usually by letting the SEO plugin fill it from the page's meta description.""")
    {
        Why = """Shared on a network, pages with one og:description say the same under their titles, whatever they're about.""",
        LearnMore = "https://ogp.me/",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.RelatedUrl,
        Evidence = ["description"]
    };

    public static readonly IssueDefinition SOCIAL_IMAGE_BROKEN = new("social-image-broken", IssueCategory.Html, IssueSeverity.Notice,
        "Open Graph or X image broken",
        """This indexable page's og:image or twitter:image is broken (after any redirects), or answers with something other than an image, such as a page. Only checked when the crawl fetched images. The detail gives the first.""",
        """Point the tag at an image that loads, or upload the missing image again.""")
    {
        Why = """A network that can't load the share image shows the link without one. Only link previews suffer, so it's a notice.""",
        LearnMore = "https://ogp.me/",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.RelatedUrl,
        References = IssueReferences.Targets,
        TargetFacts = ["statusCode", "final", "contentType"]
    };

    public static readonly IssueDefinition SOCIAL_IMAGE_TOO_LARGE = new("social-image-too-large", IssueCategory.Html, IssueSeverity.Notice,
        "Open Graph or X image over 5 MB",
        """This indexable page's og:image or twitter:image is over 5 MB, as received. Only checked when the crawl fetched images. The detail gives the first.""",
        """Use a smaller copy: 1,200 × 630 pixels, saved as JPEG or WebP, is well under 1 MB.""")
    {
        Why = """X doesn't show an image over 5 MB; Facebook's limit is 8 MB, and a large file is slow to fetch for every preview.""",
        LearnMore = "https://developer.x.com/en/docs/x-for-websites/cards/overview/markup",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.RelatedUrl,
        References = IssueReferences.Targets,
        TargetFacts = ["sizeBytes", "contentType"]
    };

    public static readonly IssueDefinition SOCIAL_IMAGE_TOO_SMALL = new("social-image-too-small", IssueCategory.Html, IssueSeverity.Notice,
        "Open Graph or X image too small",
        """This indexable page's og:image is under 200 × 200 pixels, or the image X uses (twitter:image, else og:image) is under 300 × 157 on a summary_large_image card or 144 × 144 on a summary card. The proportions aren't judged: both networks crop. Only checked when the crawl fetched images. The detail gives the first.""",
        """Use an image at least 1,200 × 630 pixels, which suits every network.""")
    {
        Why = """Facebook rejects an image under 200 × 200 pixels and recommends 1,200 × 630; X shows a small image blurred, or none.""",
        LearnMore = "https://developers.facebook.com/docs/sharing/webmasters/images/",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.RelatedUrl,
        References = IssueReferences.Targets,
        TargetFacts = ["pixelWidth", "pixelHeight"]
    };

    public static readonly IssueDefinition HEAD_TAGS_IN_BODY = new("head-tags-in-body", IssueCategory.Html, IssueSeverity.Warning,
        "Head tags in the body",
        """Tags that belong in the <head> are in the <body> as browsers and Google parse the page: the <title>; a meta description, robots, viewport or the like; Open Graph or X tags; or a <link rel="alternate"> without hreflang. Usually an element that can't be in a head, such as a <div>, an <img> or stray text, ended the head early and pushed every tag after it into the body; the detail names what's out and what ended the head. Canonical and hreflang links have their own checks. A crawl made before the crawler noted these isn't checked.""",
        """Move the element that ended the head early into the body, or remove it; tags written in the body by mistake belong in the <head>. A plugin or tag manager that injects an element high in the head is the usual cause.""")
    {
        Why = """Search engines and social sites may ignore page settings that aren't in the head: Google says so of canonicals, and treats the rest as it can. One stray element moves every tag after it, so a whole template's titles, descriptions and robots rules can be out at once.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/valid-page-metadata",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.PageDetail, FaultOf = IssueFaults.HeadEndCause
    };

    public static readonly IssueDefinition HTML_STRUCTURE_ERRORS = new("html-structure-errors", IssueCategory.Html, IssueSeverity.Notice,
        "HTML document structure errors",
        """The page's HTML has a second <head> or <body> start tag, an <html> tag after content, or a <head> after the body has begun, which the HTML standard counts as errors. A missing <head> or <body> tag isn't one: both are optional. The detail says which. A crawl made before the crawler noted them isn't checked.""",
        """Fix the template so the page has one <html>, one <head> and one <body>, in that order, with nothing before the <html>. Content printed before the page starts, such as a server warning, is a common cause.""")
    {
        Why = """Browsers recover from these the same way every time, merging or dropping the extra tags, so visitors rarely notice. But the attributes on the second tag are lost, and the same fault can move tags out of the head.""",
        LearnMore = "https://html.spec.whatwg.org/multipage/parsing.html#parse-errors",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.PageDetail
    };

    public static readonly IssueDefinition VIEWPORT_INVALID = new("viewport-invalid", IssueCategory.Html, IssueSeverity.Warning,
        "Viewport that disables zoom or sets no width",
        """The page's viewport meta tag stops visitors zooming (user-scalable=no or 0, or a maximum-scale under 2), sets neither width nor initial-scale, or the page has two viewport tags that differ. The detail lists the faults.""",
        """Use <meta name="viewport" content="width=device-width, initial-scale=1"> and nothing that limits zoom; keep one viewport tag.""")
    {
        Why = """Stopping zoom fails people who need larger text, which WCAG requires to work up to 200%, and some browsers ignore it anyway. A viewport with no width lays the page out at desktop width on phones, as a missing one does.""",
        LearnMore = "https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/meta/name/viewport",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.PageDetail
    };

    public static readonly IssueDefinition CHARSET_INVALID = new("charset-invalid", IssueCategory.Html, IssueSeverity.Notice,
        "Character encoding declared wrongly",
        """The page's character encoding, as a byte-order mark, the Content-Type header or the meta tag declares it, isn't UTF-8; or the header and the meta tag name different encodings (the header wins); or the page has two meta declarations that differ. A meta tag after the first 1024 bytes declares nothing: with the header declaring the encoding it changes nothing, and without it that's a missing encoding. The detail says which.""",
        """Save and serve the page as UTF-8: send Content-Type: text/html; charset=utf-8, and put one <meta charset="utf-8"> first in the <head>.""")
    {
        Why = """The HTML standard requires UTF-8. A page in another encoding usually shows right, but a declaration that's wrong or contradicts another makes browsers guess, and a wrong guess garbles accented letters, currency symbols and quotes.""",
        LearnMore = "https://www.w3.org/International/questions/qa-html-encoding-declarations",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.PageDetail
    };

    public static readonly IssueDefinition LANG_INVALID = new("lang-invalid", IssueCategory.Html, IssueSeverity.Notice,
        "Invalid lang attribute",
        """The page's <html lang> isn't a well-formed language tag: a 2-letter ISO 639-1 language (or a 3-letter one), then optionally a 4-letter script and a 2-letter region or 3-digit area, all separated by hyphens, such as en, en-GB or zh-Hant-TW. en_GB, english and uk-en aren't. The detail gives the value.""",
        """Set lang to the page's language code, such as en or en-GB, with a hyphen, not an underscore, and the language before the region.""")
    {
        Why = """Screen readers use lang to choose the voice and pronunciation, and browsers for hyphenation and spelling; a value they can't read is as good as none. Google detects a page's language from its content, so this is about accessibility, not search.""",
        LearnMore = "https://www.w3.org/International/questions/qa-choosing-language-tags",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.PageDetail,
        Evidence = ["language"]
    };

    public static readonly IssueDefinition MOBILE_ALTERNATE = new("mobile-alternate", IssueCategory.Html, IssueSeverity.Notice,
        "Separate mobile URL",
        """The page names a separate mobile version with <link rel="alternate" media="…"> (without hreflang). The detail gives its URL. A crawl made before the crawler noted these isn't checked.""",
        """If the site has a separate mobile site, make sure each mobile page has a rel="canonical" back to its desktop page and the same content; consider moving to one responsive page per URL.""")
    {
        Why = """Google indexes the mobile version of pages, and still supports separate mobile URLs if the alternate and canonical links pair them up, but recommends responsive design: one URL is simpler to keep right, and a mobile site that has less content than the desktop one loses it in search.""",
        LearnMore = "https://developers.google.com/search/docs/crawling-indexing/mobile/mobile-sites-mobile-first-indexing",
        Subject = IssueSubject.Page,
        Recheck = [RecheckKind.Urls],
        Grouping = IssueGrouping.None
    };

    public static IReadOnlyList<IssueDefinition> All { get; } =
    [
        INTERNAL_CLIENT_ERROR, INTERNAL_SERVER_ERROR, INTERNAL_UNREACHABLE, INTERNAL_BLOCKED, CONTENT_TYPE_MISMATCH, SOFT_404,
        REDIRECT_CHAIN, REDIRECT_LOOP, REDIRECT_BROKEN, REDIRECT_TEMPORARY, REDIRECT_TEMPORARY_CACHED, REDIRECT_DOWNGRADE, META_REFRESH,
        BROKEN_INTERNAL_LINKS, BROKEN_EXTERNAL_LINKS, LINKS_TO_REDIRECTS, INTERNAL_NOFOLLOW_LINKS, EMPTY_ANCHOR_TEXT,
        BROKEN_FRAGMENT_LINKS, ORPHAN_PAGE,
        UNCRAWLABLE_LINKS, BROKEN_RESOURCES, LINKS_TO_LOCALHOST, FEW_INTERNAL_OUTLINKS, GENERIC_ANCHOR_TEXT, NON_INDEXABLE_INLINKS_ONLY,
        DEEP_PAGE, TOO_MANY_LINKS, SLOW_EXTERNAL_LINKS, PAGINATION_NOT_LINKED, PAGINATION_BROKEN, PAGINATION_TO_NON_INDEXABLE,
        PAGINATION_SEQUENCE_ERROR,
        NOINDEX_PAGE, NOFOLLOW_PAGE, BLOCKED_BY_ROBOTS, SNIPPET_BLOCKED, ROBOTS_TXT_UNREACHABLE,
        ROBOTS_TXT_UNREACHABLE_OTHER_ORIGIN, ROBOTS_TXT_MISSING, SITE_HIDDEN_FROM_SEARCH,
        ROBOTS_DIRECTIVES_CONFLICT, NOINDEX_NEAR_HOME, UNAVAILABLE_AFTER_PASSED, MEDIA_PREVIEW_BLOCKED, TRANSLATION_BLOCKED,
        INDEXIFEMBEDDED_WITHOUT_NOINDEX,
        CANONICALISED_PAGE, CANONICAL_MISSING, CANONICAL_MULTIPLE, CANONICAL_BROKEN, CANONICAL_TO_REDIRECT,
        CANONICAL_TO_NON_INDEXABLE, CANONICAL_OUTSIDE_HEAD, CANONICAL_RELATIVE, CANONICAL_INVALID,
        NOINDEX_WITH_CANONICAL,
        CANONICAL_TO_FIRST_PAGE, CANONICAL_OTHER_DOMAIN, CANONICAL_LOOP, CANONICAL_ON_REDIRECT, CANONICAL_FRAGMENT,
        CANONICAL_INVALID_ATTRIBUTE, CANONICAL_UNLINKED,
        SITEMAP_NON_INDEXABLE, NOT_IN_SITEMAP, SITEMAP_ERROR, SITEMAP_MISSING, SITEMAP_URL_OTHER_HOST,
        ROBOTS_TXT_NO_SITEMAP, SITEMAP_OVER_LIMIT, SITEMAP_NEAR_LIMIT, SITEMAP_EMPTY, SITEMAP_URL_REPEATED, SITEMAP_LASTMOD_INVALID,
        TITLE_MISSING, TITLE_MULTIPLE, TITLE_TOO_LONG, TITLE_TOO_SHORT, TITLE_DUPLICATE,
        DESCRIPTION_MISSING, DESCRIPTION_MULTIPLE, DESCRIPTION_TOO_LONG, DESCRIPTION_TOO_SHORT, DESCRIPTION_DUPLICATE,
        H1_MISSING, H1_MULTIPLE, H1_DUPLICATE, HEADING_LEVELS_SKIPPED, H1_IMAGE_ONLY, HEADING_TOO_LONG, H2_MISSING,
        LOW_WORD_COUNT, DUPLICATE_CONTENT, NEAR_DUPLICATE_CONTENT, DUPLICATE_SITE_VERSION, PLACEHOLDER_TEXT, READABILITY_LOW, BOILERPLATE_HEAVY,
        IMAGE_MISSING_ALT, IMAGE_ALT_TOO_LONG, IMAGE_MISSING_DIMENSIONS, IMAGE_BROKEN, IMAGE_TOO_LARGE,
        IMAGE_MISSING_SRC, IMAGE_NO_SRCSET, IMAGE_LEGACY_FORMAT, IMAGE_ALT_UNHELPFUL, IMAGE_HOTLINKED, IMAGE_INLINE_LARGE,
        HREFLANG_INVALID_CODE, HREFLANG_MISSING_SELF, HREFLANG_MISSING_RETURN, HREFLANG_BROKEN_TARGET,
        HREFLANG_TO_NON_INDEXABLE, HREFLANG_DUPLICATE_CODE, HREFLANG_MISSING_X_DEFAULT, HREFLANG_OUTSIDE_HEAD,
        HREFLANG_INCONSISTENT_CODE, HREFLANG_HEADER_MISMATCH, HREFLANG_UNLINKED, HREFLANG_LANG_MISMATCH, HREFLANG_SELF_ONLY,
        STRUCTURED_DATA_INVALID, STRUCTURED_DATA_MISSING_REQUIRED, STRUCTURED_DATA_SCHEMA_ERRORS, STRUCTURED_DATA_MISSING_RECOMMENDED,
        STRUCTURED_DATA_ON_NOINDEX,
        NO_HTTPS, HTTP_PAGE, MIXED_CONTENT, SECURITY_HEADERS_MISSING, SECURITY_HEADERS_WEAK, COOKIES_INSECURE, FORM_NOT_SECURE,
        PROTOCOL_RELATIVE_LINKS,
        URL_UPPERCASE, URL_UNDERSCORES, URL_TOO_LONG, URL_TRACKING_PARAMETERS, URL_MULTIPLE_SLASHES, URL_SPACE, URL_PARAMETERS, URL_NON_ASCII,
        URL_REPETITIVE_PATH, URL_INTERNAL_SEARCH,
        SLOW_RESPONSE, LARGE_PAGE, HTML_PAST_CRAWL_LIMIT, RESOURCE_PAST_CRAWL_LIMIT,
        VIEWPORT_MISSING, LANG_MISSING, CHARSET_MISSING, OPEN_GRAPH_MISSING, X_CARD_MISSING, X_CARD_INCOMPLETE, SOCIAL_IMAGE_RELATIVE,
        OG_URL_NOT_CANONICAL, SOCIAL_TAGS_MULTIPLE, OG_TITLE_DUPLICATE, OG_DESCRIPTION_DUPLICATE, SOCIAL_IMAGE_BROKEN, SOCIAL_IMAGE_TOO_LARGE,
        SOCIAL_IMAGE_TOO_SMALL,
        HEAD_TAGS_IN_BODY, HTML_STRUCTURE_ERRORS, VIEWPORT_INVALID, CHARSET_INVALID, LANG_INVALID, MOBILE_ALTERNATE
    ];

    private static readonly Dictionary<string, IssueDefinition> BY_ID = All.ToDictionary(issue => issue.Id, StringComparer.Ordinal);

    public static IssueDefinition? Find(string id) => BY_ID.GetValueOrDefault(id);
}
