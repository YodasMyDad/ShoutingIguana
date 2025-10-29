namespace ShoutingIguana.PluginSdk.Helpers;

/// <summary>
/// URL manipulation utilities for plugins.
/// Provides common URL operations like normalization, resolution, and comparison.
/// </summary>
/// <remarks>
/// Use these helpers to handle URLs consistently across your plugin.
/// Normalized URLs ensure reliable comparisons and lookups.
/// </remarks>
/// <example>
/// <code>
/// // Normalize URLs for comparison
/// var normalized = UrlHelper.Normalize("https://Example.COM/page/");
/// // Result: "https://example.com/page"
/// 
/// // Resolve relative URLs
/// var absolute = UrlHelper.Resolve(
///     new Uri("https://example.com/section/"), 
///     "../other");
/// // Result: "https://example.com/other"
/// 
/// // Check if URL is external
/// bool isExternal = UrlHelper.IsExternal(
///     "https://example.com",
///     "https://other-site.com/page");
/// // Result: true
/// </code>
/// </example>
public static class UrlHelper
{
    /// <summary>
    /// Normalizes a URL for comparison purposes.
    /// </summary>
    /// <param name="url">The URL to normalize.</param>
    /// <returns>
    /// A normalized URL string with:
    /// - Lowercase scheme and host
    /// - Trailing slash removed from path
    /// - Fragment (#) removed
    /// - Default ports removed (80 for http, 443 for https)
    /// </returns>
    /// <remarks>
    /// <para>
    /// Use this when:
    /// - Comparing URLs for equality
    /// - Using URLs as dictionary keys
    /// - Querying repositories by address
    /// </para>
    /// <para>
    /// The normalization follows common SEO best practices where
    /// "https://Example.com/PAGE/" and "https://example.com/page"
    /// are considered the same URL.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var url1 = UrlHelper.Normalize("https://Example.COM/page/");
    /// var url2 = UrlHelper.Normalize("https://example.com/page");
    /// // url1 == url2 (both are "https://example.com/page")
    /// 
    /// // Use for lookups:
    /// var normalized = UrlHelper.Normalize(canonicalUrl);
    /// var urlInfo = await accessor.GetUrlByAddressAsync(projectId, normalized);
    /// </code>
    /// </example>
    public static string Normalize(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return url;
        }

