namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>A row a check flagged then and not now, as the comparison's pass over the check found it: ids only, its text read for a page.</summary>
/// <param name="UrlId">Its id in this crawl; null when this crawl didn't find it.</param>
internal readonly record struct ClearedRow(int ThenUrlId, int? UrlId, RowChangeState State, NotCheckedReason? Reason);
