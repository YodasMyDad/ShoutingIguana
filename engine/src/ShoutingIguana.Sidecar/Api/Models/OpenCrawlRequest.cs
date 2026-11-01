using System.Text.Json.Serialization;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <param name="Path">An absolute path to a .sicrawl file.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record OpenCrawlRequest(string? Path);
