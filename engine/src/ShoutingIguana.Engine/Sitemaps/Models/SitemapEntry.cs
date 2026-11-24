namespace ShoutingIguana.Engine.Sitemaps.Models;

/// <param name="IsSitemap">The entry is a child sitemap listed by a sitemap index, not a page.</param>
/// <param name="Lastmod">The entry's <c>&lt;lastmod&gt;</c> as written, trimmed; null when it has none (a text sitemap's never do).</param>
/// <param name="HasExtension">
/// The entry carries a tag of one of Google's sitemap extensions (<c>image:image</c>, <c>video:video</c>, <c>news:news</c>):
/// what an image, video or news sitemap lists a page for.
/// </param>
internal sealed record SitemapEntry(string Location, bool IsSitemap, string? Lastmod = null, bool HasExtension = false);
