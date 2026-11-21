using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Sidecar.Api.Models;
using ShoutingIguana.Sidecar.Crawls;
using ShoutingIguana.Sidecar.Serialization;

namespace ShoutingIguana.Sidecar.Api;

/// <summary>
/// The grids: URLs, their facets and lookup, the inspector, a check's groups and rows, the links, the sitemaps and robots.txt. Every read but the list of sitemaps is a POST with a
/// JSON body to one fixed URL per crawl (the URL inspected goes in the body, not the path), so paging, filtering and
/// moving from URL to URL never cost a CORS preflight. A request given up stops its read (<see cref="GridData"/>).
/// </summary>
internal static class GridEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet("/api/url-columns", () => TypedResults.Json(GridData.Columns, SidecarJsonContext.Default.IReadOnlyListUrlColumnDto));

        api.MapPost("/api/crawls/{id}/extractions", async (string id, HttpRequest request, CrawlData data) =>
            TypedResults.Json(data.Extractions(id, await RequestBody.ReadOptionalAsync(request, SidecarJsonContext.Default.ExtractionRequest).ConfigureAwait(false)),
                SidecarJsonContext.Default.ExtractionPageDto));

        api.MapPost("/api/crawls/{id}/urls", async (string id, HttpRequest request, GridData data, CancellationToken cancellationToken) =>
            TypedResults.Json(await data.QueryAsync(id, await RequestBody.ReadOptionalAsync(request, SidecarJsonContext.Default.GridQueryRequest).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false), SidecarJsonContext.Default.UrlPageDto));

        api.MapPost("/api/crawls/{id}/urls/facets", async (string id, HttpRequest request, GridData data, CancellationToken cancellationToken) =>
            TypedResults.Json(await data.FacetsAsync(id, await RequestBody.ReadOptionalAsync(request, SidecarJsonContext.Default.FacetsRequest).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false), SidecarJsonContext.Default.FacetsDto));

        api.MapPost("/api/crawls/{id}/urls/lookup", async (string id, HttpRequest request, GridData data, CancellationToken cancellationToken) =>
            TypedResults.Json(await data.LookupAsync(id, await RequestBody.ReadAsync(request, SidecarJsonContext.Default.UrlLookupRequest).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false), SidecarJsonContext.Default.UrlLookup));

        api.MapPost("/api/crawls/{id}/search", async (string id, HttpRequest request, GridData data, CancellationToken cancellationToken) =>
            TypedResults.Json(await data.SearchAsync(id, await RequestBody.ReadAsync(request, SidecarJsonContext.Default.SearchRequest).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false), SidecarJsonContext.Default.UrlSearchResults));

        api.MapPost("/api/crawls/{id}/urls/inspect", async (string id, HttpRequest request, GridData data, CancellationToken cancellationToken) =>
            TypedResults.Json(await data.UrlAsync(id, await RequestBody.ReadAsync(request, SidecarJsonContext.Default.UrlRequest).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false), SidecarJsonContext.Default.UrlDetailDto));

        api.MapPost("/api/crawls/{id}/urls/inlinks", async (string id, HttpRequest request, GridData data, CancellationToken cancellationToken) =>
            TypedResults.Json(await data.LinksAsync(id, LinkDirection.Inlinks, await RequestBody.ReadAsync(request, SidecarJsonContext.Default.LinksRequest).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false), SidecarJsonContext.Default.LinkPageDto));

        api.MapPost("/api/crawls/{id}/urls/outlinks", async (string id, HttpRequest request, GridData data, CancellationToken cancellationToken) =>
            TypedResults.Json(await data.LinksAsync(id, LinkDirection.Outlinks, await RequestBody.ReadAsync(request, SidecarJsonContext.Default.LinksRequest).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false), SidecarJsonContext.Default.LinkPageDto));

        api.MapPost("/api/crawls/{id}/urls/images", async (string id, HttpRequest request, GridData data, CancellationToken cancellationToken) =>
            TypedResults.Json(await data.ImagesAsync(id, await RequestBody.ReadAsync(request, SidecarJsonContext.Default.UrlImagesRequest).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false), SidecarJsonContext.Default.PageImages));

        api.MapPost("/api/crawls/{id}/urls/found-on", async (string id, HttpRequest request, GridData data, CancellationToken cancellationToken) =>
            TypedResults.Json(await data.FoundOnAsync(id, await RequestBody.ReadAsync(request, SidecarJsonContext.Default.FoundOnRequest).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false), SidecarJsonContext.Default.FoundOnDto));

        api.MapPost("/api/crawls/{id}/urls/source", async (string id, HttpRequest request, GridData data, CancellationToken cancellationToken) =>
            await data.SourceAsync(id, await RequestBody.ReadAsync(request, SidecarJsonContext.Default.UrlRequest).ConfigureAwait(false), cancellationToken)
                .ConfigureAwait(false));

        // The site's icon: an image and nothing else. Its media type is the one the engine read from its bytes, and every
        // answer already forbids a browser to guess another, or to run or load anything from it (RequestGuard).
        api.MapGet("/api/crawls/{id}/favicon", async (string id, GridData data, CancellationToken cancellationToken) =>
            await data.FaviconAsync(id, cancellationToken).ConfigureAwait(false) is { } favicon
                ? Results.Bytes(favicon.Bytes, favicon.ContentType)
                : Results.NoContent());

        api.MapGet("/api/link-columns", () => TypedResults.Json(GridData.LinkColumns, SidecarJsonContext.Default.IReadOnlyListLinkColumnDto));

        api.MapPost("/api/crawls/{id}/links", async (string id, HttpRequest request, GridData data, CancellationToken cancellationToken) =>
            TypedResults.Json(await data.LinkGridAsync(id, await RequestBody.ReadOptionalAsync(request, SidecarJsonContext.Default.GridQueryRequest).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false), SidecarJsonContext.Default.LinkGridPageDto));

        api.MapPost("/api/crawls/{id}/links/facets", async (string id, HttpRequest request, GridData data, CancellationToken cancellationToken) =>
            TypedResults.Json(await data.LinkFacetsAsync(id, await RequestBody.ReadOptionalAsync(request, SidecarJsonContext.Default.FacetsRequest).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false), SidecarJsonContext.Default.FacetsDto));

        api.MapPost("/api/crawls/{id}/links/ends", async (string id, HttpRequest request, GridData data, CancellationToken cancellationToken) =>
            TypedResults.Json(await data.LinkEndsAsync(id, await RequestBody.ReadOptionalAsync(request, SidecarJsonContext.Default.LinkEndsRequest).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false), SidecarJsonContext.Default.LinkEndPage));

        api.MapPost("/api/crawls/{id}/links/groups", async (string id, HttpRequest request, GridData data, CancellationToken cancellationToken) =>
            TypedResults.Json(await data.LinkGroupsAsync(id, await RequestBody.ReadOptionalAsync(request, SidecarJsonContext.Default.LinkGroupsRequest).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false), SidecarJsonContext.Default.LinkGroupPageDto));

        api.MapGet("/api/crawls/{id}/sitemaps", async (string id, GridData data, CancellationToken cancellationToken) =>
            TypedResults.Json(await data.SitemapsAsync(id, cancellationToken).ConfigureAwait(false), SidecarJsonContext.Default.SitemapList));

        api.MapPost("/api/crawls/{id}/sitemaps/urls", async (string id, HttpRequest request, GridData data, CancellationToken cancellationToken) =>
            TypedResults.Json(await data.SitemapUrlsAsync(id, await RequestBody.ReadAsync(request, SidecarJsonContext.Default.GridQueryRequest).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false), SidecarJsonContext.Default.UrlPageDto));

        api.MapPost("/api/crawls/{id}/robots", async (string id, HttpRequest request, GridData data, CancellationToken cancellationToken) =>
            TypedResults.Json(await data.RobotsAsync(id, await RequestBody.ReadOptionalAsync(request, SidecarJsonContext.Default.RobotsOriginsRequest).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false), SidecarJsonContext.Default.RobotsOriginPageDto));

        api.MapPost("/api/crawls/{id}/robots/file", async (string id, HttpRequest request, GridData data, CancellationToken cancellationToken) =>
            TypedResults.Json(await data.RobotsFileAsync(id, await RequestBody.ReadAsync(request, SidecarJsonContext.Default.RobotsFileRequest).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false), SidecarJsonContext.Default.RobotsFileDto));

        api.MapPost("/api/crawls/{id}/robots/blocked", async (string id, HttpRequest request, GridData data, CancellationToken cancellationToken) =>
            TypedResults.Json(await data.RobotsBlockedAsync(id, await RequestBody.ReadAsync(request, SidecarJsonContext.Default.RobotsBlockedRequest).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false), SidecarJsonContext.Default.RobotsBlockedPage));

        api.MapPost("/api/crawls/{id}/issues/{checkId}/groups", async (string id, string checkId, HttpRequest request, GridData data, CancellationToken cancellationToken) =>
            TypedResults.Json(await data.GroupsAsync(id, checkId, await RequestBody.ReadOptionalAsync(request, SidecarJsonContext.Default.GroupsRequest).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false), SidecarJsonContext.Default.IssueGroupPageDto));

        api.MapPost("/api/crawls/{id}/issues/{checkId}/rows", async (string id, string checkId, HttpRequest request, GridData data, CancellationToken cancellationToken) =>
            TypedResults.Json(await data.RowsAsync(id, checkId, await RequestBody.ReadOptionalAsync(request, SidecarJsonContext.Default.GridQueryRequest).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false), SidecarJsonContext.Default.UrlPageDto));

        api.MapPost("/api/crawls/{id}/issues/{checkId}/fixed", async (string id, string checkId, HttpRequest request, GridData data, CancellationToken cancellationToken) =>
            TypedResults.Json(await data.FixedAsync(id, checkId, await RequestBody.ReadOptionalAsync(request, SidecarJsonContext.Default.FixedRequest).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false), SidecarJsonContext.Default.FixedPageDto));

        // The fix loop: the comparison with the crawl's previous crawl, or the one `with` names.
        api.MapGet("/api/crawls/{id}/changes", async (string id, HttpRequest request, GridData data, CancellationToken cancellationToken) =>
            await data.ChangesAsync(id, With(request.Query), cancellationToken).ConfigureAwait(false) switch
            {
                ChangesDto changes => Results.Json(changes, SidecarJsonContext.Default.ChangesDto),
                var none => Results.Json((NoComparisonDto)none, SidecarJsonContext.Default.NoComparisonDto)
            });

        api.MapPost("/api/crawls/{id}/issues/{checkId}/targets", async (string id, string checkId, HttpRequest request, GridData data, CancellationToken cancellationToken) =>
            TypedResults.Json(await data.TargetsAsync(id, checkId, await RequestBody.ReadAsync(request, SidecarJsonContext.Default.FoundOnRequest).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false), SidecarJsonContext.Default.IssueTargetsDto));
    }

    /// <summary>The crawl <c>/changes</c> compares with: <c>previous</c> when left out, or once given, a crawl's id.</summary>
    private static string? With(IQueryCollection query) => query["with"] switch
    {
        { Count: 0 } => null,
        { Count: 1 } with when !string.IsNullOrEmpty(with[0]) => with[0],
        _ => throw Errors.Problems.BadRequest("with must be previous, or one crawl's id.")
    };
}
