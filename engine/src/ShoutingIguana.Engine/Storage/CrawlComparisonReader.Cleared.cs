using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Storage;

/// <summary>What a check flagged in the other crawl and doesn't flag now: by URL, in the other crawl's order by importance, or by cause.</summary>
public static partial class CrawlComparisonReader
{
    private static readonly IReadOnlySet<RowChangeState> CLEARED = new HashSet<RowChangeState> { RowChangeState.Fixed, RowChangeState.Gone, RowChangeState.NotChecked };

    private static void Validate(ClearedQuery query)
    {
        Dictionary<string, List<string>> errors = new(StringComparer.Ordinal);
        UrlQueryCompiler.ValidatePaging(query.Offset, query.Limit, after: null, errors);
        foreach (var state in (query.States ?? []).Where(state => !CLEARED.Contains(state)).Take(1))
        {
            errors["states"] = [$"A row the check no longer flags is fixed, gone or not checked, not {state}."];
        }

        UrlGridReader.ThrowIfInvalid(errors);
    }

    private static ClearedPage? Cleared(GridReadContext now, GridReadContext then, string checkId, ClearedQuery query)
    {
        if (Pair(now, then) is not { } pair)
        {
            return null;
        }

        var check = IssueCatalog.Find(checkId);
        if (query.Group is not null && check?.Grouping is null or IssueGrouping.None)
        {
            throw UrlQueryException.For("group", "This check lists its issues without groups, so there's no group to narrow them to.");
        }

        // No URL can be matched (counts only), or nothing was flagged then to clear.
        var empty = new ClearedPage(checkId, pair.Verdict.Comparable, query.Level, null, query.Offset, [], []);
        if (pair.CountsOnly || !pair.Then.Ran(checkId))
        {
            return empty;
        }

        var states = query.States is { Count: > 0 } asked ? asked.ToHashSet() : CLEARED;
        var rows = ClearedInOrder(now, then, pair, checkId);
        if (query.Level == ClearedLevel.Groups)
        {
            return check is not { Grouping: not IssueGrouping.None } grouped
                ? empty with { Total = 0 }
                : ClearedGroups(now, then, pair, grouped, rows, states, query);
        }

        var members = query.Group is { } group ? GroupMembers(then, check!, group) : null;
        var matching = rows.Where(row => states.Contains(row.State) && (members is null || members.Contains(row.ThenUrlId))).ToList();
        List<ClearedIssue> page = [];
        using (var command = then.Command("""
                   SELECT u.url, i.detail, r.url
                   FROM issues i JOIN urls u ON u.id = i.url_id LEFT JOIN urls r ON r.id = i.related_url_id
                   WHERE i.issue = $check AND i.url_id = $id
                   """, [new("$check", checkId), new("$id", 0)]))
        {
            foreach (var row in matching.Skip(query.Offset).Take(query.Limit))
            {
                command.Parameters["$id"].Value = row.ThenUrlId;
                then.Token.ThrowIfCancellationRequested();
                using var reader = command.ExecuteReader();
                if (reader.Read())
                {
                    page.Add(new ClearedIssue(reader.GetString(0), row.UrlId, row.ThenUrlId, row.State, row.Reason,
                        reader.IsDBNull(1) ? null : reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
                }
            }
        }

        return empty with { Total = matching.Count, Rows = page };
    }

    /// <summary>The causes then with no URL now, largest first, each with how its URLs then moved (in the states asked for).</summary>
    private static ClearedPage ClearedGroups(GridReadContext now, GridReadContext then, ComparisonPair pair, IssueDefinition check, IReadOnlyList<ClearedRow> rows,
        IReadOnlySet<RowChangeState> states, ClearedQuery query)
    {
        var pass = GroupPassOf(now, then, pair, check);
        var stateOf = rows.Where(row => states.Contains(row.State)).ToDictionary(row => row.ThenUrlId, row => row.State);
        var tallies = pass.Cleared.ToDictionary(group => (group.Detail, group.RelatedUrlId), _ => new long[3]);
        foreach (var (detail, related, urlId) in Members(then, check))
        {
            if (tallies.TryGetValue((detail, related), out var tally) && stateOf.TryGetValue(urlId, out var state))
            {
                tally[state switch { RowChangeState.Fixed => 0, RowChangeState.Gone => 1, _ => 2 }]++;
            }
        }

        var column = SharedTextColumn(check);
        if (column is not null)
        {
            // A crawl made before the share columns: its clusters' share text wasn't stored.
            using var has = then.Command("SELECT COUNT(*) FROM pragma_table_info('responses') WHERE name = $column", [new("$column", column)]);
            column = has.ExecuteScalar() is 0L ? null : column;
        }

        List<ClearedGroup> groups = [];
        using var shared = column is null ? null : then.Command($"SELECT {column} FROM responses WHERE url_id = $id", [new("$id", 0)]);
        foreach (var group in pass.Cleared.Skip(query.Offset).Take(query.Limit))
        {
            string? text = null;
            if (shared is not null && group.RelatedUrlId is { } id)
            {
                shared.Parameters["$id"].Value = id;
                text = shared.ExecuteScalar() as string;
            }

            var tally = tallies[(group.Detail, group.RelatedUrlId)];
            groups.Add(new ClearedGroup(group.Detail, group.RelatedUrl, text, group.Pages, tally[0], tally[1], tally[2]));
        }

        return new ClearedPage(check.Id, pair.Verdict.Comparable, ClearedLevel.Groups, pass.Cleared.Count, query.Offset, [], groups);
    }

    /// <summary>
    /// The rows a check flagged then and not now, in the other crawl's order by importance (as its rows come by default:
    /// <see cref="IssueGroupReader.ImportanceOrder"/>), kept with the check's join.
    /// </summary>
    private static IReadOnlyList<ClearedRow> ClearedInOrder(GridReadContext now, GridReadContext then, ComparisonPair pair, string checkId) =>
        KeptLarge<IReadOnlyList<ClearedRow>>(now, then, $"cleared|{checkId}", () =>
        {
            var byId = RowPass(now, then, pair, checkId).Cleared.ToDictionary(row => row.ThenUrlId);
            if (byId.Count == 0)
            {
                return (IReadOnlyList<ClearedRow>)[];
            }

            List<ClearedRow> ordered = new(byId.Count);
            foreach (var urlId in ImportanceOrder(then, checkId))
            {
                if (byId.TryGetValue(urlId, out var row))
                {
                    ordered.Add(row);
                }
            }

            return ordered;
        });

    /// <summary>A check's rows in one file in the order its rows come by default: by importance, then the URL, then its id.</summary>
    private static IEnumerable<int> ImportanceOrder(GridReadContext context, string checkId)
    {
        var used = IssueCatalog.Find(checkId)?.Subject == IssueSubject.Url ? "s.used_on" : "s.inlinks";
        var sql = context.Summary.Available
            ? $"""
               SELECT i.url_id FROM issues i JOIN urls u ON u.id = i.url_id LEFT JOIN url_summary s ON s.url_id = i.url_id
               WHERE i.issue = $check
               ORDER BY {used} DESC NULLS LAST, s.depth ASC NULLS LAST, u.url ASC, i.url_id ASC
               """
            : $"""
               SELECT i.url_id FROM issues i JOIN urls u ON u.id = i.url_id
               WHERE i.issue = $check
               ORDER BY CASE WHEN u.depth >= {CrawlDepth.UNLINKED} THEN NULL ELSE u.depth END ASC NULLS LAST, u.url ASC, i.url_id ASC
               """;
        using var command = context.Command(sql, [new("$check", checkId)]);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            yield return reader.GetInt32(0);
        }
    }

    /// <summary>The URLs (ids in the other crawl) of one cause then, named by its detail and its other URL's text.</summary>
    private static HashSet<int> GroupMembers(GridReadContext then, IssueDefinition check, ClearedGroupKey group)
    {
        int? related = null;
        if (group.RelatedUrl is { } url)
        {
            using var lookup = new ComparisonLookup(then);
            if (lookup.Find(url) is not { } found)
            {
                return [];
            }

            related = found.Id;
        }

        return [.. Members(then, check).Where(member => member.Detail == group.Detail && member.RelatedUrlId == related).Select(member => member.UrlId)];
    }

    /// <summary>The text a duplicate cluster's pages share, as the Issues screen names it: none for the content checks.</summary>
    private static string? SharedTextColumn(IssueDefinition check) =>
        check == IssueCatalog.TITLE_DUPLICATE ? "title"
        : check == IssueCatalog.DESCRIPTION_DUPLICATE ? "meta_description"
        : check == IssueCatalog.H1_DUPLICATE ? "h1"
        : check == IssueCatalog.OG_TITLE_DUPLICATE ? "og_title"
        : check == IssueCatalog.OG_DESCRIPTION_DUPLICATE ? "og_description"
        : null;
}
