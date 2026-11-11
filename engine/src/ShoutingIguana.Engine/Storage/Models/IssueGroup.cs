namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>The issues of one check with the same detail and other URL.</summary>
/// <param name="Pages">
/// The URLs in the group. The name is from before a check said what its flagged URLs are (<c>IssueDefinition.Subject</c>):
/// for a check that can flag any URL (a missing image, a redirect) they aren't all pages. <see cref="Urls"/> is the same
/// count under a name that's true of every check; this one stays for what already reads it.
/// </param>
/// <param name="Share">
/// Its part of the check's flagged URLs, 0 to 1. A check grouped by the other URL has a page in a group per URL it names,
/// so its groups' shares can add up to more than 1.
/// </param>
/// <param name="LikelyTemplate">
/// Probably a fault in the site's template (<see cref="IssueGroupReader.IsLikelyTemplate"/>): one fix clears the group.
/// </param>
public sealed record IssueGroup(string? Detail, int? RelatedUrlId, string? RelatedUrl, long Pages, double Share, bool LikelyTemplate)
{
    /// <summary>The URLs in the group: pages for a check that flags only pages, any URL for one that can flag any.</summary>
    public long Urls => Pages;
}
