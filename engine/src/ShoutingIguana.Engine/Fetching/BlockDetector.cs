using ShoutingIguana.Engine.Fetching.Models;

namespace ShoutingIguana.Engine.Fetching;

/// <summary>
/// Recognises responses where a firewall or bot protection stopped the crawler, so they are reported as
/// "Blocked" rather than looking like ordinary 403/503 pages. Only signals vendors send when they challenge or
/// block count: protected sites carry x-datadome, x-iinfo, x-sucuri-id and Cloudflare's challenge-platform
/// scripts on every response, so a real 403 from such a site stays a 403. Body checks only run on error
/// statuses, so a normal page with a CAPTCHA contact form is never flagged.
/// </summary>
internal static class BlockDetector
{
    public static string? Detect(int statusCode, IReadOnlyList<ResponseHeader> headers, string? body)
    {
        if (GetHeader(headers, "cf-mitigated")?.Contains("challenge", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "Cloudflare challenge";
        }

        if (GetHeader(headers, "x-amzn-waf-action") is { } awsAction)
        {
            return $"AWS WAF {awsAction.ToLowerInvariant()}";
        }

        // DataDome marks every challenge or block it serves with X-DD-B (1 is a CAPTCHA, 2 a block).
        if (GetHeader(headers, "x-dd-b") is not null)
        {
            return "DataDome";
        }

        if (statusCode == 429)
        {
            return "Rate limited (HTTP 429)";
        }

        if (statusCode is not (401 or 403 or 405 or 503))
        {
            return null;
        }

        if (GetHeader(headers, "x-sucuri-block") is not null ||
            Contains(body, "Access Denied - Sucuri Website Firewall") || Contains(body, "Sucuri WebSite Firewall - Access Denied"))
        {
            return "Sucuri";
        }

        if (Contains(body, "captcha-delivery.com"))
        {
            return "DataDome";
        }

        if (Contains(body, "Incapsula incident ID") || Contains(body, "_Incapsula_Resource?CWUDNSAI="))
        {
            return "Imperva";
        }

        if (body is null)
        {
            return null;
        }

        // Cloudflare injects /cdn-cgi/challenge-platform/ scripts into ordinary pages, and Turnstile widgets carry
        // cf-chl- ids, so only the challenge page's own options object and the interstitial and block pages count.
        var server = GetHeader(headers, "server") ?? string.Empty;
        if (server.Contains("cloudflare", StringComparison.OrdinalIgnoreCase) &&
            (Contains(body, "_cf_chl_opt") || Contains(body, "Just a moment...") ||
             Contains(body, "Attention Required! | Cloudflare") || Contains(body, "Sorry, you have been blocked")))
        {
            return "Cloudflare";
        }

        if (server.Contains("AkamaiGHost", StringComparison.OrdinalIgnoreCase) && Contains(body, "Access Denied"))
        {
            return "Akamai";
        }

        if (Contains(body, "px-captcha") || Contains(body, "_pxCaptcha"))
        {
            return "HUMAN (PerimeterX)";
        }

        if (Contains(body, "g-recaptcha") || Contains(body, "h-captcha") || Contains(body, "cf-turnstile"))
        {
            return "CAPTCHA challenge";
        }

        return null;
    }

    private static string? GetHeader(IReadOnlyList<ResponseHeader> headers, string name)
    {
        foreach (var header in headers)
        {
            if (header.Name == name)
            {
                return header.Value;
            }
        }

        return null;
    }

    private static bool Contains(string? body, string marker) =>
        body?.Contains(marker, StringComparison.OrdinalIgnoreCase) == true;
}
