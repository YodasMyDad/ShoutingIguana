namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// The origins a search matches, counted for a filter bar: all of them, those that blocked at least one URL and those
/// that blocked none. Each is how many rows the list has under that filter.
/// </summary>
public sealed record RobotsOriginCounts(long All, long Blocked, long NotBlocked);
