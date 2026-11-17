using System.Text;
using ShoutingIguana.Sidecar.Crawls;
using ShoutingIguana.Sidecar.Exports;
using ShoutingIguana.Sidecar.Serialization;

namespace ShoutingIguana.Sidecar.Api;

/// <summary>
/// Export (E5): what a grid shows as CSV or Excel, the workbook of everything and the report, each written by the
/// sidecar to a path the UI chose with a save dialog, as a job; and the report's document itself, to show.
/// </summary>
internal static class ExportEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapPost("/api/crawls/{id}/export", async (string id, HttpRequest request, ExportManager exports) =>
            TypedResults.Json(exports.Start(id, await RequestBody.ReadAsync(request, SidecarJsonContext.Default.ExportRequestDto).ConfigureAwait(false)),
                SidecarJsonContext.Default.ExportDto, statusCode: StatusCodes.Status201Created));

        api.MapGet("/api/exports/{exportId}", (string exportId, ExportManager exports) => TypedResults.Json(exports.Get(exportId), SidecarJsonContext.Default.ExportDto));

        api.MapPost("/api/exports/{exportId}/cancel", (string exportId, ExportManager exports) =>
        {
            exports.Cancel(exportId);
            return Results.StatusCode(StatusCodes.Status202Accepted);
        });

        // The document as text: the UI gives it to a sandboxed frame. Like every answer, it carries the API's own headers,
        // so its address is nothing a browser could open as a page.
        api.MapGet("/api/crawls/{id}/report", async (string id, HttpRequest request, GridData data, CancellationToken cancellationToken) =>
            TypedResults.Text(await data.ReportAsync(id, ExportRequests.Report(request.Query, out var compareWith), compareWith, cancellationToken).ConfigureAwait(false),
                "text/html", Encoding.UTF8));
    }
}
