using System.Globalization;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Parsing;
using ShoutingIguana.Engine.Parsing.Models;

namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>
/// What a page's robots rules say beyond index and follow (<see cref="IndexabilityRules"/> has those): rules that
/// contradict each other, a noindex near the home page, a date to drop out of search that has passed, and the rules that
/// keep images, video or translation out of search. Rules count for all crawlers, Googlebot and the crawl's own token, as
/// the crawler read them (<see cref="RobotsMetaParser"/>).
/// </summary>
/// <param name="robotsToken">The crawl's robots token, as its user agent names it.</param>
internal sealed class DirectiveRules(UrlTable urls, string robotsToken) : IPageRule
{
    private const string X_ROBOTS_TAG = "x-robots-tag";
    private const string UNAVAILABLE_AFTER = "unavailable_after";

    /// <summary>The most clicks from the start at which a noindex page is near the home page.</summary>
    private const int NEAR_HOME_CLICKS = 2;

    /// <summary>Path segments that name a page a site keeps out of search on purpose: a noindex on one is no surprise.</summary>
    private static readonly HashSet<string> PRIVATE_SEGMENTS = new(
    [
        "cart", "basket", "checkout", "account", "my-account", "login", "log-in", "signin", "sign-in", "register", "signup",
        "sign-up", "logout", "wishlist", "search", "thank-you", "thanks", "order-received", "password", "reset-password"
    ], StringComparer.OrdinalIgnoreCase);

    /// <summary>Zones the date formats robots rules use name by letters, which .NET's parser doesn't read.</summary>
    private static readonly (string Zone, string Offset)[] ZONES =
    [
        ("GMT", "+00:00"), ("UTC", "+00:00"), ("UT", "+00:00"), ("EST", "-05:00"), ("EDT", "-04:00"), ("CST", "-06:00"),
        ("CDT", "-05:00"), ("MST", "-07:00"), ("MDT", "-06:00"), ("PST", "-08:00"), ("PDT", "-07:00"),
        // What PHP's DATE_RFC850 (Drupal's, WordPress's) writes on a European server, and the other zones whose letters
        // mean one thing: dargaud.com sends "Saturday, 04-Oct-25 00:00:00 CEST".
        ("CET", "+01:00"), ("CEST", "+02:00"), ("WET", "+00:00"), ("WEST", "+01:00"), ("EET", "+02:00"), ("EEST", "+03:00"),
        ("BST", "+01:00"), ("MSK", "+03:00"), ("JST", "+09:00"), ("AEST", "+10:00"), ("AEDT", "+11:00"), ("NZST", "+12:00"), ("NZDT", "+13:00")
    ];

    /// <summary>RFC 822 and 850 forms (with the zone as an offset) that the lenient parse may not take.</summary>
    private static readonly string[] DATE_FORMATS =
    [
        "dddd, dd-MMM-yy HH:mm:ss zzz", "dddd, dd-MMM-yyyy HH:mm:ss zzz", "dd-MMM-yy HH:mm:ss zzz", "dd-MMM-yyyy HH:mm:ss zzz",
        "dd-MMM-yyyy HH:mm zzz", "dd-MMM-yyyy", "ddd, d MMM yy HH:mm:ss zzz", "ddd, d MMM yy HH:mm zzz", "d MMM yy HH:mm:ss zzz",
        "d MMM yy HH:mm zzz"
    ];

    public IEnumerable<IssueHit> Check(AnalyzedPage page)
    {
        var robots = page.Facts.Robots;
        var metaContents = page.Facts.MetaTags.Where(tag => RobotsMetaParser.AppliesToMetaName(tag.Name, robotsToken)).Select(tag => tag.Content).ToList();
        var headerValues = page.Headers.Where(header => header.Name == X_ROBOTS_TAG).Select(header => header.Value).ToList();

        if (Conflict(metaContents, headerValues) is { } conflict)
        {
            yield return new IssueHit(IssueCatalog.ROBOTS_DIRECTIVES_CONFLICT, page.UrlId, conflict);
        }

        if (robots.NoIndex && page.Depth is >= 1 and <= NEAR_HOME_CLICKS && page.UrlId != urls.HomeId && page.UrlId != urls.StartId && !IsPrivate(page.Uri))
        {
            yield return new IssueHit(IssueCatalog.NOINDEX_NEAR_HOME, page.UrlId, $"{RuleText.Count(page.Depth, "click", "clicks")} from the start");
        }

        if (!robots.NoIndex && UnavailableAfter(metaContents, headerValues) is { } unavailable && unavailable.Date < page.FetchedAt)
        {
            yield return new IssueHit(IssueCatalog.UNAVAILABLE_AFTER_PASSED, page.UrlId, $"{UNAVAILABLE_AFTER}: {RuleText.Cut(unavailable.Written, 60)}, passed");
        }

        if (page.IsIndexable && robots.Directives.Select(Compact).Where(BlocksMedia).Distinct().ToList() is { Count: > 0 } media)
        {
            yield return new IssueHit(IssueCatalog.MEDIA_PREVIEW_BLOCKED, page.UrlId, string.Join(", ", media));
        }

        if (page.IsIndexable && robots.Directives.Contains("notranslate"))
        {
            yield return new IssueHit(IssueCatalog.TRANSLATION_BLOCKED, page.UrlId);
        }

        if (!robots.NoIndex && robots.Directives.Contains("indexifembedded"))
        {
            yield return new IssueHit(IssueCatalog.INDEXIFEMBEDDED_WITHOUT_NOINDEX, page.UrlId);
        }
    }

