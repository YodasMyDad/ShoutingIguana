namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>A grid query ready to run: its conditions (the scope's first), its sort, and the columns its rows carry.</summary>
/// <param name="Scope">The scope's own condition, whose count is the grid's total; null for every URL.</param>
/// <param name="Columns">The columns asked for that have values in this mode, the URL's id first.</param>
/// <param name="Unavailable">The columns asked for that need a URL summary there isn't.</param>
/// <param name="Hash">The query's filters, search and sort, hashed, so a cursor from another query is refused.</param>
internal sealed record CompiledQuery(
    GridMode Mode,
    GridPredicate? Scope,
    IReadOnlyList<GridPredicate> Predicates,
    IReadOnlyList<GridSortKey> Sort,
    IReadOnlyList<UrlColumnDefinition> Columns,
    IReadOnlyList<string> Unavailable,
    string Hash)
{
    /// <summary>Every condition: the scope's, then the filters' and the search's.</summary>
    public IEnumerable<GridPredicate> All => Scope is null ? Predicates : Predicates.Prepend(Scope);
}
