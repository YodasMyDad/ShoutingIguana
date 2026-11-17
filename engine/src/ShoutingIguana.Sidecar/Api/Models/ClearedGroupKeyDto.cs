using System.Text.Json.Serialization;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>One cause in the other crawl, by what names it there: its detail and its related URL's text, as ids differ between files.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record ClearedGroupKeyDto
{
    public string? Detail { get; init; }
    public string? RelatedUrl { get; init; }
}
