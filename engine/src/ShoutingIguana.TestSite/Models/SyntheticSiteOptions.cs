namespace ShoutingIguana.TestSite.Models;

public sealed record SyntheticSiteOptions
{
    /// <summary>Home page plus products (/p/1 ... /p/{PageCount-1}).</summary>
    public int PageCount { get; init; } = 1_000;

    /// <summary>Links in each product's content area, including next/previous/tree links.</summary>
    public int ContentLinksPerPage { get; init; } = 20;

    public int Seed { get; init; } = 42;

    /// <summary>Gzip HTML/XML/CSS/JS responses, as most real sites do.</summary>
    public bool Compress { get; init; } = true;

    /// <summary>
    /// Serve the site over HTTPS with a self-signed certificate (<see cref="SyntheticSiteServer.Certificate"/>), plus
    /// plain HTTP on the same port, and plant the checks that only apply to HTTPS sites. The external site stays HTTP.
    /// </summary>
    public bool Https { get; init; }

    /// <summary>Declare sitemaps in robots.txt and serve them. Without them the orphan pages can't be found.</summary>
    public bool Sitemaps { get; init; } = true;

    /// <summary>
    /// With <see cref="Sitemaps"/>, name the sitemap in robots.txt. Without the line the crawler still finds it, at
    /// /sitemap.xml, so <c>robots-txt-no-sitemap</c> says robots.txt doesn't name it.
    /// </summary>
    public bool SitemapInRobots { get; init; } = true;

    /// <summary>
    /// A plain HTTP site whose https:// version, on the same port, doesn't answer, and which links to it: once from the
    /// plants hub, a URL the crawler asks for and finds dead, and <see cref="SyntheticSite.DEAD_TWIN_UNCHECKED"/> times
    /// from the first orphan page. Only the sitemap leads to that page, so the crawler reads it once no linked URL is in
    /// flight, by when it has written the twin off: those URLs are never asked for ("Not checked"), as on a real site
    /// that links a dead www twin. The benchmark plants it, so the stats and the URL summary are proven to count such
    /// URLs alike. Off by default: a crawl that is stopped and resumed forgets which origins it wrote off, so it may ask
    /// for some of them, and the dead URL's retries add a few seconds to a small crawl. Expects robots.txt respected.
    /// </summary>
    public bool DeadTwin { get; init; }

    /// <summary>
    /// The home page and every product page are noindex, as on a site launched with its staging setting left on, so the
    /// site as a whole keeps itself out of search: once more than half of its pages are noindex (a
    /// <see cref="PageCount"/> of about 105 or more, as the categories, static pages and planted pages aren't noindex;
    /// the canonicalised ones among those are no part of the count), <c>site-hidden-from-search</c> is reported on the
    /// home page. The pages' other checks follow: no product is indexable, so their titles and descriptions aren't
    /// judged, and the home page and each product is a sitemap entry that can't be indexed. Off by default, and in the
    /// benchmark.
    /// </summary>
    public bool HiddenFromSearch { get; init; }

    /// <summary>
    /// Every product page is noindex and the home page is not, as on a shop that keeps its many listings out of search
    /// on purpose. More than half of the pages are then noindex (from the same <see cref="PageCount"/> as
    /// <see cref="HiddenFromSearch"/>), and <c>site-hidden-from-search</c> must not be reported: the home page can be
    /// indexed, so the site isn't hiding itself. The products' other checks follow as they do there. Off by default.
    /// </summary>
    public bool NoindexProducts { get; init; }

    /// <summary>
    /// Product pages behind a rate limit with a renewing penalty, like a real shop's: a crawl that keeps to the pace the
    /// site teaches it ends with every page fetched, and one the limit never lets through ends with them not checked.
    /// Null, the default, for none.
    /// </summary>
    public SyntheticRateLimit? RateLimit { get; init; }

    /// <summary>
    /// Which version of the site to serve: 0, the site as it has always been, or 1, the same site "after the fixes",
    /// for the fix loop's tests. Revision 1 fixes some planted problems, leaves others, and plants a few new ones, each
    /// on its own URLs, so a comparison of a crawl of each finds every state exactly
    /// (<see cref="SyntheticSite.GetChanges"/>). A running server switches between them
    /// (<see cref="SyntheticSiteServer.Revision"/>).
    /// </summary>
    public int Revision { get; init; }
}
