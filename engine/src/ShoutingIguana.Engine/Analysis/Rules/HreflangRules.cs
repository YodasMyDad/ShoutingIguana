using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing.Models;

namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>
/// Hreflang annotations on indexable pages: valid codes, a self-reference, one URL per code, x-default and working
/// targets on each page, then return links across pages. Only annotations in the head or an HTTP Link header count,
/// as with Google, and only on indexable pages: Google reads hreflang on canonical pages, so a canonicalised variant
/// that repeats its canonical's set is doing the recommended thing.
/// </summary>
internal sealed class HreflangRules(UrlTable urls, Func<int, string?>? readUrl = null) : IPageRule, ISiteRule
{
    /// <summary>Every declared alternate as (page &lt;&lt; 32 | alternate): 8 bytes each, sorted for the return-link lookups.</summary>
    private readonly List<long> _alternates = [];

    // Batch 8: the code each page names each alternate under, the codes each page names itself under, and every page
    // named as an alternate.
    private readonly Dictionary<long, string> _codes = [];
    private readonly Dictionary<int, List<string>> _selfCodes = [];
    private readonly HashSet<int> _named = [];

    public IEnumerable<IssueHit> Check(AnalyzedPage page)
    {
        var all = page.Facts.Hreflangs;
        if (!page.IsIndexable || all.Count == 0)
        {
            yield break;
        }

        var honoured = all.Where(hreflang => hreflang.InHead || hreflang.FromHttpHeader).ToList();
        if (honoured.Count < all.Count)
        {
            var outside = RuleText.Count(all.Count - honoured.Count, "entry", "entries") + " in the body";
            yield return new IssueHit(IssueCatalog.HREFLANG_OUTSIDE_HEAD, page.UrlId,
                RuleText.HeadEnded(page.Facts.HeadEnd) is { } ended ? RuleText.Fit($"{outside}: {char.ToLowerInvariant(ended[0])}{ended[1..]}") : outside);
        }

        if (honoured.Count == 0)
        {
            yield break;
        }

        var invalid = honoured.Select(hreflang => hreflang.Language).Where(code => !HreflangCodes.IsValid(code))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (invalid.Count > 0)
        {
            yield return new IssueHit(IssueCatalog.HREFLANG_INVALID_CODE, page.UrlId, string.Join(", ", invalid));
        }

        if (!honoured.Any(hreflang => hreflang.Url == page.Url))
        {
            yield return new IssueHit(IssueCatalog.HREFLANG_MISSING_SELF, page.UrlId, MissingSelf(page.Url, honoured));
        }

        var conflicting = honoured.Where(hreflang => hreflang.Url is not null)
            .GroupBy(hreflang => hreflang.Language, StringComparer.OrdinalIgnoreCase)
            .Select(group => (Code: group.Key, Urls: group.Select(hreflang => hreflang.Url!).Distinct(StringComparer.Ordinal).ToList()))
            .Where(group => group.Urls.Count > 1)
            .ToList();
        if (conflicting.Count > 0)
        {
            // Each code with the URLs it names, the codes first: grouped by them alone (IssueFaults.HreflangCodes).
            yield return new IssueHit(IssueCatalog.HREFLANG_DUPLICATE_CODE, page.UrlId, RuleText.Fit(string.Join("; ",
                conflicting.Select(group => $"{group.Code}: {string.Join(" · ", group.Urls.Select(url => RuleText.Near(url, page.Url)))}"))));
        }

        if (!honoured.Any(hreflang => hreflang.Language.Equals("x-default", StringComparison.OrdinalIgnoreCase)))
        {
            yield return new IssueHit(IssueCatalog.HREFLANG_MISSING_X_DEFAULT, page.UrlId);
        }

        if (HeaderMismatch(honoured) is { } mismatch)
        {
            yield return new IssueHit(IssueCatalog.HREFLANG_HEADER_MISMATCH, page.UrlId, mismatch);
        }

        // Its own code, against <html lang>, both written well: Google reads neither for the language, so it's a sign one is wrong.
        var selfCode = honoured.FirstOrDefault(hreflang => hreflang.Url == page.Url && !hreflang.Language.Equals("x-default", StringComparison.OrdinalIgnoreCase))?.Language;
        var lang = page.Facts.Language?.Trim();
        if (selfCode is not null && lang is not null && HreflangCodes.IsValid(selfCode) && HtmlRules.IsLanguageTag(lang) &&
            !string.Equals(selfCode.Split('-')[0], lang.Split('-')[0], StringComparison.OrdinalIgnoreCase))
        {
            yield return new IssueHit(IssueCatalog.HREFLANG_LANG_MISMATCH, page.UrlId, $"hreflang {selfCode}, lang {lang}");
        }

        if (honoured.All(hreflang => hreflang.Url == page.Url))
        {
            yield return new IssueHit(IssueCatalog.HREFLANG_SELF_ONLY, page.UrlId);
        }

        TargetSet broken = new(), nonIndexable = new();
        foreach (var target in HonouredTargets(page))
        {
            var status = urls[target];
            if (!status.Fetched)
            {
                continue;
            }

            if (status.IsBroken)
            {
                broken.Add(target);
            }
            // As with canonicals: another site's robots.txt disallowing the crawler says nothing about whether Google can
            // see the page, and a site refusing the crawler (a firewall, a rate limit) leaves the URL unverified.
            else if (status.Outcome == FetchOutcome.Redirect || (status.Outcome == FetchOutcome.RobotsDisallowed && status.IsInternal) ||
                     status is { IsPage: true, IsIndexablePage: false })
            {
                nonIndexable.Add(target);
            }
        }

        if (broken.Count > 0)
        {
            yield return new IssueHit(IssueCatalog.HREFLANG_BROKEN_TARGET, page.UrlId, RuleText.Count(broken.Count, "URL", "URLs"), broken.First) { Targets = broken.Ids };
        }

        if (nonIndexable.Count > 0)
        {
            yield return new IssueHit(IssueCatalog.HREFLANG_TO_NON_INDEXABLE, page.UrlId,
                RuleText.Count(nonIndexable.Count, "URL", "URLs"), nonIndexable.First) { Targets = nonIndexable.Ids };
        }
    }

