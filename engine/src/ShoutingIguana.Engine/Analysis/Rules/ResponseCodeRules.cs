using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Robots;
using ShoutingIguana.Engine.Robots.Models;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>
/// Internal URLs of any type (pages, images, scripts) that failed, were blocked, or are disallowed by robots.txt. The
/// crawler's probes of the site's other versions aren't the site's URLs unless the site leads to them: a dead www twin
/// is normal, and <see cref="SiteVersionRules"/> judges how they answer.
/// </summary>
/// <param name="robots">The robots.txt files the crawl read, to name the rule that blocked each URL it refused.</param>
/// <param name="robotsToken">The product token the crawl matched robots.txt groups with.</param>
/// <param name="readUrl">A URL's address by its id, for the rule that refused it.</param>
internal sealed class ResponseCodeRules(UrlTable urls, IReadOnlyDictionary<int, string> blockReasons, IReadOnlyList<RobotsRecord>? robots = null,
    string? robotsToken = null, Func<int, string?>? readUrl = null) : ISiteRule
{
    /// <summary>Each origin's robots.txt as the crawl read it, parsed once with its lines; null for an origin without one.</summary>
    private readonly Dictionary<string, (RobotsTxt Rules, List<RobotsLine> Lines)?> _parsed = new(StringComparer.Ordinal);

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
                // Refused because robots.txt couldn't be read says so; otherwise the rule that refused it, with its line, so each
                // rule is one group: "Disallow: /checkout, line 50".
                FetchOutcome.RobotsDisallowed => new IssueHit(IssueCatalog.BLOCKED_BY_ROBOTS, id, StoredReason(id) ?? RuleOf(id)),
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

    /// <summary>
    /// The robots.txt rule that refused a URL, as the file writes it, and its line: "Disallow: /checkout, line 50". The same
    /// answer the robots.txt screen gives (<see cref="RobotsTxt.Match"/>). Null where no rule of the file as stored refuses it.
    /// </summary>
    private string? RuleOf(int id)
    {
        if (robots is null || robotsToken is null || readUrl?.Invoke(id) is not { } url || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var origin = uri.GetLeftPart(UriPartial.Authority);
        if (!_parsed.TryGetValue(origin, out var parsed))
        {
            List<RobotsLine> lines = [];
            _parsed[origin] = parsed = robots.FirstOrDefault(file => file.Origin == origin) is { Content: { } content }
                ? (RobotsTxtParser.Parse(content, robotsToken, lines), lines)
                : null;
        }

        return parsed is { } file && file.Rules.Match(uri.PathAndQuery) is { Allow: false, Line: > 0 } rule && rule.Line <= file.Lines.Count
            ? $"{file.Lines[rule.Line - 1].Text.Trim()}, line {rule.Line:N0}"
            : null;
    }
}
