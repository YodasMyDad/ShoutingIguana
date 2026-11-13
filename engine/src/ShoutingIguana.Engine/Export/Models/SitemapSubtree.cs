namespace ShoutingIguana.Engine.Export.Models;

/// <summary>How far the crawl got with the sitemaps under one, however deep: what an index's own counts rest on (<see cref="SitemapListWords.SubtreeOf"/>).</summary>
/// <param name="Sitemaps">The sitemaps under it that the crawl has.</param>
/// <param name="Read">Those whose own counts are known: read, or failed after an entry was read.</param>
/// <param name="More">
/// The crawl may not have every sitemap it lists: it's still reading sitemaps, stopped before it had read them all, or
/// left one unread at its limits, which stands for any more like it.
/// </param>
internal readonly record struct SitemapSubtree(int Sitemaps, int Read, bool More)
{
    /// <summary>The crawl has every sitemap under it, and read each.</summary>
    public bool Whole => !More && Read == Sitemaps;
}
