namespace ShoutingIguana.Sidecar.Api.Models;

/// <param name="Type"><c>number</c>, <c>text</c>, <c>enum</c> or <c>boolean</c>.</param>
/// <param name="IssueRowsOnly">One of a check's own columns, on its rows only.</param>
/// <param name="Title">The column's name in words (E5), as a header shows it and an export writes it.</param>
/// <param name="Unit">The unit of its numbers (E5), <c>ms</c> or <c>bytes</c>, which an export's header carries after the title; null when it has none.</param>
/// <param name="ValueTitles">An enum's values in words (E5), by value, as a cell shows them and an export writes them; empty for other types.</param>
internal sealed record UrlColumnDto(
    string Id,
    string Type,
    bool NeedsAnalysis,
    bool Sortable,
    bool Filterable,
    IReadOnlyList<string> Values,
    bool IssueRowsOnly,
    string Title,
    string? Unit,
    IReadOnlyDictionary<string, string> ValueTitles);
