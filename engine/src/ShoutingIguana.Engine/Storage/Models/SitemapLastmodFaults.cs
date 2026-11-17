namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>A sitemap's entries whose lastmod is in the future, or isn't a date (<see cref="Sitemaps.SitemapLastmod"/>).</summary>
internal sealed record SitemapLastmodFaults(string SitemapUrl, int Future, int Unreadable);
