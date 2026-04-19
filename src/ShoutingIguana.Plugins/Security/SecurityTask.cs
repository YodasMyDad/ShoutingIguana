using System;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using ShoutingIguana.PluginSdk;
using ShoutingIguana.PluginSdk.Helpers;

namespace ShoutingIguana.Plugins.Security;

/// <summary>
/// HTTPS security analysis: mixed content, certificate validity, security headers.
/// </summary>
public class SecurityTask(ILogger logger) : UrlTaskBase
{
    // Constants
    private const int MIN_HSTS_MAX_AGE_SECONDS = 31536000; // 1 year in seconds

    public override string Key => "Security";
    public override string DisplayName => "Security & HTTPS";
    public override string Description => "Validates HTTPS implementation, detects mixed content, and checks security headers";
    public override int Priority => 15; // Run early

    public override async Task ExecuteAsync(UrlContext ctx, CancellationToken ct)
    {
        // Only analyze HTML pages
        if (ctx.Metadata.ContentType?.Contains("text/html") != true)
        {
            return;
        }

        if (string.IsNullOrEmpty(ctx.RenderedHtml))
        {
            return;
        }

        // Only analyze successful pages
        if (ctx.Metadata.StatusCode < 200 || ctx.Metadata.StatusCode >= 300)
        {
            return;
        }

        // Only analyze internal URLs
        if (UrlHelper.IsExternal(ctx.Project.BaseUrl, ctx.Url.ToString()))
        {
            return;
        }

        try
        {
            // Check if this is an HTTPS page
            var isHttps = ctx.Url.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);

            if (isHttps)
            {
                // Check for mixed content on HTTPS pages
                await CheckMixedContentAsync(ctx);
                
                // Check security headers
                await CheckSecurityHeadersAsync(ctx);
            }
            else
            {
                // HTTP page - recommend HTTPS
                await RecommendHttpsAsync(ctx);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error analyzing security for {Url}", ctx.Url);
        }
    }

    private async Task CheckMixedContentAsync(UrlContext ctx)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(ctx.RenderedHtml);

        var mixedContentResources = new List<(string Type, string Url)>();

        // Check images
        var imgNodes = doc.DocumentNode.SelectNodes("//img[@src]");
        if (imgNodes != null)
        {
            foreach (var img in imgNodes)
            {
                var src = img.GetAttributeValue("src", "");
                if (IsHttpUrl(src))
                {
                    mixedContentResources.Add(("Image", src));
                }
            }
        }

        // Check scripts
        var scriptNodes = doc.DocumentNode.SelectNodes("//script[@src]");
        if (scriptNodes != null)
        {
            foreach (var script in scriptNodes)
            {
                var src = script.GetAttributeValue("src", "");
                if (IsHttpUrl(src))
                {
                    mixedContentResources.Add(("Script", src));
                }
            }
        }

        // Check stylesheets
        var linkNodes = doc.DocumentNode.SelectNodes("//link[@rel='stylesheet'][@href]");
        if (linkNodes != null)
        {
            foreach (var link in linkNodes)
            {
                var href = link.GetAttributeValue("href", "");
                if (IsHttpUrl(href))
                {
                    mixedContentResources.Add(("Stylesheet", href));
                }
            }
        }

        // Check iframes
        var iframeNodes = doc.DocumentNode.SelectNodes("//iframe[@src]");
        if (iframeNodes != null)
        {
            foreach (var iframe in iframeNodes)
            {
                var src = iframe.GetAttributeValue("src", "");
                if (IsHttpUrl(src))
                {
                    mixedContentResources.Add(("Iframe", src));
                }
            }
        }

        // Check video/audio sources
        var mediaNodes = doc.DocumentNode.SelectNodes("//video[@src] | //audio[@src] | //source[@src]");
        if (mediaNodes != null)
        {
            foreach (var media in mediaNodes)
            {
                var src = media.GetAttributeValue("src", "");
                if (IsHttpUrl(src))
                {
                    mixedContentResources.Add(("Media", src));
                }
            }
        }

        // Check fonts and other resources
        var objectNodes = doc.DocumentNode.SelectNodes("//object[@data]");
        if (objectNodes != null)
        {
            foreach (var obj in objectNodes)
            {
                var data = obj.GetAttributeValue("data", "");
                if (IsHttpUrl(data))
                {
                    mixedContentResources.Add(("Object", data));
                }
            }
        }

