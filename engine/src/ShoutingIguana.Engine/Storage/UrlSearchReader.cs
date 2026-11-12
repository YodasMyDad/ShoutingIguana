using System.Globalization;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// Search as it's typed: the best few of a crawl's URLs whose URL or title contains a text, for the command palette,
/// which asks on every keystroke.
/// <list type="bullet">
/// <item><b>What matches</b> is what the URL grid's <c>search</c> matches: the URL or the title contains the text, the
/// case of ASCII letters ignored. So the count given with the results is the grid's under the same search.</item>
/// <item><b>The order</b> is by how the text matches (<see cref="UrlSearchRank"/>: the text is the URL or its path, then
/// starts a segment of its path, its host name or a word of its title, then is anywhere), then the site's own URLs
/// before others, a match in the URL before one in the title, the shorter URL (or title) first, and the URL the crawl
/// found first.</item>
/// <item><b>Nothing is added to the crawl file.</b> The URLs and titles are read once into memory
/// (<see cref="SearchText"/>) and kept in the <see cref="ReadCache"/> it's given while the crawl stands still, so each
/// keystroke after the first is a pass over that text and no read of the file but for the few results' statuses. A
/// file made by any engine searches the same way.</item>
/// <item><b>A running crawl</b> is answered for one moment of it, at most <see cref="ReadCache.LIVE_FOR"/> old: the
/// results, their titles and both counts are of the URLs and titles the crawl had then, and only each result's status
/// and outcome are read as they now stand. Each read is one snapshot, held for that read only.</item>
/// </list>
/// </summary>
public static class UrlSearchReader
{
    public const int DEFAULT_LIMIT = 10;

    public const int MAX_LIMIT = 50;

    /// <summary>The longest text searched for, in characters: the URL grid's search's.</summary>
    public const int MAX_TEXT = UrlQueryCompiler.MAX_TEXT;

