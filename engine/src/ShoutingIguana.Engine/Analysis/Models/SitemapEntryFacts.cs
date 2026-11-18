using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Analysis.Models;

/// <summary>What the sitemap checks read from the crawl's sitemap entries (<c>sitemap_urls</c>), beyond the files.</summary>
internal sealed record SitemapEntryFacts
{
    /// <summary>URLs more than one sitemap file lists, with the sitemaps that do, in the order the crawl found them.</summary>
    public IReadOnlyDictionary<int, IReadOnlyList<string>> Repeated { get; init; } = new Dictionary<int, IReadOnlyList<string>>();

    /// <summary>Entries on another scheme than every sitemap listing them: the URL's scheme and the first such sitemap's.</summary>
    public IReadOnlyDictionary<int, (string Url, string Sitemap)> OtherScheme { get; init; } = new Dictionary<int, (string Url, string Sitemap)>();

    /// <summary>Each sitemap with entries whose lastmod is in the future or unreadable.</summary>
    public IReadOnlyList<SitemapLastmodFaults> Lastmods { get; init; } = [];
}
