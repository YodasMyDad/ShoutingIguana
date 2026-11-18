using System.Globalization;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Fetching;
using ShoutingIguana.Engine.Parsing.Models;

namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>
/// HTTPS and mixed content on every page. Site-wide settings (HTTPS itself, security headers) are checked once, on
/// the home page, instead of repeating the same finding on every URL. On a page Shopify served (its headers say so), the
/// security headers and the cookies Shopify sets are Shopify's, which the store's owner can't change: their issues say so
/// (<see cref="IssueHit.Platform"/>). A cookie of any other name leaves the page's cookie issue the site's own.
/// </summary>
/// <param name="hsts">The HSTS the home page sends, which keeps browsers off an HTTP copy of the site's HTTPS pages.</param>
internal sealed class SecurityRules(UrlTable urls, HstsPolicy? hsts = null) : IPageRule
{
    /// <summary>A year: hstspreload.org's least max-age, and so the least that keeps HSTS on between visits.</summary>
    private const long HSTS_MIN_SECONDS = 31_536_000;

    /// <summary>Parts of a session or login cookie's name (sessionid, PHPSESSID, auth_token, jwt…).</summary>
    private static readonly string[] SESSION_NAMES = ["sess", "sid", "auth", "token", "login", "jwt"];

    public IEnumerable<IssueHit> Check(AnalyzedPage page)
    {
        var isHttps = page.Uri.Scheme == Uri.UriSchemeHttps;
        var platform = ShopifySignals.FromHeaders(page.Headers) is null ? null : ShopifySignals.OWNER;
        if (page.UrlId == urls.HomeId)
        {
            if (!isHttps)
            {
                yield return new IssueHit(IssueCatalog.NO_HTTPS, page.UrlId);
            }

            var missing = MissingHeaders(page, isHttps);
            if (missing.Count > 0)
            {
                yield return new IssueHit(IssueCatalog.SECURITY_HEADERS_MISSING, page.UrlId, $"Missing {string.Join(", ", missing)}") { Platform = platform };
            }

            if (WeakHeaders(page, isHttps) is { Count: > 0 } weak)
            {
                yield return new IssueHit(IssueCatalog.SECURITY_HEADERS_WEAK, page.UrlId, RuleText.Fit(string.Join("; ", weak))) { Platform = platform };
            }
        }
        else if (!isHttps && urls[urls.HomeId].IsHttps)
        {
            yield return new IssueHit(IssueCatalog.HTTP_PAGE, page.UrlId);
        }

        if (InsecureCookies(page, isHttps) is { Count: > 0 } cookies)
        {
            yield return new IssueHit(IssueCatalog.COOKIES_INSECURE, page.UrlId, RuleText.Fit(string.Join("; ", cookies.Select(cookie => cookie.Fault))))
            {
                Platform = cookies.All(cookie => ShopifySignals.STOREFRONT_COOKIES.Contains(cookie.Name)) ? platform : null
            };
        }

        if (InsecureForm(page.Facts.Forms, isHttps) is { } form)
        {
            yield return new IssueHit(IssueCatalog.FORM_NOT_SECURE, page.UrlId, form);
        }

        // Where HSTS keeps browsers on HTTPS, an HTTPS page has no HTTP copy for them to take the scheme of.
        if (page.Facts.ProtocolRelativeUrls is { } relative && hsts?.Covers(page.Uri) != true)
        {
            yield return ProtocolRelative(page, relative);
        }

        if (!isHttps)
        {
            yield break;
        }

        TargetSet insecure = new();
        foreach (var link in page.Links)
        {
            if ((link.Kind is LinkKind.Image or LinkKind.Script or LinkKind.Stylesheet or LinkKind.Iframe or LinkKind.Media) &&
                urls[link.ToId] is { Known: true, IsHttps: false })
            {
                insecure.Add(link.ToId);
            }
        }

        if (insecure.Count > 0)
        {
            yield return new IssueHit(IssueCatalog.MIXED_CONTENT, page.UrlId,
                RuleText.Count(insecure.Count, "HTTP resource", "HTTP resources"), insecure.First) { Targets = insecure.Ids };
        }
    }

    /// <summary>
    /// The page's URLs written without their scheme. Each link of the page's written that way names its target, in the page's
    /// order, so every one can be listed; one in no link (a resource hint, a base) names no URL, and the detail counts it
    /// and gives the first. A page parsed before links noted how they were written names none.
    /// </summary>
    private static IssueHit ProtocolRelative(AnalyzedPage page, ProtocolRelativeFact relative)
    {
        TargetSet written = new();
        foreach (var link in page.Links.Where(link => link.Flags.HasFlag(LinkFlags.ProtocolRelative)))
        {
            written.Add(link.ToId);
        }

        var detail = relative switch
        {
            { Unlinked: > 0, FirstUnlinked: { } unlinked } when relative.Unlinked >= relative.Count =>
                $"{RuleText.Count(relative.Count, "URL in a tag that isn't a link or file", "URLs in tags that aren't links or files")}, such as {unlinked}",
            { Unlinked: > 0, FirstUnlinked: { } unlinked } =>
                $"{RuleText.Count(relative.Count, "URL", "URLs")}, such as {relative.First}; " +
                $"{RuleText.Count(relative.Unlinked, "in a tag that isn't a link or file", "in tags that aren't links or files")}, such as {unlinked}",
            _ => $"{RuleText.Count(relative.Count, "URL", "URLs")}, such as {relative.First}"
        };

        return written.First is { } first
            ? new IssueHit(IssueCatalog.PROTOCOL_RELATIVE_LINKS, page.UrlId, RuleText.Fit(detail), first)
            {
                Targets = relative.Unlinked > 0 ? [.. written.Ids, IssueHit.NO_OTHER_URL] : written.Ids
            }
            : new IssueHit(IssueCatalog.PROTOCOL_RELATIVE_LINKS, page.UrlId, RuleText.Fit(detail));
    }

