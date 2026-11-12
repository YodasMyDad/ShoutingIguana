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
    }
}
