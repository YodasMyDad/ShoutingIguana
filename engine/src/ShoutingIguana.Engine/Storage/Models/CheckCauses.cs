namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// What one check that found something comes to as work: the separate things to change rather than the URLs it flagged,
/// and the other checks fixing it also clears (<see cref="IssueCauseReader"/>).
/// </summary>
/// <param name="Causes">
/// The separate things to change: a group that shares a cause (one other URL, as a broken link's target; or a fault in the
/// site's template) is one, and every other flagged URL is one of its own. So 264 pages with the same logo link without
/// text are one; 40 titles too long are 40. Never more than the check's groups and flagged URLs allow.
/// </param>
/// <param name="InTemplate">Of <see cref="Causes"/>, those that are likely a fault in the site's template: one fix each.</param>
/// <param name="Clears">
/// The checks of its <c>IssueDefinition.Clears</c> that found something and that a fix of it clears on at least one page,
/// in the order the catalog names them.
/// </param>
public sealed record CheckCauses(long Causes, long InTemplate, IReadOnlyList<ClearedCheck> Clears);
