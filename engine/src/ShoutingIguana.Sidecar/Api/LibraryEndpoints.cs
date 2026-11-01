using ShoutingIguana.Sidecar.Crawls;
using ShoutingIguana.Sidecar.Serialization;

namespace ShoutingIguana.Sidecar.Api;

/// <summary>The crawls the library lists, and adding, removing and deleting them.</summary>
internal static class LibraryEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet("/api/crawls", (CrawlManager crawls) => TypedResults.Json(crawls.List(), SidecarJsonContext.Default.IReadOnlyListCrawlListItem));

        api.MapGet("/api/crawls/{id}", (string id, CrawlManager crawls) => TypedResults.Json(crawls.Get(id), SidecarJsonContext.Default.CrawlListItem));

        api.MapPost("/api/crawls/open", async (HttpRequest request, CrawlManager crawls) =>
        {
            var body = await RequestBody.ReadAsync(request, SidecarJsonContext.Default.OpenCrawlRequest).ConfigureAwait(false);
            return TypedResults.Json(crawls.Open(body.Path ?? string.Empty), SidecarJsonContext.Default.CrawlListItem);
        });

        api.MapDelete("/api/crawls/{id}", (string id, CrawlManager crawls) =>
        {
            crawls.Remove(id);
            return TypedResults.NoContent();
        });

        api.MapPost("/api/crawls/{id}/restore", (string id, CrawlManager crawls) =>
            TypedResults.Json(crawls.Restore(id), SidecarJsonContext.Default.CrawlListItem));

        api.MapPost("/api/crawls/{id}/delete-file", (string id, CrawlManager crawls) =>
        {
            crawls.DeleteFile(id);
            return TypedResults.NoContent();
        });
    }
}
