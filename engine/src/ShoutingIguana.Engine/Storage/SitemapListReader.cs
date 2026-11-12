using System.Globalization;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Robots;
using ShoutingIguana.Engine.Sitemaps;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// The sitemaps a crawl looked for: each with how the crawl came by it, what became of it, and the URLs it lists counted as
/// the URL grid judges them (fetched, indexable or not), so each count opens its rows in the sitemap's URL grid
/// (<see cref="UrlGridReader.QuerySitemap"/>). An index counts the URLs of every sitemap under it, each once. How a sitemap
/// was found isn't stored: it's worked out from the stored robots.txt (by the crawler's own parser) and the crawl's
/// options, as the crawler's discovery goes through them.
/// </summary>
public static class SitemapListReader
{
    /// <summary>Every sitemap the crawl looked for, in the order it found them; none for a crawl that found none.</summary>
    /// <exception cref="InvalidOperationException">The file isn't a crawl, or was written with another schema.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public static SitemapList Read(string databasePath, ReadCache? cache = null, CancellationToken cancellationToken = default) =>
        GridReadContext.Read(databasePath, cache, cancellationToken, context => context.Cached("sitemaps", () => Read(context)));

    private static SitemapList Read(GridReadContext context)
    {
        List<(int Id, string Url, int? ParentId, int? Status, bool IsIndex, int Entries, string? Error, bool IsProbe)> stored = [];
        using (var command = context.Command("SELECT id, url, parent_id, status_code, is_index, url_count, error, is_probe FROM sitemaps ORDER BY id", []))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                stored.Add((reader.GetInt32(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetInt32(2), reader.IsDBNull(3) ? null : reader.GetInt32(3),
                    !reader.IsDBNull(4) && reader.GetInt64(4) != 0, reader.IsDBNull(5) ? 0 : reader.GetInt32(5), reader.IsDBNull(6) ? null : reader.GetString(6),
                    !reader.IsDBNull(7) && reader.GetInt64(7) != 0));
            }
        }

        var done = context.Scalar("SELECT sitemaps_done FROM crawl WHERE id = 1", []) != 0;
        var discovery = !context.Options.DiscoverSitemaps ? SitemapDiscovery.Off
            : done ? SitemapDiscovery.Done
            : context.Summary.Reason == SummaryUnavailableReason.Crawling ? SitemapDiscovery.Running
            : SitemapDiscovery.Unfinished;

        var (declared, given) = Sources(context);
        var own = OwnCounts(context);
        var children = stored.Where(sitemap => sitemap.ParentId is not null).ToLookup(sitemap => sitemap.ParentId!.Value, sitemap => sitemap.Id);
        var disjoint = new Lazy<bool>(() => Distinct(context) == own.Values.Sum(counts => counts.Listed));
        List<SitemapInfo> sitemaps = [];
        foreach (var sitemap in stored)
        {
            SitemapSource? foundVia = sitemap.ParentId is not null ? SitemapSource.Index
                : sitemap.IsProbe ? SitemapSource.DefaultLocation
                : declared.Contains(sitemap.Url) ? SitemapSource.RobotsTxt
                : given.Contains(sitemap.Url) ? SitemapSource.Options
                : null;
            var state = sitemap.Error is null
                ? sitemap.Status is null ? SitemapState.Pending : SitemapState.Read
                : sitemap.Error.StartsWith(SitemapDiscoverer.NOT_READ_ERROR + ": ", StringComparison.Ordinal) ? SitemapState.NotRead : SitemapState.Failed;

            // The URLs a sitemap with others under it lists are theirs too, each once: added up when no URL is in two sitemaps
            // (the usual case), counted apart otherwise.
            var under = Under(sitemap.Id, children);
            var counts = under.Count == 1 ? own.GetValueOrDefault(sitemap.Id, NONE)
                : disjoint.Value ? under.Select(id => own.GetValueOrDefault(id, NONE)).Aggregate(NONE, (sum, each) => sum.Plus(each))
                : UnderCounts(context, sitemap.Id);
            sitemaps.Add(new SitemapInfo(sitemap.Id, sitemap.Url, sitemap.ParentId, foundVia, state, sitemap.Status, sitemap.IsIndex, sitemap.Entries,
                children[sitemap.Id].Count(), sitemap.Error, counts));
        }

