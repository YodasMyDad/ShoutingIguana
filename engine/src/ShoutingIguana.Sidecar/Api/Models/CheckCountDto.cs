namespace ShoutingIguana.Sidecar.Api.Models;

/// <param name="Count">URLs the check flagged: 0 means it ran and found nothing.</param>
/// <param name="LikelyTemplate">
/// For a check that doesn't group, that it flagged nearly every page audited, so it's probably the site's template (evidence).
/// </param>
/// <param name="Causes">
/// The separate things to change its issues come to (headline counts): a group that shares a cause, or is likely the site's
/// template, is one; any other URL is one of its own. Null for a check that found nothing, one this engine doesn't know,
/// and when the analysis changed while it was read.
/// </param>
/// <param name="TemplateCauses">Of <paramref name="Causes"/>, those likely in the site's template. Null when it is.</param>
/// <param name="Clears">
/// The checks a fix of this one also clears, with the pages it clears there (related checks); empty when none, and when
/// <paramref name="Causes"/> is null.
/// </param>
/// <remarks>
/// <see cref="Platform"/>: the platform that set everything the check found, which the site's owner can't change (a Shopify
/// store's security headers, Shopify's own cookies), with why; null for a check of the site's own, and for an analysis by
/// an engine from before it. Such a check still counts, and is in no score line and not in Fix first.
/// </remarks>
internal sealed record CheckCountDto(string CheckId, long Count, bool LikelyTemplate = false)
{
    public long? Causes { get; init; }

    public long? TemplateCauses { get; init; }

    public IReadOnlyList<ClearedCheckDto> Clears { get; init; } = [];

    public PlatformDto? Platform { get; init; }
}
