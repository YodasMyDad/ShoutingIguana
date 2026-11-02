using System.Text.Json.Serialization;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <param name="Url">A URL, or a path on the start URL's origin.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record UrlLookupRequest(string? Url);
