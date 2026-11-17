using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.TestSite;

/// <summary>
/// The site's revisions, for the fix loop: revision 0 is the site as it has always been, and revision 1 the same site
/// "after the fixes" (<see cref="SyntheticSiteOptions.Revision"/>). Revision 1 mends some planted problems, leaves the
/// rest, and plants a few new ones, each on its own URLs, so a comparison of a crawl of each finds every state:
/// <list type="bullet">
/// <item>of the products' broken links, a quarter of the pages are restored, a quarter of the links dropped, and a
/// quarter moved to another URL that doesn't exist either (fixed at the target and at the page, no longer linked, and a
/// new URL behind a link that is still broken);</item>
/// <item>of the products whose title copies the one before, a third get their own, and a third keep it while the page
/// before is renamed and the page after takes the old title (a duplicate cluster matched by the pages it keeps, not its
/// name);</item>
/// <item>the long-title plant gets a normal title, the capitals one answers 500 (not checked, and a new server error),
/// the short-title plant is removed and unlinked (no longer found), and a new plant with a long title appears;</item>
/// <item>the second H1 moves from its plant to another page, the noindex of the robots plant goes, the large image is
/// replaced at its address, a chain's first hop goes straight to its end, an hreflang alternate links back, robots.txt
/// allows one private URL, the sitemap index drops its missing file, and the first category is listed;</item>
/// <item>the home page loses a security header (a check that found nothing finds something), and the HTTPS site's
/// plain-HTTP side answers robots.txt and redirects its home page.</item>
/// </list>
/// <see cref="GetChanges"/> gives the exact comparison.
/// </summary>
public sealed partial class SyntheticSite
{
    /// <summary>The newest revision a site can serve.</summary>
    public const int LATEST_REVISION = 1;

    /// <summary>The private URL revision 1's robots.txt allows.</summary>
    private const string UNBLOCKED_PRIVATE = "/private/1";

    /// <summary>What revision 1 adds to a broken link it moves to another URL that doesn't exist either.</summary>
    private const string RELINKED = "-relinked";

    /// <summary>Products link /private/{product % 10} every this many products.</summary>
    private const int PRIVATE_LINK_EVERY = 61;

    private static readonly Dictionary<string, string> HOME_HEADERS_AFTER = SECURITY_HEADERS
        .Where(header => header.Key != "Referrer-Policy")
        .ToDictionary(header => header.Key, header => header.Value, StringComparer.OrdinalIgnoreCase);

    private enum BrokenLinkFate
    {
        None,
        Broken,
        Restored,
        Dropped,
        Relinked
    }

    public int Revision => _options.Revision;

    /// <summary>The same site, at another revision.</summary>
    public SyntheticSite AtRevision(int revision) =>
        revision == Revision ? this : new SyntheticSite(_options with { Revision = revision }, new Uri(_base), new Uri(_external));

    /// <summary>What a correct crawl and analysis of the site at <paramref name="revision"/> must find.</summary>
    public SyntheticSiteExpectations GetExpectations(int revision) => AtRevision(revision).GetExpectations();

    /// <summary>
    /// What a correct comparison of a crawl of revision <paramref name="to"/> with a crawl of revision
    /// <paramref name="from"/> must find, both complete crawls with the same options.
    /// </summary>
    /// <param name="judging">What each check needs of a URL to judge it: the catalog's, which this project doesn't reference.</param>
    public SyntheticSiteChanges GetChanges(int from, int to, Func<string, SyntheticJudging> judging)
    {
        var before = AtRevision(from);
        var after = AtRevision(to);
        var then = before.GetExpectations();
        var now = after.GetExpectations();
        var ids = then.IssueUrls.Keys.Concat(now.IssueUrls.Keys).Concat(then.SkippedChecks).Concat(now.SkippedChecks)
            .Where(id => id != SyntheticSiteExpectations.SLOW_RESPONSE)
            .ToHashSet(StringComparer.Ordinal);

        Dictionary<string, SyntheticCheckChange> checks = new(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            var ranThen = !then.SkippedChecks.Contains(id);
            var ranNow = !now.SkippedChecks.Contains(id);
            var flaggedThen = ranThen ? then.IssueUrls.GetValueOrDefault(id) ?? new HashSet<string>() : new HashSet<string>();
            var flaggedNow = ranNow ? now.IssueUrls.GetValueOrDefault(id) ?? new HashSet<string>() : new HashSet<string>();
            var judges = judging(id);

            Dictionary<string, SyntheticRowState> rows = new(StringComparer.Ordinal);
            foreach (var url in flaggedNow)
            {
                rows[url] = flaggedThen.Contains(url) ? SyntheticRowState.Still
                    : !ranThen ? SyntheticRowState.NotComparedThen
                    : before.Fate(url) switch
                    {
                        SyntheticUrlFate.NotFound => SyntheticRowState.NewUrl,
                        var fate when Judges(judges, fate) => SyntheticRowState.New,
                        _ => SyntheticRowState.NotComparedThen
                    };
            }

            foreach (var url in flaggedThen.Where(url => !flaggedNow.Contains(url)))
            {
                rows[url] = !ranNow ? SyntheticRowState.NotChecked
                    : after.Fate(url) switch
                    {
                        SyntheticUrlFate.NotFound => SyntheticRowState.Gone,
                        var fate when Judges(judges, fate) => SyntheticRowState.Fixed,
                        _ => SyntheticRowState.NotChecked
                    };
            }

            int? countThen = ranThen ? flaggedThen.Count : null;
            int? countNow = ranNow ? flaggedNow.Count : null;
            var state = (countThen, countNow) switch
            {
                (null, _) => SyntheticCheckState.NewCheck,
                (_, null) => SyntheticCheckState.NotCheckedNow,
                (0, > 0) => SyntheticCheckState.New,
                (> 0, 0) => rows.Values.Any(row => row == SyntheticRowState.NotChecked) ? SyntheticCheckState.Fewer : SyntheticCheckState.Fixed,
                var (was, @is) when @is < was => SyntheticCheckState.Fewer,
                var (was, @is) when @is > was => SyntheticCheckState.More,
                _ => SyntheticCheckState.Same
            };
            checks[id] = new SyntheticCheckChange(id, state, countThen, countNow, rows);
        }

        return new SyntheticSiteChanges(checks);
    }

