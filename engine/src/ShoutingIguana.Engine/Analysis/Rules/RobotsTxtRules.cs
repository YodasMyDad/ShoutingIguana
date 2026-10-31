using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>
/// robots.txt of the site's own origins, reported once on the home page. The crawl also reads other sites'
/// robots.txt before checking links to them; those aren't the site owner's to fix.
/// </summary>
internal sealed class RobotsTxtRules(UrlTable urls, IReadOnlyList<RobotsRecord> robots, CrawlScope scope) : ISiteRule
{
    public void Observe(AnalyzedPage page)
    {
    }

    public IEnumerable<IssueHit> Finish()
    {
        if (urls.HomeId == 0)
        {
            yield break;
        }

        var own = robots.Where(file => Uri.TryCreate(file.Origin, UriKind.Absolute, out var origin) && scope.IsInternal(origin)).ToList();

        // RFC 9309: a 5xx or no answer means "disallow everything"; a 4xx means there are no rules.
        var unreachable = own.Where(file => file.StatusCode is null or >= 500).ToList();
        if (unreachable.Count > 0)
        {
            yield return new IssueHit(IssueCatalog.ROBOTS_TXT_UNREACHABLE, urls.HomeId, string.Join(", ",
                unreachable.Select(file => $"{file.Origin}/robots.txt ({(file.StatusCode is { } status ? $"HTTP {status}" : file.Error ?? "no response")})")));
        }

        var missing = own.Where(file => file.StatusCode is >= 400 and < 500).ToList();
        if (missing.Count > 0)
        {
            yield return new IssueHit(IssueCatalog.ROBOTS_TXT_MISSING, urls.HomeId, string.Join(", ",
                missing.Select(file => $"{file.Origin}/robots.txt (HTTP {file.StatusCode})")));
        }
    }
}
