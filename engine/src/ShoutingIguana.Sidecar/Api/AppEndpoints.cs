using ShoutingIguana.Engine;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Sidecar.Api.Models;
using ShoutingIguana.Sidecar.Errors;
using ShoutingIguana.Sidecar.Models;
using ShoutingIguana.Sidecar.Serialization;

namespace ShoutingIguana.Sidecar.Api;

/// <summary>The sidecar itself, and what every check means.</summary>
internal static class AppEndpoints
{
    private static readonly ChecksDto CHECKS = new(
        [.. Enum.GetValues<IssueCategory>().Select(category => new CategoryDto(category, category.DisplayName()))],
        [.. IssueCatalog.All.Select(check => new CheckDto(check.Id, check.Category, check.Severity, check.Title, check.Description, check.Fix))]);

    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet("/api/health", (SidecarOptions options) => TypedResults.Json(
            new HealthDto(SidecarInfo.Version, EngineInfo.Version, options.DataDirectory, options.StateDirectory, Environment.ProcessId),
            SidecarJsonContext.Default.HealthDto));

        api.MapPost("/api/shutdown", (HttpContext context, IHostApplicationLifetime lifetime) =>
        {
            // Once the answer is sent: stopping waits for the requests in flight, and this is one.
            context.Response.OnCompleted(() =>
            {
                lifetime.StopApplication();
                return Task.CompletedTask;
            });
            return Results.StatusCode(StatusCodes.Status202Accepted);
        });

        api.MapGet("/api/checks", () => TypedResults.Json(CHECKS, SidecarJsonContext.Default.ChecksDto));

        // Every path, those that look like files included (/api/foo.json): the default pattern leaves those out.
        api.MapFallback("{*path}", (HttpContext context) => ProblemWriter.WriteAsync(context, Problems.NotFound()));
    }
}
