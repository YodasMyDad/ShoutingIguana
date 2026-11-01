using System.Text.Json.Serialization;

namespace ShoutingIguana.Sidecar.Api.Models;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record StartCrawlRequest
{
    public CrawlOptionsDto? Options { get; init; }

    /// <summary>
    /// Analyse the crawl when it completes: true unless false is given. A crawl that stops isn't analysed by itself.
    /// Nullable because the source-generated reader sets an init property it wasn't given to its type's default.
    /// </summary>
    public bool? AnalyzeWhenDone { get; init; }
}
