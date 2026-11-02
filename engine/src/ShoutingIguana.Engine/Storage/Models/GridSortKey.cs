namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>One key of a compiled sort: its SQL with and without its index, and the column it sorts by.</summary>
/// <param name="Indexed">An index gives this order, so the plan can walk it (the URL summary's, the row id, the URL's).</param>
internal sealed record GridSortKey(UrlColumnDefinition Column, string Sql, string Disabled, bool Descending, bool Nullable, bool Indexed);
