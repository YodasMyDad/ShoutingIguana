namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>Where a cursor points: the last row read, by its sort keys' values and its URL id.</summary>
/// <param name="Values">Each sort key's value: a long, a string, or null.</param>
internal sealed record GridCursorPosition(IReadOnlyList<object?> Values, long Id);
