namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// The URLs a sitemap lists, each once (an index: those of every sitemap under it), as the URL grid's <c>fetched</c>,
/// <c>indexable</c> and <c>outcome</c> columns judge them, so each count is the rows of the sitemap's URL grid under
/// that filter.
/// </summary>
/// <param name="Fetched">Those the crawl has an outcome for.</param>
/// <param name="Indexable">Those judged indexable.</param>
/// <param name="NonIndexable">Those judged not; the rest have no verdict (not fetched, or only status-checked).</param>
/// <param name="RobotsDisallowed">Those robots.txt kept the crawl from: the grid's <c>outcome</c> <c>robotsDisallowed</c>.</param>
/// <param name="RedirectingOrBroken">
/// Those that answered with a redirect or an error, or didn't answer: the grid's <c>outcome</c> <c>redirect</c>,
/// <c>clientError</c>, <c>serverError</c>, <c>timeout</c> or <c>networkError</c>. Not the ones robots.txt or a firewall
/// kept the crawl from, nor those not checked, which aren't known to be broken.
/// </param>
public sealed record SitemapUrlCounts(long Listed, long Fetched, long Indexable, long NonIndexable, long RobotsDisallowed = 0, long RedirectingOrBroken = 0)
{
    /// <summary>The outcomes <see cref="RedirectingOrBroken"/> counts.</summary>
    public static IReadOnlyList<UrlOutcome> REDIRECTING_OR_BROKEN { get; } =
        [UrlOutcome.Redirect, UrlOutcome.ClientError, UrlOutcome.ServerError, UrlOutcome.Timeout, UrlOutcome.NetworkError];

    /// <summary>These and another sitemap's, added up: for sitemaps that share no URL.</summary>
    public SitemapUrlCounts Plus(SitemapUrlCounts other) =>
        new(Listed + other.Listed, Fetched + other.Fetched, Indexable + other.Indexable, NonIndexable + other.NonIndexable,
            RobotsDisallowed + other.RobotsDisallowed, RedirectingOrBroken + other.RedirectingOrBroken);
}