    /// <summary>Of its checks, the one across pages: the rest it reports page by page, from <see cref="Check"/>.</summary>
    public IReadOnlyList<IssueDefinition> Checks { get; } = [IssueCatalog.HREFLANG_MISSING_RETURN, IssueCatalog.HREFLANG_INCONSISTENT_CODE, IssueCatalog.HREFLANG_UNLINKED];

    public void Observe(AnalyzedPage page)
    {
        if (!page.IsIndexable)
        {
            return;
        }

        foreach (var target in HonouredTargets(page).Distinct())
        {
            _alternates.Add(Pair(page.UrlId, target));
            _named.Add(target);
        }

        foreach (var link in page.Links.Where(link => link.Kind == LinkKind.Hreflang && !link.Flags.HasFlag(LinkFlags.OutsideHead) && link.Text is not null))
        {
            if (link.ToId == page.UrlId)
            {
                (_selfCodes.TryGetValue(page.UrlId, out var codes) ? codes : _selfCodes[page.UrlId] = []).Add(link.Text!);
            }
            else
            {
                _codes.TryAdd(Pair(page.UrlId, link.ToId), link.Text!);
            }
        }
    }

    public IEnumerable<IssueHit> Finish()
    {
        _alternates.Sort();
        for (var start = 0; start < _alternates.Count;)
        {
            var pageId = (int)(_alternates[start] >> 32);
            TargetSet missing = new();
            var end = start;
            for (; end < _alternates.Count && (int)(_alternates[end] >> 32) == pageId; end++)
            {
                // Broken and non-indexable alternates are reported on their own; other sites' pages aren't parsed.
                var target = (int)_alternates[end];
                if (urls[target] is { IsInternal: true, IsIndexablePage: true } && _alternates.BinarySearch(Pair(target, pageId)) < 0)
                {
                    missing.Add(target);
                }
            }

            if (missing.Count > 0)
            {
                yield return new IssueHit(IssueCatalog.HREFLANG_MISSING_RETURN, pageId,
                    $"{RuleText.Count(missing.Count, "alternate doesn't", "alternates don't")} link back", missing.First) { Targets = missing.Ids };
            }

            if (Inconsistent(pageId, start, end) is { } inconsistent)
            {
                yield return inconsistent;
            }

            start = end;
        }

        // A version no <a href> from a page the analysis read leads to, and no sitemap entry lists. Something else may reach
        // it (a redirect, a canonical, a refresh), so the detail says what isn't there, not what is.
        foreach (var named in _named.Order())
        {
            if (urls[named] is { IsInternal: true, IsIndexablePage: true, InSitemap: false } && urls.Inlinks(named) == 0)
            {
                yield return new IssueHit(IssueCatalog.HREFLANG_UNLINKED, named, "No link or sitemap entry leads here");
            }
        }
    }