        // Report mixed content if found
        if (mixedContentResources.Any())
        {
            var groupedByType = mixedContentResources.GroupBy(r => r.Type).ToList();
            
            var resourceSummary = string.Join(", ", groupedByType.Select(g => $"{g.Count()} {g.Key}(s)"));
            var row = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("Issue", $"Mixed Content ({mixedContentResources.Count} HTTP resources)")
                .Set("Protocol", "HTTPS")
                .Set("Details", resourceSummary)
                .SetExplanation( BuildMixedContentDescription(resourceSummary))
                .SetSeverity(Severity.Error);
            
            await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
        }
    }

    private async Task CheckSecurityHeadersAsync(UrlContext ctx)
    {
        var missingHeaders = new List<string>();
        var weakHeaders = new List<(string Header, string Issue)>();

        // Check HSTS (Strict-Transport-Security)
        if (FirstHeader(ctx, "strict-transport-security") is { } hsts)
        {
            // Validate HSTS header
            var maxAgeMatch = Regex.Match(hsts, @"max-age=(\d+)", RegexOptions.IgnoreCase);
            if (maxAgeMatch.Success)
            {
                var maxAge = int.Parse(maxAgeMatch.Groups[1].Value);
                if (maxAge < MIN_HSTS_MAX_AGE_SECONDS)
                {
                    weakHeaders.Add(("Strict-Transport-Security", $"max-age is {maxAge} seconds (recommend {MIN_HSTS_MAX_AGE_SECONDS}+ for 1 year)"));
                }
            }
            else
            {
                weakHeaders.Add(("Strict-Transport-Security", "Missing max-age directive"));
            }
        }
        else
        {
            missingHeaders.Add("Strict-Transport-Security (HSTS)");
        }

        // Check Content-Security-Policy
        if (!HasHeader(ctx, "content-security-policy") && !HasHeader(ctx, "content-security-policy-report-only"))
        {
            missingHeaders.Add("Content-Security-Policy (CSP)");
        }

        // Check X-Content-Type-Options
        if (FirstHeader(ctx, "x-content-type-options") is not { } xcto)
        {
            missingHeaders.Add("X-Content-Type-Options");
        }
        else if (!xcto.Equals("nosniff", StringComparison.OrdinalIgnoreCase))
        {
            weakHeaders.Add(("X-Content-Type-Options", $"Value is '{xcto}' (should be 'nosniff')"));
        }

        // Check X-Frame-Options
        if (!HasHeader(ctx, "x-frame-options"))
        {
            missingHeaders.Add("X-Frame-Options");
        }

        // Check Referrer-Policy
        if (!HasHeader(ctx, "referrer-policy"))
        {
            missingHeaders.Add("Referrer-Policy");
        }

        // Check Permissions-Policy (replaces the legacy Feature-Policy header).
        // Modern browsers honour Permissions-Policy; a missing value means every
        // feature (geolocation, camera, autoplay, etc.) is implicitly allowed.
        if (!HasHeader(ctx, "permissions-policy") && !HasHeader(ctx, "feature-policy"))
        {
            missingHeaders.Add("Permissions-Policy");
        }

        // Report missing security headers
        if (missingHeaders.Any() || weakHeaders.Any())
        {
            var headersSummary = $"{missingHeaders.Count} missing, {weakHeaders.Count} weak";
            var row = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("Issue", "Missing/Weak Security Headers")
                .Set("Protocol", "HTTPS")
                .Set("Details", headersSummary)
                .SetExplanation( BuildSecurityHeadersDescription(missingHeaders, weakHeaders))
                .SetSeverity(Severity.Warning);
            
            await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
        }

        // Check for secure cookies
        await CheckSecureCookiesAsync(ctx);
    }

    private async Task CheckSecureCookiesAsync(UrlContext ctx)
    {
        if (!ctx.Headers.TryGetValue("set-cookie", out var cookies) || cookies.Count == 0)
        {
            return;
        }

        var insecureCookies = new List<string>();
        var missingHttpOnly = new List<string>();

        foreach (var cookie in cookies)
        {
            if (string.IsNullOrWhiteSpace(cookie))
                continue;

            // Each Set-Cookie value is one cookie. Name is everything up to the first '='.
            var eq = cookie.IndexOf('=');
            if (eq <= 0)
                continue;

            var cookieName = cookie[..eq].Trim();

            // Attributes live after the first ';'. Split so "Secure" inside a cookie value
            // can't produce a false negative.
            var attributeSegment = cookie.IndexOf(';') is var semi and > 0
                ? cookie[semi..]
                : string.Empty;

            if (!HasCookieAttribute(attributeSegment, "Secure"))
            {
                insecureCookies.Add(cookieName);
            }

            if (!HasCookieAttribute(attributeSegment, "HttpOnly"))
            {
                missingHttpOnly.Add(cookieName);
            }
        }

        if (insecureCookies.Count > 0 || missingHttpOnly.Count > 0)
        {
            var cookieDetails = $"{insecureCookies.Count} without Secure, {missingHttpOnly.Count} without HttpOnly";
            var row = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("Issue", "Insecure Cookies")
                .Set("Protocol", "HTTPS")
                .Set("Details", cookieDetails)
                .SetExplanation(BuildCookieDescription(insecureCookies, missingHttpOnly))
                .SetSeverity(Severity.Warning);

            await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
        }
    }

    private static bool HasHeader(UrlContext ctx, string name)
        => ctx.Headers.TryGetValue(name, out var values) && values.Count > 0;

    private static string? FirstHeader(UrlContext ctx, string name)
        => ctx.Headers.TryGetValue(name, out var values) ? values.FirstOrDefault() : null;

    private static bool HasCookieAttribute(string attributeSegment, string attributeName)
    {
        if (string.IsNullOrEmpty(attributeSegment))
            return false;

        foreach (var part in attributeSegment.Split(';'))
        {
            var trimmed = part.Trim();
            if (trimmed.Equals(attributeName, StringComparison.OrdinalIgnoreCase))
                return true;

            // Handle attributes like "SameSite=Strict" — take the left side.
            var eq = trimmed.IndexOf('=');
            if (eq > 0 && trimmed[..eq].Trim().Equals(attributeName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private async Task RecommendHttpsAsync(UrlContext ctx)
    {
        // Only report on important pages (depth <= 2)
        if (ctx.Metadata.Depth <= 2)
        {
            var row = ReportRow.Create()
                .Set("Page", ctx.Url.ToString())
                .Set("Issue", "HTTP Instead of HTTPS")
                .Set("Protocol", "HTTP")
                .Set("Details", $"Depth {ctx.Metadata.Depth} (important page)")
                .SetExplanation( BuildHttpRecommendationDescription(ctx.Metadata.Depth))
                .SetSeverity(Severity.Warning);
            
            await ctx.Reports.ReportAsync(Key, row, ctx.Metadata.UrlId, default);
        }
    }

    private static string BuildMixedContentDescription(string resourceSummary)
    {
        return $"Mixing HTTP resources ({resourceSummary}) into this HTTPS page weakens the TLS connection and may lead browsers to block or warn about those assets; serve them over HTTPS or remove them.";
    }

    private static string BuildSecurityHeadersDescription(
        List<string> missingHeaders,
        List<(string Header, string Issue)> weakHeaders)
    {
        var segments = new List<string>
        {
            "Security headers give browsers guardrails that block clickjacking, MIME sniffing, and other browser-based attacks."
        };

        if (missingHeaders.Any())
        {
            segments.Add($"Missing: {string.Join(", ", missingHeaders)}.");
        }

        if (weakHeaders.Any())
        {
            var weakDetails = string.Join("; ", weakHeaders.Select(h => $"{h.Header} ({h.Issue})"));
            segments.Add($"Weak settings: {weakDetails}.");
        }

        segments.Add("Add or harden the recommended headers to keep the page protected.");
        return string.Join(" ", segments);
    }

    private static string BuildCookieDescription(List<string> insecureCookies, List<string> missingHttpOnly)
    {
        var segments = new List<string>
        {
            "Set-Cookie headers should include Secure and HttpOnly so cookie data stays encrypted and is unavailable to scripts."
        };

        if (insecureCookies.Any())
        {
            segments.Add($"Missing Secure: {string.Join(", ", insecureCookies)}.");
        }

        if (missingHttpOnly.Any())
        {
            segments.Add($"Missing HttpOnly: {string.Join(", ", missingHttpOnly)}.");
        }

        return string.Join(" ", segments);
    }

    private static string BuildHttpRecommendationDescription(int depth)
    {
        return $"Important page (depth {depth}) should be served over HTTPS so visitors stay protected, browsers avoid warnings, and sensitive data stays encrypted.";
    }

    private bool IsHttpUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        // Check for absolute HTTP URLs
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}


