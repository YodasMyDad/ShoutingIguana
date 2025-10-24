using Microsoft.Extensions.Logging;
using ShoutingIguana.PluginSdk;

namespace ShoutingIguana.Plugins.Redirects;

/// <summary>
/// Analyzes redirect chains and identifies potential issues.
/// </summary>
public class RedirectsTask(ILogger logger) : UrlTaskBase
{
    private readonly ILogger _logger = logger;
    private const int MAX_REDIRECT_CHAIN_LENGTH = 3;

    public override string Key => "Redirects";
    public override string DisplayName => "Redirects";
    public override int Priority => 20;

    public override async Task ExecuteAsync(UrlContext ctx, CancellationToken ct)
    {
        // Check if this URL is a redirect
        var statusCode = ctx.Metadata.StatusCode;
        
        if (statusCode >= 300 && statusCode < 400)
        {
            // This is a redirect
            var location = ctx.Headers.ContainsKey("Location") ? ctx.Headers["Location"] : null;
            
            if (!string.IsNullOrEmpty(location))
            {
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Info,
                    $"REDIRECT_{statusCode}",
                    $"Redirects to: {location}",
                    new
                    {
                        fromUrl = ctx.Url.ToString(),
                        toUrl = location,
                        statusCode
                    });

                // Check for protocol canonicalization (http -> https)
                if (ctx.Url.Scheme == "http" && location.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    await ctx.Findings.ReportAsync(
                        Key,
                        Severity.Info,
                        "HTTPS_REDIRECT",
                        "HTTP to HTTPS redirect detected",
                        new
                        {
                            fromUrl = ctx.Url.ToString(),
                            toUrl = location
                        });
                }

                // Check for www canonicalization
                var fromHost = ctx.Url.Host;
                if (Uri.TryCreate(location, UriKind.Absolute, out var toUri))
                {
                    var toHost = toUri.Host;
                    
                    if (fromHost.StartsWith("www.") && !toHost.StartsWith("www."))
                    {
                        await ctx.Findings.ReportAsync(
                            Key,
                            Severity.Info,
                            "WWW_REDIRECT",
                            "WWW to non-WWW redirect detected",
                            new
                            {
                                fromUrl = ctx.Url.ToString(),
                                toUrl = location
                            });
                    }
                    else if (!fromHost.StartsWith("www.") && toHost.StartsWith("www."))
                    {
                        await ctx.Findings.ReportAsync(
                            Key,
                            Severity.Info,
                            "NON_WWW_REDIRECT",
                            "Non-WWW to WWW redirect detected",
                            new
                            {
                                fromUrl = ctx.Url.ToString(),
                                toUrl = location
                            });
                    }
                }
            }
            else
            {
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Error,
                    "MISSING_LOCATION",
                    $"Redirect status {statusCode} but missing Location header",
                    new
                    {
                        url = ctx.Url.ToString(),
                        statusCode
                    });
            }
        }
        // Don't report successful responses - only report actual redirects and issues
        // to avoid database bloat (would create findings for every non-redirect URL)
    }
}

