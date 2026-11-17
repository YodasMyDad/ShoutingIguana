namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>One check's rows joined across two crawls: how they moved, and the rows flagged then and not now.</summary>
internal sealed record CheckPass(RowSplit Split, IReadOnlyList<ClearedRow> Cleared);
