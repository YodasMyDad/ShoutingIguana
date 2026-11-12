namespace ShoutingIguana.Sidecar.Api.Models;

/// <param name="Type"><c>number</c>, <c>text</c>, <c>enum</c> or <c>boolean</c>.</param>
/// <param name="Title">The column's name in words (E5): "Kind", "Target status".</param>
/// <param name="Unit">The unit of its numbers (E5), or null.</param>
/// <param name="ValueTitles">An enum's values in words (E5), by value; empty for other types.</param>
internal sealed record LinkColumnDto(
    string Id,
    string Type,
    bool NeedsAnalysis,
    bool Sortable,
    bool Filterable,
    IReadOnlyList<string> Values,
    string Title,
    string? Unit,
    IReadOnlyDictionary<string, string> ValueTitles);
