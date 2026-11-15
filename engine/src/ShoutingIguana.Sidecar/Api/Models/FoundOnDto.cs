namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>Where each URL asked for is found, in the order asked; a URL the crawl doesn't have is left out.</summary>
internal sealed record FoundOnDto(IReadOnlyList<UrlFoundOnDto> Urls);
