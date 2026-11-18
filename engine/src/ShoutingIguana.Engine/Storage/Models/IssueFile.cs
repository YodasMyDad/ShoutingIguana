namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>A file a check of the site's own files is about: a robots.txt or a sitemap (<see cref="IssueFileReader"/>).</summary>
/// <param name="Url">The file's full URL, as the check's detail names it.</param>
/// <param name="SitemapId">The sitemap's id, as the sitemaps list gives it, for a sitemap the crawl knows; null for a robots.txt.</param>
public sealed record IssueFile(string Url, long? SitemapId);
