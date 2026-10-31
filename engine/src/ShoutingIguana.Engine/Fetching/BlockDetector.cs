using ShoutingIguana.Engine.Fetching.Models;

namespace ShoutingIguana.Engine.Fetching;

/// <summary>
/// Recognises responses where a firewall or bot protection stopped the crawler, so they are reported as
/// "Blocked" rather than looking like ordinary 403/503 pages. Body checks only run on error statuses, so a
/// normal page with a CAPTCHA contact form is never flagged.
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

        if (statusCode == 429)
        {
            return "Rate limited (HTTP 429)";
        }

        if (statusCode is not (401 or 403 or 405 or 503))
        {
            return null;
        }

        if (GetHeader(headers, "x-datadome") is not null || SetsCookie(headers, "datadome="))
        {
            return "DataDome";
        }

        if (GetHeader(headers, "x-iinfo") is not null || Contains(body, "Incapsula incident ID"))
        {
            return "Imperva";
        }

        if (GetHeader(headers, "x-sucuri-id") is not null || Contains(body, "Sucuri WebSite Firewall"))
        {
            return "Sucuri";
        }

        if (body is null)
        {
            return null;
        }

        var server = GetHeader(headers, "server") ?? string.Empty;
        if (server.Contains("cloudflare", StringComparison.OrdinalIgnoreCase) &&
            (Contains(body, "challenge-platform") || Contains(body, "cf-chl") ||
             Contains(body, "Just a moment...") || Contains(body, "Attention Required! | Cloudflare")))
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

    private static bool SetsCookie(IReadOnlyList<ResponseHeader> headers, string cookiePrefix) =>
        headers.Any(header => header.Name == "set-cookie" &&
                              header.Value.StartsWith(cookiePrefix, StringComparison.OrdinalIgnoreCase));

    private static bool Contains(string? body, string marker) =>
        body?.Contains(marker, StringComparison.OrdinalIgnoreCase) == true;
}
