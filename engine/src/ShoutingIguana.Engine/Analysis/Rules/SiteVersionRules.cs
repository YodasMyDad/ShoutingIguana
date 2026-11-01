using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>
/// The site's other versions (the bare domain or www, over http or https) must redirect to the main one, the origin of
/// the home page. Each version is the start URL's path there, which the crawler probes at the start of every crawl. A
/// version that answers with a page, or redirects to another version that does, is a second copy of the site. One that
/// is dead, fails, is blocked or is disallowed isn't flagged: a dead www twin is normal.
/// </summary>
/// <param name="start">The start URL, normalised.</param>
/// <param name="findUrl">The id of a stored URL, or 0 when the crawl never found it.</param>
/// <param name="readUrl">The stored URL with an id, or null.</param>
internal sealed class SiteVersionRules(UrlTable urls, Uri start, Func<string, int> findUrl, Func<int, string?> readUrl) : ISiteRule
{
    private readonly List<int> _versionIds = [.. SiteVersions.Of(start).Select(findUrl)];
    private readonly HashSet<string> _versionOrigins = [.. SiteVersions.Of(start).Append(start.AbsoluteUri).Select(SiteVersions.OriginOf)];
    private readonly string? _home = readUrl(urls.HomeId);

    /// <summary>
    /// Whether the crawl knows how every version answers: the home is a page, and every version was fetched, as was the
    /// end of its redirects when that is on another version. A crawl stopped before the probes ran can't say, nor can a
    /// crawl file from before the crawler probed. (A URL the crawl never found has id 0, which is never fetched.)
    /// </summary>
    public bool CanCheck => urls[urls.HomeId].IsPage && _versionIds.All(IsAnswered);

    public IReadOnlyList<IssueDefinition> Checks { get; } = [IssueCatalog.DUPLICATE_SITE_VERSION];

    public void Observe(AnalyzedPage page)
    {
    }

    public IEnumerable<IssueHit> Finish()
    {
        if (!CanCheck)
        {
            yield break;
        }

        foreach (var id in _versionIds)
        {
            var trace = urls.TraceRedirects(id);
            var final = urls[trace.FinalId];
            if (final is not { Fetched: true, Outcome: FetchOutcome.Success, IsHtml: true } || readUrl(trace.FinalId) is not { } finalUrl ||
                !IsOtherVersion(finalUrl))
            {
                continue;
            }

            yield return trace.Hops == 0
                ? new IssueHit(IssueCatalog.DUPLICATE_SITE_VERSION, id, $"Answers HTTP {final.StatusCode} instead of redirecting to {_home}")
                : new IssueHit(IssueCatalog.DUPLICATE_SITE_VERSION, id,
                    $"Redirects to {finalUrl}, which answers HTTP {final.StatusCode} instead of redirecting to {_home}", trace.FinalId);
        }
    }

    private bool IsAnswered(int id)
    {
        if (!urls[id].Fetched)
        {
            return false;
        }

        var final = urls.TraceRedirects(id).FinalId;
        return urls[final].Fetched || readUrl(final) is not { } finalUrl || !IsOtherVersion(finalUrl);
    }

    /// <summary>On one of the site's versions, but not the home page's.</summary>
    private bool IsOtherVersion(string url)
    {
        var origin = SiteVersions.OriginOf(url);
        return _versionOrigins.Contains(origin) && origin != SiteVersions.OriginOf(_home!);
    }
}
