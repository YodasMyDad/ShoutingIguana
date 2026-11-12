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
    public IReadOnlyList<IssueDefinition> Checks { get; } = [IssueCatalog.INTERNAL_CLIENT_ERROR, IssueCatalog.INTERNAL_SERVER_ERROR, IssueCatalog.INTERNAL_UNREACHABLE, IssueCatalog.INTERNAL_BLOCKED, IssueCatalog.BLOCKED_BY_ROBOTS];

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
                FetchOutcome.ClientError => new IssueHit(IssueCatalog.INTERNAL_CLIENT_ERROR, id, HttpStatusNames.Describe(url.StatusCode)),
                FetchOutcome.ServerError => new IssueHit(IssueCatalog.INTERNAL_SERVER_ERROR, id, HttpStatusNames.Describe(url.StatusCode)),
                FetchOutcome.Timeout => new IssueHit(IssueCatalog.INTERNAL_UNREACHABLE, id, "Timed out"),
                FetchOutcome.NetworkError => new IssueHit(IssueCatalog.INTERNAL_UNREACHABLE, id, "Connection failed"),
                FetchOutcome.Blocked => new IssueHit(IssueCatalog.INTERNAL_BLOCKED, id, StoredReason(id) ?? HttpStatusNames.Describe(url.StatusCode)),
                // Refused because robots.txt couldn't be read says so; a robots.txt rule needs no explaining.
                FetchOutcome.RobotsDisallowed => new IssueHit(IssueCatalog.BLOCKED_BY_ROBOTS, id, StoredReason(id)),
                _ => (IssueHit?)null
            };

            if (hit is { } found)
            {
                yield return found;
            }
        }
    }

    /// <summary>The crawl's own reason, a status in it by its name: "Rate limited (429 Too Many Requests)".</summary>
    private string? StoredReason(int id) => blockReasons.GetValueOrDefault(id) is { } reason ? HttpStatusNames.NameIn(reason) : null;
}
