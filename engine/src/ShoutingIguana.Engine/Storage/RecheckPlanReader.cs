using Microsoft.Data.Sqlite;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// What a recheck ("Recheck these N pages", the fix loop) would do, without doing it: read only. The URLs it judges are
/// the check's flagged URLs (or a group's, or a list's), most important first as the check's rows come. What it fetches
/// follows from the check's <see cref="IssueDefinition.References"/>:
/// <list type="bullet">
/// <item>a check of what others refer to (a 404, a redirect, a blocked URL) fetches the judged URLs first, then, for those
/// still failing, the pages referring to them, where the fix may be (a link removed or changed);</item>
/// <item>a check of pages holding a bad reference (a broken link, a canonical to a redirect) fetches the pages and every
/// URL their issues name together: either end may hold the fix;</item>
/// <item>any other fetches the judged pages.</item>
/// </list>
/// At most <see cref="LIMIT"/> URLs in all: past it the most important judged URLs are taken, and the rest are left as
/// the crawl found them.
/// </summary>
public static class RecheckPlanReader
{
    /// <summary>The most URLs a recheck sets out to fetch, its one-step URLs aside.</summary>
    public const int LIMIT = 1_000;

    /// <summary>A recheck is paced as a crawl's slow start begins, so it fetches no faster than this, whatever the crawl managed.</summary>
    private const long MIN_MS_PER_URL = 250;

    /// <summary>The page of a check's rows read at a time for their order.</summary>
    private const int ROWS_PAGE = 1_000;

    /// <summary>What a recheck of <paramref name="ask"/> would do, and whether it can run now.</summary>
    /// <param name="settings">The thresholds an analysis would run with now, to tell whether the crawl's is out of date.</param>
    /// <exception cref="ArgumentException">The ask names no check and no URLs, both a group and URLs, an unknown check, or URLs the crawl doesn't have.</exception>
    /// <exception cref="InvalidOperationException">The file isn't a crawl this engine can read.</exception>
    public static RecheckPlan Plan(string databasePath, RecheckAsk ask, AnalysisSettings settings, int limit = LIMIT)
    {
        if (ask.CheckId is null && ask.UrlIds is null)
        {
            throw new ArgumentException("A recheck needs a check or a list of URLs.", nameof(ask));
        }

        if (ask is { Group: not null, UrlIds: not null } || ask is { Group: not null, CheckId: null })
        {
            throw new ArgumentException("A group is one of a check's: give it with its check, and without a list of URLs.", nameof(ask));
        }

        if (ask.UrlIds is { Count: 0 or > LIMIT })
        {
            throw new ArgumentException($"A list of URLs to recheck has 1 to {LIMIT:N0} of them.", nameof(ask));
        }

        var check = ask.CheckId is null ? null
            : IssueCatalog.Find(ask.CheckId) ?? throw new ArgumentException($"Unknown check '{ask.CheckId}'.", nameof(ask));
        var summary = CrawlSummaryReader.ReadWithoutCounts(databasePath);
        var analysis = IssueReader.ReadAnalysis(databasePath);
        var skipped = analysis is null ? [] : IssueReader.ReadSkippedChecks(databasePath);
        IReadOnlyList<RecheckKind> kinds = ask.UrlIds is not null || check is null
            ? check?.Recheck.Contains(RecheckKind.Sitemaps) == true ? [RecheckKind.Urls, RecheckKind.Sitemaps] : [RecheckKind.Urls]
            : check.Recheck;

        var refused = summary.Status is CrawlStatus.Running or CrawlStatus.Paused
            ? new RecheckRefusal(RecheckRefusalCode.NotAnalysable, "This check was interrupted. Continue it first, then recheck.")
            : analysis is null
                ? new RecheckRefusal(RecheckRefusalCode.NotAnalyzed, "This check hasn't been analysed yet.")
                : check is not null && (check.Recheck.Count == 0 && ask.UrlIds is null || skipped.Any(skip => skip.CheckId == check.Id))
                    ? new RecheckRefusal(RecheckRefusalCode.Unsupported, check.Recheck.Count == 0
                        ? "Only checking the whole site again can show whether this is fixed."
                        : "This check couldn't run on this crawl, so there's nothing to recheck.")
                    : CrawlDatabase.IsInUse(databasePath)
                        ? new RecheckRefusal(RecheckRefusalCode.CrawlBusy, "This check is open elsewhere. Try again once it's done.")
                        : null;

        var countBefore = check is null || analysis is null ? (long?)null : IssueReader.ReadCounts(databasePath).GetValueOrDefault(check.Id);
        if (refused is { Code: not RecheckRefusalCode.CrawlBusy })
        {
            return new RecheckPlan(check?.Id, ask.Group, ask.UrlIds is not null, kinds, 0, 0, 0, limit, false, 0, 0, false, false, countBefore, refused);
        }

        var (judgedOrder, judgedTotal) = JudgedInOrder(databasePath, check, ask, limit);
        using var connection = CrawlDatabase.OpenReader(databasePath);
        using var snapshot = connection.BeginTransaction(deferred: true);
        var scope = Scope(connection, snapshot, check, judgedOrder, limit, out var judgedInScope);
        if (kinds.Contains(RecheckKind.SiteVersions))
        {
            AddProbes(connection, snapshot, check, scope);
        }

        var robotsOrigins = kinds.Contains(RecheckKind.RobotsTxt) ? RobotsOrigins(connection, snapshot, CrawlDatabase.ReadOptions(databasePath), check) : [];

        var phase1 = scope.Count(url => url.Phase == 1);
        var phase2 = scope.Count(url => url.Phase == 2);
        var crawled = summary.CrawledUrlCount ?? Count(connection, snapshot, "SELECT COUNT(*) FROM responses");
        var msPerUrl = summary.TimeCrawling is { } time && crawled > 0 ? Math.Max(MIN_MS_PER_URL, (long)time.TotalMilliseconds / crawled) : MIN_MS_PER_URL;
        var estimate = (phase1 + phase2 + robotsOrigins.Count) * msPerUrl + (long)(analysis?.Duration.TotalMilliseconds ?? 0);
        var analysisFirst = analysis is not null && AnalysisFreshness.Check(analysis, summary.UpdatedAt, settings).Count > 0;
        var notRechecked = judgedTotal - judgedInScope;
        var referrersCut = scope.Count < limit ? false : check?.References == IssueReferences.Sources;
        return new RecheckPlan(check?.Id, ask.Group, ask.UrlIds is not null, kinds, judgedTotal, phase1, phase2, limit, notRechecked > 0 || referrersCut,
            notRechecked, estimate, phase1 + phase2 > crawled / 2.0, analysisFirst, countBefore, refused)
        {
            Scope = scope,
            RobotsOrigins = robotsOrigins
        };
    }

