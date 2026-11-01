using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Fetching.Models;

namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>
/// Internal URLs of any type (pages, images, scripts) that failed, were blocked, or are disallowed by robots.txt. The
/// crawler's probes of the site's other versions aren't the site's URLs unless the site leads to them: a dead www twin
/// is normal, and <see cref="SiteVersionRules"/> judges how they answer.
/// </summary>
internal sealed class ResponseCodeRules(UrlTable urls, IReadOnlyDictionary<int, string> blockReasons) : ISiteRule
{
    public void Observe(AnalyzedPage page)
    {
    }

    public IEnumerable<IssueHit> Finish()
    {
        for (var id = 1; id < urls.Length; id++)
        {
            var url = urls[id];
            if (!url.IsInternal || !url.Fetched || url.IsSiteVersionProbe)
            {
                continue;
            }

            var hit = url.Outcome switch
            {
                FetchOutcome.ClientError => new IssueHit(IssueCatalog.INTERNAL_CLIENT_ERROR, id, $"HTTP {url.StatusCode}"),
                FetchOutcome.ServerError => new IssueHit(IssueCatalog.INTERNAL_SERVER_ERROR, id, $"HTTP {url.StatusCode}"),
                FetchOutcome.Timeout => new IssueHit(IssueCatalog.INTERNAL_UNREACHABLE, id, "Timed out"),
                FetchOutcome.NetworkError => new IssueHit(IssueCatalog.INTERNAL_UNREACHABLE, id, "Connection failed"),
                FetchOutcome.Blocked => new IssueHit(IssueCatalog.INTERNAL_BLOCKED, id, blockReasons.GetValueOrDefault(id) ?? $"HTTP {url.StatusCode}"),
                // Refused because robots.txt couldn't be read says so; a robots.txt rule needs no explaining.
                FetchOutcome.RobotsDisallowed => new IssueHit(IssueCatalog.BLOCKED_BY_ROBOTS, id, blockReasons.GetValueOrDefault(id)),
                _ => (IssueHit?)null
            };

            if (hit is { } found)
            {
                yield return found;
            }
        }
    }
}
