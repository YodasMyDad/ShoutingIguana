namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>The URL ids a condition matched, as a JSON array for <c>json_each</c>; null when there were too many to keep.</summary>
internal sealed record IdSet(string? Json, int Count);
