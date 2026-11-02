namespace ShoutingIguana.Sidecar.Api.Models;

/// <param name="Type"><c>number</c>, <c>text</c>, <c>enum</c> or <c>boolean</c>.</param>
/// <param name="IssueRowsOnly">One of a check's own columns, on its rows only.</param>
internal sealed record UrlColumnDto(string Id, string Type, bool NeedsAnalysis, bool Sortable, bool Filterable, IReadOnlyList<string> Values, bool IssueRowsOnly);
