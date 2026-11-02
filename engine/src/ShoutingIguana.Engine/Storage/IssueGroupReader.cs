using System.Globalization;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// A check's issues for the Issues screen: grouped by their cause, so a template fault is one row rather than one per
/// page, and its rows as a URL grid, all of them or one group's. How a check's issues group is the catalog's to say
/// (<see cref="IssueDefinition.Grouping"/>): a detail that's a measurement ("78 characters") would make a group per value.
/// The issues are the last analysis's, whatever its state, as the Issues screen shows them.
/// </summary>
public static class IssueGroupReader
{
    /// <summary>The fewest URLs a group needs to be called a likely template fault, however large its share.</summary>
    public const int TEMPLATE_MIN_PAGES = 10;

    /// <summary>
    /// One page of a check's groups, largest first. Null when the crawl has never been analysed; a check the analysis
    /// skipped has no total and no groups, and one that isn't grouped has its total and no groups.
    /// </summary>
    /// <exception cref="UrlQueryException">The paging is out of range.</exception>
    public static IssueGroupPage? ReadGroups(string databasePath, string checkId, int offset, int limit, ReadCache? cache = null)
    {
        Dictionary<string, List<string>> errors = new(StringComparer.Ordinal);
        UrlQueryCompiler.ValidatePaging(offset, limit, after: null, errors);
        UrlGridReader.ThrowIfInvalid(errors);

        // A check from a newer engine than this one, which the file holds but the catalog doesn't know, is listed flat.
        var grouping = IssueCatalog.Find(checkId)?.Grouping ?? IssueGrouping.None;
        using var context = GridReadContext.Open(databasePath, cache);
        if (context.Analysis is null)
        {
            return null;
        }

        KeyValuePair<string, object>[] check = [new("$check", checkId)];
        if (Flagged(context, checkId) is not { } total)
        {
            return new IssueGroupPage(checkId, grouping, null, 0, offset, []);
        }

        if (grouping == IssueGrouping.None)
        {
            return new IssueGroupPage(checkId, grouping, total, 0, offset, []);
        }

        var key = grouping == IssueGrouping.RelatedUrl ? "related_url_id" : "detail, related_url_id";
        var groupCount = context.Count($"groups|{checkId}", () =>
            context.Scalar($"SELECT COUNT(*) FROM (SELECT 1 FROM issues WHERE issue = $check GROUP BY {key})", check));

        List<IssueGroup> groups = [];
        using var command = context.Command($"""
            SELECT {(grouping == IssueGrouping.RelatedUrl ? "NULL" : "g.detail")}, g.related_url_id, u.url, g.pages
            FROM (SELECT {key}, COUNT(*) AS pages FROM issues WHERE issue = $check GROUP BY {key}
                  ORDER BY pages DESC, {key} LIMIT {limit.ToString(CultureInfo.InvariantCulture)} OFFSET {offset.ToString(CultureInfo.InvariantCulture)}) g
            LEFT JOIN urls u ON u.id = g.related_url_id
            ORDER BY g.pages DESC, {string.Join(", ", key.Split(", ").Select(column => "g." + column))}
            """, check);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var related = reader.IsDBNull(1) ? (int?)null : reader.GetInt32(1);
            var pages = reader.GetInt64(3);
            groups.Add(new IssueGroup(
                reader.IsDBNull(0) ? null : reader.GetString(0),
                related,
                reader.IsDBNull(2) ? null : reader.GetString(2),
                pages,
                total == 0 ? 0 : (double)pages / total,
                IsLikelyTemplate(grouping, pages, total, related is not null)));
        }

        return new IssueGroupPage(checkId, grouping, total, groupCount, offset, groups);
    }

    /// <summary>
    /// One page of a check's rows as a URL grid: a row per URL the check flagged, with the URL grid's columns and the
    /// issue's own (its detail, the other URL and its status), filtered, searched and sorted as the URL grid is. Narrowed
    /// to one group by <paramref name="group"/>. Null when the crawl has never been analysed.
    /// </summary>
    /// <inheritdoc cref="UrlGridReader.Query" path="/exception"/>
    public static IssueRowPage? ReadRows(string databasePath, string checkId, UrlQuery query, IssueGroupKey? group = null, ReadCache? cache = null)
    {
        var grouping = IssueCatalog.Find(checkId)?.Grouping ?? IssueGrouping.None;
        var scope = new GridScope(checkId, group, grouping);
        var errors = UrlQueryCompiler.Validate(query, scope);
        if (group is not null && grouping == IssueGrouping.None)
        {
            errors["group"] = ["This check lists its issues without groups, so there's no group to narrow them to."];
        }

        UrlGridReader.ThrowIfInvalid(errors);
        using var context = GridReadContext.Open(databasePath, cache);
        if (context.Analysis is null)
        {
            return null;
        }

        return Flagged(context, checkId) is null
            ? new IssueRowPage(null, context.Summary)
            : new IssueRowPage(UrlGridReader.Read(context, query, scope), context.Summary);
    }

    /// <summary>
    /// A group one fix probably clears, as it's in the site's template: it holds at least half of the check's flagged URLs,
    /// and at least <see cref="TEMPLATE_MIN_PAGES"/>, and either they share the other URL involved (the logo link without
    /// text, a canonical every page names) or its detail names a fault in the page's own HTML ("Missing og:image"). How
    /// a URL itself answered ("HTTP 404") is no evidence of a template, however many share it.
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
