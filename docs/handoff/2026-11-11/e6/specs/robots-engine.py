M = 'src/ShoutingIguana.Engine/Storage/Models/'

WRITES = [
(M + 'RobotsOriginQuery.cs', '''namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>Which of a crawl's origins to list (<see cref="RobotsReader.ReadOrigins(string, RobotsOriginQuery, ReadCache?, CancellationToken)"/>).</summary>
public sealed record RobotsOriginQuery
{
    /// <summary>Only the origins whose name contains this text, case ignored; every origin when null or empty.</summary>
    public string? Search { get; init; }

    /// <summary>True for only the origins that blocked at least one URL, false for only those that blocked none; both when null.</summary>
    public bool? Blocked { get; init; }

    public int Offset { get; init; }

    public int Limit { get; init; } = UrlGridReader.DEFAULT_LIMIT;
}
'''),
(M + 'RobotsOriginCounts.cs', '''namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// The origins a search matches, counted for a filter bar: all of them, those that blocked at least one URL and those
/// that blocked none. Each is how many rows the list has under that filter.
/// </summary>
public sealed record RobotsOriginCounts(long All, long Blocked, long NotBlocked);
'''),
]

EDITS = [
(M + 'RobotsOriginPage.cs',
'''public sealed record RobotsOriginPage(long Total, int Offset, bool Respected, string UserAgent, long Blocked, IReadOnlyList<RobotsOrigin> Origins);
''',
'''public sealed record RobotsOriginPage(long Total, int Offset, bool Respected, string UserAgent, long Blocked, IReadOnlyList<RobotsOrigin> Origins)
{
    /// <summary>The origins the search and the filter asked for match, which the page is of; every origin with neither.</summary>
    public long Matching { get; init; } = Total;

    /// <summary>The origins the search matches, counted by whether they blocked anything.</summary>
    public RobotsOriginCounts Counts { get; init; } = new(Total, 0, Total);
}
'''),
(M + 'RobotsFile.cs',
'''public sealed record RobotsFile(RobotsOrigin Origin, string UserAgent, IReadOnlyList<RobotsFileLine> Lines, RobotsUnmatched Unmatched);
''',
'''public sealed record RobotsFile(RobotsOrigin Origin, string UserAgent, IReadOnlyList<RobotsFileLine> Lines, RobotsUnmatched Unmatched)
{
    /// <summary>False for a crawl told to ignore robots.txt, which blocks nothing: as <see cref="RobotsOriginPage.Respected"/>.</summary>
    public bool Respected { get; init; } = true;
}
'''),
('src/ShoutingIguana.Engine/Storage/RobotsReader.cs',
'''    public static RobotsOriginPage ReadOrigins(string databasePath, int offset = 0, int limit = UrlGridReader.DEFAULT_LIMIT, ReadCache? cache = null,
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
''',
'''    public static RobotsOriginPage ReadOrigins(string databasePath, int offset = 0, int limit = UrlGridReader.DEFAULT_LIMIT, ReadCache? cache = null,
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
        if (query.Search is { Length: > UrlQueryCompiler.MAX_TEXT })
        {
            UrlQueryCompiler.Add(errors, "search", $"Search for at most {UrlQueryCompiler.MAX_TEXT:N0} characters.");
        }

        UrlGridReader.ThrowIfInvalid(errors);
        return GridReadContext.Read(databasePath, cache, cancellationToken, context =>
        {
            var origins = Origins(context);
            var searched = string.IsNullOrEmpty(query.Search)
                ? origins
                : [.. origins.Where(origin => origin.Origin.Contains(query.Search, StringComparison.OrdinalIgnoreCase))];
            var blocking = searched.LongCount(origin => origin.Blocked > 0);
            var matching = query.Blocked is { } blocked ? [.. searched.Where(origin => origin.Blocked > 0 == blocked)] : searched;
            return new RobotsOriginPage(origins.Count, query.Offset, context.Options.RespectRobotsTxt, context.RobotsToken, origins.Sum(origin => origin.Blocked),
                [.. matching.Skip(query.Offset).Take(query.Limit)])
            {
                Matching = matching.Count,
                Counts = new RobotsOriginCounts(searched.Count, blocking, searched.Count - blocking)
            };
        });
    }
'''),
('src/ShoutingIguana.Engine/Storage/RobotsReader.cs',
'''                new RobotsUnmatched(read.Blocked.LongCount(url => url.Reason == RobotsBlockReason.Unreachable), read.Blocked.LongCount(url => url.Reason == RobotsBlockReason.Unknown)));
        });
''',
'''                new RobotsUnmatched(read.Blocked.LongCount(url => url.Reason == RobotsBlockReason.Unreachable), read.Blocked.LongCount(url => url.Reason == RobotsBlockReason.Unknown)))
            {
                Respected = context.Options.RespectRobotsTxt
            };
        });
'''),
('src/ShoutingIguana.Engine/Storage/RobotsReader.cs',
'''    /// <param name="line">Only the URLs this line refused.</param>
    /// <param name="reason">Only the URLs refused for this reason.</param>
    /// <inheritdoc cref="ReadOrigins" path="/exception"/>
    public static RobotsBlockedPage? ReadBlocked(string databasePath, string origin, int? line = null, RobotsBlockReason? reason = null, int offset = 0,
        int limit = UrlGridReader.DEFAULT_LIMIT, ReadCache? cache = null, CancellationToken cancellationToken = default)
    {
''',
'''    /// <param name="line">Only the URLs this line refused.</param>
    /// <param name="reason">Only the URLs refused for this reason.</param>
    /// <param name="urlId">Only this URL: the page then holds it, with why it was refused, or nothing when the origin didn't block it.</param>
    /// <inheritdoc cref="ReadOrigins(string, RobotsOriginQuery, ReadCache?, CancellationToken)" path="/exception"/>
    public static RobotsBlockedPage? ReadBlocked(string databasePath, string origin, int? line = null, RobotsBlockReason? reason = null, int offset = 0,
        int limit = UrlGridReader.DEFAULT_LIMIT, ReadCache? cache = null, CancellationToken cancellationToken = default, int? urlId = null)
    {
'''),
('src/ShoutingIguana.Engine/Storage/RobotsReader.cs',
'''            var matching = blocked.Where(url => (line is null || url.Line == line) && (reason is null || url.Reason == reason)).ToList();
''',
'''            var matching = blocked.Where(url => (line is null || url.Line == line) && (reason is null || url.Reason == reason) && (urlId is null || url.Id == urlId)).ToList();
'''),
('src/ShoutingIguana.Engine/Storage/RobotsReader.cs',
'''    /// <inheritdoc cref="ReadOrigins" path="/exception"/>
    public static RobotsFile? ReadFile(''',
'''    /// <inheritdoc cref="ReadOrigins(string, RobotsOriginQuery, ReadCache?, CancellationToken)" path="/exception"/>
    public static RobotsFile? ReadFile('''),
('src/ShoutingIguana.Engine/Storage/RobotsReader.cs',
'''    private static void ThrowIfInvalid(int offset, int limit)
''',
'''    /// <summary>The origin of a stored URL as the list of origins names it: all that comes before the first "/" after its "://".</summary>
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

    private static void ThrowIfInvalid(int offset, int limit)
'''),
]
