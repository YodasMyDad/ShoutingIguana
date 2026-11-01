using ShoutingIguana.Sidecar.Errors.Models;
using ShoutingIguana.Sidecar.Serialization;

namespace ShoutingIguana.Sidecar.Errors;

internal static class ProblemWriter
{
    public const string CONTENT_TYPE = "application/problem+json";

    public static Task WriteAsync(HttpContext context, ProblemDto problem)
    {
        context.Response.StatusCode = problem.Status;
        if (problem.Status == StatusCodes.Status401Unauthorized)
        {
            context.Response.Headers.WWWAuthenticate = "Bearer";
        }

        return context.Response.WriteAsJsonAsync(problem, SidecarJsonContext.Default.ProblemDto, CONTENT_TYPE, context.RequestAborted);
    }
}
