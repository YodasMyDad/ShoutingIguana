using ShoutingIguana.Sidecar.Crawls;
using ShoutingIguana.Sidecar.Serialization;

namespace ShoutingIguana.Sidecar.Api;

/// <summary>Analysing a crawl, and reading what it found: the analysis, the stats and each check's flagged URLs.</summary>
internal static class AnalysisEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapPost("/api/crawls/{id}/analyze", (string id, CrawlManager crawls) =>
        {
            crawls.Analyze(id);
            return Results.StatusCode(StatusCodes.Status202Accepted);
        });

        api.MapPost("/api/crawls/{id}/analyze/cancel", (string id, CrawlManager crawls) =>
        {
            crawls.CancelAnalysis(id);
            return Results.StatusCode(StatusCodes.Status202Accepted);
        });

        api.MapGet("/api/crawls/{id}/analysis", (string id, CrawlData data) => TypedResults.Json(data.Analysis(id), SidecarJsonContext.Default.AnalysisDto));

        api.MapGet("/api/crawls/{id}/stats", async (string id, GridData data, CancellationToken cancellationToken) =>
            TypedResults.Json(await data.StatsAsync(id, cancellationToken).ConfigureAwait(false), SidecarJsonContext.Default.CrawlStatsDto));

        // JSON null for any other site, said as such: an empty body would read as no answer.
        api.MapGet("/api/crawls/{id}/shopify", async (string id, GridData data, CancellationToken cancellationToken) =>
            await data.ShopifyAsync(id, cancellationToken).ConfigureAwait(false) is { } access
                ? Results.Json(access, SidecarJsonContext.Default.ShopifyAccessDto)
                : Results.Content("null", "application/json"));

        api.MapGet("/api/crawls/{id}/issues/{checkId}", (string id, string checkId, HttpRequest request, CrawlData data) =>
            TypedResults.Json(data.Issues(id, checkId, request.Query), SidecarJsonContext.Default.IssuesPageDto));
    }
}
