using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using ShoutingIguana.PluginSdk;
using System.Text.RegularExpressions;

namespace ShoutingIguana.Plugins.Redirects;

/// <summary>
/// Analyzes redirect chains, loops, and canonicalization issues.
/// </summary>
public class RedirectsTask(ILogger logger) : UrlTaskBase
{
    private readonly ILogger _logger = logger;
    private const int MAX_REDIRECT_CHAIN_LENGTH = 3;
    private const int WARNING_REDIRECT_CHAIN_LENGTH = 2;

    public override string Key => "Redirects";
    public override string DisplayName => "Redirects";
    public override int Priority => 20;

    public override async Task ExecuteAsync(UrlContext ctx, CancellationToken ct)
    {
        try
        {
            var statusCode = ctx.Metadata.StatusCode;
            
            // Check if this URL is a redirect
            if (statusCode >= 300 && statusCode < 400)
            {
                await AnalyzeHttpRedirectAsync(ctx);
            }
            
            // Check for meta refresh redirects (even on 200 OK pages)
            if (statusCode == 200 && !string.IsNullOrEmpty(ctx.RenderedHtml))
            {
                await CheckMetaRefreshAsync(ctx);
            }
            
            // Check for JavaScript redirects (on any successful page)
            if (statusCode == 200 && ctx.Page != null && !string.IsNullOrEmpty(ctx.RenderedHtml))
            {
                await CheckJavaScriptRedirectAsync(ctx);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing redirects for {Url}", ctx.Url);
        }
    }

    private async Task AnalyzeHttpRedirectAsync(UrlContext ctx)
    {
        var statusCode = ctx.Metadata.StatusCode;
        var location = ctx.Headers.ContainsKey("Location") ? ctx.Headers["Location"] : null;
        
        if (string.IsNullOrEmpty(location))
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
            return;
        }

        // Report the redirect
        await ctx.Findings.ReportAsync(
            Key,
            Severity.Info,
            $"REDIRECT_{statusCode}",
            $"Redirects to: {location}",
            new
            {
                fromUrl = ctx.Url.ToString(),
                toUrl = location,
                statusCode,
                redirectType = GetRedirectType(statusCode)
            });

        // Check for temporary vs permanent redirects
        if (statusCode == 302 || statusCode == 307)
        {
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "TEMPORARY_REDIRECT",
                $"Using {statusCode} (temporary) redirect - consider 301 for permanent moves",
                new
                {
                    url = ctx.Url.ToString(),
                    toUrl = location,
                    statusCode,
                    recommendation = "Use 301 (permanent) redirects for SEO benefits"
                });
        }

        // Check for mixed content (HTTPS -> HTTP)
        if (ctx.Url.Scheme == "https" && location.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Error,
                "MIXED_CONTENT_REDIRECT",
                "HTTPS page redirects to HTTP (security issue)",
                new
                {
                    fromUrl = ctx.Url.ToString(),
                    toUrl = location,
                    warning = "This breaks HTTPS and causes security warnings"
                });
        }

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
                    toUrl = location,
                    note = "Good practice for security"
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

            // Check for trailing slash inconsistencies
            var fromPath = ctx.Url.AbsolutePath;
            var toPath = toUri.AbsolutePath;
            
            if (fromPath.TrimEnd('/') == toPath.TrimEnd('/') && 
                fromPath.EndsWith("/") != toPath.EndsWith("/"))
            {
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Info,
                    "TRAILING_SLASH_REDIRECT",
                    "Redirect due to trailing slash inconsistency",
                    new
                    {
                        fromUrl = ctx.Url.ToString(),
                        toUrl = location,
                        note = "Ensure consistent trailing slash usage across the site"
                    });
            }
        }
    }

    private async Task CheckMetaRefreshAsync(UrlContext ctx)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(ctx.RenderedHtml!);

        var metaRefreshNode = doc.DocumentNode.SelectSingleNode("//meta[@http-equiv='refresh']");
        if (metaRefreshNode != null)
        {
            var content = metaRefreshNode.GetAttributeValue("content", "");
            if (!string.IsNullOrEmpty(content))
            {
                // Parse meta refresh: "0;url=http://example.com"
                var match = Regex.Match(content, @"(\d+)\s*;\s*url=(.+)", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var delay = int.Parse(match.Groups[1].Value);
                    var targetUrl = match.Groups[2].Value.Trim();

                    await ctx.Findings.ReportAsync(
                        Key,
                        Severity.Warning,
                        "META_REFRESH_REDIRECT",
                        $"Page uses meta refresh redirect (not SEO-friendly): {targetUrl}",
                        new
                        {
                            url = ctx.Url.ToString(),
                            targetUrl,
                            delay,
                            recommendation = "Use HTTP 301/302 redirects instead of meta refresh"
                        });
                }
            }
        }
    }

    private async Task CheckJavaScriptRedirectAsync(UrlContext ctx)
    {
        try
        {
            // Check for common JavaScript redirect patterns
            var hasWindowLocationRedirect = ctx.RenderedHtml!.Contains("window.location") &&
                                           (ctx.RenderedHtml.Contains("window.location.href") ||
                                            ctx.RenderedHtml.Contains("window.location.replace") ||
                                            ctx.RenderedHtml.Contains("window.location.assign"));

            var hasLocationRedirect = ctx.RenderedHtml.Contains("location.href") ||
                                     ctx.RenderedHtml.Contains("location.replace") ||
                                     ctx.RenderedHtml.Contains("location.assign");

            if (hasWindowLocationRedirect || hasLocationRedirect)
            {
                // Try to extract the target URL from JavaScript
                var scriptMatches = Regex.Matches(ctx.RenderedHtml, 
                    @"(?:window\.)?location\.(?:href|replace|assign)\s*=?\s*[""']([^""']+)[""']",
                    RegexOptions.IgnoreCase);

                if (scriptMatches.Count > 0)
                {
                    var targetUrl = scriptMatches[0].Groups[1].Value;
                    
                    await ctx.Findings.ReportAsync(
                        Key,
                        Severity.Warning,
                        "JAVASCRIPT_REDIRECT",
                        $"Page uses JavaScript redirect (not ideal for SEO): {targetUrl}",
                        new
                        {
                            url = ctx.Url.ToString(),
                            targetUrl,
                            recommendation = "Use HTTP 301/302 redirects for better SEO"
                        });
                }
                else
                {
                    await ctx.Findings.ReportAsync(
                        Key,
                        Severity.Info,
                        "JAVASCRIPT_REDIRECT_DETECTED",
                        "Page appears to use JavaScript redirect",
                        new
                        {
                            url = ctx.Url.ToString(),
                            note = "JavaScript redirects are not ideal for SEO"
                        });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error checking for JavaScript redirects on {Url}", ctx.Url);
        }
    }

    private string GetRedirectType(int statusCode)
    {
        return statusCode switch
        {
            301 => "Permanent",
            302 => "Temporary (Found)",
            303 => "See Other",
            307 => "Temporary (Preserve Method)",
            308 => "Permanent (Preserve Method)",
            _ => "Unknown"
        };
    }
}

