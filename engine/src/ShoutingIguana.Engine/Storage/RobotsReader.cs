using System.Globalization;
using ShoutingIguana.Engine.Fetching.Models;
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
/// origin while the crawl stands still.
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
        CancellationToken cancellationToken = default)
    {
        ThrowIfInvalid(offset, limit);
        return GridReadContext.Read(databasePath, cache, cancellationToken, context =>
        {
            var origins = Origins(context);
            return new RobotsOriginPage(origins.Count, offset, context.Options.RespectRobotsTxt, context.RobotsToken, origins.Sum(origin => origin.Blocked),
                [.. origins.Skip(offset).Take(limit)]);
        });
    }

    /// <summary>
    /// One origin's robots.txt, line by line: what each line is, whether it's part of the group the crawl obeyed, and how
    /// many of the origin's blocked URLs each disallow line refused. Null when the crawl has no robots.txt for the origin.
    /// </summary>
    /// <inheritdoc cref="ReadOrigins" path="/exception"/>
    public static RobotsFile? ReadFile(string databasePath, string origin, ReadCache? cache = null, CancellationToken cancellationToken = default) =>
        GridReadContext.Read(databasePath, cache, cancellationToken, context =>
        {
            if (Origins(context).FirstOrDefault(known => known.Origin == origin) is not { } found)
            {
                return null;
            }

            var read = Read(context, found);
            var blocked = read.Blocked.Where(url => url.Reason == RobotsBlockReason.Rule).GroupBy(url => url.Line!.Value).ToDictionary(line => line.Key, line => line.LongCount());
            return new RobotsFile(found, context.RobotsToken,
                [
                    .. read.Lines.Select(line => new RobotsFileLine(line.Number, line.Text, line.Kind, line.Applies,
                        line is { Kind: RobotsLineKind.Disallow, Applies: true } ? blocked.GetValueOrDefault(line.Number) : null, line.Problem))
                ],
                new RobotsUnmatched(read.Blocked.LongCount(url => url.Reason == RobotsBlockReason.Unreachable), read.Blocked.LongCount(url => url.Reason == RobotsBlockReason.Unknown)));
        });

    /// <summary>
    /// One page of the URLs robots.txt kept the crawl from on an origin, in crawl order, each with why: the line that
    /// refused it, that robots.txt couldn't be read, or that the stored file doesn't refuse it. Null when the crawl has no
    /// robots.txt for the origin.
    /// </summary>
    /// <param name="line">Only the URLs this line refused.</param>
    /// <param name="reason">Only the URLs refused for this reason.</param>
    /// <inheritdoc cref="ReadOrigins" path="/exception"/>
    public static RobotsBlockedPage? ReadBlocked(string databasePath, string origin, int? line = null, RobotsBlockReason? reason = null, int offset = 0,
        int limit = UrlGridReader.DEFAULT_LIMIT, ReadCache? cache = null, CancellationToken cancellationToken = default)
    {
        ThrowIfInvalid(offset, limit);
        return GridReadContext.Read(databasePath, cache, cancellationToken, context =>
        {
            if (Origins(context).FirstOrDefault(known => known.Origin == origin) is not { } found)
            {
                return null;
            }

            var blocked = Read(context, found).Blocked;
            var matching = blocked.Where(url => (line is null || url.Line == line) && (reason is null || url.Reason == reason)).ToList();
            return new RobotsBlockedPage(origin, blocked.Count, matching.Count, offset, [.. matching.Skip(offset).Take(limit)]);
        });
    }

    /// <summary>What an origin's stored answer meant to the crawl, as its cache took it and the analysis's robots.txt checks do.</summary>
    internal static RobotsState StateOf(int? statusCode, string? error) => statusCode switch
    {
        >= 200 and <= 299 => RobotsState.Read,
        >= 400 and <= 499 and not 429 => RobotsState.Missing,

        // A robots.txt behind redirects that can't be followed is stored without a status, and allows everything.
        null when error?.Contains("redirects", StringComparison.OrdinalIgnoreCase) == true => RobotsState.Missing,
        _ => RobotsState.Unreachable
    };

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

        var start = Uri.TryCreate(context.StartUrl, UriKind.Absolute, out var startUri) ? startUri : null;
        var scope = start is null ? null : new CrawlScope(start, context.Options.IncludeSubdomains, context.Options.StayInStartFolder);
        bool IsInternal(string origin) => scope is not null && Uri.TryCreate(origin, UriKind.Absolute, out var uri) && scope.IsInternal(uri);

        List<RobotsOrigin> origins = [];
        using (var command = context.Command("SELECT origin, status_code, error, fetched_at, length(CAST(content AS BLOB)) FROM robots", []))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                var origin = reader.GetString(0);
                var status = reader.IsDBNull(1) || reader.GetInt64(1) is < int.MinValue or > int.MaxValue ? (int?)null : reader.GetInt32(1);
                var error = reader.IsDBNull(2) ? null : reader.GetString(2);
                origins.Add(new RobotsOrigin(origin, IsInternal(origin), StateOf(status, error), status, reader.IsDBNull(3) ? null : Time(reader.GetInt64(3)), error,
                    reader.IsDBNull(4) ? null : reader.GetInt64(4), blocked.GetValueOrDefault(origin)));
            }
        }

        // URLs stored as refused on an origin the crawl kept no robots.txt for (another program's file) still count, and list.
        origins.AddRange(blocked.Keys.Except(origins.Select(origin => origin.Origin), StringComparer.Ordinal)
            .Select(origin => new RobotsOrigin(origin, IsInternal(origin), null, null, null, null, null, blocked[origin])));

        var startOrigin = start?.GetLeftPart(UriPartial.Authority);
        return origins.OrderByDescending(origin => origin.Origin == startOrigin).ThenByDescending(origin => origin.IsInternal)
            .ThenByDescending(origin => origin.IsInternal ? 0 : origin.Blocked).ThenBy(origin => origin.Origin, StringComparer.Ordinal).ToList();
    });

    private static DateTimeOffset Time(long milliseconds) =>
        DateTimeOffset.FromUnixTimeMilliseconds(Math.Clamp(milliseconds, DateTimeOffset.MinValue.ToUnixTimeMilliseconds(), DateTimeOffset.MaxValue.ToUnixTimeMilliseconds()));

    /// <summary>
    /// An origin's stored file as the crawler's parser reads it, and its blocked URLs, each matched against it by the
    /// crawler's own matcher. Kept per origin while the crawl stands still: every blocked URL is matched once.
    /// </summary>
    private static (IReadOnlyList<RobotsLine> Lines, IReadOnlyList<RobotsBlockedUrl> Blocked) Read(GridReadContext context, RobotsOrigin origin) =>
        context.CachedLarge("robots|file|" + origin.Origin, () =>
        {
            List<RobotsLine> lines = [];
            RobotsTxt? rules = null;
            if (origin.State == RobotsState.Read)
            {
                using var file = context.Command("SELECT content FROM robots WHERE origin = $origin", [new("$origin", origin.Origin)]);
                rules = RobotsTxtParser.Parse(file.ExecuteScalar() as string ?? string.Empty, context.RobotsToken, lines);
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

            return ((IReadOnlyList<RobotsLine>)lines, (IReadOnlyList<RobotsBlockedUrl>)blocked);
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
                origin.StatusCode is { } status ? string.Create(CultureInfo.InvariantCulture, $"HTTP {status}") : origin.Error)
            : new RobotsBlockedUrl(id, url, RobotsBlockReason.Unknown, null, null, null);
    }
}