    /// <summary>
    /// Security headers there but set too weakly: Strict-Transport-Security (on HTTPS) without a max-age of a year or more,
    /// as the preload list asks; X-Content-Type-Options other than nosniff, its one directive.
    /// </summary>
    private static List<string> WeakHeaders(AnalyzedPage page, bool isHttps)
    {
        List<string> weak = [];
        if (isHttps && page.Header("strict-transport-security") is { } hsts)
        {
            switch (HstsPolicy.MaxAge(hsts))
            {
                case null:
                    weak.Add("HSTS without max-age");
                    break;
                case (var written, null):
                    weak.Add($"HSTS max-age {RuleText.Quote(written ?? string.Empty, 20)} unreadable");
                    break;
                case (_, { } seconds) when seconds < HSTS_MIN_SECONDS:
                    weak.Add(string.Create(CultureInfo.InvariantCulture, $"HSTS max-age {seconds} ({Duration(seconds)}), under a year"));
                    break;
            }
        }

        if (page.Header("x-content-type-options") is { } options && !options.Trim().Equals("nosniff", StringComparison.OrdinalIgnoreCase))
        {
            weak.Add($"X-Content-Type-Options {RuleText.Quote(options, 30)}");
        }

        return weak;
    }

    /// <summary>"1 day", "30 days", "6 hours", "0 seconds": how long a max-age keeps HSTS.</summary>
    private static string Duration(long seconds) => seconds switch
    {
        >= 86_400 => RuleText.Count((int)(seconds / 86_400), "day", "days"),
        >= 3_600 => RuleText.Count((int)(seconds / 3_600), "hour", "hours"),
        _ => RuleText.Count((int)seconds, "second", "seconds")
    };

    /// <summary>
    /// The page's cookies set without basic protection: on HTTPS without Secure; SameSite=None without Secure, which
    /// browsers refuse; and a session or login cookie (by its name) without HttpOnly. Each by its name, with what it lacks:
    /// "sessionid: no Secure, no HttpOnly".
    /// </summary>
    private static List<(string Name, string Fault)> InsecureCookies(AnalyzedPage page, bool isHttps)
    {
        List<(string Name, string Fault)> faults = [];
        foreach (var cookie in page.Headers.Where(header => header.Name == "set-cookie").Select(header => header.Value))
        {
            var parts = cookie.Split(';').Select(part => part.Trim()).ToList();
            var name = parts[0].Split('=', 2)[0].Trim();
            if (name.Length == 0)
            {
                continue;
            }

            bool Has(string attribute) => parts.Skip(1).Any(part => part.Split('=', 2)[0].Trim().Equals(attribute, StringComparison.OrdinalIgnoreCase));
            var secure = Has("Secure");
            var sameSiteNone = parts.Skip(1).Any(part => part.Replace(" ", string.Empty, StringComparison.Ordinal).Equals("SameSite=None", StringComparison.OrdinalIgnoreCase));
            List<string> lacking = [];
            if (!secure && (isHttps || sameSiteNone))
            {
                lacking.Add(sameSiteNone ? "SameSite=None without Secure" : "no Secure");
            }

            if (!Has("HttpOnly") && SESSION_NAMES.Any(part => name.Contains(part, StringComparison.OrdinalIgnoreCase)))
            {
                lacking.Add("no HttpOnly");
            }

            if (lacking.Count > 0)
            {
                faults.Add((name, $"{RuleText.Cut(name, 40)}: {string.Join(", ", lacking)}"));
            }
        }

        return faults;
    }

    /// <summary>A form sending what's typed over plain HTTP: from an HTTPS page to an http:// action, or a password form on an HTTP page.</summary>
    private static string? InsecureForm(IReadOnlyList<FormFact> forms, bool isHttps)
    {
        if (isHttps)
        {
            return forms.FirstOrDefault(form => form.ActionUrl?.StartsWith("http://", StringComparison.OrdinalIgnoreCase) == true) is { } form
                ? RuleText.Fit($"Sends to {form.ActionUrl}")
                : null;
        }

        return forms.Any(form => form.HasPassword) ? "A password form on an http:// page" : null;
    }

    private static List<string> MissingHeaders(AnalyzedPage page, bool isHttps)
    {
        List<string> missing = [];
        if (isHttps && page.Header("strict-transport-security") is null)
        {
            missing.Add("Strict-Transport-Security");
        }

        if (page.Header("x-content-type-options") is null)
        {
            missing.Add("X-Content-Type-Options");
        }

        var policy = page.Header("content-security-policy");
        if (policy is null)
        {
            missing.Add("Content-Security-Policy");
        }

        if (page.Header("x-frame-options") is null && policy?.Contains("frame-ancestors", StringComparison.OrdinalIgnoreCase) != true)
        {
            missing.Add("X-Frame-Options");
        }

        if (page.Header("referrer-policy") is null)
        {
            missing.Add("Referrer-Policy");
        }

        return missing;
    }
}
