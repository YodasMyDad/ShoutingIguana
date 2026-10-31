namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>The writer's in-memory view of one known URL, keyed by its 128-bit hash.</summary>
internal record struct UrlEntry(int Id, int Depth, UrlState State, SkipReason Skip, FetchMode Mode);
