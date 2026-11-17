namespace ShoutingIguana.Sidecar.Api.Models;

/// <param name="Count">URLs the check flagged: 0 means it ran and found nothing.</param>
/// <param name="LikelyTemplate">
/// For a check that doesn't group, that it flagged nearly every page audited, so it's probably the site's template (evidence).
/// </param>
internal sealed record CheckCountDto(string CheckId, long Count, bool LikelyTemplate = false);
