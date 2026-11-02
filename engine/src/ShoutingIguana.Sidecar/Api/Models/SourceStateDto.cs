namespace ShoutingIguana.Sidecar.Api.Models;

/// <param name="Stored">The crawl stored the URL's HTML, for <c>/source</c>.</param>
internal sealed record SourceStateDto(bool Stored);