    /// <summary>
    /// The URLs a recheck judges, most important first, up to <paramref name="limit"/> of them, and how many there are in
    /// all: the check's rows in their order by default (its group's, narrowed), or the list as given. A check a recheck
    /// reads only by its sitemaps, robots.txt or the site's versions judges the URLs it flagged all the same.
    /// </summary>
    private static (List<int> Ids, int Total) JudgedInOrder(string databasePath, IssueDefinition? check, RecheckAsk ask, int limit)
    {
        if (ask.UrlIds is { } listed)
        {
            return ([.. listed.Distinct()], listed.Distinct().Count());
        }

        List<int> ids = [];
        long total = 0;
        for (var offset = 0; ids.Count < limit; offset += ROWS_PAGE)
        {
            var page = IssueGroupReader.ReadRows(databasePath, check!.Id, new UrlQuery { Columns = ["url"], Offset = offset, Limit = ROWS_PAGE }, ask.Group)?.Page;
            if (page is null)
            {
                break;
            }

            total = page.Matching;
            ids.AddRange(page.Rows.Select(row => row.Id));
            if (page.Rows.Count < ROWS_PAGE)
            {
                break;
            }
        }

        return ([.. ids.Take(limit)], (int)total);
    }

    /// <summary>
    /// The URLs a recheck fetches, as the crawl has them, in the order it fetches them: the judged URLs within the cap, each
    /// with the URLs a targets check's issue on it names; then, for a sources check, the pages referring to each judged URL,
    /// most important (shallowest) first, as far as the cap allows. A judged URL whose own needs pass the cap is left out,
    /// as are the ones after it.
    /// </summary>
    private static List<RecheckScopeUrl> Scope(SqliteConnection connection, SqliteTransaction snapshot, IssueDefinition? check, List<int> judged, int limit,
        out int judgedInScope)
    {
        List<RecheckScopeUrl> scope = [];
        HashSet<int> taken = [];
        judgedInScope = 0;
        var refersFromPages = check?.References == IssueReferences.Sources;
        var namesTargets = check?.References == IssueReferences.Targets;
        foreach (var id in judged)
        {
            if (Url(connection, snapshot, id) is not { } url)
            {
                throw new ArgumentException($"The crawl has no URL {id}.", nameof(judged));
            }

            var (flagged, detail, related) = check is null ? (false, null, null) : Issue(connection, snapshot, check.Id, id);
            List<int> targets = check is null ? [] : Targets(connection, snapshot, check.Id, id);
            var named = namesTargets ? targets.Where(target => target != id && !taken.Contains(target)).Distinct().ToList()
                : RecheckDuplicates.TextOf(check?.Id) is not null ? ClusterMembers(connection, snapshot, check!.Id, targets).Where(member => member != id && !taken.Contains(member)).ToList()
                : [];
            if (taken.Count + 1 + named.Count > limit && !taken.Contains(id))
            {
                break;
            }

            judgedInScope++;
            if (taken.Add(id))
            {
                scope.Add(url with { Role = RecheckRole.Judged, Phase = 1, FlaggedBefore = flagged, DetailBefore = detail, RelatedBefore = related, Targets = targets });
            }
            else
            {
                // Named by a judged page before it: it is judged too.
                var index = scope.FindIndex(entry => entry.Id == id);
                scope[index] = scope[index] with { Role = RecheckRole.Judged, FlaggedBefore = flagged, DetailBefore = detail, RelatedBefore = related, Targets = targets };
            }

            foreach (var target in named)
            {
                if (taken.Add(target) && Url(connection, snapshot, target) is { } namedUrl)
                {
                    var (namedFlagged, namedDetail, namedRelated) = Issue(connection, snapshot, check!.Id, target);
                    scope.Add(namedUrl with { Role = RecheckRole.Named, Phase = 1, FlaggedBefore = namedFlagged, DetailBefore = namedDetail, RelatedBefore = namedRelated });
                }
            }
        }

        if (!refersFromPages)
        {
            return scope;
        }

        foreach (var judgedUrl in scope.Where(url => url.Role == RecheckRole.Judged).ToList())
        {
            foreach (var referrer in Referrers(connection, snapshot, judgedUrl.Id))
            {
                if (taken.Count >= limit)
                {
                    return scope;
                }

                if (taken.Add(referrer) && Url(connection, snapshot, referrer) is { } referring)
                {
                    var (referrerFlagged, referrerDetail, referrerRelated) = Issue(connection, snapshot, check!.Id, referrer);
                    scope.Add(referring with
                    {
                        Role = RecheckRole.Referring, Phase = 2, FlaggedBefore = referrerFlagged, DetailBefore = referrerDetail, RelatedBefore = referrerRelated,
                        Refers = judgedUrl.Id
                    });
                }
            }
        }

        return scope;
    }

