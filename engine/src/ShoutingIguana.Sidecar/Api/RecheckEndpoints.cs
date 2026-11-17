using ShoutingIguana.Sidecar.Crawls;
using ShoutingIguana.Sidecar.Serialization;

namespace ShoutingIguana.Sidecar.Api;

/// <summary>
/// Rechecks (the fix loop's "Recheck these N pages"): what one would do, starting one, and the crawl's rechecks with their
/// URLs, as its file keeps them.
/// </summary>
internal static class RecheckEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapPost("/api/crawls/{id}/rechecks/plan", async (string id, HttpRequest request, CrawlManager crawls) =>
            TypedResults.Json(crawls.PlanRecheck(id, await RequestBody.ReadAsync(request, SidecarJsonContext.Default.RecheckAskRequest).ConfigureAwait(false)),
                SidecarJsonContext.Default.RecheckPlanDto));

        api.MapPost("/api/crawls/{id}/rechecks", async (string id, HttpRequest request, CrawlManager crawls) =>
            TypedResults.Json(crawls.Recheck(id, await RequestBody.ReadAsync(request, SidecarJsonContext.Default.StartRecheckRequest).ConfigureAwait(false)),
                SidecarJsonContext.Default.RecheckPlanDto, statusCode: StatusCodes.Status202Accepted));

        api.MapGet("/api/crawls/{id}/rechecks", (string id, CrawlData data) => TypedResults.Json(data.Rechecks(id), SidecarJsonContext.Default.RechecksDto));

        api.MapGet("/api/crawls/{id}/rechecks/{number}", (string id, string number, CrawlData data) =>
            TypedResults.Json(data.Recheck(id, number), SidecarJsonContext.Default.RecheckDto));

        api.MapPost("/api/crawls/{id}/rechecks/{number}/rows", async (string id, string number, HttpRequest request, CrawlData data) =>
            TypedResults.Json(data.RecheckRows(id, number, await RequestBody.ReadOptionalAsync(request, SidecarJsonContext.Default.RecheckRowsRequest).ConfigureAwait(false)),
                SidecarJsonContext.Default.RecheckRowPage));
    }
}
