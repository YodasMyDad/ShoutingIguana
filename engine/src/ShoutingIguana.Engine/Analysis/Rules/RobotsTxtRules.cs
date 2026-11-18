using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>
/// robots.txt of the site's own origins, reported once on the home page. The crawl also reads other sites'
/// robots.txt before checking links to them; those aren't the site owner's to fix. An unavailable robots.txt stops
/// crawling of its own origin only, so one on a subdomain or the http:// version is less serious than the main one.
/// </summary>
/// <param name="mainOrigins">The start URL's origin and the home page's, e.g. https://example.com.</param>
/// <param name="siteVersionOnlyOrigins">
/// Origins only the crawler's probe of the site's other versions went to (a www twin, the http:// version) and nothing on
/// the site links to. Their robots.txt is left out: a dead www twin's would fail on nearly every crawl.
/// </param>
internal sealed class RobotsTxtRules(UrlTable urls, IReadOnlyList<RobotsRecord> robots, CrawlScope scope, IReadOnlySet<string> mainOrigins,
    IReadOnlySet<string>? siteVersionOnlyOrigins = null, IReadOnlyList<StoredSitemap>? sitemaps = null) : ISiteRule
{
    public IReadOnlyList<IssueDefinition> Checks { get; } =
        [IssueCatalog.ROBOTS_TXT_UNREACHABLE, IssueCatalog.ROBOTS_TXT_UNREACHABLE_OTHER_ORIGIN, IssueCatalog.ROBOTS_TXT_MISSING, IssueCatalog.ROBOTS_TXT_NO_SITEMAP];

    public void Observe(AnalyzedPage page)
    {
    }

    public IEnumerable<IssueHit> Finish()
    {
        if (urls.HomeId == 0)
        {
            yield break;
        }

        var own = robots.Where(file => Uri.TryCreate(file.Origin, UriKind.Absolute, out var origin) && scope.IsInternal(origin) &&
                                       siteVersionOnlyOrigins?.Contains(file.Origin) != true).ToList();

        // RFC 9309 and Google: a 5xx, a 429 or no answer means "disallow everything"; another 4xx, or a robots.txt
        // behind too many redirects, means there are no rules (the crawler treats them the same way).
        var unreachable = own.Where(file => file.StatusCode is >= 500 or 429 || (file.StatusCode is null && !TooManyRedirects(file))).ToLookup(file => mainOrigins.Contains(file.Origin));
        if (unreachable[true].Any())
        {
            yield return new IssueHit(IssueCatalog.ROBOTS_TXT_UNREACHABLE, urls.HomeId, string.Join(", ", unreachable[true].Select(Describe)));
        }

        if (unreachable[false].Any())
        {
            yield return new IssueHit(IssueCatalog.ROBOTS_TXT_UNREACHABLE_OTHER_ORIGIN, urls.HomeId, string.Join(", ", unreachable[false].Select(Describe)));
        }

        var missing = own.Where(file => file.StatusCode is >= 400 and < 500 and not 429 || TooManyRedirects(file)).ToList();
        if (missing.Count > 0)
        {
            yield return new IssueHit(IssueCatalog.ROBOTS_TXT_MISSING, urls.HomeId, string.Join(", ", missing.Select(Describe)));
        }

        // A sitemap found anyway (the probe of /sitemap.xml, or the settings) that no main origin's robots.txt names. With
        // none found, sitemap-missing says so; a robots.txt that didn't answer says nothing either way.
        var answered = own.Where(file => mainOrigins.Contains(file.Origin) && file.StatusCode is >= 200 and < 300).ToList();
        if (sitemaps?.FirstOrDefault(sitemap => sitemap.IsRead) is { } found && answered.Count > 0 && !answered.Any(file => NamesSitemap(file.Content)))
        {
            yield return new IssueHit(IssueCatalog.ROBOTS_TXT_NO_SITEMAP, urls.HomeId,
                $"{answered[0].Origin}/robots.txt has no Sitemap: line; the sitemap was found at {found.Url}");
        }
    }

    /// <summary>A robots.txt line that names a sitemap: "Sitemap:" at its start, in any case (RFC 9309, Google).</summary>
    internal static bool NamesSitemap(string? content) =>
        content is not null && content.Split('\n').Any(line => line.Trim().TrimStart('\uFEFF').StartsWith("sitemap:", StringComparison.OrdinalIgnoreCase));

    /// <summary>RobotsCache records a robots.txt behind too many redirects without a status, and allows everything.</summary>
    internal static bool TooManyRedirects(RobotsRecord file) =>
        file.StatusCode is null && file.Error?.Contains("redirects", StringComparison.OrdinalIgnoreCase) == true;

    private static string Describe(RobotsRecord file) =>
        $"{file.Origin}/robots.txt ({(file.StatusCode is { } status ? HttpStatusNames.Describe(status) : file.Error is { } error ? HttpStatusNames.NameIn(error) : "no response")})";
}
