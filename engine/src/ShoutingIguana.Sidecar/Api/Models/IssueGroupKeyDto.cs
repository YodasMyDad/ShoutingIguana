using System.Text.Json.Serialization;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>One group of a check's issues: its detail and other URL, null matching null. A relatedUrl check's group is its URL alone.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record IssueGroupKeyDto
{
    public string? Detail { get; init; }
    public int? RelatedUrlId { get; init; }
}
