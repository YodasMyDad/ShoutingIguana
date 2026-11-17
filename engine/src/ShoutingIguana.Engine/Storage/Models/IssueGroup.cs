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
    /// <summary>
    /// What the group's related URL is (its status, the response it ends at, where its redirects end, whether it can be
    /// indexed), or null for a group without one.
    /// </summary>
    public UrlFacts? Related { get; init; }

    /// <summary>
    /// What a duplicate cluster's pages share, as its first page writes it: the title of <c>title-duplicate</c>'s, the
    /// meta description of <c>description-duplicate</c>'s, the H1 of <c>h1-duplicate</c>'s. Null for every other group.
    /// </summary>
    public string? SharedText { get; init; }

    /// <summary>The URLs in the group: pages for a check that flags only pages, any URL for one that can flag any.</summary>
    public long Urls => Pages;
}
