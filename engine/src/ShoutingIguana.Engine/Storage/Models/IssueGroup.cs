namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>The issues of one check with the same detail and other URL.</summary>
/// <param name="Pages">The URLs in the group.</param>
/// <param name="Share">Its part of the check's flagged URLs, 0 to 1.</param>
/// <param name="LikelyTemplate">
/// Probably a fault in the site's template (<see cref="IssueGroupReader.IsLikelyTemplate"/>): one fix clears the group.
/// </param>
public sealed record IssueGroup(string? Detail, int? RelatedUrlId, string? RelatedUrl, long Pages, double Share, bool LikelyTemplate);
