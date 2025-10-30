using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using ShoutingIguana.PluginSdk;
using ShoutingIguana.PluginSdk.Helpers;
using System.Text.RegularExpressions;

namespace ShoutingIguana.Plugins.Security;

/// <summary>
/// HTTPS security analysis: mixed content, certificate validity, security headers.
/// </summary>
public class SecurityTask(ILogger logger) : UrlTaskBase
{
    private readonly ILogger _logger = logger;

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
            _logger.LogError(ex, "Error analyzing security for {Url}", ctx.Url);
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
            
            var builder = FindingDetailsBuilder.Create()
                .AddItem($"Found {mixedContentResources.Count} HTTP resources on HTTPS page")
                .AddItem("❌ CRITICAL SECURITY ISSUE");
            
            foreach (var group in groupedByType)
            {
                builder.BeginNested($"🔓 {group.Key}s loading over HTTP ({group.Count()})");
                foreach (var resource in group.Take(5))
                {
                    builder.AddItem(resource.Url);
                }
                if (group.Count() > 5)
                {
                    builder.AddItem($"... and {group.Count() - 5} more");
                }
            }
            
            builder.BeginNested("⚠️ Impact")
                .AddItem("Browser shows 'Not Secure' warning")
                .AddItem("Breaks HTTPS encryption - user data at risk")
                .AddItem("Google ranking penalty for mixed content")
                .AddItem("Users may see security warnings and leave");
            
            builder.BeginNested("💡 Recommendations")
                .AddItem("Change all HTTP:// URLs to HTTPS://")
                .AddItem("Use protocol-relative URLs (//example.com) if needed")
                .AddItem("Or use relative URLs (/images/photo.jpg)")
                .AddItem("Test all resources load correctly over HTTPS");
            
