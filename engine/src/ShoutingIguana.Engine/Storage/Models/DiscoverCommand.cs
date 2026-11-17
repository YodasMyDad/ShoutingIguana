namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>URLs found outside page links: the start URL, or entries of a sitemap.</summary>
internal sealed record DiscoverCommand(IReadOnlyList<OutLink> Urls, DiscoverySource Source, int? SitemapId) : WriterCommand
{
    /// <summary>A sitemap's entries' lastmods, as <see cref="Urls"/> orders them; null for a source that has none.</summary>
    public IReadOnlyList<long?>? Lastmods { get; init; }
}
