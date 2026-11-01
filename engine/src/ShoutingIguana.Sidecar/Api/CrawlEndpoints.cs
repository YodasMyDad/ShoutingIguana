using ShoutingIguana.Sidecar.Api.Models;
using ShoutingIguana.Sidecar.Crawls;
using ShoutingIguana.Sidecar.Errors;
using ShoutingIguana.Sidecar.Events;
using ShoutingIguana.Sidecar.Models;
using ShoutingIguana.Sidecar.Serialization;

namespace ShoutingIguana.Sidecar.Api;

/// <summary>Starting, pausing, stopping and continuing crawls, and the live events they send.</summary>
internal static class CrawlEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapPost("/api/crawls", async (HttpRequest request, CrawlManager crawls) =>
        {
            var body = await RequestBody.ReadOptionsAsync(request, SidecarJsonContext.Default.StartCrawlRequest).ConfigureAwait(false);
            var options = body.Options ?? throw Problems.BadRequest("Give the new crawl's options.");
            var item = await crawls.StartAsync(options, body.AnalyzeWhenDone ?? true).ConfigureAwait(false);
            return TypedResults.Json(item, SidecarJsonContext.Default.CrawlListItem, statusCode: StatusCodes.Status201Created);
        });

        api.MapPost("/api/crawls/{id}/pause", (string id, CrawlManager crawls) =>
        {
            crawls.Pause(id);
            return TypedResults.NoContent();
        });

        api.MapPost("/api/crawls/{id}/resume", (string id, CrawlManager crawls) =>
        {
            crawls.Resume(id);
            return TypedResults.NoContent();
        });

        api.MapPost("/api/crawls/{id}/stop", (string id, CrawlManager crawls) =>
        {
            crawls.Stop(id);
            return Results.StatusCode(StatusCodes.Status202Accepted);
        });

        api.MapGet("/api/crawls/{id}/options", (string id, CrawlData data) =>
            TypedResults.Json(data.StoredOptions(id), SidecarJsonContext.Default.CrawlOptionsDto));

        api.MapPost("/api/crawls/{id}/continue", async (string id, HttpRequest request, CrawlManager crawls) =>
        {
            var body = await RequestBody.ReadOptionalAsync(request, SidecarJsonContext.Default.ContinueCrawlRequest, optionFieldErrors: true)
                .ConfigureAwait(false);
            var item = await crawls.ContinueAsync(id, body ?? new ContinueCrawlRequest()).ConfigureAwait(false);
            return TypedResults.Json(item, SidecarJsonContext.Default.CrawlListItem);
        });

        api.MapGet("/api/events", (HttpContext context, EventHub hub, SidecarOptions options, IHostApplicationLifetime lifetime) =>
            EventStream.WriteAsync(context, hub, options.KeepAliveInterval, lifetime.ApplicationStopping));

        api.MapGet("/api/crawls/{id}/alerts", (string id, CrawlManager crawls) =>
            TypedResults.Json(crawls.Alerts(id), SidecarJsonContext.Default.IReadOnlyListAlertDto));
    }
}
