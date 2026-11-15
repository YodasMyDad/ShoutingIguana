using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>Where a URL is found: the links to it and the pages they're on, the first of them, the sitemaps listing it.</summary>
/// <param name="PagesExact">False when <paramref name="Pages"/> is a floor: past the links the engine reads to count pages.</param>
/// <param name="Sources">The first links to it, each naming the page it's on as an inlink names it.</param>
/// <param name="Start">It's the crawl's start URL.</param>
internal sealed record UrlFoundOnDto(int UrlId, long Links, long Pages, bool PagesExact, IReadOnlyList<LinkRowDto> Sources, UrlSitemaps Sitemaps, bool Start)
{
    public static UrlFoundOnDto From(UrlFoundOn url) =>
        new(url.UrlId, url.Links, url.Pages, url.PagesExact, [.. url.Sources.Select(LinkRowDto.From)], url.Sitemaps, url.Start);
}