    /// <summary>Every probe of the site's other versions, which a recheck of the site's versions asks again, with where they redirect.</summary>
    private static void AddProbes(SqliteConnection connection, SqliteTransaction snapshot, IssueDefinition? check, List<RecheckScopeUrl> scope)
    {
        List<int> probes = [];
        using (var command = Command(connection, snapshot, $"SELECT id FROM urls WHERE source = {(int)DiscoverySource.SiteVersion} ORDER BY id"))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                probes.Add(reader.GetInt32(0));
            }
        }

        foreach (var id in probes.Where(id => scope.All(url => url.Id != id)))
        {
            if (Url(connection, snapshot, id) is { } probe)
            {
                var (flagged, detail, related) = check is null ? (false, null, null) : Issue(connection, snapshot, check.Id, id);
                scope.Add(probe with { Role = RecheckRole.Named, FlaggedBefore = flagged, DetailBefore = detail, RelatedBefore = related });
            }
        }
    }

    private static RecheckScopeUrl? Url(SqliteConnection connection, SqliteTransaction snapshot, int id)
    {
        using var command = Command(connection, snapshot, "SELECT url, is_internal, depth, source FROM urls WHERE id = $id", ("$id", id));
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new RecheckScopeUrl(id, reader.GetString(0), reader.GetBoolean(1), reader.GetInt32(2), (DiscoverySource)reader.GetInt32(3), RecheckRole.Judged, 1,
                false, null, null, [], null)
            : null;
    }

    private static (bool Flagged, string? Detail, int? Related) Issue(SqliteConnection connection, SqliteTransaction snapshot, string checkId, int id)
    {
        using var command = Command(connection, snapshot, "SELECT detail, related_url_id FROM issues WHERE issue = $check AND url_id = $id", ("$check", checkId), ("$id", id));
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? (true, reader.IsDBNull(0) ? null : reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetInt32(1))
            : (false, null, null);
    }

    /// <summary>Every other URL the check's issue on <paramref name="id"/> names: its targets, or its one other URL in an analysis without them.</summary>
    private static List<int> Targets(SqliteConnection connection, SqliteTransaction snapshot, string checkId, int id)
    {
        List<int> targets = [];
        using (var command = Command(connection, snapshot, $"""
                   SELECT related_url_id FROM issue_targets WHERE issue = $check AND url_id = $id AND related_url_id <> {IssueHit.NO_OTHER_URL}
                   UNION SELECT related_url_id FROM issues WHERE issue = $check AND url_id = $id AND related_url_id IS NOT NULL
                   ORDER BY 1
                   """, ("$check", checkId), ("$id", id)))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                targets.Add(reader.GetInt32(0));
            }
        }

        return targets;
    }

    /// <summary>Every page of the duplicate clusters named by their first pages, as the analysis stored them.</summary>
    private static List<int> ClusterMembers(SqliteConnection connection, SqliteTransaction snapshot, string checkId, List<int> clusters)
    {
        List<int> members = [];
        foreach (var cluster in clusters)
        {
            using var command = Command(connection, snapshot, """
                SELECT url_id FROM issue_targets WHERE issue = $check AND related_url_id = $cluster
                UNION SELECT url_id FROM issues WHERE issue = $check AND related_url_id = $cluster
                UNION SELECT $cluster
                ORDER BY 1
                """, ("$check", checkId), ("$cluster", cluster));
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                members.Add(reader.GetInt32(0));
            }
        }

        return [.. members.Distinct()];
    }

    /// <summary>The pages linking to <paramref name="id"/> by any kind of link, shallowest first, then in crawl order.</summary>
    private static List<int> Referrers(SqliteConnection connection, SqliteTransaction snapshot, int id)
    {
        List<int> referrers = [];
        using var command = Command(connection, snapshot, """
            SELECT DISTINCT l.from_id, u.depth FROM links l JOIN urls u ON u.id = l.from_id
            WHERE l.to_id = $id AND l.from_id <> $id
            ORDER BY u.depth, l.from_id
            """, ("$id", id));
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            referrers.Add(reader.GetInt32(0));
        }

        return referrers;
    }

    /// <summary>
    /// The origins whose robots.txt a robots.txt check is about, read again beside the start URL's: the crawl's own (its
    /// start host, and the site's versions it covered, as the robots.txt checks judge them) that the check's issues name.
    /// Never another site's: the crawl read those before checking links to them, and they aren't the site owner's to fix.
    /// </summary>
    private static List<string> RobotsOrigins(SqliteConnection connection, SqliteTransaction snapshot, CrawlOptions options, IssueDefinition? check)
    {
        if (check is null || !UrlNormalizer.TryNormalize(options.StartUrl, out var startUrl))
        {
            return [];
        }

        var scope = new CrawlScope(new Uri(startUrl), options.IncludeSubdomains, options.StayInStartFolder);
        List<string> details = [];
        using (var command = Command(connection, snapshot, "SELECT detail FROM issues WHERE issue = $check AND detail IS NOT NULL", ("$check", check.Id)))
        {
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                details.Add(reader.GetString(0));
            }
        }

        List<string> origins = [];
        using (var command = Command(connection, snapshot, "SELECT origin FROM robots ORDER BY origin"))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                var origin = reader.GetString(0);
                if (Uri.TryCreate(origin, UriKind.Absolute, out var uri) && scope.IsInternal(uri) &&
                    details.Any(detail => detail.Contains($"{origin}/robots.txt", StringComparison.Ordinal)))
                {
                    origins.Add(origin);
                }
            }
        }

        return origins;
    }

    private static long Count(SqliteConnection connection, SqliteTransaction snapshot, string sql)
    {
        using var command = Command(connection, snapshot, sql);
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction snapshot, string sql, params (string Name, object Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.Transaction = snapshot;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return command;
    }
}
