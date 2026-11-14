namespace ShoutingIguana.Sidecar.Api.Models;

/// <param name="StartUrl">The address to start the crawl from, with its scheme.</param>
/// <param name="Reason"><c>typed</c>, <c>https</c>, <c>http</c> or <c>noAnswer</c>: see docs/v2-sidecar-api.md.</param>
internal sealed record StartUrlDto(string StartUrl, string Reason);
