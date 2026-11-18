using System.Globalization;
using System.Text.RegularExpressions;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>
/// The page's canonical: whether it's declared properly, and whether the URL it names can be indexed. Only canonicals
/// in the head or an HTTP Link header count, as with Google.
/// </summary>
/// <param name="readUrl">The stored URL with an id, or null: a canonical that redirects is reported with the URL its redirects end at.</param>
/// <param name="readFacts">
/// A stored page's facts, or null: whether the page a later page's canonical and rel="prev" both name has a rel="prev" of
/// its own, which says whether it's the first page of the series. Without it, only a page number in the URL tells.
/// </param>
internal sealed partial class CanonicalRules(UrlTable urls, Func<int, string?> readUrl, Func<int, PageFacts?>? readFacts = null) : IPageRule
{
    /// <summary>Query parameters that number the pages of a series, as shops, blogs and CMSs write them.</summary>
    private static readonly string[] PAGE_PARAMETERS = ["page", "p", "pg", "paged", "pagenum"];

    /// <summary>The most characters of a canonical's fragment a detail quotes.</summary>
    private const int FRAGMENT_MAX = 60;

    public IEnumerable<IssueHit> Check(AnalyzedPage page)
    {
        var canonicals = page.Facts.Canonicals;
        var honoured = canonicals.Where(canonical => canonical.InHead || canonical.FromHttpHeader).ToList();
        if (honoured.Count == 0)
        {
            if (canonicals.Count > 0)
            {
                yield return new IssueHit(IssueCatalog.CANONICAL_OUTSIDE_HEAD, page.UrlId, RuleText.HeadEnded(page.Facts.HeadEnd));
            }
            else if (page.IsIndexable)
            {
                yield return new IssueHit(IssueCatalog.CANONICAL_MISSING, page.UrlId);
            }

            yield break;
        }

        if (honoured.FirstOrDefault(canonical => canonical.Url is null) is { } invalid)
        {
            yield return new IssueHit(IssueCatalog.CANONICAL_INVALID, page.UrlId, invalid.Href);
        }

        var distinct = honoured.Where(canonical => canonical.Url is not null).DistinctBy(canonical => canonical.Url, StringComparer.Ordinal).ToList();
        if (distinct.Count > 1)
        {
            // Each URL with where it's declared, so the wrong one can be turned off: a tag the template writes, or a header the server sends.
            yield return new IssueHit(IssueCatalog.CANONICAL_MULTIPLE, page.UrlId, RuleText.Fit(
                $"{RuleText.Count(distinct.Count, "URL", "URLs")}: {string.Join(" · ", distinct.Select(canonical => $"{canonical.Url} ({(canonical.FromHttpHeader ? "HTTP header" : "head")})"))}"));
        }

        if (honoured.FirstOrDefault(canonical => canonical is { FromHttpHeader: false, Url: not null } && !IsAbsolute(canonical.Href)) is { } relative)
        {
            yield return new IssueHit(IssueCatalog.CANONICAL_RELATIVE, page.UrlId, relative.Href);
        }

        // Google drops the fragment, so the canonical still works; the markup is wrong all the same.
        if (honoured.Select(canonical => Fragment(canonical.Href)).FirstOrDefault(fragment => fragment is not null) is { } fragment)
        {
            yield return new IssueHit(IssueCatalog.CANONICAL_FRAGMENT, page.UrlId, RuleText.Cut(fragment, FRAGMENT_MAX));
        }

        if (honoured.FirstOrDefault(canonical => canonical is { FromHttpHeader: false, Attributes: not null }) is { Attributes: { } attributes })
        {
            yield return new IssueHit(IssueCatalog.CANONICAL_INVALID_ATTRIBUTE, page.UrlId, attributes);
        }

        // The first honoured canonical decides, as it does for indexability.
        if (honoured.FirstOrDefault(canonical => canonical.Url is not null) is not { Url: { } canonicalUrl } || canonicalUrl == page.Url)
        {
            yield break;
        }

        var targetId = page.Links.FirstOrDefault(link => link.Kind == LinkKind.Canonical && !link.Flags.HasFlag(LinkFlags.OutsideHead)).ToId;
        var related = targetId > 0 ? targetId : (int?)null;
        if (page.IndexabilityReason == IndexabilityReason.Canonicalised)
        {
            // A canonical to another site, or from a later page of a series to its first, is a fault of its own, which says
            // more than that the page is canonicalised.
            if (targetId > 0 && !urls[targetId].IsInternal && Uri.TryCreate(canonicalUrl, UriKind.Absolute, out var elsewhere))
            {
                yield return new IssueHit(IssueCatalog.CANONICAL_OTHER_DOMAIN, page.UrlId, $"On another host: {elsewhere.Authority}", related);
            }
            else if (LaterPageNamingTheFirst(page, canonicalUrl, targetId) is { } laterPage)
            {
                yield return new IssueHit(IssueCatalog.CANONICAL_TO_FIRST_PAGE, page.UrlId, laterPage, related);
            }
            else
            {
                yield return new IssueHit(IssueCatalog.CANONICALISED_PAGE, page.UrlId, null, related);
            }

            if (urls.IsOnCanonicalLoop(page.UrlId))
            {
                yield return new IssueHit(IssueCatalog.CANONICAL_LOOP, page.UrlId, LoopText(page), related);
            }
        }

        // The noindex wins, so the page isn't canonicalised, but the two signals contradict each other. Not when the
        // canonical leads back to this page through redirects (it's a self-reference), or leads nowhere and is ignored.
        var pointsElsewhere = targetId == 0 || (urls.TraceRedirects(targetId).FinalId != page.UrlId && !urls.IsBrokenThroughRedirects(targetId));
        if (page.Facts.Robots.NoIndex && pointsElsewhere)
        {
            yield return new IssueHit(IssueCatalog.NOINDEX_WITH_CANONICAL, page.UrlId, null, targetId > 0 ? targetId : null);
        }

        if (targetId == 0 || urls[targetId] is not { Fetched: true } target)
        {
            yield break;
        }

        // A canonical that leads nowhere, even through redirects, is ignored, which leaves the page indexable.
        if (urls.IsBrokenThroughRedirects(targetId))
        {
            yield return new IssueHit(IssueCatalog.CANONICAL_BROKEN, page.UrlId, $"The canonical URL {RuleText.BrokenStatus(urls, targetId)}", targetId);
        }
        else if (target.Outcome == FetchOutcome.Redirect)
        {
            // /page/ naming /page, which redirects to /page/, still points search engines at /page/.
            var trace = urls.TraceRedirects(targetId);
            var detail = trace.FinalId == page.UrlId
                ? "The canonical URL redirects back to this page"
                : $"The canonical URL {RuleText.Status(target)}{EndsAt(trace)}";
            yield return new IssueHit(IssueCatalog.CANONICAL_TO_REDIRECT, page.UrlId, detail, targetId);
        }
        // Another site disallowing the crawler says nothing about whether Google can see the page. Nor does any site
        // refusing it (a firewall, a bot challenge, a rate limit): the URL is unverified, as the sitemap checks treat it,
        // and internal-blocked reports it.
        // A canonical loop's pages each name one that's canonicalised in turn: the loop says it better.
        else if (((target.IsInternal && target.Outcome == FetchOutcome.RobotsDisallowed) || target is { IsPage: true, IsIndexablePage: false }) &&
                 !urls.IsOnCanonicalLoop(page.UrlId))
        {
            yield return new IssueHit(IssueCatalog.CANONICAL_TO_NON_INDEXABLE, page.UrlId, $"The canonical URL {RuleText.Status(target)}", targetId);
        }
    }