        return new SitemapList(discovery, sitemaps, context.Summary);
    }

    private static readonly SitemapUrlCounts NONE = new(0, 0, 0, 0);

    /// <summary>A sitemap and every sitemap under it, however deep, each once.</summary>
    private static HashSet<int> Under(int id, ILookup<int, int> children)
    {
        HashSet<int> under = [id];
        Queue<int> pending = new([id]);
        while (pending.TryDequeue(out var next))
        {
            foreach (var child in children[next].Where(under.Add))
            {
                pending.Enqueue(child);
            }
        }

        return under;
    }

    /// <summary>
    /// The sitemaps the start URL's robots.txt declares, and those the crawl's options gave, each as the crawler's
    /// discovery resolves and normalises them (<see cref="SitemapDiscoverer"/>): a relative Sitemap line resolves against
    /// robots.txt. The robots.txt is the stored one, read by the crawler's parser.
    /// </summary>
    private static (HashSet<string> Declared, HashSet<string> Given) Sources(GridReadContext context)
    {
        HashSet<string> declared = new(StringComparer.Ordinal);
        HashSet<string> given = new(StringComparer.Ordinal);
        foreach (var sitemap in context.Options.ExtraSitemaps)
        {
            if (UrlNormalizer.TryNormalize(sitemap, out var normalized))
            {
                given.Add(normalized);
            }
        }

        if (!Uri.TryCreate(context.StartUrl, UriKind.Absolute, out var start))
        {
            return (declared, given);
        }

        using var command = context.Command("SELECT status_code, error, content FROM robots WHERE origin = $origin", [new("$origin", start.GetLeftPart(UriPartial.Authority))]);
        using var reader = command.ExecuteReader();
        if (!reader.Read() || reader.IsDBNull(2) || RobotsReader.StateOf(reader.IsDBNull(0) ? null : reader.GetInt32(0), reader.IsDBNull(1) ? null : reader.GetString(1)) != RobotsState.Read)
        {
            return (declared, given);
        }

        var robotsUri = new Uri(start, "/robots.txt");
        foreach (var sitemap in RobotsTxtParser.Parse(reader.GetString(2), context.RobotsToken).Sitemaps)
        {
            if (UrlNormalizer.TryNormalize(UrlNormalizer.TryResolve(robotsUri, sitemap, out var resolved) ? resolved.AbsoluteUri : sitemap, out var normalized))
            {
                declared.Add(normalized);
            }
        }

        return (declared, given);
    }

    /// <summary>
    /// The URL grid's <c>fetched</c>, <c>indexable</c> and <c>outcome</c> columns over the URLs listed, by the URL grid's
    /// own SQL for the mode: the outcome is each URL's response's, with the URL summary as without it. With the summary,
    /// a URL it has a 2xx status for answered (or was a block page, which has its own outcome): it's neither refused by
    /// robots.txt nor redirecting or broken, and its response isn't looked up, which keeps the list's one pass over a
    /// hundred thousand listed URLs at what it took before the two counts.
    /// </summary>
    private static (string From, string Sums) Judged(GridReadContext context)
    {
        string Sql(string column) => UrlColumns.Find(column, issueColumns: false)!.Sql(context.Mode)!.Replace(UrlColumns.REASON, context.Reason, StringComparison.Ordinal);
        var (fetched, indexable, outcome) = (Sql("fetched"), Sql("indexable"), Sql("outcome"));
        var from = context.Mode == GridMode.Summary
            ? "JOIN url_summary s ON s.url_id = m.url_id"
            : "JOIN urls u ON u.id = m.url_id LEFT JOIN responses r ON r.url_id = u.id" +
              (context.HasEffectiveIndexability ? " LEFT JOIN effective_indexability e ON e.url_id = u.id" : string.Empty);
        if (context.Mode == GridMode.Summary)
        {
            outcome = $"CASE WHEN s.status_code BETWEEN 200 AND 299 THEN NULL ELSE (SELECT {outcome} FROM responses r WHERE r.url_id = m.url_id) END";
        }

        var broken = string.Join(", ", SitemapUrlCounts.REDIRECTING_OR_BROKEN.Select(value => ((int)value).ToString(CultureInfo.InvariantCulture)));
        return (from, $"COUNT(*), COALESCE(SUM(({fetched}) <> 0), 0), COALESCE(SUM(({indexable}) = 1), 0), COALESCE(SUM(({indexable}) = 0), 0), " +
                      $"COALESCE(SUM(({outcome}) = {(int)UrlOutcome.RobotsDisallowed}), 0), COALESCE(SUM(({outcome}) IN ({broken})), 0)");
    }

    /// <summary>Each sitemap's own listed URLs, counted in one pass over what the sitemaps list.</summary>
    private static Dictionary<int, SitemapUrlCounts> OwnCounts(GridReadContext context)
    {
        var (from, sums) = Judged(context);
        Dictionary<int, SitemapUrlCounts> counts = [];
        using var command = context.Command($"SELECT m.sitemap_id, {sums} FROM sitemap_urls m {from} GROUP BY m.sitemap_id", []);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            counts[reader.GetInt32(0)] = new SitemapUrlCounts(reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3), reader.GetInt64(4), reader.GetInt64(5), reader.GetInt64(6));
        }

        return counts;
    }

    /// <summary>The URLs any sitemap lists, each once.</summary>
    private static long Distinct(GridReadContext context)
    {
        var listed = new UrlIdSet(context.Scalar("SELECT COALESCE(MAX(id), 0) FROM urls", []));
        using var command = context.Command(
            $"SELECT COUNT({ReaderFunctions.ADD_TO_SET}({context.Use(listed).ToString(CultureInfo.InvariantCulture)}, m.url_id)) FROM sitemap_urls m", []);
        command.ExecuteScalar();
        return listed.Seal(0).Count;
    }

    /// <summary>The URLs a sitemap and those under it list, each once, where some are listed twice.</summary>
    private static SitemapUrlCounts UnderCounts(GridReadContext context, int sitemapId)
    {
        var (from, sums) = Judged(context);
        using var command = context.Command(
            $"SELECT {sums} FROM (SELECT DISTINCT url_id FROM sitemap_urls WHERE sitemap_id IN ({UrlQueryCompiler.SITEMAPS_UNDER})) m {from}",
            [new(UrlQueryCompiler.SITEMAP_PARAMETER, sitemapId)]);
        using var reader = command.ExecuteReader();
        reader.Read();
        return new SitemapUrlCounts(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3), reader.GetInt64(4), reader.GetInt64(5));
    }
}
