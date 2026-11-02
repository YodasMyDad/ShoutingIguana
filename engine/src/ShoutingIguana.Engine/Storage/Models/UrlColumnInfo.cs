namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>A column the URL grid can show, for the column chooser and the filter bar.</summary>
/// <param name="NeedsAnalysis">It reads the URL summary, so it has no values without one (<see cref="SummaryState"/>).</param>
/// <param name="Values">An enum's values, in order; empty for other types.</param>
public sealed record UrlColumnInfo(string Id, UrlColumnType Type, bool NeedsAnalysis, bool Sortable, bool Filterable, IReadOnlyList<string> Values);
