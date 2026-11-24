using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>
/// The site's own URLs judged by their text (batch 7 of docs/v2-new-checks.md): internal links tagged for ad tracking, and
/// paths that repeat themselves, as a relative link building ever-longer URLs does.
/// </summary>
/// <param name="internalUrls">Every internal URL the crawl has, with its id.</param>
internal sealed class UrlTextRules(UrlTable urls, IReadOnlyList<(int Id, string Url)> internalUrls) : ISiteRule
{
    /// <summary>The ad networks' click ids, as Screaming Frog lists them.</summary>
    private static readonly HashSet<string> TRACKING = new(StringComparer.OrdinalIgnoreCase) { "gclid", "fbclid", "msclkid", "mc_cid", "_ga" };

    /// <summary>Google Analytics' campaign parameters all start so: utm_source, utm_medium, and GA4's utm_source_platform and the like.</summary>
    private const string CAMPAIGN_PREFIX = "utm_";

    private const int MAX_RUN = 3;

    public IReadOnlyList<IssueDefinition> Checks { get; } = [IssueCatalog.URL_TRACKING_PARAMETERS, IssueCatalog.URL_REPETITIVE_PATH];

    public void Observe(AnalyzedPage page)
    {
    }

    public IEnumerable<IssueHit> Finish()
    {
        foreach (var (id, url) in internalUrls)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                continue;
            }

            // A link between the site's pages: an <a href> on a page the analysis read.
            if (uri.Query.Length > 1 && urls.Inlinks(id) > 0 &&
                UrlRules.ParameterNames(uri.Query).Where(IsTracking).ToList() is { Count: > 0 } tracking)
            {
                yield return new IssueHit(IssueCatalog.URL_TRACKING_PARAMETERS, id, RuleText.Fit(string.Join(", ", tracking)));
            }

            // One a page of the crawl links to or uses: the link that builds the path is there to fix. A URL only a
            // sitemap lists was written that way on purpose (a publication's attachment under its own name).
            if (urls.UsedOn(id) > 0 && IsRepetitive(uri.AbsolutePath))
            {
                yield return new IssueHit(IssueCatalog.URL_REPETITIVE_PATH, id);
            }
        }
    }

    private static bool IsTracking(string parameter) =>
        TRACKING.Contains(parameter) || (parameter.Length > CAMPAIGN_PREFIX.Length && parameter.StartsWith(CAMPAIGN_PREFIX, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// A path with one segment three times or more, or a run of one to three segments repeated back to back (/shop/shop/,
    /// /a/b/a/b/). A segment that's only digits repeats nothing: a date's month and day are often the same number
    /// (/2025/11/11/), and so are ids.
    /// </summary>
    internal static bool IsRepetitive(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries).Where(segment => !segment.All(char.IsAsciiDigit)).ToArray();
        if (segments.GroupBy(segment => segment, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() >= 3))
        {
            return true;
        }

        for (var run = 1; run <= MAX_RUN; run++)
        {
            for (var start = 0; start + 2 * run <= segments.Length; start++)
            {
                if (Enumerable.Range(0, run).All(offset => segments[start + offset].Equals(segments[start + run + offset], StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
