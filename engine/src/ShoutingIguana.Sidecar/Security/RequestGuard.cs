using ShoutingIguana.Sidecar.Errors;

namespace ShoutingIguana.Sidecar.Security;

/// <summary>
/// Runs before anything else: the Host must be the loopback address on the bound port (DNS rebinding), an Origin must
/// be allowed, and the token must be there. CORS preflights carry no credentials, so they are answered for allowed
/// origins without one. CORS headers only ever go to allowed origins, and never allow credentials: there are no cookies.
/// </summary>
internal sealed class RequestGuard(RequestDelegate next, SidecarSecurity security)
{
    private const string ALLOWED_METHODS = "GET, POST, PUT, DELETE, OPTIONS";
    private const string ALLOWED_HEADERS = "authorization, content-type, last-event-id";

    /// <summary>Chromium's cap: it caches a preflight for at most two hours.</summary>
    private const string PREFLIGHT_MAX_AGE = "7200";

    private const string REQUEST_PRIVATE_NETWORK = "Access-Control-Request-Private-Network";
    private const string ALLOW_PRIVATE_NETWORK = "Access-Control-Allow-Private-Network";

    public Task InvokeAsync(HttpContext context)
    {
        var isPreflight = HttpMethods.IsOptions(context.Request.Method);
        if (!isPreflight)
        {
            // Every answer is about this moment's state, so nothing may keep one. At the last moment, so a failed
            // request's problem (which clears the response) carries it too. And none is ever a page: a browser must not
            // guess otherwise from its bytes, nor run or load anything if one were opened as a document.
            context.Response.OnStarting(() =>
            {
                context.Response.Headers.CacheControl = "no-store";
                context.Response.Headers.XContentTypeOptions = "nosniff";
                context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
                return Task.CompletedTask;
            });
        }

        if (!SidecarSecurity.IsAllowedHost(context.Request.Host, context.Connection.LocalPort))
        {
            return ProblemWriter.WriteAsync(context, Problems.BadHost());
        }

        var origins = context.Request.Headers.Origin;
        var origin = origins.Count == 1 ? origins[0] : null;
        if (origins.Count > 0)
        {
            if (origin is null || !security.IsAllowedOrigin(origin))
            {
                return ProblemWriter.WriteAsync(context, Problems.OriginNotAllowed());
            }

            context.Response.OnStarting(() =>
            {
                context.Response.Headers.AccessControlAllowOrigin = origin;
                context.Response.Headers.Vary = "Origin";
                return Task.CompletedTask;
            });
        }

        if (isPreflight)
        {
            if (origin is null)
            {
                return ProblemWriter.WriteAsync(context, Problems.OriginNotAllowed());
            }

            context.Response.StatusCode = StatusCodes.Status204NoContent;
            context.Response.Headers.AccessControlAllowMethods = ALLOWED_METHODS;
            context.Response.Headers.AccessControlAllowHeaders = ALLOWED_HEADERS;
            context.Response.Headers.AccessControlMaxAge = PREFLIGHT_MAX_AGE;

            // Chromium's Private Network Access asks before a page may call a loopback address.
            if (string.Equals(context.Request.Headers[REQUEST_PRIVATE_NETWORK], "true", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.Headers[ALLOW_PRIVATE_NETWORK] = "true";
            }

            return Task.CompletedTask;
        }

        return security.IsAuthorized(context.Request.Headers.Authorization)
            ? next(context)
            : ProblemWriter.WriteAsync(context, Problems.Unauthorized());
    }
}