    /// <summary>
    /// What a correct crawl of this revision makes of <paramref name="url"/>: for the URLs a revision changes, and any a
    /// check flags. Throws for a URL it can't tell, so a gap in what it knows fails a test rather than passing it.
    /// </summary>
    public SyntheticUrlFate Fate(string url)
    {
        if (_plantsByUrl.TryGetValue(url, out var plant))
        {
            return FateOf(plant);
        }

        // A plant of another revision isn't this one's: nothing leads to it.
        if (Enumerable.Range(0, LATEST_REVISION + 1).Where(revision => revision != Revision).Any(revision => AtRevision(revision)._plantsByUrl.ContainsKey(url)))
        {
            return SyntheticUrlFate.NotFound;
        }

        if (url.StartsWith(_external + "/", StringComparison.Ordinal))
        {
            return SyntheticUrlFate.Answered;
        }

        var plainHttpBase = IsHttps ? "http" + _base["https".Length..] : null;
        var plainHttp = plainHttpBase is not null && url.StartsWith(plainHttpBase + "/", StringComparison.Ordinal);
        if (!plainHttp && !url.StartsWith(_base + "/", StringComparison.Ordinal))
        {
            throw new ArgumentException($"The synthetic site can't tell what a crawl makes of {url}.", nameof(url));
        }

        var path = url[(plainHttp ? plainHttpBase!.Length : _base.Length)..];
        if (path.StartsWith("/broken/", StringComparison.Ordinal))
        {
            var product = path["/broken/".Length..].Replace(RELINKED, string.Empty, StringComparison.Ordinal);
            var linked = int.TryParse(product, out var number) && number is > 0 && number < PageCount &&
                         ContentLinks(number).Any(link => link.Href == path);
            return linked ? FateOf(Handle(path, external: false, plainHttp)) : SyntheticUrlFate.NotFound;
        }

        if (TryNumber(path, "/private/", string.Empty, out var area))
        {
            return !IsPrivateLinked(area) ? SyntheticUrlFate.NotFound
                : path == UnblockedPrivate ? SyntheticUrlFate.Page
                : SyntheticUrlFate.NotRead;
        }

        return FateOf(Handle(path, external: false, plainHttp));
    }

    /// <summary>
    /// What fetching <paramref name="url"/> itself gives at this revision, whether or not anything leads to it: what a
    /// recheck, which asks for URLs the crawl had, finds. A page this revision removed answers 404; robots.txt's refusals
    /// stand. Throws for a URL it can't tell.
    /// </summary>
    public SyntheticUrlFate FetchFate(string url)
    {
        if (_plantsByUrl.TryGetValue(url, out var plant))
        {
            return FateOf(plant);
        }

        if (url.StartsWith(_external + "/", StringComparison.Ordinal))
        {
            return SyntheticUrlFate.Answered;
        }

        var plainHttpBase = IsHttps ? "http" + _base["https".Length..] : null;
        var plainHttp = plainHttpBase is not null && url.StartsWith(plainHttpBase + "/", StringComparison.Ordinal);
        if (!plainHttp && !url.StartsWith(_base + "/", StringComparison.Ordinal))
        {
            throw new ArgumentException($"The synthetic site can't tell what fetching {url} gives.", nameof(url));
        }

        var path = url[(plainHttp ? plainHttpBase!.Length : _base.Length)..];
        if (TryNumber(path, "/private/", string.Empty, out _))
        {
            return path == UNBLOCKED_PRIVATE && Revision >= 1 ? SyntheticUrlFate.Page : SyntheticUrlFate.NotRead;
        }

        return FateOf(Handle(path, external: false, plainHttp));
    }

