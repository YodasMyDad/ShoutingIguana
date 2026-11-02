using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Sidecar.Api.Models;
using ShoutingIguana.Sidecar.Crawls;
using ShoutingIguana.Sidecar.Serialization;

namespace ShoutingIguana.Sidecar.Api;

/// <summary>
/// The grids: URLs, their facets and lookup, the inspector, and a check's groups and rows. Grid queries are POSTs with a
/// JSON body to one fixed URL per grid, so paging and filtering never cost a CORS preflight.
/// </summary>
internal static class GridEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet("/api/url-columns", () => TypedResults.Json(GridData.Columns, SidecarJsonContext.Default.IReadOnlyListUrlColumnDto));

        api.MapPost("/api/crawls/{id}/urls", async (string id, HttpRequest request, GridData data) =>
            TypedResults.Json(data.Query(id, await RequestBody.ReadOptionalAsync(request, SidecarJsonContext.Default.GridQueryRequest).ConfigureAwait(false)),
                SidecarJsonContext.Default.UrlPageDto));

        api.MapPost("/api/crawls/{id}/urls/facets", async (string id, HttpRequest request, GridData data) =>
            TypedResults.Json(data.Facets(id, await RequestBody.ReadOptionalAsync(request, SidecarJsonContext.Default.FacetsRequest).ConfigureAwait(false)),
                SidecarJsonContext.Default.FacetsDto));

        api.MapPost("/api/crawls/{id}/urls/lookup", async (string id, HttpRequest request, GridData data) =>
            TypedResults.Json(data.Lookup(id, await RequestBody.ReadAsync(request, SidecarJsonContext.Default.UrlLookupRequest).ConfigureAwait(false)),
                SidecarJsonContext.Default.UrlRef));

        api.MapGet("/api/crawls/{id}/urls/{urlId:int}", (string id, int urlId, GridData data) =>
            TypedResults.Json(data.Url(id, urlId), SidecarJsonContext.Default.UrlDetailDto));

        api.MapPost("/api/crawls/{id}/urls/{urlId:int}/inlinks", async (string id, int urlId, HttpRequest request, GridData data) =>
            TypedResults.Json(data.Links(id, urlId, LinkDirection.Inlinks,
                await RequestBody.ReadOptionalAsync(request, SidecarJsonContext.Default.LinksRequest).ConfigureAwait(false)), SidecarJsonContext.Default.LinkPageDto));

        api.MapPost("/api/crawls/{id}/urls/{urlId:int}/outlinks", async (string id, int urlId, HttpRequest request, GridData data) =>
            TypedResults.Json(data.Links(id, urlId, LinkDirection.Outlinks,
                await RequestBody.ReadOptionalAsync(request, SidecarJsonContext.Default.LinksRequest).ConfigureAwait(false)), SidecarJsonContext.Default.LinkPageDto));

        api.MapGet("/api/crawls/{id}/urls/{urlId:int}/source", (string id, int urlId, GridData data) =>
            TypedResults.Json(data.Source(id, urlId), SidecarJsonContext.Default.HtmlSourceDto));

        api.MapPost("/api/crawls/{id}/issues/{checkId}/groups", async (string id, string checkId, HttpRequest request, GridData data) =>
            TypedResults.Json(data.Groups(id, checkId, await RequestBody.ReadOptionalAsync(request, SidecarJsonContext.Default.GroupsRequest).ConfigureAwait(false)),
                SidecarJsonContext.Default.IssueGroupPageDto));

        api.MapPost("/api/crawls/{id}/issues/{checkId}/rows", async (string id, string checkId, HttpRequest request, GridData data) =>
            TypedResults.Json(data.Rows(id, checkId, await RequestBody.ReadOptionalAsync(request, SidecarJsonContext.Default.GridQueryRequest).ConfigureAwait(false)),
                SidecarJsonContext.Default.UrlPageDto));
    }
}