    /// <summary>
    /// The meta tags and the header, each read on its own, giving opposite rules for indexing or following, with what each
    /// says: <c>The HTML says "index, follow"; the X-Robots-Tag header says "noindex"</c>. Null when they agree, or only one
    /// says anything.
    /// </summary>
    private string? Conflict(List<string> metaContents, List<string> headerValues)
    {
        if (metaContents.Count == 0 || headerValues.Count == 0)
        {
            return null;
        }

        var meta = RobotsMetaParser.Parse(metaContents, [], robotsToken);
        var header = RobotsMetaParser.Parse([], headerValues, robotsToken);
        var indexing = (meta.NoIndex && Allows(header, "index")) || (header.NoIndex && Allows(meta, "index"));
        var following = (meta.NoFollow && Allows(header, "follow")) || (header.NoFollow && Allows(meta, "follow"));
        return indexing || following
            ? RuleText.Fit($"The HTML says {RuleText.Quote(string.Join(", ", metaContents))}; the X-Robots-Tag header says {RuleText.Quote(string.Join(", ", headerValues))}")
            : null;
    }

    /// <summary>The rules say, in so many words, to index (or follow): <c>index</c>, or <c>all</c> for both.</summary>
    private static bool Allows(RobotsDirectives rules, string what) => rules.Directives.Contains(what) || rules.Directives.Contains("all");

    /// <summary>
    /// The first <c>unavailable_after</c> rule for the crawler with a date search engines can read, and the date as written.
    /// The raw text is read, not the parsed rules, which split on commas and so cut RFC 850 dates ("Monday, 25-Jun-07 …").
    /// </summary>
    private (DateTimeOffset Date, string Written)? UnavailableAfter(List<string> metaContents, List<string> headerValues)
    {
        foreach (var text in metaContents.Concat(headerValues.SelectMany(ForThisCrawler)))
        {
            var at = text.IndexOf(UNAVAILABLE_AFTER, StringComparison.OrdinalIgnoreCase);
            if (at < 0)
            {
                continue;
            }

            var rest = text[(at + UNAVAILABLE_AFTER.Length)..].TrimStart();
            if (!rest.StartsWith(':'))
            {
                continue;
            }

            // The date runs to the end, or to the comma before the next rule: the longest part that reads as a date.
            rest = rest[1..].Trim();
            for (var end = rest.Length; end > 0; end = rest.LastIndexOf(',', end - 1))
            {
                var written = rest[..end].Trim();
                if (TryReadDate(written, out var date))
                {
                    return (date, written);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The parts of an X-Robots-Tag value that apply to the crawler: all of it, or after a crawler's name (<c>googlebot:</c>)
    /// only the runs for Googlebot and the crawl's token, as <see cref="RobotsMetaParser"/> reads them.
    /// </summary>
    private IEnumerable<string> ForThisCrawler(string value)
    {
        List<string> applying = [];
        var applies = true;
        foreach (var part in value.Split(',', StringSplitOptions.TrimEntries))
        {
            var directive = part;
            var colon = part.IndexOf(':');
            if (colon > 0)
            {
                var prefix = part[..colon].Trim();
                if (!prefix.Contains(' ') && !RobotsMetaParser.IsValuedDirective(prefix.ToLowerInvariant()))
                {
                    applies = prefix.Equals("googlebot", StringComparison.OrdinalIgnoreCase) || prefix.Equals(robotsToken, StringComparison.OrdinalIgnoreCase);
                    directive = part[(colon + 1)..].Trim();
                }
            }

            if (applies)
            {
                applying.Add(directive);
            }
        }

        return applying.Count == 0 ? [] : [string.Join(", ", applying)];
    }

    /// <summary>
    /// A date as RFC 822, RFC 850 or ISO 8601 write it, the formats Google names, a zone given by letters (GMT, PST) read
    /// as its offset, and none taken as UTC.
    /// </summary>
    internal static bool TryReadDate(string text, out DateTimeOffset date)
    {
        var value = text.Trim();
        foreach (var (zone, offset) in ZONES)
        {
            if (value.EndsWith(" " + zone, StringComparison.OrdinalIgnoreCase))
            {
                value = value[..^zone.Length] + offset;
                break;
            }
        }

        const DateTimeStyles STYLES = DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal;
        return DateTimeOffset.TryParseExact(value, DATE_FORMATS, CultureInfo.InvariantCulture, STYLES, out date) ||
               (value.Any(char.IsAsciiDigit) && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, STYLES, out date));
    }

    /// <summary>A rule with no space round its colon: <c>max-image-preview: none</c> is <c>max-image-preview:none</c>.</summary>
    private static string Compact(string directive) => directive.Replace(" ", string.Empty, StringComparison.Ordinal);

    private static bool BlocksMedia(string directive) => directive is "noimageindex" or "max-image-preview:none" or "max-video-preview:0";

    /// <summary>A path segment names a cart, an account, a login or such: a page kept out of search on purpose.</summary>
    private static bool IsPrivate(Uri uri) =>
        uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(segment => PRIVATE_SEGMENTS.Contains(Uri.UnescapeDataString(segment)));
}