    /// <summary>
    /// Where the canonical's redirects end, which is the URL the canonical should name: " to https://example.com/page" after
    /// one redirect, " and, after 3 redirects, ends at https://example.com/page" after more. Only a URL that answered is
    /// known to be the end: one the crawl never asked for, or was refused at, may redirect again, so it is where the
    /// redirects lead, and the detail says why no more is known: ", which wasn't checked", ", which is blocked by robots.txt".
    /// </summary>
    private string EndsAt(RedirectTrace trace)
    {
        if (readUrl(trace.FinalId) is not { } final)
        {
            return string.Empty;
        }

        var last = urls[trace.FinalId];
        var unknown = last switch
        {
            { Fetched: false } => ", which wasn't checked",
            { Outcome: FetchOutcome.Success } => string.Empty,
            _ => $", which {RuleText.Status(last)}"
        };

        return trace.Hops == 1
            ? $" to {final}{unknown}"
            : $" and, after {RuleText.Count(trace.Hops, "redirect", "redirects")}, {(unknown.Length == 0 ? "ends at" : "leads to")} {final}{unknown}";
    }

    /// <summary>
    /// "Page 2 names page 1 as its canonical", when the page is a later page of a series whose first page its canonical
    /// names: its URL numbers it 2 or more (<c>?page=2</c>, <c>/page/2/</c>) and the canonical is that URL with the number
    /// taken out or set to 1; or the canonical is where its rel="prev" leads, a page with no rel="prev" of its own. Null
    /// otherwise.
    /// </summary>
    private string? LaterPageNamingTheFirst(AnalyzedPage page, string canonicalUrl, int targetId)
    {
        if (PageNumber(page.Uri) is { } numbered && numbered.FirstPages.Contains(canonicalUrl))
        {
            return string.Create(CultureInfo.InvariantCulture, $"Page {numbered.Number:N0} names page 1 as its canonical");
        }

        var previous = page.Facts.Pagination.FirstOrDefault(link => link is { IsNext: false, Url: not null })?.Url;
        return previous == canonicalUrl && targetId > 0 && readFacts?.Invoke(targetId) is { } first && !first.Pagination.Any(link => !link.IsNext)
            ? "A later page names page 1 as its canonical"
            : null;
    }

