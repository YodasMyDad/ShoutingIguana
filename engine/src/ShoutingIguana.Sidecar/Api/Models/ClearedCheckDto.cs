namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>Another check a fix of one clears (related checks).</summary>
/// <param name="CheckId">The check cleared.</param>
/// <param name="Pages">Its pages whose every target is a URL the fixed check flagged: the pages a fix clears there.</param>
/// <param name="All">They are every page it flagged: the fix clears it entirely.</param>
internal sealed record ClearedCheckDto(string CheckId, long Pages, bool All);