            builder.WithTechnicalMetadata("url", ctx.Url.ToString())
                .WithTechnicalMetadata("mixedContentCount", mixedContentResources.Count)
                .WithTechnicalMetadata("resourceTypes", groupedByType.Select(g => new { type = g.Key, count = g.Count() }).ToArray())
                .WithTechnicalMetadata("examples", mixedContentResources.Take(10).Select(r => new { type = r.Type, url = r.Url }).ToArray());
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Error,
                "MIXED_CONTENT",
                $"HTTPS page loads {mixedContentResources.Count} HTTP resources (mixed content security issue)",
                builder.Build());
        }
    }

    private async Task CheckSecurityHeadersAsync(UrlContext ctx)
    {
        var missingHeaders = new List<string>();
        var weakHeaders = new List<(string Header, string Issue)>();

        // Check HSTS (Strict-Transport-Security)
        if (ctx.Headers.TryGetValue("strict-transport-security", out var hsts))
        {
            // Validate HSTS header
            var maxAgeMatch = Regex.Match(hsts, @"max-age=(\d+)", RegexOptions.IgnoreCase);
            if (maxAgeMatch.Success)
            {
                var maxAge = int.Parse(maxAgeMatch.Groups[1].Value);
                if (maxAge < 31536000) // Less than 1 year
                {
                    weakHeaders.Add(("Strict-Transport-Security", $"max-age is {maxAge} seconds (recommend 31536000+ for 1 year)"));
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
        if (!ctx.Headers.ContainsKey("content-security-policy") && !ctx.Headers.ContainsKey("content-security-policy-report-only"))
        {
            missingHeaders.Add("Content-Security-Policy (CSP)");
        }

        // Check X-Content-Type-Options
        if (!ctx.Headers.ContainsKey("x-content-type-options"))
        {
            missingHeaders.Add("X-Content-Type-Options");
        }
        else if (ctx.Headers.TryGetValue("x-content-type-options", out var xcto) && !xcto.Equals("nosniff", StringComparison.OrdinalIgnoreCase))
        {
            weakHeaders.Add(("X-Content-Type-Options", $"Value is '{xcto}' (should be 'nosniff')"));
        }

        // Check X-Frame-Options
        if (!ctx.Headers.ContainsKey("x-frame-options"))
        {
            missingHeaders.Add("X-Frame-Options");
        }

        // Check Referrer-Policy
        if (!ctx.Headers.ContainsKey("referrer-policy"))
        {
            missingHeaders.Add("Referrer-Policy");
        }

        // Report missing security headers
        if (missingHeaders.Any() || weakHeaders.Any())
        {
            var builder = FindingDetailsBuilder.Create()
                .AddItem($"Security headers: {missingHeaders.Count} missing, {weakHeaders.Count} weak");
            
            if (missingHeaders.Any())
            {
                builder.BeginNested("🔒 Missing Security Headers");
                foreach (var header in missingHeaders)
                {
                    builder.AddItem(header);
                }
            }
            
            if (weakHeaders.Any())
            {
                builder.BeginNested("⚠️ Weak Security Headers");
                foreach (var (header, issue) in weakHeaders)
                {
                    builder.AddItem($"{header}: {issue}");
                }
            }
            
            builder.BeginNested("ℹ️ Security Headers Purpose")
                .AddItem("HSTS: Force HTTPS for all future visits")
                .AddItem("CSP: Prevent XSS and code injection attacks")
                .AddItem("X-Content-Type-Options: Prevent MIME-type sniffing")
                .AddItem("X-Frame-Options: Prevent clickjacking attacks")
                .AddItem("Referrer-Policy: Control referrer information leakage");
            
            builder.BeginNested("💡 Recommendations")
                .AddItem("Add missing security headers via server configuration")
                .AddItem("HSTS example: Strict-Transport-Security: max-age=31536000; includeSubDomains")
                .AddItem("CSP example: Content-Security-Policy: default-src 'self'")
                .AddItem("These headers protect users and improve trust signals");
            
            builder.WithTechnicalMetadata("url", ctx.Url.ToString())
                .WithTechnicalMetadata("missingHeaders", missingHeaders.ToArray())
                .WithTechnicalMetadata("weakHeaders", weakHeaders.Select(h => new { header = h.Header, issue = h.Issue }).ToArray());
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "MISSING_SECURITY_HEADERS",
                $"Missing or weak security headers: {string.Join(", ", missingHeaders.Concat(weakHeaders.Select(h => h.Header)))}",
                builder.Build());
        }

        // Check for secure cookies
        await CheckSecureCookiesAsync(ctx);
    }

    private async Task CheckSecureCookiesAsync(UrlContext ctx)
    {
        // Check Set-Cookie headers
        if (ctx.Headers.TryGetValue("set-cookie", out var setCookie))
        {
            // Note: Set-Cookie can be a single cookie or multiple separated by commas
            // However, cookie values themselves can contain commas (in Expires dates)
            // More robust parsing would split on "; " boundaries but for basic check this works
            var cookies = new List<string>();
            
            // Simple split - may not be perfect but catches most cases
            if (setCookie.Contains("; "))
            {
                // Likely a single cookie with attributes
                cookies.Add(setCookie);
            }
            else
            {
                // Multiple cookies or simple cookie
                cookies = setCookie.Split(',').Select(c => c.Trim()).ToList();
            }
            
            var insecureCookies = new List<string>();
            var missingHttpOnly = new List<string>();

            foreach (var cookie in cookies)
            {
                if (string.IsNullOrWhiteSpace(cookie))
                    continue;
                
                var cookieParts = cookie.Split('=');
                if (cookieParts.Length < 2)
                    continue;
                
                var cookieName = cookieParts[0].Trim();
                
                // Check for Secure flag
                if (!cookie.Contains("Secure", StringComparison.OrdinalIgnoreCase))
                {
                    insecureCookies.Add(cookieName);
                }
                
                // Check for HttpOnly flag (prevents JavaScript access)
                if (!cookie.Contains("HttpOnly", StringComparison.OrdinalIgnoreCase))
                {
                    missingHttpOnly.Add(cookieName);
                }
            }

            if (insecureCookies.Any() || missingHttpOnly.Any())
            {
                var builder = FindingDetailsBuilder.Create()
                    .AddItem($"Found {cookies.Count} cookie(s) with security issues");
                
                if (insecureCookies.Any())
                {
                    builder.BeginNested("🔓 Cookies without Secure flag");
                    foreach (var name in insecureCookies.Take(5))
                    {
                        builder.AddItem(name);
                    }
                    if (insecureCookies.Count > 5)
                    {
                        builder.AddItem($"... and {insecureCookies.Count - 5} more");
                    }
                }
                
                if (missingHttpOnly.Any())
                {
                    builder.BeginNested("⚠️ Cookies without HttpOnly flag");
                    foreach (var name in missingHttpOnly.Take(5))
                    {
                        builder.AddItem(name);
                    }
                    if (missingHttpOnly.Count > 5)
                    {
                        builder.AddItem($"... and {missingHttpOnly.Count - 5} more");
                    }
                }
                
                builder.BeginNested("⚠️ Security Risk")
                    .AddItem("Secure flag: Prevents cookies sent over unencrypted HTTP")
                    .AddItem("HttpOnly flag: Prevents JavaScript access (XSS protection)");
                
                builder.BeginNested("💡 Recommendations")
                    .AddItem("Add Secure flag to all cookies on HTTPS sites")
                    .AddItem("Add HttpOnly flag to session cookies")
                    .AddItem("Example: Set-Cookie: sessionid=abc123; Secure; HttpOnly");
                
                builder.WithTechnicalMetadata("url", ctx.Url.ToString())
                    .WithTechnicalMetadata("insecureCookies", insecureCookies.ToArray())
                    .WithTechnicalMetadata("missingHttpOnly", missingHttpOnly.ToArray());
                
                await ctx.Findings.ReportAsync(
                    Key,
                    Severity.Warning,
                    "INSECURE_COOKIES",
                    $"Cookies missing security flags: {insecureCookies.Count} without Secure, {missingHttpOnly.Count} without HttpOnly",
                    builder.Build());
            }
        }
    }

    private async Task RecommendHttpsAsync(UrlContext ctx)
    {
        // Only report on important pages (depth <= 2)
        if (ctx.Metadata.Depth <= 2)
        {
            var details = FindingDetailsBuilder.Create()
                .AddItem($"Page URL: {ctx.Url}")
                .AddItem($"Protocol: HTTP (insecure)")
                .AddItem($"Page depth: {ctx.Metadata.Depth} (important page)")
                .BeginNested("⚠️ Impact")
                    .AddItem("Google confirmed HTTPS is a ranking signal")
                    .AddItem("Browsers show 'Not Secure' warning")
                    .AddItem("Users may not trust your site")
                    .AddItem("Data transmitted in plain text (security risk)")
                .BeginNested("💡 Recommendations")
                    .AddItem("Obtain an SSL/TLS certificate (free from Let's Encrypt)")
                    .AddItem("Configure server to serve site over HTTPS")
                    .AddItem("Redirect all HTTP traffic to HTTPS (301 permanent)")
                    .AddItem("Update internal links to use HTTPS")
                .WithTechnicalMetadata("url", ctx.Url.ToString())
                .WithTechnicalMetadata("protocol", "HTTP")
                .WithTechnicalMetadata("depth", ctx.Metadata.Depth)
                .Build();
            
            await ctx.Findings.ReportAsync(
                Key,
                Severity.Warning,
                "HTTP_NOT_HTTPS",
                "Important page served over HTTP instead of HTTPS (ranking signal + user trust)",
                details);
        }
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