    /// <summary>
    /// The page's number in its series, as its URL writes it (a query parameter such as <c>page=3</c>, or a path ending
    /// <c>/page/3</c> or <c>/page-3</c>), when it's 2 or more, with the URLs its first page can have: the number taken out,
    /// or set to 1. Null for a URL that numbers no later page.
    /// </summary>
    internal static (int Number, HashSet<string> FirstPages)? PageNumber(Uri uri)
    {
        HashSet<string> firsts = new(StringComparer.Ordinal);
        var query = uri.Query.TrimStart('?');
        var parameters = query.Length == 0 ? [] : query.Split('&');
        for (var index = 0; index < parameters.Length; index++)
        {
            var (name, value) = parameters[index].Split('=', 2) is [var key, var given] ? (key, given) : (parameters[index], string.Empty);
            if (!PAGE_PARAMETERS.Contains(Uri.UnescapeDataString(name), StringComparer.OrdinalIgnoreCase) ||
                !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number < 2)
            {
                continue;
            }

            var others = parameters.Where((_, other) => other != index).ToList();
            AddFirst(firsts, uri, uri.AbsolutePath, string.Join('&', others));
            AddFirst(firsts, uri, uri.AbsolutePath, string.Join('&', parameters.Select((parameter, other) => other == index ? $"{name}=1" : parameter)));
            return (number, firsts);
        }

        if (PagePath().Match(uri.AbsolutePath) is { Success: true } match &&
            int.TryParse(match.Groups["number"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var pathNumber) && pathNumber >= 2)
        {
            var before = uri.AbsolutePath[..match.Index];
            AddFirst(firsts, uri, before.Length == 0 ? "/" : before, query);
            AddFirst(firsts, uri, before + "/", query);
            AddFirst(firsts, uri, before + match.Value.Replace(match.Groups["number"].Value, "1", StringComparison.Ordinal), query);
            return (pathNumber, firsts);
        }

        return null;
    }

    private static void AddFirst(HashSet<string> firsts, Uri uri, string path, string query)
    {
        var builder = new UriBuilder(uri) { Path = path, Query = query, Fragment = string.Empty };
        firsts.Add(UrlNormalizer.Normalize(builder.Uri));
    }

    /// <summary>"A loop of 2: /a/ → /b/ → back to /a/": the loop's pages from this one, each as it reads beside this page's URL.</summary>
    private string LoopText(AnalyzedPage page)
    {
        var loop = urls.CanonicalLoop(page.UrlId);
        var steps = loop.Select(id => readUrl(id) is { } url ? RuleText.Near(url, page.Url) : "?");
        return RuleText.Fit($"A loop of {loop.Count}: {string.Join(" → ", steps)} → back to {RuleText.Near(page.Url, page.Url)}");
    }

    /// <summary>The fragment of a canonical's href as written (<c>#reviews</c>), or null without one, or with only a bare #.</summary>
    private static string? Fragment(string href)
    {
        var written = href.Trim();
        var hash = written.IndexOf('#');
        return hash >= 0 && hash < written.Length - 1 ? written[hash..] : null;
    }

    [GeneratedRegex(@"/page[/-](?<number>[0-9]+)/?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PagePath();

    private static bool IsAbsolute(string href) =>
        href.TrimStart().StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        href.TrimStart().StartsWith("https://", StringComparison.OrdinalIgnoreCase);
}
