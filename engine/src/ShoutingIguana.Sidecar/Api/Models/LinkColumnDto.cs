namespace ShoutingIguana.Sidecar.Api.Models;

/// <param name="Type"><c>number</c>, <c>text</c>, <c>enum</c> or <c>boolean</c>.</param>
internal sealed record LinkColumnDto(string Id, string Type, bool NeedsAnalysis, bool Sortable, bool Filterable, IReadOnlyList<string> Values);