    /// <summary>
    /// The page's alternates that link back, but under another code than the one the page names them by: "Names /fr/ as
    /// "fr"; /fr/ calls itself "fr-ca"". A return link that's missing is another check's.
    /// </summary>
    private IssueHit? Inconsistent(int pageId, int start, int end)
    {
        TargetSet differing = new();
        string? first = null;
        for (var index = start; index < end; index++)
        {
            var target = (int)_alternates[index];
            if (!_codes.TryGetValue(Pair(pageId, target), out var named) || !_selfCodes.TryGetValue(target, out var own) ||
                _alternates.BinarySearch(Pair(target, pageId)) < 0 || own.Contains(named, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            differing.Add(target);
            if (first is null && readUrl is not null && readUrl(target) is { } targetUrl && readUrl(pageId) is { } pageUrl)
            {
                var near = RuleText.Near(targetUrl, pageUrl);
                first = RuleText.Fit($"Names {near} as \"{named}\"; {near} calls itself \"{own[0]}\"");
            }
        }

        return differing.Count == 0
            ? null
            : new IssueHit(IssueCatalog.HREFLANG_INCONSISTENT_CODE, pageId,
                first ?? RuleText.Count(differing.Count, "alternate", "alternates"), differing.First) { Targets = differing.Ids };
    }

    /// <summary>
    /// The page's hreflang in its HTML and in its Link header, when it has both and they name different sets: "The HTML
    /// names 3 versions, the header 2; only in the HTML: de".
    /// </summary>
    private static string? HeaderMismatch(IReadOnlyList<HreflangFact> honoured)
    {
        static string Key(HreflangFact hreflang) => $"{hreflang.Language.ToLowerInvariant()} {hreflang.Url}";
        var html = honoured.Where(hreflang => !hreflang.FromHttpHeader).ToList();
        var header = honoured.Where(hreflang => hreflang.FromHttpHeader).ToList();
        if (html.Count == 0 || header.Count == 0)
        {
            return null;
        }

        var htmlKeys = html.Select(Key).ToHashSet(StringComparer.Ordinal);
        var headerKeys = header.Select(Key).ToHashSet(StringComparer.Ordinal);
        if (htmlKeys.SetEquals(headerKeys))
        {
            return null;
        }

        List<string> parts = [$"The HTML names {RuleText.Count(htmlKeys.Count, "version", "versions")}, the header {headerKeys.Count:N0}"];
        var onlyHtml = html.Where(hreflang => !headerKeys.Contains(Key(hreflang))).Select(hreflang => hreflang.Language).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var onlyHeader = header.Where(hreflang => !htmlKeys.Contains(Key(hreflang))).Select(hreflang => hreflang.Language).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (onlyHtml.Count > 0)
        {
            parts.Add($"only in the HTML: {string.Join(", ", onlyHtml)}");
        }

        if (onlyHeader.Count > 0)
        {
            parts.Add($"only in the header: {string.Join(", ", onlyHeader)}");
        }

        return RuleText.Fit(string.Join("; ", parts));
    }

    /// <summary>
    /// How many entries the page lists, none of them it, and the one that nearly is (another scheme, www or not, a trailing
    /// slash, letters' case), which is the usual slip: "None of its 4 entries names this URL; "en" names http://www.x.com/en".
    /// </summary>
    private static string MissingSelf(string pageUrl, IReadOnlyList<HreflangFact> honoured)
    {
        var none = honoured.Count == 1 ? "Its one entry doesn't name this URL" : $"None of its {RuleText.Count(honoured.Count, "entry", "entries")} names this URL";
        var self = Loosely(pageUrl);
        return honoured.FirstOrDefault(hreflang => hreflang.Url is { } url && Loosely(url) == self) is { } near
            ? RuleText.Fit($"{none}; \"{near.Language}\" names {near.Url}")
            : none;
    }

    /// <summary>A URL without its scheme, a leading www., a trailing slash or letters' case.</summary>
    private static string Loosely(string url)
    {
        var rest = url.ToLowerInvariant();
        rest = rest[(rest.IndexOf("://", StringComparison.Ordinal) is var scheme and >= 0 ? scheme + 3 : 0)..];
        rest = rest.StartsWith("www.", StringComparison.Ordinal) ? rest[4..] : rest;
        return rest.TrimEnd('/');
    }

    private static long Pair(int page, int target) => ((long)page << 32) | (uint)target;

    private static IEnumerable<int> HonouredTargets(AnalyzedPage page) => page.Links
        .Where(link => link.Kind == LinkKind.Hreflang && !link.Flags.HasFlag(LinkFlags.OutsideHead) && link.ToId != page.UrlId)
        .Select(link => link.ToId);
}
