using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Fetching;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>
/// What the site's redirects say in their headers besides where they go: a canonical that names another URL than the
/// one they end at. A redirect has no page facts, so its stored headers are read once every page is known.
/// </summary>
/// <param name="readRedirectHeaders">The headers of each of the site's own URLs that redirected (<c>AnalysisReader.ReadRedirectHeaders</c>).</param>
/// <param name="readUrl">The stored URL with an id, or null.</param>
internal sealed class RedirectHeaderRules(
    UrlTable urls,
    Func<IEnumerable<(int UrlId, IReadOnlyList<ResponseHeader> Headers)>> readRedirectHeaders,
    Func<int, string?> readUrl) : ISiteRule
{
    public IReadOnlyList<IssueDefinition> Checks { get; } = [IssueCatalog.CANONICAL_ON_REDIRECT];

    public void Observe(AnalyzedPage page)
    {
    }

    public IEnumerable<IssueHit> Finish()
    {
        foreach (var (id, headers) in readRedirectHeaders())
        {
            if (CanonicalOnRedirect(id, headers) is { } hit)
            {
                yield return hit;
            }
        }
    }

    /// <summary>
    /// A redirect whose <c>Link</c> header names a canonical other than where its redirects end: search engines follow
    /// the redirect, so the header only misleads. One whose canonical is where it ends agrees with it, and one that ends
    /// nowhere (no usable Location) has nothing to compare with.
    /// </summary>
    private IssueHit? CanonicalOnRedirect(int id, IReadOnlyList<ResponseHeader> headers)
    {
        var links = headers.Where(header => header.Name == "link").Select(header => header.Value).ToList();
        if (links.Count == 0 || readUrl(id) is not { } url)
        {
            return null;
        }

        var canonical = LinkHeaderParser.Parse(links).FirstOrDefault(entry => entry.Rel.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("canonical"));
        var finalId = urls.TraceRedirects(id).FinalId;
        if (canonical is null || finalId == id || readUrl(finalId) is not { } final ||
            !UrlNormalizer.TryResolve(new Uri(url), canonical.Url, out var named))
        {
            return null;
        }

        var namedUrl = UrlNormalizer.Normalize(named);
        return namedUrl == final
            ? null
            : new IssueHit(IssueCatalog.CANONICAL_ON_REDIRECT, id, RuleText.Fit($"Redirects to {final}; its canonical header names {namedUrl}"));
    }
}
