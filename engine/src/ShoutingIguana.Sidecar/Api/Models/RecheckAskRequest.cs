using System.Text.Json.Serialization;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>
/// What a recheck is asked to settle (the fix loop): a check, optionally one of its groups, or a list of URLs (with the
/// check to judge them by, or none). <c>POST …/rechecks/plan</c> takes it as it is.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal record RecheckAskRequest
{
    public string? CheckId { get; init; }

    /// <summary>One of the check's groups, as <c>/rows</c> takes it.</summary>
    public IssueGroupKeyDto? Group { get; init; }

    /// <summary>The URLs to judge, by their ids in the crawl: 1 to 1,000 of them, in place of the check's.</summary>
    public IReadOnlyList<long>? UrlIds { get; init; }
}