    private static bool Judges(SyntheticJudging judging, SyntheticUrlFate fate) => judging switch
    {
        SyntheticJudging.Responses => fate is SyntheticUrlFate.Page or SyntheticUrlFate.Answered or SyntheticUrlFate.Failed,
        SyntheticJudging.Urls => fate is SyntheticUrlFate.Page or SyntheticUrlFate.Answered,
        _ => fate is SyntheticUrlFate.Page
    };

    private static SyntheticUrlFate FateOf(Plant plant) => plant switch
    {
        { Status: Plant.REFUSED or Plant.NOT_FOLLOWED } => SyntheticUrlFate.NotRead,
        { Status: 200, IsPage: true, IsAnalysed: true } => SyntheticUrlFate.Page,
        { Status: 200 } => SyntheticUrlFate.Answered,
        _ => FateOf(plant.Respond())
    };

    private static SyntheticUrlFate FateOf(SyntheticResponse response) => response switch
    {
        { Abort: true } => SyntheticUrlFate.Failed,
        { StatusCode: 403, Headers: { } headers } when headers.ContainsKey("cf-mitigated") => SyntheticUrlFate.Failed,
        { StatusCode: 200 } when response.ContentType.StartsWith("text/html", StringComparison.Ordinal) => SyntheticUrlFate.Page,
        _ => SyntheticUrlFate.Answered
    };

    private BrokenLinkFate FateOfBrokenLink(int product) =>
        product % 29 != 0 || product < 1 || product >= PageCount ? BrokenLinkFate.None
        : Revision == 0 ? BrokenLinkFate.Broken
        : (product / 29 % 4) switch
        {
            1 => BrokenLinkFate.Restored,
            2 => BrokenLinkFate.Dropped,
            3 => BrokenLinkFate.Relinked,
            _ => BrokenLinkFate.Broken
        };

    private bool IsRestored(string href) =>
        TryNumber(href, "/broken/", string.Empty, out var product) && FateOfBrokenLink(product) == BrokenLinkFate.Restored;

    /// <summary>Whether a product links /private/{area}: every 61st does, to the area its last digit names.</summary>
    private bool IsPrivateLinked(int area) =>
        Enumerable.Range(1, Math.Max(0, (PageCount - 1) / PRIVATE_LINK_EVERY)).Any(step => step * PRIVATE_LINK_EVERY % 10 == area);

    /// <summary>The private URL robots.txt allows in this revision, when a product links it, or null.</summary>
    private string? UnblockedPrivate => Revision >= 1 && IsPrivateLinked(1) ? UNBLOCKED_PRIVATE : null;

    private bool IsCategoryListed(int category) => Revision >= 1 && category == 0;

    /// <summary>The paths the extra sitemap lists in this revision beside its own: what revision 1 adds and mends.</summary>
    private IEnumerable<string> RevisionListedPaths()
    {
        if (Revision == 0)
        {
            yield break;
        }

        for (var product = 29; product < PageCount; product += 29)
        {
            if (FateOfBrokenLink(product) == BrokenLinkFate.Restored)
            {
                yield return $"/broken/{product}";
            }
        }

        if (UnblockedPrivate is { } unblocked)
        {
            yield return unblocked;
        }

        for (var category = 0; category < CATEGORY_COUNT; category++)
        {
            if (IsCategoryListed(category))
            {
                yield return $"/c/{category}";
            }
        }
    }

    /// <summary>
    /// A product's title. A product whose number is a multiple of 37 copies the one before's, so the pair is a duplicate.
    /// In revision 1, a third of those take their own (the pair is fixed), and a third keep the copy while the page
    /// before is renamed and the page after takes the old title (one cluster, matched by the page it keeps).
    /// </summary>
    private string TitleFor(int product)
    {
        if (Revision >= 1)
        {
            if (HasDuplicateTitle(product) && DuplicateTitleFate(product) == 1)
            {
                return TitleOf(product);
            }

            if (HasDuplicateTitle(product + 1) && DuplicateTitleFate(product + 1) == 2)
            {
                return $"Product {product}: {Words(product, 97, 2)}, renamed | Synthetic Store";
            }

            if (product >= 2 && HasDuplicateTitle(product - 1) && DuplicateTitleFate(product - 1) == 2)
            {
                return TitleOf(product - 2);
            }
        }

        return TitleOf(HasDuplicateTitle(product) ? product - 1 : product);
    }

    private static int DuplicateTitleFate(int product) => product / 37 % 3;
}
