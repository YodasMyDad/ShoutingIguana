namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>A robots.txt or sitemap a check of the site's own files is about.</summary>
/// <param name="Url">The file's full URL.</param>
/// <param name="SitemapId">The sitemap's id, as <c>GET /api/crawls/{id}/sitemaps</c> lists it; null for a robots.txt.</param>
internal sealed record IssueFileDto(string Url, long? SitemapId);
