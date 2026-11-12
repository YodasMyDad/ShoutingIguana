namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>A column the URL grid can show, for the column chooser and the filter bar.</summary>
/// <param name="NeedsAnalysis">It reads the URL summary, so it has no values without one (<see cref="SummaryState"/>).</param>
/// <param name="Values">An enum's values, in order; empty for other types.</param>
public sealed record UrlColumnInfo(string Id, UrlColumnType Type, bool NeedsAnalysis, bool Sortable, bool Filterable, IReadOnlyList<string> Values)
{
    private static readonly IReadOnlyDictionary<string, string> NO_WORDS = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// The column's name in words, as a grid's header shows it and an export writes it ("Status", "Target status"), so the
    /// two can't differ.
    /// </summary>
    public string Title { get; init; } = Id;

    /// <summary>The unit of its numbers (<c>ms</c>, <c>bytes</c>), which an export's header carries after the title; null when it has none.</summary>
    public string? Unit { get; init; }

    /// <summary>An enum's values in words, by value ("clientError" is "Client error"), as a cell shows them; empty for other types.</summary>
    public IReadOnlyDictionary<string, string> ValueTitles { get; init; } = NO_WORDS;
}
