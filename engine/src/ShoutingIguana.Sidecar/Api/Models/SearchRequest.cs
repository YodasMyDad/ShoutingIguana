using System.Text.Json.Serialization;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>A search as it's typed, for the palette: the text so far, and how many results to give.</summary>
/// <param name="Text">What's typed so far; empty finds nothing, and readies the crawl for the first letter.</param>
/// <param name="Limit">1 to 50; 10 when left out.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record SearchRequest(string? Text, int? Limit);
