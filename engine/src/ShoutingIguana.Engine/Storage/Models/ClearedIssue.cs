namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>A row a check flagged in the other crawl and doesn't flag now.</summary>
/// <param name="Url">The URL, as both crawls store it.</param>
/// <param name="UrlId">Its id in this crawl; null when this crawl didn't find it.</param>
/// <param name="ThenUrlId">Its id in the other crawl.</param>
/// <param name="State"><see cref="RowChangeState.Fixed"/>, <see cref="RowChangeState.Gone"/> or <see cref="RowChangeState.NotChecked"/>.</param>
/// <param name="Reason">Why it wasn't judged; null unless <see cref="RowChangeState.NotChecked"/>.</param>
/// <param name="DetailThen">The issue's detail then.</param>
/// <param name="RelatedUrlThen">The other URL the issue named then, as its text.</param>
public sealed record ClearedIssue(
    string Url,
    int? UrlId,
    int ThenUrlId,
    RowChangeState State,
    NotCheckedReason? Reason,
    string? DetailThen,
    string? RelatedUrlThen);
