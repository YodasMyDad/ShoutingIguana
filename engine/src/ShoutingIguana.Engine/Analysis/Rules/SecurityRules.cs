using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Parsing.Models;

namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>
/// HTTPS and mixed content on every page. Site-wide settings (HTTPS itself, security headers) are checked once, on
/// the home page, instead of repeating the same finding on every URL.
/// </summary>
internal sealed class SecurityRules(UrlTable urls) : IPageRule
{
    public IEnumerable<IssueHit> Check(AnalyzedPage page)
    {
        var isHttps = page.Uri.Scheme == Uri.UriSchemeHttps;
        if (page.UrlId == urls.HomeId)
        {
            if (!isHttps)
            {
                yield return new IssueHit(IssueCatalog.NO_HTTPS, page.UrlId);
            }

            var missing = MissingHeaders(page, isHttps);
            if (missing.Count > 0)
            {
                yield return new IssueHit(IssueCatalog.SECURITY_HEADERS_MISSING, page.UrlId, $"Missing {string.Join(", ", missing)}");
            }
        }
        else if (!isHttps && urls[urls.HomeId].IsHttps)
        {
            yield return new IssueHit(IssueCatalog.HTTP_PAGE, page.UrlId);
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
                RuleText.Count(insecure.Count, "HTTP resource", "HTTP resources"), insecure.First);
        }
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
