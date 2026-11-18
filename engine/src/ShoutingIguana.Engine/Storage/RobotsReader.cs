using System.Globalization;
using Microsoft.Data.Sqlite;
using ShoutingIguana.Engine.Analysis.Rules;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Robots;
using ShoutingIguana.Engine.Robots.Models;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// robots.txt as a crawl read it: each origin's answer, the file line by line, and the URLs it kept the crawl from, each
/// with the line that refused it. The crawl stores each origin's last answer and, per URL, only that robots.txt refused it,
/// so the line is worked out here, from the stored file, by the crawler's own parser and matcher
/// (<see cref="RobotsTxtParser"/>, <see cref="RobotsTxt.Match"/>) for the crawl's own user agent: the answer the crawl
/// acted on, never a second reading of the rules. Each read is one snapshot of the file; what's worked out is kept per
/// origin while the crawl stands still, and briefly while it runs. An origin's answer, its lines and its blocked URLs are
/// read and kept as one, so everything one answer says of an origin is of one moment of a running crawl: the lines'
/// counts and the URLs no line accounts for add up to the origin's blocked URLs.
/// </summary>
public static class RobotsReader
{
    /// <summary>
    /// One page of the crawl's origins: the site's own first, the start URL's before the others, then the rest by the URLs
    /// they blocked, most first, then by name. A crawl asks for robots.txt on every origin it requests a URL on, other
    /// sites' too, so there can be thousands.
    /// </summary>
    /// <exception cref="UrlQueryException">The paging is out of range.</exception>
    /// <exception cref="InvalidOperationException">The file isn't a crawl, or was written with another schema.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public static RobotsOriginPage ReadOrigins(string databasePath, int offset = 0, int limit = UrlGridReader.DEFAULT_LIMIT, ReadCache? cache = null,
        CancellationToken cancellationToken = default) =>
        ReadOrigins(databasePath, new RobotsOriginQuery { Offset = offset, Limit = limit }, cache, cancellationToken);

    /// <summary>
    /// One page of the origins a search and a filter match, in the list's order: those whose name contains a text, and
    /// those that blocked something (or nothing). <see cref="RobotsOriginPage.Total"/> and <see cref="RobotsOriginPage.Blocked"/>
    /// stay the crawl's; <see cref="RobotsOriginPage.Matching"/> is what the page is of, and
    /// <see cref="RobotsOriginPage.Counts"/> what the search matches by whether it blocked anything, for a filter bar.
    /// </summary>
    /// <exception cref="UrlQueryException">The paging is out of range, or the text too long.</exception>
    /// <exception cref="InvalidOperationException">The file isn't a crawl, or was written with another schema.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public static RobotsOriginPage ReadOrigins(string databasePath, RobotsOriginQuery query, ReadCache? cache = null, CancellationToken cancellationToken = default)
    {
        Dictionary<string, List<string>> errors = new(StringComparer.Ordinal);
        UrlQueryCompiler.ValidatePaging(query.Offset, query.Limit, after: null, errors);
        ValidateSearch(query.Search, errors);
        UrlGridReader.ThrowIfInvalid(errors);
        return GridReadContext.Read(databasePath, cache, cancellationToken, context => OriginPage(context, query, query.Limit));
    }

    /// <summary>
    /// Every origin a search and a filter match, in the list's order, from one snapshot of the file: what an export of
    /// the list writes, so a running crawl's file is of one moment of it. The paging asked for is ignored. Says whether
    /// the crawl was running.
    /// </summary>
    /// <inheritdoc cref="ReadOrigins(string, RobotsOriginQuery, ReadCache?, CancellationToken)" path="/exception"/>
    internal static (RobotsOriginPage Page, bool Live) ReadEveryOrigin(string databasePath, RobotsOriginQuery query, ReadCache? cache, CancellationToken cancellationToken)
    {
        Dictionary<string, List<string>> errors = new(StringComparer.Ordinal);
        ValidateSearch(query.Search, errors);
        UrlGridReader.ThrowIfInvalid(errors);
        return GridReadContext.Read(databasePath, cache, cancellationToken,
            context => (OriginPage(context, query with { Offset = 0 }, int.MaxValue), context.Stamp!.Live));
    }

    /// <summary>What's wrong with a search of the origins, under <c>search</c>: only its length.</summary>
    internal static void ValidateSearch(string? search, Dictionary<string, List<string>> errors)
    {
        if (search is { Length: > UrlQueryCompiler.MAX_TEXT })
        {
            UrlQueryCompiler.Add(errors, "search", $"Search for at most {UrlQueryCompiler.MAX_TEXT:N0} characters.");
        }
    }

    private static RobotsOriginPage OriginPage(GridReadContext context, RobotsOriginQuery query, int limit)
    {
        var origins = Origins(context);
        var searched = string.IsNullOrEmpty(query.Search)
            ? origins
            : [.. origins.Where(origin => origin.Origin.Contains(query.Search, StringComparison.OrdinalIgnoreCase))];
        var blocking = searched.LongCount(origin => origin.Blocked > 0);
        var matching = query.Blocked is { } blocked ? [.. searched.Where(origin => origin.Blocked > 0 == blocked)] : searched;
        return new RobotsOriginPage(origins.Count, query.Offset, context.Options.RespectRobotsTxt, context.RobotsToken, origins.Sum(origin => origin.Blocked),
            [.. matching.Skip(query.Offset).Take(limit)])
        {
            Matching = matching.Count,
            Counts = new RobotsOriginCounts(searched.Count, blocking, searched.Count - blocking)
        };
    }

    /// <summary>
    /// One origin's robots.txt, line by line: what each line is, whether it's part of the group the crawl obeyed, and how
    /// many of the origin's blocked URLs each disallow line refused. Null when the crawl has no robots.txt for the origin.
    /// </summary>
    /// <inheritdoc cref="ReadOrigins(string, RobotsOriginQuery, ReadCache?, CancellationToken)" path="/exception"/>
    public static RobotsFile? ReadFile(string databasePath, string origin, ReadCache? cache = null, CancellationToken cancellationToken = default) =>
        GridReadContext.Read(databasePath, cache, cancellationToken, context =>
        {
            if (!Origins(context).Any(known => known.Origin == origin))
            {
                return null;
            }

            var read = Read(context, origin);
            var blocked = read.Blocked.Where(url => url.Reason == RobotsBlockReason.Rule).GroupBy(url => url.Line!.Value).ToDictionary(line => line.Key, line => line.LongCount());
            return new RobotsFile(read.Origin, context.RobotsToken,
                [
                    .. read.Lines.Select(line => new RobotsFileLine(line.Number, line.Text, line.Kind, line.Applies,
                        line is { Kind: RobotsLineKind.Disallow, Applies: true } ? blocked.GetValueOrDefault(line.Number) : null, line.Problem))
                ],
                new RobotsUnmatched(read.Blocked.LongCount(url => url.Reason == RobotsBlockReason.Unreachable), read.Blocked.LongCount(url => url.Reason == RobotsBlockReason.Unknown)))
            {
                Respected = context.Options.RespectRobotsTxt
            };
        });

    /// <summary>
    /// One page of the URLs robots.txt kept the crawl from on an origin, in crawl order, each with why: the line that
    /// refused it, that robots.txt couldn't be read, or that the stored file doesn't refuse it. Null when the crawl has no
    /// robots.txt for the origin.
    /// </summary>
    /// <param name="line">Only the URLs this line refused.</param>
    /// <param name="reason">Only the URLs refused for this reason.</param>
    /// <param name="urlId">Only this URL: the page then holds it, with why it was refused, or nothing when the origin didn't block it.</param>
    /// <param name="conflict">
    /// Only the URLs that a sitemap lists or a menu links to (true), or only the others (false): none while what blocking
    /// them costs isn't known (<see cref="RobotsBlockedUrl.Conflict"/>).
    /// </param>
    /// <inheritdoc cref="ReadOrigins(string, RobotsOriginQuery, ReadCache?, CancellationToken)" path="/exception"/>
    public static RobotsBlockedPage? ReadBlocked(string databasePath, string origin, int? line = null, RobotsBlockReason? reason = null, int offset = 0,
        int limit = UrlGridReader.DEFAULT_LIMIT, ReadCache? cache = null, CancellationToken cancellationToken = default, int? urlId = null, bool? conflict = null)
    {
        ThrowIfInvalid(offset, limit);
        return GridReadContext.Read(databasePath, cache, cancellationToken, context =>
        {
            if (!Origins(context).Any(known => known.Origin == origin))
            {
                return null;
            }

            var blocked = Read(context, origin).Blocked;
            var costs = Costs(context, origin);
            var matching = blocked.Where(url => (line is null || url.Line == line) && (reason is null || url.Reason == reason) && (urlId is null || url.Id == urlId) &&
                                                (conflict is null || (costs?.TryGetValue(url.Id, out var cost) == true && IsConflict(cost) == conflict))).ToList();
            return new RobotsBlockedPage(origin, blocked.Count, matching.Count, offset, [.. matching.Skip(offset).Take(limit).Select(url => Costed(context, url, costs))])
            {
                Conflicts = costs?.Values.LongCount(IsConflict)
            };
        });
    }

    /// <summary>
    /// What blocking each of an origin's blocked URLs costs, by URL id, as the analysis counted it: the pages linking to it
    /// and whether a sitemap lists it (from the URL summary), and whether a menu links to it (from the link summary, or the
    /// links to it where there's none). Two lookups a blocked URL, by key, never a pass over the links. Null when the crawl
    /// has changed since its analysis, whose counts would then be of another crawl. Kept as the origin's list is.
    /// </summary>
    private static Dictionary<int, (long? LinkingPages, bool? InSitemap, bool InMenu)>? Costs(GridReadContext context, string name)
    {
        if (context.Mode != GridMode.Summary)
        {
            return null;
        }

        var hyperlink = (int)LinkKind.Hyperlink;
        var navigation = (int)LinkPosition.Navigation;
        var menu = context.LinkSummary is null
            ? $"EXISTS (SELECT 1 FROM links l WHERE l.to_id = u.id AND l.kind = {hyperlink} AND l.position = {navigation} AND l.from_id <> u.id)"
            : $"EXISTS (SELECT 1 FROM link_summary ls WHERE ls.to_id = u.id AND ls.kind = {hyperlink} AND ls.position = {navigation} AND ls.self = 0)";
        return context.CachedLarge("robots|costs|" + name, () =>
        {
            Dictionary<int, (long? LinkingPages, bool? InSitemap, bool InMenu)> costs = [];
            using var command = context.Command($"""
                SELECT u.id, s.linking_pages, s.in_sitemap, {menu}
                FROM urls u JOIN responses r ON r.url_id = u.id LEFT JOIN url_summary s ON s.url_id = u.id
                WHERE u.url >= $origin || '/' AND u.url < $origin || '0' AND r.outcome = {(int)FetchOutcome.RobotsDisallowed}
                """, [new("$origin", name)]);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                costs[reader.GetInt32(0)] = (reader.IsDBNull(1) ? null : reader.GetInt64(1), reader.IsDBNull(2) ? null : reader.GetBoolean(2), reader.GetBoolean(3));
            }

            return costs;
        });
    }

    private static bool IsConflict((long? LinkingPages, bool? InSitemap, bool InMenu) cost) => cost.InSitemap == true || cost.InMenu;

    /// <summary>
    /// A blocked URL of the page with what blocking it costs, and the sitemaps that list it, which are looked up for the
    /// page's rows alone: a lookup by key in each sitemap's list.
    /// </summary>
    private static RobotsBlockedUrl Costed(GridReadContext context, RobotsBlockedUrl url, Dictionary<int, (long? LinkingPages, bool? InSitemap, bool InMenu)>? costs)
    {
        if (costs is null || !costs.TryGetValue(url.Id, out var cost))
        {
            return url;
        }

        var listed = cost.InSitemap == true ? UrlInspectorReader.ListedIn(context, url.Id) : null;
        return url with
        {
            LinkingPages = cost.LinkingPages,
            InSitemap = cost.InSitemap,
            Sitemaps = listed?.Total,
            Sitemap = listed?.Sitemaps.FirstOrDefault(),
            InMenu = cost.InMenu,
            Conflict = IsConflict(cost)
        };
    }

    /// <summary>What an origin's stored answer meant to the crawl, as its cache took it and the analysis's robots.txt checks do.</summary>
    /// <param name="onStartOrigin">
    /// The origin is the start URL's, which no answer from refuses everything on; any other origin that gives no answer
    /// gets no verdict (<see cref="RobotsState.Unanswered"/>).
    /// </param>
    internal static RobotsState StateOf(int? statusCode, string? error, bool onStartOrigin = true) => statusCode switch
    {
        >= 200 and <= 299 => RobotsState.Read,
        >= 400 and <= 499 and not 429 => RobotsState.Missing,

        // A robots.txt behind redirects that can't be followed is stored without a status, and allows everything.
        null when error?.Contains("redirects", StringComparison.OrdinalIgnoreCase) == true => RobotsState.Missing,
        null when !onStartOrigin => RobotsState.Unanswered,
        _ => RobotsState.Unreachable
    };

    /// <summary>The origin of a stored URL as the list of origins names it: all that comes before the first "/" after its "://".</summary>
    internal static string? OriginOf(string url)
    {
        var scheme = url.IndexOf("://", StringComparison.Ordinal);
        if (scheme < 0)
        {
            return null;
        }

        var path = url.IndexOf('/', scheme + 3);
        return path < 0 ? url : url[..path];
    }

    /// <summary>Whether the crawl kept a robots.txt answer for the origin, so its file can be asked for.</summary>
    internal static bool HasAnswer(GridReadContext context, string origin) =>
        context.Scalar("SELECT COUNT(*) FROM robots WHERE origin = $origin", [new("$origin", origin)]) > 0;

    /// <summary>
    /// Why robots.txt kept the crawl from one URL, as the origin's list of blocked URLs says it (<see cref="ReadBlocked"/>),
    /// worked out for this URL alone: the origin's stored file is parsed, once per origin while the crawl stands still,
    /// and this URL matched against it, so inspecting one refused URL of an origin that refused a hundred thousand
    /// matches one.
    /// </summary>
    /// <param name="error">What the crawl stored with the URL's refusal.</param>
    internal static RobotsBlockedUrl WhyRefused(GridReadContext context, int urlId, string url, string? error)
    {
        var name = OriginOf(url) ?? string.Empty;
        var (origin, lines, rules) = context.Cached("robots|rules|" + name, () =>
        {
            List<RobotsLine> parsed = [];
            RobotsTxt? read = null;
            var answered = new RobotsOrigin(name, IsInternal(name, Site(context).Scope), null, null, null, null, null, 0);
            using var file = context.Command($"SELECT content, {ANSWER} FROM robots WHERE origin = $origin", [new("$origin", name)]);
            using var answer = file.ExecuteReader();
            if (answer.Read())
            {
                answered = Answered(name, Site(context).Scope, answer, first: 1, blocked: 0);
                if (answered.State == RobotsState.Read)
                {
                    read = RobotsTxtParser.Parse(answer.GetValue(0) as string ?? string.Empty, context.RobotsToken, parsed);
                }
            }

            return (answered, parsed, read);
        });
        return Why(urlId, url, error, origin, rules, lines);
    }

    /// <summary>
    /// Why robots.txt refused a URL, with the origin whose file did (<see cref="WhyRefused"/>), or null for a URL it didn't
    /// refuse: the URL grid's <c>robotsRule</c>.
    /// </summary>
    internal static RobotsRefusal? RuleOf(GridReadContext context, int urlId)
    {
        using var command = context.Command(
            $"SELECT u.url, r.error FROM urls u JOIN responses r ON r.url_id = u.id WHERE u.id = $id AND r.outcome = {(int)FetchOutcome.RobotsDisallowed}",
            [new("$id", urlId)]);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        var url = reader.GetString(0);
        var why = WhyRefused(context, urlId, url, reader.IsDBNull(1) ? null : reader.GetString(1));
        return new RobotsRefusal(OriginOf(url), why.Reason, why.Line, why.Rule);
    }

    private static void ThrowIfInvalid(int offset, int limit)
    {
        Dictionary<string, List<string>> errors = new(StringComparer.Ordinal);
        UrlQueryCompiler.ValidatePaging(offset, limit, after: null, errors);
        UrlGridReader.ThrowIfInvalid(errors);
    }

    /// <summary>Every origin with its robots.txt and the URLs it blocked, in the order the list shows them. Kept while the crawl stands still.</summary>
    private static List<RobotsOrigin> Origins(GridReadContext context) => context.Cached("robots|origins", () =>
    {
        // The crawl's refused URLs counted by origin: all that comes before the first "/" after "://" of a stored URL.
        Dictionary<string, long> blocked = new(StringComparer.Ordinal);
        using (var command = context.Command($"""
                   SELECT substr(u.url, 1, instr(u.url, '://') + 2 + instr(substr(u.url, instr(u.url, '://') + 3) || '/', '/') - 1), COUNT(*)
                   FROM responses r JOIN urls u ON u.id = r.url_id WHERE r.outcome = {(int)FetchOutcome.RobotsDisallowed} GROUP BY 1
                   """, []))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                blocked[reader.GetString(0)] = reader.GetInt64(1);
            }
        }

        var (start, scope) = Site(context);
        List<RobotsOrigin> origins = [];
        using (var command = context.Command($"SELECT origin, {ANSWER} FROM robots", []))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                var origin = reader.GetString(0);
                origins.Add(Answered(origin, scope, reader, first: 1, blocked.GetValueOrDefault(origin)));
            }
        }

        // URLs stored as refused on an origin the crawl kept no robots.txt for (another program's file) still count, and list.
        origins.AddRange(blocked.Keys.Except(origins.Select(origin => origin.Origin), StringComparer.Ordinal)
            .Select(origin => new RobotsOrigin(origin, IsInternal(origin, scope), null, null, null, null, null, blocked[origin])));

        var startOrigin = start?.GetLeftPart(UriPartial.Authority);
        return origins.OrderByDescending(origin => origin.Origin == startOrigin).ThenByDescending(origin => origin.IsInternal)
            .ThenByDescending(origin => origin.IsInternal ? 0 : origin.Blocked).ThenBy(origin => origin.Origin, StringComparer.Ordinal).ToList();
    });

    /// <summary>What <c>robots</c> keeps of an origin's answer, as <see cref="Answered"/> reads it.</summary>
    private const string ANSWER = "status_code, error, fetched_at, length(CAST(content AS BLOB))";

    /// <summary>An origin with its stored answer, from a row that holds <see cref="ANSWER"/> from <paramref name="first"/> on.</summary>
    private static RobotsOrigin Answered(string origin, CrawlScope? scope, SqliteDataReader reader, int first, long blocked)
    {
        var status = reader.IsDBNull(first) || reader.GetInt64(first) is < int.MinValue or > int.MaxValue ? (int?)null : reader.GetInt32(first);
        var error = reader.IsDBNull(first + 1) ? null : reader.GetString(first + 1);
        var onStartOrigin = origin == scope?.StartOrigin.GetLeftPart(UriPartial.Authority);
        return new RobotsOrigin(origin, IsInternal(origin, scope), StateOf(status, error, onStartOrigin), status, reader.IsDBNull(first + 2) ? null : Time(reader.GetInt64(first + 2)), error,
            reader.IsDBNull(first + 3) ? null : reader.GetInt64(first + 3), blocked);
    }

    /// <summary>The crawl's start URL and what it took for the site's own, by its options.</summary>
    private static (Uri? Start, CrawlScope? Scope) Site(GridReadContext context)
    {
        var start = Uri.TryCreate(context.StartUrl, UriKind.Absolute, out var startUri) ? startUri : null;
        return (start, start is null ? null : new CrawlScope(start, context.Options.IncludeSubdomains, context.Options.StayInStartFolder));
    }

    private static bool IsInternal(string origin, CrawlScope? scope) => scope is not null && Uri.TryCreate(origin, UriKind.Absolute, out var uri) && scope.IsInternal(uri);

    private static DateTimeOffset Time(long milliseconds) =>
        DateTimeOffset.FromUnixTimeMilliseconds(Math.Clamp(milliseconds, DateTimeOffset.MinValue.ToUnixTimeMilliseconds(), DateTimeOffset.MaxValue.ToUnixTimeMilliseconds()));

    /// <summary>
    /// An origin's answer, its stored file as the crawler's parser reads it, and its blocked URLs, each matched against the
    /// file by the crawler's own matcher: all of one read, and kept as one per origin, so every blocked URL is matched once
    /// and what's said of the origin is of the moment its URLs were read, not of the list's (<see cref="Origins"/>).
    /// </summary>
    private sealed record OriginRead(RobotsOrigin Origin, IReadOnlyList<RobotsLine> Lines, IReadOnlyList<RobotsBlockedUrl> Blocked);

    private static OriginRead Read(GridReadContext context, string name) =>
        context.CachedLarge("robots|file|" + name, () =>
        {
            List<RobotsLine> lines = [];
            RobotsTxt? rules = null;
            var origin = new RobotsOrigin(name, IsInternal(name, Site(context).Scope), null, null, null, null, null, 0);
            using (var file = context.Command($"SELECT content, {ANSWER} FROM robots WHERE origin = $origin", [new("$origin", name)]))
            using (var answer = file.ExecuteReader())
            {
                if (answer.Read())
                {
                    origin = Answered(name, Site(context).Scope, answer, first: 1, blocked: 0);
                    if (origin.State == RobotsState.Read)
                    {
                        rules = RobotsTxtParser.Parse(answer.GetValue(0) as string ?? string.Empty, context.RobotsToken, lines);
                    }
                }
            }

            // The origin's URLs sort between its "/" and the character after it, on the index of URLs.
            List<RobotsBlockedUrl> blocked = [];
            using var command = context.Command($"""
                SELECT u.id, u.url, r.error FROM urls u JOIN responses r ON r.url_id = u.id
                WHERE u.url >= $origin || '/' AND u.url < $origin || '0' AND r.outcome = {(int)FetchOutcome.RobotsDisallowed}
                ORDER BY u.id
                """, [new("$origin", origin.Origin)]);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var url = reader.GetString(1);
                blocked.Add(Why(reader.GetInt32(0), url, reader.IsDBNull(2) ? null : reader.GetString(2), origin, rules, lines));
            }

            return new OriginRead(origin with { Blocked = blocked.Count }, lines, blocked);
        });

    /// <summary>
    /// Why a URL was refused. The crawl stores an error with one refused because robots.txt couldn't be read; one a rule
    /// refused has none, and the rule is the one the matcher gives for the stored file. A file read again since (a crawl
    /// continued later keeps each origin's last answer) may not refuse it any more, or may not have been read at all.
    /// </summary>
    private static RobotsBlockedUrl Why(int id, string url, string? error, RobotsOrigin origin, RobotsTxt? rules, List<RobotsLine> lines)
    {
        if (error?.StartsWith(RobotsTxt.UNREACHABLE_ERROR, StringComparison.Ordinal) == true)
        {
            return new RobotsBlockedUrl(id, url, RobotsBlockReason.Unreachable, null, null, error[RobotsTxt.UNREACHABLE_ERROR.Length..].TrimStart(':', ' ') is { Length: > 0 } why ? why : null);
        }

        if (rules is not null && Uri.TryCreate(url, UriKind.Absolute, out var uri) && rules.Match(uri.PathAndQuery) is { Allow: false, Line: > 0 } rule &&
            rule.Line <= lines.Count)
        {
            return new RobotsBlockedUrl(id, url, RobotsBlockReason.Rule, rule.Line, lines[rule.Line - 1].Text.Trim(), null);
        }

        // An engine from before the error was stored with the URL: the origin's own answer says robots.txt couldn't be read.
        return origin.State == RobotsState.Unreachable
            ? new RobotsBlockedUrl(id, url, RobotsBlockReason.Unreachable, null, null,
                origin.StatusCode is { } status ? HttpStatusNames.Describe(status) : origin.Error)
            : new RobotsBlockedUrl(id, url, RobotsBlockReason.Unknown, null, null, null);
    }
}
