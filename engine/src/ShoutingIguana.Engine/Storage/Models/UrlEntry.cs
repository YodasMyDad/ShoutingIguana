namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>The writer's in-memory view of one known URL, keyed by its 128-bit hash.</summary>
/// <param name="IsProbe">Only a site-version probe has found it so far (<see cref="DiscoverySource.SiteVersion"/>). It fits in the entry's padding.</param>
internal record struct UrlEntry(int Id, int Depth, UrlState State, SkipReason Skip, FetchMode Mode, bool IsProbe = false);
