using System.Globalization;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// A check's issues for the Issues screen: grouped by their cause, so a template fault is one row rather than one per
/// page, and its rows as a URL grid, all of them or one group's. How a check's issues group is the catalog's to say
/// (<see cref="IssueDefinition.Grouping"/>): a detail that's a measurement ("78 characters") would make a group per value.
/// A check grouped by the other URL is grouped by every URL each issue names, so a page linking to three broken URLs is in
/// three groups, and its groups' pages can add up to more than the check's; one whose detail is each page's own instance of
/// a fault is grouped by the fault (<see cref="IssueDefinition.FaultOf"/>). The issues are the last analysis's, whatever its
/// state, as the Issues screen shows them.
/// </summary>
public static class IssueGroupReader
{
    /// <summary>The fewest URLs a group needs to be called a likely template fault, however large its share.</summary>
    public const int TEMPLATE_MIN_PAGES = 10;

    /// <summary>
    /// One page of a check's groups, largest first. Null when the crawl has never been analysed; a check the analysis
    /// skipped has no total, no group count and no groups, and one that isn't grouped has its total and no groups.
    /// </summary>
    /// <exception cref="UrlQueryException">The paging is out of range.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public static IssueGroupPage? ReadGroups(string databasePath, string checkId, int offset, int limit, ReadCache? cache = null,
        CancellationToken cancellationToken = default)
    {
        Dictionary<string, List<string>> errors = new(StringComparer.Ordinal);
        UrlQueryCompiler.ValidatePaging(offset, limit, after: null, errors);
        UrlGridReader.ThrowIfInvalid(errors);
        return GridReadContext.Read(databasePath, cache, cancellationToken, context => ReadGroups(context, checkId, offset, limit));
    }

    private static IssueGroupPage? ReadGroups(GridReadContext context, string checkId, int offset, int limit)
    {
        // A check from a newer engine than this one, which the file holds but the catalog doesn't know, is listed flat.
        var definition = IssueCatalog.Find(checkId);
        var grouping = definition?.Grouping ?? IssueGrouping.None;
        if (context.Analysis is null)
        {
            return null;
        }

        KeyValuePair<string, object>[] check = [new("$check", checkId)];
        if (Flagged(context, checkId) is not { } total)
        {
            return new IssueGroupPage(checkId, grouping, null, null, offset, []);
        }

        if (grouping == IssueGrouping.None)
        {
            return new IssueGroupPage(checkId, grouping, total, 0, offset, []);
        }

        var counted = Counted(context, definition!, grouping);
        var groupCount = context.Count($"groups|{checkId}", () => context.Scalar($"SELECT COUNT(*) FROM ({counted})", check));

        List<IssueGroup> groups = [];
        using var command = context.Command($"""
            SELECT g.detail, g.related_url_id, u.url, g.pages
            FROM (SELECT * FROM ({counted}) ORDER BY pages DESC, detail, related_url_id
                  LIMIT {limit.ToString(CultureInfo.InvariantCulture)} OFFSET {offset.ToString(CultureInfo.InvariantCulture)}) g
            LEFT JOIN urls u ON u.id = g.related_url_id
            ORDER BY g.pages DESC, g.detail, g.related_url_id
            """, check);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            // An id past the crawl's (a crafted file's) names no URL.
            var related = reader.IsDBNull(1) || reader.GetInt64(1) is < int.MinValue or > int.MaxValue ? (int?)null : reader.GetInt32(1);
            var pages = reader.GetInt64(3);

            // Links to sections missing on the page itself name no other URL, and are a fault in the page's own HTML.
            var sharesCause = related is not null || definition == IssueCatalog.BROKEN_FRAGMENT_LINKS;
            groups.Add(new IssueGroup(
                reader.IsDBNull(0) ? null : reader.GetString(0),
                related,
                reader.IsDBNull(2) ? null : reader.GetString(2),
                pages,
                total == 0 ? 0 : (double)pages / total,
                IsLikelyTemplate(grouping, pages, total, sharesCause)));
        }

        return new IssueGroupPage(checkId, grouping, total, groupCount, offset, groups);
    }

    /// <summary>
    /// The check's groups counted, <c>(detail, related_url_id, pages)</c>, in no order, from where the analysis keeps them:
    /// every URL each issue names, those naming none (<see cref="Analysis.Models.IssueHit.NO_OTHER_URL"/>) a group without
    /// one; the fault each issue is an instance of; or, for the rest and for an earlier engine's analysis, each issue's own
    /// detail and first other URL.
    /// </summary>
    private static string Counted(GridReadContext context, IssueDefinition check, IssueGrouping grouping) => grouping switch
    {
        IssueGrouping.RelatedUrl when context.HasIssueTargets => $"""
            SELECT NULL AS detail, NULLIF(related_url_id, {IssueHit.NO_OTHER_URL}) AS related_url_id, COUNT(*) AS pages
            FROM issue_targets WHERE issue = $check GROUP BY issue_targets.related_url_id
            """,
        IssueGrouping.PageDetail when context.HasIssueTargets && check.FaultOf is not null =>
            "SELECT fault AS detail, NULL AS related_url_id, COUNT(*) AS pages FROM issue_faults WHERE issue = $check GROUP BY fault",
        IssueGrouping.RelatedUrl =>
            "SELECT NULL AS detail, related_url_id, COUNT(*) AS pages FROM issues WHERE issue = $check GROUP BY related_url_id",
        _ => "SELECT detail, related_url_id, COUNT(*) AS pages FROM issues WHERE issue = $check GROUP BY detail, related_url_id"
    };

    /// <summary>
    /// The group one issue is in, as <see cref="Counted"/> makes a check's groups: the key that finds it among them, and that
    /// narrows the check's rows to it. An issue of a check grouped by the other URL is in the group of each URL it names: the
    /// one it shows as the other URL involved when that is one of them (a duplicate shows a page that repeats it, but is
    /// grouped under its cluster's first page), otherwise the first. Null for a check that isn't grouped, one this engine
    /// doesn't know, and an issue without a detail of a check grouped by the fault its detail shows.
    /// </summary>
    internal static IssueGroupKey? GroupOf(GridReadContext context, string checkId, int urlId, string? detail, int? relatedUrlId)
    {
        var check = IssueCatalog.Find(checkId);
        switch (check?.Grouping)
        {
            case null or IssueGrouping.None:
                return null;
            case IssueGrouping.RelatedUrl when context.HasIssueTargets:
                List<int> named = [];
                using (var command = context.Command("SELECT related_url_id FROM issue_targets WHERE url_id = $url AND issue = $check ORDER BY related_url_id",
                           [new("$url", urlId), new("$check", checkId)]))
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        named.Add(reader.GetInt32(0));
                    }
                }

                if (named.Count == 0)
                {
                    return null;
                }

                var group = relatedUrlId is { } related && named.Contains(related) ? related : named[0];
                return new IssueGroupKey(null, group == IssueHit.NO_OTHER_URL ? null : group);
            case IssueGrouping.RelatedUrl:
                return new IssueGroupKey(null, relatedUrlId);
            case IssueGrouping.PageDetail when context.HasIssueTargets && check.FaultOf is { } faultOf:
                return detail is null ? null : new IssueGroupKey(faultOf(detail), null);
            default:
                return new IssueGroupKey(detail, relatedUrlId);
        }
    }

    /// <summary>
    /// One page of a check's rows as a URL grid: a row per URL the check flagged, with the URL grid's columns and the
    /// issue's own (its detail, the other URL and its status), filtered, searched and sorted as the URL grid is. Narrowed
    /// to one group by <paramref name="group"/>. Null when the crawl has never been analysed.
    /// </summary>
    /// <inheritdoc cref="UrlGridReader.Query" path="/exception"/>
    public static IssueRowPage? ReadRows(string databasePath, string checkId, UrlQuery query, IssueGroupKey? group = null, ReadCache? cache = null,
        CancellationToken cancellationToken = default)
    {
        var grouping = IssueCatalog.Find(checkId)?.Grouping ?? IssueGrouping.None;
        var scope = new GridScope(checkId, group, grouping);
        var errors = UrlQueryCompiler.Validate(query, scope);
        if (group is not null && grouping == IssueGrouping.None)
        {
            errors["group"] = ["This check lists its issues without groups, so there's no group to narrow them to."];
        }

        UrlGridReader.ThrowIfInvalid(errors);
        return UrlGridReader.Refusing(query.Filters, () => GridReadContext.Read(databasePath, cache, cancellationToken, context =>
        {
            if (context.Analysis is null)
            {
                return null;
            }

            return Flagged(context, checkId) is null
                ? new IssueRowPage(null, context.Summary)
                : new IssueRowPage(UrlGridReader.Read(context, query, scope), context.Summary);
        }));
    }

    /// <summary>
    /// A group one fix probably clears, as it's in the site's template: it holds at least half of the check's flagged URLs,
    /// and at least <see cref="TEMPLATE_MIN_PAGES"/>, and either they share the other URL involved (the logo link without
    /// text, a canonical every page names) or its detail names a fault in the page's own HTML ("Missing og:image"). How
    /// a URL itself answered ("HTTP 404") is no evidence of a template, however many share it. Where a check's detail takes
    /// only a value or two (no H1 at all, or an empty one), it says no more than that most of its pages share the fault.
    /// </summary>
    public static bool IsLikelyTemplate(IssueGrouping grouping, long pages, long total, bool sharesUrl) =>
        pages >= TEMPLATE_MIN_PAGES && pages * 2 >= total && (sharesUrl || grouping == IssueGrouping.PageDetail);

    /// <summary>The URLs the check flagged in the last analysis; null when the analysis didn't run it.</summary>
    private static long? Flagged(GridReadContext context, string checkId)
    {
        using var command = context.Command("SELECT url_count FROM issue_types WHERE id = $check", [new("$check", checkId)]);
        return command.ExecuteScalar() is long count ? count : null;
    }
}
