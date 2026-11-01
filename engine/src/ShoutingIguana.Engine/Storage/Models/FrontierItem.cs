namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>A queued URL claimed for fetching.</summary>
/// <param name="Source">How it was found, as stored when it was claimed: a site-version probe is told apart by it.</param>
internal sealed record FrontierItem(int Id, string Url, int Depth, FetchMode Mode, bool IsInternal, DiscoverySource Source);
