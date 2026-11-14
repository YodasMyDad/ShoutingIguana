using ShoutingIguana.Engine.Fetching;
using ShoutingIguana.Sidecar.Errors;
using ShoutingIguana.Sidecar.Serialization;

namespace ShoutingIguana.Sidecar.Api;

/// <summary>The proxy test: one fetch through the proxy the user typed, and what happened, in plain words.</summary>
internal static class ProxyEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        // 200 whatever the fetch found (a refused proxy is an answer, not a failed call). 400 for a proxy or an address
        // that can't be fetched through at all.
        api.MapPost("/api/proxy/test", async (HttpRequest request) =>
        {
            var body = await RequestBody.ReadAsync(request, SidecarJsonContext.Default.ProxyTestRequest).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(body.ProxyUrl) || !ProxyTester.IsValidProxy(body.ProxyUrl.Trim()))
            {
                throw Problems.BadRequest("The proxy must be a whole address, such as http://proxy.example.com:8080.");
            }

            if (string.IsNullOrWhiteSpace(body.Url) ||
                !Uri.TryCreate(body.Url.Trim(), UriKind.Absolute, out var url) ||
                url.Scheme is not ("http" or "https") ||
                url.UserInfo.Length > 0)
            {
                throw Problems.BadRequest("The address to test with must be a whole web address, such as https://example.com/.");
            }

            var result = await ProxyTester.TestAsync(body.ProxyUrl.Trim(), url, request.HttpContext.RequestAborted).ConfigureAwait(false);
            return TypedResults.Json(result, SidecarJsonContext.Default.ProxyTestResult);
        });
    }
}