    /// <summary>
    /// The best few URLs whose URL or title contains the text, best first, with how many match in all. An empty text
    /// matches every URL and ranks none: it reads the crawl's text, so a palette can ask as it opens.
    /// </summary>
    /// <param name="cache">Where the crawl's text is kept between searches; none reads it from the file each time.</param>
    /// <param name="cancellationToken">Stops the search within milliseconds.</param>
    /// <exception cref="UrlQueryException">There's no text, it's too long or holds a zero character, or the limit is out of range.</exception>
    /// <exception cref="InvalidOperationException">The file isn't a crawl, or was written with another schema.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public static UrlSearchResults Search(string databasePath, string text, int limit = DEFAULT_LIMIT, ReadCache? cache = null,
        CancellationToken cancellationToken = default)
    {
        var needle = Needle(text, limit);
        return GridReadContext.Read(databasePath, cache, cancellationToken, context => Search(context, needle, limit));
    }

    /// <summary>
    /// The same, where the crawl's text is kept for the crawl as it stands, and null where it would have to be read: it
    /// opens the file, reads its stamp and passes over the text in memory, a millisecond or two on 100,000 URLs, and
    /// passes over nothing in the file. For a host that queues its reads, which a keystroke needn't queue for.
    /// </summary>
    /// <inheritdoc cref="Search(string, string, int, ReadCache?, CancellationToken)" path="/exception"/>
    public static UrlSearchResults? SearchKept(string databasePath, string text, int limit, ReadCache cache, CancellationToken cancellationToken = default)
    {
        var needle = Needle(text, limit);
        return GridReadContext.Read(databasePath, cache, cancellationToken, context => context.HasText(TEXT) ? Search(context, needle, limit) : null);
    }

    /// <summary>What the crawl's text is kept under.</summary>
    private const string TEXT = "search|text";

    /// <summary>The text as it's looked for, once it and the limit are known to be ones a search takes.</summary>
    private static byte[] Needle(string text, int limit)
    {
        Dictionary<string, List<string>> errors = new(StringComparer.Ordinal);
        if (text is null)
        {
            UrlQueryCompiler.Add(errors, "text", "Give the text to search for.");
        }
        else if (text.Length > MAX_TEXT)
        {
            UrlQueryCompiler.Add(errors, "text", $"Search for at most {MAX_TEXT:N0} characters.");
        }
        else if (text.Contains('\0', StringComparison.Ordinal))
        {
            UrlQueryCompiler.Add(errors, "text", "The text can't hold a zero character.");
        }

        if (limit is < 1 or > MAX_LIMIT)
        {
            UrlQueryCompiler.Add(errors, "limit", $"limit must be from 1 to {MAX_LIMIT}.");
        }

        UrlGridReader.ThrowIfInvalid(errors);
        return SearchText.Needle(text!);
    }

    private static UrlSearchResults Search(GridReadContext context, byte[] needle, int limit)
    {
        var crawl = context.CachedText(TEXT, () => Read(context));
        if (needle.Length == 0)
        {
            // Nothing typed yet: every URL matches, as with no search in the grid, and none is ranked. The crawl's text
            // has been read, so the first letter typed is answered as fast as the rest.
            return new UrlSearchResults([], crawl.Count, crawl.Count);
        }

        var hits = crawl.Search(needle, limit, context.Token);
        var facts = Facts(context, [.. hits.Best.Select(hit => crawl.Id(hit.Ordinal))]);
        return new UrlSearchResults(
            [
                .. hits.Best.Select(hit =>
                {
                    var id = crawl.Id(hit.Ordinal);
                    var (status, outcome) = facts.GetValueOrDefault(id);
                    return new UrlSearchResult(id, crawl.Url(hit.Ordinal), crawl.Title(hit.Ordinal), crawl.IsInternal(hit.Ordinal), status, outcome, hit.Rank, hit.Field);
                })
            ],
            hits.Matching,
            crawl.Count);
    }

    /// <summary>
    /// Every URL of the crawl with its title, read into memory in two passes of one snapshot: the URLs in the order the
    /// crawl found them, then the titles, each row handed over as SQLite reads it, so no string is made of any.
    /// </summary>
    private static SearchText Read(GridReadContext context)
    {
        SearchText text = new();
        context.SearchText = text;
        try
        {
            context.Scalar($"SELECT COUNT({ReaderFunctions.SEARCH_URL}(id, url, is_internal)) FROM urls", []);
            context.Scalar($"SELECT COUNT({ReaderFunctions.SEARCH_TITLE}(url_id, title)) FROM responses WHERE title IS NOT NULL", []);
        }
        finally
        {
            context.SearchText = null;
        }

        return text.Seal();
    }

    /// <summary>The results' status and outcome as the read's snapshot has them, read as the URL grid's columns read them.</summary>
    private static Dictionary<int, (int? Status, UrlOutcome? Outcome)> Facts(GridReadContext context, IReadOnlyList<int> ids)
    {
        Dictionary<int, (int? Status, UrlOutcome? Outcome)> facts = [];
        if (ids.Count == 0)
        {
            return facts;
        }

        var status = UrlColumns.STATUS_CODE.CrawlSql!;
        var outcome = UrlColumns.Find("outcome", issueColumns: false)!.CrawlSql!;
        using var command = context.Command(
            $"SELECT u.id, {status}, {outcome} FROM urls u LEFT JOIN responses r ON r.url_id = u.id WHERE u.id IN ({string.Join(",", ids.Select(id => id.ToString(CultureInfo.InvariantCulture)))})",
            []);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            facts[reader.GetInt32(0)] = (
                reader.IsDBNull(1) || reader.GetInt64(1) is < int.MinValue or > int.MaxValue ? null : reader.GetInt32(1),
                !reader.IsDBNull(2) && reader.GetInt64(2) is var number and >= 0 && number < Enum.GetValues<UrlOutcome>().Length ? (UrlOutcome)number : null);
        }

        return facts;
    }
}
