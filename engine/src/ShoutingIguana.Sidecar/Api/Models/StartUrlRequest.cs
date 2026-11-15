using System.Text.Json.Serialization;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>An address as the user typed it, and the options the crawl would start with (the saved defaults when left out).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record StartUrlRequest
{
    public string? Address { get; init; }

    public CrawlOptionsDto? Options { get; init; }
}
