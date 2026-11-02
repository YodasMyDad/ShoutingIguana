using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Sidecar.Api.Models;
using ShoutingIguana.Sidecar.Crawls;
using ShoutingIguana.Sidecar.Serialization;

namespace ShoutingIguana.Sidecar.Api;

/// <summary>
/// The grids: URLs, their facets and lookup, the inspector, and a check's groups and rows. Every read is a POST with a
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
                cancellationToken).ConfigureAwait(false), SidecarJsonContext.Default.UrlRef));

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

        api.MapPost("/api/crawls/{id}/issues/{checkId}/groups", async (string id, string checkId, HttpRequest request, GridData data, CancellationToken cancellationToken) =>
            TypedResults.Json(await data.GroupsAsync(id, checkId, await RequestBody.ReadOptionalAsync(request, SidecarJsonContext.Default.GroupsRequest).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false), SidecarJsonContext.Default.IssueGroupPageDto));

        api.MapPost("/api/crawls/{id}/issues/{checkId}/rows", async (string id, string checkId, HttpRequest request, GridData data, CancellationToken cancellationToken) =>
            TypedResults.Json(await data.RowsAsync(id, checkId, await RequestBody.ReadOptionalAsync(request, SidecarJsonContext.Default.GridQueryRequest).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false), SidecarJsonContext.Default.UrlPageDto));
    }
}
