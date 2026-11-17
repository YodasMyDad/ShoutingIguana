using Microsoft.Data.Sqlite;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// The let-go walk at the end of a recheck: which URLs nothing the crawl still has leads to any more, now that pages the
/// recheck fetched again dropped links. Owner fixes a broken footer link by removing it: the 404 stays in the file, and
/// would still be flagged though no fresh crawl would find it. So each URL that lost a link is looked at, and what only it
/// led to in turn: the part of the link graph the recheck changed (everything reachable from those URLs). Within it, a URL
/// is kept if it is the start URL, a probe of the site's other versions or listed in a sitemap, or if a URL outside that
/// part links to it (any kind of link: a redirect, a canonical, an image), or a URL kept within it does. The rest are let
/// go, pages that lead only to each other among them. Past <see cref="MAX_REGION"/> URLs, the walk is of the whole graph
/// from the start URL, the probes and the sitemaps instead. A URL still queued (a stopped crawl's) is never let go: the
/// crawl's queue is left as it was.
/// </summary>
internal static class RecheckLetGo
{
    /// <summary>The part of the graph walked from what leads into it; past it, the whole graph is walked from its roots.</summary>
    internal const int MAX_REGION = 10_000;

    /// <summary>The URLs to let go, in id order.</summary>
    /// <param name="lostLinks">URLs that lost a link to them in the recheck.</param>
    public static List<int> Find(SqliteConnection connection, SqliteTransaction? transaction, IReadOnlyCollection<int> lostLinks, int maxRegion = MAX_REGION)
    {
        if (lostLinks.Count == 0)
        {
            return [];
        }

        using var outgoing = Prepare(connection, transaction, "SELECT to_id FROM links WHERE from_id = $id");
        using var incoming = Prepare(connection, transaction, "SELECT from_id FROM links WHERE to_id = $id");
        var roots = Roots(connection, transaction);
        var region = Reachable(outgoing, lostLinks, maxRegion);
        var kept = region is not null
            ? KeptWithin(incoming, outgoing, region, roots)
            : Reachable(outgoing, roots, int.MaxValue)!;

        // The changed part, walked again in full when it was too big to walk from what leads into it.
        region ??= Reachable(outgoing, lostLinks, int.MaxValue, stopAt: kept)!;
        using var state = Prepare(connection, transaction, "SELECT state, skip_reason FROM urls WHERE id = $id");
        return [.. region.Where(id => !kept.Contains(id) && MayLetGo(state, id)).Order()];
    }

    /// <summary>The start URL, the probes of the site's other versions, and every URL a sitemap lists.</summary>
    private static HashSet<int> Roots(SqliteConnection connection, SqliteTransaction? transaction)
    {
        HashSet<int> roots = [];
        using var command = Prepare(connection, transaction, $"""
            SELECT id FROM urls WHERE source IN ({(int)DiscoverySource.Seed}, {(int)DiscoverySource.SiteVersion})
            UNION SELECT url_id FROM sitemap_urls
            """);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            roots.Add(reader.GetInt32(0));
        }

        return roots;
    }

    /// <summary>
    /// Everything reachable from <paramref name="from"/> by any link, them included, or null once it passes
    /// <paramref name="most"/>. A URL in <paramref name="stopAt"/> isn't entered.
    /// </summary>
    private static HashSet<int>? Reachable(SqliteCommand outgoing, IEnumerable<int> from, int most, IReadOnlySet<int>? stopAt = null)
    {
        HashSet<int> seen = [];
        Queue<int> next = new();
        foreach (var id in from.Where(id => stopAt?.Contains(id) != true && seen.Add(id)))
        {
            next.Enqueue(id);
        }

        while (next.TryDequeue(out var id))
        {
            if (seen.Count > most)
            {
                return null;
            }

            foreach (var target in Ids(outgoing, id))
            {
                if (stopAt?.Contains(target) != true && seen.Add(target))
                {
                    next.Enqueue(target);
                }
            }
        }

        return seen;
    }

    /// <summary>
    /// Of the changed part, what is still led to: its roots and the URLs linked from outside it, then everything they
    /// reach within it.
    /// </summary>
    private static HashSet<int> KeptWithin(SqliteCommand incoming, SqliteCommand outgoing, HashSet<int> region, HashSet<int> roots)
    {
        var entries = region.Where(id => roots.Contains(id) || Ids(incoming, id).Any(from => !region.Contains(from))).ToList();
        HashSet<int> kept = [.. entries];
        Queue<int> next = new(entries);
        while (next.TryDequeue(out var id))
        {
            foreach (var target in Ids(outgoing, id).Where(target => region.Contains(target) && kept.Add(target)))
            {
                next.Enqueue(target);
            }
        }

        return kept;
    }

    /// <summary>Fetched, or skipped, and not let go already: a URL still queued is the crawl's, for its continue.</summary>
    private static bool MayLetGo(SqliteCommand state, int id)
    {
        state.Parameters[0].Value = id;
        using var reader = state.ExecuteReader();
        return reader.Read() && (UrlState)reader.GetInt32(0) is UrlState.Done or UrlState.Skipped && (SkipReason)reader.GetInt32(1) != SkipReason.NoLongerLinked;
    }

    private static List<int> Ids(SqliteCommand command, int id)
    {
        command.Parameters[0].Value = id;
        List<int> ids = [];
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            ids.Add(reader.GetInt32(0));
        }

        return ids;
    }

    private static SqliteCommand Prepare(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        if (sql.Contains("$id", StringComparison.Ordinal))
        {
            command.Parameters.Add("$id", SqliteType.Integer);
        }

        return command;
    }
}