        try
        {
            var uri = new Uri(url, UriKind.Absolute);
            
            // Build normalized URL
            var normalized = uri.GetLeftPart(UriPartial.Path);
            
            // Remove trailing slash from path (except for root)
            if (normalized.EndsWith("/") && uri.PathAndQuery.Length > 1)
            {
                normalized = normalized.TrimEnd('/');
            }
            
            // Convert to lowercase (scheme and host are case-insensitive, path is case-sensitive in some servers)
            // For SEO purposes, we normalize the whole URL to lowercase
            return normalized.ToLowerInvariant();
        }
        catch (UriFormatException)
        {
            // If it's not a valid URI, return as-is
            return url;
        }
    }

    /// <summary>
    /// Resolves a relative or absolute URL against a base URL.
    /// </summary>
    /// <param name="baseUri">The base URL to resolve against.</param>
    /// <param name="relativeUrl">The relative or absolute URL to resolve.</param>
    /// <returns>
    /// The resolved absolute URL as a string.
    /// If the relative URL is already absolute, it returns unchanged.
    /// </returns>
    /// <remarks>
    /// Handles all standard relative URL patterns:
    /// - Relative paths: "page.html" -> "https://example.com/section/page.html"
    /// - Parent paths: "../other.html" -> "https://example.com/other.html"
    /// - Root-relative: "/about.html" -> "https://example.com/about.html"
    /// - Query strings: "?q=search" -> "https://example.com/page?q=search"
    /// - Anchors: "#section" -> "https://example.com/page#section"
    /// </remarks>
    /// <example>
    /// <code>
    /// var baseUri = new Uri("https://example.com/section/page.html");
    /// 
    /// // Relative path
    /// var url1 = UrlHelper.Resolve(baseUri, "other.html");
    /// // Result: "https://example.com/section/other.html"
    /// 
    /// // Parent directory
    /// var url2 = UrlHelper.Resolve(baseUri, "../about.html");
    /// // Result: "https://example.com/about.html"
    /// 
    /// // Root-relative
    /// var url3 = UrlHelper.Resolve(baseUri, "/contact.html");
    /// // Result: "https://example.com/contact.html"
    /// 
    /// // Already absolute
    /// var url4 = UrlHelper.Resolve(baseUri, "https://other.com/page");
    /// // Result: "https://other.com/page"
    /// </code>
    /// </example>
    public static string Resolve(Uri baseUri, string relativeUrl)
    {
        return Resolve(baseUri, relativeUrl, null);
    }

    /// <summary>
    /// Resolves a relative or absolute URL against a base URL, optionally respecting HTML base tag.
    /// </summary>
    /// <param name="baseUri">The current page URL to resolve against.</param>
    /// <param name="relativeUrl">The relative or absolute URL to resolve.</param>
    /// <param name="baseTagUri">Optional base tag URI from HTML &lt;base href&gt;. When provided, relative URLs resolve against this instead of baseUri.</param>
    /// <returns>
    /// The resolved absolute URL as a string.
    /// If the relative URL is already absolute, it returns unchanged.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This method respects HTML base tags, matching browser behavior.
    /// If a base tag is present, relative URLs resolve against it instead of the page URL.
    /// </para>
    /// <para>
    /// Resolution priority:
    /// 1. If URL is absolute → return as-is
    /// 2. If baseTagUri is provided → resolve relative to base tag
    /// 3. Otherwise → resolve relative to current page URL (baseUri)
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var pageUri = new Uri("https://example.com/section/page.html");
    /// var baseTagUri = new Uri("https://example.com/");
    /// 
    /// // Without base tag - relative to page directory
    /// var url1 = UrlHelper.Resolve(pageUri, "other.html", null);
    /// // Result: "https://example.com/section/other.html"
    /// 
    /// // With base tag - relative to base tag
    /// var url2 = UrlHelper.Resolve(pageUri, "other.html", baseTagUri);
    /// // Result: "https://example.com/other.html"
    /// </code>
    /// </example>
    public static string Resolve(Uri baseUri, string relativeUrl, Uri? baseTagUri)
    {
        if (string.IsNullOrWhiteSpace(relativeUrl))
        {
            return relativeUrl;
        }

        try
        {
            // Check if it's already an absolute URL
            if (Uri.TryCreate(relativeUrl, UriKind.Absolute, out var absoluteUri))
            {
                return absoluteUri.ToString();
            }
            
            // Use base tag URI if provided, otherwise use page URI
            var resolveAgainst = baseTagUri ?? baseUri;
            
            // Resolve as relative URL
            if (Uri.TryCreate(resolveAgainst, relativeUrl, out var resolvedUri))
            {
                return resolvedUri.ToString();
            }
            
            // If resolution fails, return as-is
            return relativeUrl;
        }
        catch
        {
            return relativeUrl;
        }
    }

    /// <summary>
    /// Determines if a target URL is external to the base URL (different domain).
    /// </summary>
    /// <param name="baseUrl">The base URL to compare against (typically your site's URL).</param>
    /// <param name="targetUrl">The target URL to check.</param>
    /// <returns>
    /// <c>true</c> if the target URL is on a different domain; 
    /// <c>false</c> if it's on the same domain or is a relative URL.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This method compares hosts (domains) to determine if a link is external.
    /// It automatically handles:
    /// - www vs non-www (treated as same domain)
    /// - Case-insensitive domain comparison
    /// - Subdomain differences (subdomain.example.com vs example.com are considered different)
    /// </para>
    /// <para>
    /// <b>Important:</b> Relative URLs (like "../page" or "/about") are always considered internal.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var baseUrl = "https://example.com";
    /// 
    /// // External domain
    /// bool ext1 = UrlHelper.IsExternal(baseUrl, "https://other-site.com/page");
    /// // Result: true
    /// 
    /// // Same domain (www ignored)
    /// bool ext2 = UrlHelper.IsExternal(baseUrl, "https://www.example.com/page");
    /// // Result: false
    /// 
    /// // Same domain, different protocol
    /// bool ext3 = UrlHelper.IsExternal(baseUrl, "http://example.com/page");
    /// // Result: false (same domain)
    /// 
    /// // Subdomain (considered external)
    /// bool ext4 = UrlHelper.IsExternal(baseUrl, "https://blog.example.com/page");
    /// // Result: true
    /// 
    /// // Relative URL (internal)
    /// bool ext5 = UrlHelper.IsExternal(baseUrl, "/about");
    /// // Result: false
    /// </code>
    /// </example>
    public static bool IsExternal(string baseUrl, string targetUrl)
    {
        // Try to parse both URLs
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
        {
            return true; // Can't determine, assume external
        }
        
        if (!Uri.TryCreate(targetUrl, UriKind.Absolute, out var targetUri))
        {
            return false; // Relative URL is internal
        }
        
        // Compare hosts (case-insensitive)
        var baseHost = baseUri.Host.ToLowerInvariant();
        var targetHost = targetUri.Host.ToLowerInvariant();
        
        // Remove "www." prefix for comparison
        if (baseHost.StartsWith("www."))
        {
            baseHost = baseHost.Substring(4);
        }
        if (targetHost.StartsWith("www."))
        {
            targetHost = targetHost.Substring(4);
        }
        
        // Different hosts = external
        return baseHost != targetHost;
    }

    /// <summary>
    /// Extracts the domain (host) from a URL.
    /// </summary>
    /// <param name="url">The URL to extract the domain from.</param>
    /// <returns>
    /// The domain/host portion of the URL, or null if the URL is invalid.
    /// Removes "www." prefix automatically.
    /// </returns>
    /// <example>
    /// <code>
    /// var domain1 = UrlHelper.GetDomain("https://www.example.com/page");
    /// // Result: "example.com"
    /// 
    /// var domain2 = UrlHelper.GetDomain("https://blog.example.com/post");
    /// // Result: "blog.example.com"
    /// </code>
    /// </example>
    public static string? GetDomain(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }
        
        var host = uri.Host.ToLowerInvariant();
        
        // Remove www. prefix
        if (host.StartsWith("www."))
        {
            return host.Substring(4);
        }
        
        return host;
    }

    /// <summary>
    /// Checks if a URL uses HTTPS protocol.
    /// </summary>
    /// <param name="url">The URL to check.</param>
    /// <returns><c>true</c> if the URL uses HTTPS; <c>false</c> otherwise.</returns>
    /// <example>
    /// <code>
    /// bool secure = UrlHelper.IsHttps("https://example.com");
    /// // Result: true
    /// 
    /// if (!UrlHelper.IsHttps(url))
    /// {
    ///     await ctx.Findings.ReportAsync(
    ///         Key,
    ///         Severity.Warning,
    ///         "HTTP_NOT_HTTPS",
    ///         $"Page uses HTTP instead of HTTPS: {url}",
    ///         null);
    /// }
    /// </code>
    /// </example>
    public static bool IsHttps(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    /// <summary>
    /// Combines a base URL with a path, handling trailing/leading slashes correctly.
    /// </summary>
    /// <param name="baseUrl">The base URL (e.g., "https://example.com").</param>
    /// <param name="path">The path to append (e.g., "/api/endpoint" or "api/endpoint").</param>
    /// <returns>The combined URL.</returns>
    /// <example>
    /// <code>
    /// var url = UrlHelper.Combine("https://example.com", "api/endpoint");
    /// // Result: "https://example.com/api/endpoint"
    /// 
    /// var url2 = UrlHelper.Combine("https://example.com/", "/api/endpoint");
    /// // Result: "https://example.com/api/endpoint"
    /// </code>
    /// </example>
    public static string Combine(string baseUrl, string path)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return path;
        }
        if (string.IsNullOrWhiteSpace(path))
        {
            return baseUrl;
        }

        baseUrl = baseUrl.TrimEnd('/');
        path = path.TrimStart('/');
        
        return $"{baseUrl}/{path}";
    }

    /// <summary>
    /// Extracts the base tag URI from HTML content if present.
    /// </summary>
    /// <param name="htmlContent">The HTML content to parse.</param>
    /// <param name="currentPageUri">The current page URI used to resolve relative base hrefs.</param>
    /// <returns>
    /// The resolved base tag URI if found and valid; otherwise, null.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This method parses HTML to find &lt;base href="..."&gt; tags, which browsers use
    /// to resolve relative URLs. The base href can be:
    /// - Absolute: &lt;base href="https://example.com/"&gt;
    /// - Root-relative: &lt;base href="/"&gt;
    /// - Path-relative: &lt;base href="/subfolder/"&gt;
    /// </para>
    /// <para>
    /// If the base href is relative, it's resolved against the current page URL.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var html = "&lt;html&gt;&lt;head&gt;&lt;base href=\"/\"&gt;&lt;/head&gt;&lt;/html&gt;";
    /// var pageUri = new Uri("https://example.com/section/page.html");
    /// var baseUri = UrlHelper.ExtractBaseTag(html, pageUri);
    /// // Result: new Uri("https://example.com/")
    /// </code>
    /// </example>
    public static Uri? ExtractBaseTag(string htmlContent, Uri currentPageUri)
    {
        if (string.IsNullOrWhiteSpace(htmlContent))
        {
            return null;
        }

        try
        {
            // Simple regex-based extraction to avoid loading HtmlAgilityPack dependency in SDK
            // Look for <base href="..."> tag (case-insensitive, with optional attributes)
            var match = System.Text.RegularExpressions.Regex.Match(
                htmlContent,
                @"<base\s+[^>]*href\s*=\s*[""']([^""']+)[""']",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (!match.Success)
            {
                return null;
            }

            var baseHref = match.Groups[1].Value;
            if (string.IsNullOrWhiteSpace(baseHref))
            {
                return null;
            }

            // Try to parse as absolute URI
            if (Uri.TryCreate(baseHref, UriKind.Absolute, out var absoluteBaseUri))
            {
                return absoluteBaseUri;
            }

            // Try to resolve as relative to current page
            if (Uri.TryCreate(currentPageUri, baseHref, out var resolvedBaseUri))
            {
                return resolvedBaseUri;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }
}

