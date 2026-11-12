namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>A sitemap of the crawl: its id, which <see cref="SitemapListReader.Read"/> and the sitemap's URL grid know it by, and the URL it was found at.</summary>
public sealed record SitemapRef(int Id, string Url);
