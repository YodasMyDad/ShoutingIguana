namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>One JSON-LD block of a page.</summary>
/// <param name="Valid">It parses as JSON. Trailing commas and comments don't.</param>
/// <param name="Types">Its <c>@type</c>s, those in a <c>@graph</c> included, in order.</param>
/// <param name="Error">Why it doesn't parse.</param>
/// <param name="Truncated">The page's block was longer than the parser keeps, so it was cut short and isn't judged.</param>
public sealed record StructuredDataBlock(bool Valid, IReadOnlyList<string> Types, string Json, string? Error, bool Truncated);
