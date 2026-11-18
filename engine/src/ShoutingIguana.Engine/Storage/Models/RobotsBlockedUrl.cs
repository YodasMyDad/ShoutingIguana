namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// A URL robots.txt kept the crawl from, with why, and what blocking it costs as the analysis counted it: the pages that
/// link to it, the sitemaps that list it, whether a menu links to it. Those are null when the crawl has changed since its
/// analysis (or was never analysed), as the URL grid's columns of the URL summary are.
/// </summary>
/// <param name="Line">The line of the stored file that refused it, for <see cref="RobotsBlockReason.Rule"/>.</param>
/// <param name="Rule">That line's text.</param>
/// <param name="Error">How robots.txt failed, for <see cref="RobotsBlockReason.Unreachable"/>, when stored.</param>
public sealed record RobotsBlockedUrl(int Id, string Url, RobotsBlockReason Reason, int? Line, string? Rule, string? Error)
{
    /// <summary>The pages that link to it, a page's links to itself left out: the URL summary's <c>linking_pages</c>.</summary>
    public long? LinkingPages { get; init; }

    /// <summary>
    /// A sitemap lists it; false only when every sitemap was read and none does, else null when none of those read does
    /// (the URL summary's <c>in_sitemap</c>).
    /// </summary>
    public bool? InSitemap { get; init; }

    /// <summary>The sitemaps that list it; null where <see cref="InSitemap"/> isn't true.</summary>
    public long? Sitemaps { get; init; }

    /// <summary>The first sitemap that lists it, in the order the crawl found them.</summary>
    public SitemapRef? Sitemap { get; init; }

    /// <summary>A page's menu (a hyperlink in its navigation) links to it, a page's link to itself left out.</summary>
    public bool? InMenu { get; init; }

    /// <summary>
    /// The site asks for it and blocks it: a sitemap lists it, or a menu links to it. Null while what blocking it costs
    /// isn't known.
    /// </summary>
    public bool? Conflict { get; init; }
}
