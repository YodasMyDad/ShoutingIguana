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
    /// A plain HTTP site whose https:// version, on the same port, doesn't answer, and which links to it: once from the
    /// plants hub, a URL the crawler asks for and finds dead, and <see cref="SyntheticSite.DEAD_TWIN_UNCHECKED"/> times
    /// from the first orphan page. Only the sitemap leads to that page, so the crawler reads it once no linked URL is in
    /// flight, by when it has written the twin off: those URLs are never asked for ("Not checked"), as on a real site
    /// that links a dead www twin. The benchmark plants it, so the stats and the URL summary are proven to count such
    /// URLs alike. Off by default: a crawl that is stopped and resumed forgets which origins it wrote off, so it may ask
    /// for some of them, and the dead URL's retries add a few seconds to a small crawl. Expects robots.txt respected.
    /// </summary>
    public bool DeadTwin { get; init; }
}
