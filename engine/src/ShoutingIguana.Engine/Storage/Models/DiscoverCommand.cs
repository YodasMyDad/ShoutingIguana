namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>URLs found outside page links: the start URL, or entries of a sitemap.</summary>
internal sealed record DiscoverCommand(IReadOnlyList<OutLink> Urls, DiscoverySource Source, int? SitemapId) : WriterCommand;
