namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// The URLs a sitemap lists, each once (an index: those of every sitemap under it), as the URL grid's <c>fetched</c> and
/// <c>indexable</c> columns judge them, so each count is the rows of the sitemap's URL grid under that filter.
/// </summary>
/// <param name="Fetched">Those the crawl has an outcome for.</param>
/// <param name="Indexable">Those judged indexable.</param>
/// <param name="NonIndexable">Those judged not; the rest have no verdict (not fetched, or only status-checked).</param>
public sealed record SitemapUrlCounts(long Listed, long Fetched, long Indexable, long NonIndexable);
