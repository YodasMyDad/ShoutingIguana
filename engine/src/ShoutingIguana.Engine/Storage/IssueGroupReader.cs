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

    /// <summary>The part of the pages audited, in tenths, a check that doesn't group must flag to be called a likely template fault.</summary>
    public const int TEMPLATE_CHECK_TENTHS = 9;

    /// <summary>
    /// One page of a check's groups, largest first. Null when the crawl has never been analysed; a check the analysis
    /// skipped has no total, no group count and no groups, and one that isn't grouped has its total and no groups.
    /// </summary>
    /// <exception cref="UrlQueryException">The paging is out of range.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public static IssueGroupPage? ReadGroups(string databasePath, string checkId, int offset, int limit, ReadCache? cache = null,
        CancellationToken cancellationToken = default) =>
        ReadGroups(databasePath, checkId, offset, limit, sort: null, cache, cancellationToken);

    /// <summary>
    /// One page of a check's groups, in <paramref name="sort"/>'s order: by default the largest first, and for
    /// <c>image-too-large</c> the largest file first. Each group names what its related URL is, and a duplicate cluster what
    /// its pages share. Null when the crawl has never been analysed.
    /// </summary>
    /// <exception cref="UrlQueryException">The paging is out of range, or the groups can't be in that order.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public static IssueGroupPage? ReadGroups(string databasePath, string checkId, int offset, int limit, IssueGroupSort? sort, ReadCache? cache = null,
        CancellationToken cancellationToken = default)
    {
        ValidateGroups(checkId, offset, limit, sort);
        return GridReadContext.Read(databasePath, cache, cancellationToken, context => ReadGroups(context, checkId, offset, limit, sort));
    }

    /// <summary>
    /// One page of a check's groups, as <see cref="ReadGroups(string, string, int, int, IssueGroupSort?, ReadCache?, CancellationToken)"/>
    /// reads them, compared with another crawl of the site (the fix loop): each group gains how it moved since
    /// (<see cref="IssueGroup.Change"/>), and the page how far the two can be compared and how many causes then have no
    /// URL now (<see cref="IssueGroupPage.Compared"/>). The order, the groups and the paging are as without it. Both files
    /// are read in one snapshot each (<see cref="CrawlComparisonReader"/>). Null when this crawl was never analysed; when
    /// the other wasn't, the page is as without it.
    /// </summary>
    /// <param name="compareWith">The other crawl's file.</param>
    /// <exception cref="UrlQueryException">The paging is out of range, or the groups can't be in that order.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public static IssueGroupPage? ReadGroups(string databasePath, string checkId, int offset, int limit, IssueGroupSort? sort, string compareWith,
        ReadCache? cache = null, CancellationToken cancellationToken = default)
    {
        ValidateGroups(checkId, offset, limit, sort);
        return GridReadContext.Read(databasePath, cache, cancellationToken, context => ReadGroups(context, checkId, offset, limit, sort) is not { } page
            ? null
            : GridReadContext.Read(compareWith, cache, context.Token, other => CrawlComparisonReader.GroupChangesOf(context, other, checkId, page.Groups) is { } changes
                ? page with { Compared = changes.Compared, Groups = [.. page.Groups.Select((group, index) => group with { Change = changes.Changes[index] })] }
                : page));
    }

    private static void ValidateGroups(string checkId, int offset, int limit, IssueGroupSort? sort)
    {
        Dictionary<string, List<string>> errors = new(StringComparer.Ordinal);
        UrlQueryCompiler.ValidatePaging(offset, limit, after: null, errors);
        if (sort == IssueGroupSort.Size && IssueCatalog.Find(checkId)?.Grouping != IssueGrouping.RelatedUrl)
        {
            errors["sort"] = ["This check's groups aren't each a URL, so they have no file size to be ordered by."];
        }

        UrlGridReader.ThrowIfInvalid(errors);
    }

    /// <summary>The order a check's groups come in when none is asked for: <c>image-too-large</c>'s largest file first, any other's largest group.</summary>
    public static IssueGroupSort DefaultSort(string checkId) => checkId == IssueCatalog.IMAGE_TOO_LARGE.Id ? IssueGroupSort.Size : IssueGroupSort.Pages;

    private static IssueGroupPage? ReadGroups(GridReadContext context, string checkId, int offset, int limit, IssueGroupSort? sort)
    {
        // A check from a newer engine than this one, which the file holds but the catalog doesn't know, is listed flat.
        var definition = IssueCatalog.Find(checkId);
        var grouping = definition?.Grouping ?? IssueGrouping.None;
        var order = sort ?? DefaultSort(checkId);
        if (context.Analysis is null)
        {
            return null;
        }

        KeyValuePair<string, object>[] check = [new("$check", checkId)];
        if (Flagged(context, checkId) is not { } total)
        {
            return new IssueGroupPage(checkId, grouping, null, null, offset, []) { Sort = order };
        }

        if (grouping == IssueGrouping.None)
        {
            return new IssueGroupPage(checkId, grouping, total, 0, offset, [])
            {
                Sort = order, LikelyTemplate = IsLikelyTemplateCheck(definition, total, context.Analysis.PagesAnalyzed)
            };
        }

        var counted = Counted(context, definition!, grouping);
        var groupCount = context.Count($"groups|{checkId}", () => context.Scalar($"SELECT COUNT(*) FROM ({counted})", check));

        // By size: the file the related URL ends at, through its redirects where a URL summary says where they end.
        var end = context.Mode == GridMode.Summary ? "COALESCE((SELECT f.final_url_id FROM url_summary f WHERE f.url_id = c.related_url_id), c.related_url_id)" : "c.related_url_id";
        var (sized, inner, outer) = order == IssueGroupSort.Size
            ? ($"SELECT c.*, (SELECT z.size_bytes FROM responses z WHERE z.url_id = {end}) AS size FROM ({counted}) c",
               "size IS NULL, size DESC, size * pages DESC, related_url_id",
               "g.size IS NULL, g.size DESC, g.size * g.pages DESC, g.related_url_id")
            : (counted, "pages DESC, detail, related_url_id", "g.pages DESC, g.detail, g.related_url_id");

        List<IssueGroup> groups = [];
        using var command = context.Command($"""
            SELECT g.detail, g.related_url_id, u.url, g.pages
            FROM (SELECT * FROM ({sized}) ORDER BY {inner}
                  LIMIT {limit.ToString(CultureInfo.InvariantCulture)} OFFSET {offset.ToString(CultureInfo.InvariantCulture)}) g
            LEFT JOIN urls u ON u.id = g.related_url_id
            ORDER BY {outer}
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

        reader.Close();
        return new IssueGroupPage(checkId, grouping, total, groupCount, offset, Described(context, definition!, groups)) { Sort = order };
    }

    /// <summary>The groups with what each related URL is, and for a cluster of duplicate texts, the text.</summary>
    private static List<IssueGroup> Described(GridReadContext context, IssueDefinition check, List<IssueGroup> groups)
    {
        var related = groups.Where(group => group.RelatedUrlId is not null).Select(group => group.RelatedUrlId!.Value).Distinct().ToList();
        var facts = UrlFactsReader.Read(context, related);
        var column = check == IssueCatalog.TITLE_DUPLICATE ? "title"
            : check == IssueCatalog.DESCRIPTION_DUPLICATE ? "meta_description"
            : check == IssueCatalog.H1_DUPLICATE ? "h1"
            : check == IssueCatalog.OG_TITLE_DUPLICATE ? "og_title"
            : check == IssueCatalog.OG_DESCRIPTION_DUPLICATE ? "og_description"
            : null;
        return
        [
            .. groups.Select(group => group.RelatedUrlId is not { } id
                ? group
                : group with { Related = facts.GetValueOrDefault(id), SharedText = column is null ? null : SharedText(context, column, id) })
        ];
    }

    private static string? SharedText(GridReadContext context, string column, int urlId)
    {
        // A crawl made before the share columns has no share text to give.
        using (var has = context.Command("SELECT COUNT(*) FROM pragma_table_info('responses') WHERE name = $column", [new("$column", column)]))
        {
            if (has.ExecuteScalar() is 0L)
            {
                return null;
            }
        }

        using var command = context.Command($"SELECT {column} FROM responses WHERE url_id = $id", [new("$id", urlId)]);
        return command.ExecuteScalar() as string;
    }

    /// <summary>
    /// A check that doesn't group, whose flagged pages are nearly every page audited (at least <see cref="TEMPLATE_CHECK_TENTHS"/>
    /// tenths of them, and at least <see cref="TEMPLATE_MIN_PAGES"/>): no viewport, lang or x-default anywhere is the
    /// site's template, one fix. Only for a check of pages that isn't reported once for the whole site.
    /// </summary>
    public static bool IsLikelyTemplateCheck(IssueDefinition? check, long flagged, long pagesAnalyzed) =>
        check is { Subject: IssueSubject.Page, Grouping: IssueGrouping.None, ReportedOnce: false } &&
        flagged >= TEMPLATE_MIN_PAGES && flagged * 10 >= pagesAnalyzed * TEMPLATE_CHECK_TENTHS;

    /// <summary>
    /// The order a check's rows come in when none is asked for: the URLs that matter most to the site first. A check of
    /// pages by the hyperlinks to each, a check of any URL (a missing image, a redirect) by the pages that use it, then
    /// by clicks from the start and the URL; without a URL summary, which counts those, by clicks and URL alone.
    /// </summary>
    /// <remarks>
    /// A check whose rows come fewest links first (<see cref="IssueRowOrder.FewestLinksFirst"/>: pages missing from the
    /// sitemaps) comes by its pages' hyperlinks, fewest first, then the most words; without a summary, deepest first.
    /// </remarks>
    public static IReadOnlyList<UrlSort> ImportanceOrder(IssueDefinition? check, bool summary) => (check?.RowOrder, summary) switch
    {
        (IssueRowOrder.FewestLinksFirst, true) => [new UrlSort("inlinks"), new UrlSort("wordCount", Descending: true), new UrlSort("url")],
        (IssueRowOrder.FewestLinksFirst, false) => [new UrlSort("depth", Descending: true), new UrlSort("url")],
        (_, true) => [new UrlSort(check?.Subject == IssueSubject.Url ? "usedOn" : "inlinks", Descending: true), new UrlSort("depth"), new UrlSort("url")],
        _ => [new UrlSort("depth"), new UrlSort("url")]
    };

    /// <summary>
    /// The check's groups counted, <c>(detail, related_url_id, pages)</c>, in no order, from where the analysis keeps them:
    /// every URL each issue names, those naming none (<see cref="Analysis.Models.IssueHit.NO_OTHER_URL"/>) a group without
    /// one; the fault each issue is an instance of; or, for the rest and for an earlier engine's analysis, each issue's own
    /// detail and first other URL.
    /// </summary>
    internal static string Counted(GridReadContext context, IssueDefinition check, IssueGrouping grouping) => grouping switch
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
    /// The check's issues by the group each is in, <c>(detail, related_url_id, url_id)</c>, in no order: the rows behind
    /// <see cref="Counted"/>'s counts, a URL in the group of each URL its issue names.
    /// </summary>
    internal static string Members(GridReadContext context, IssueDefinition check, IssueGrouping grouping) => grouping switch
    {
        IssueGrouping.RelatedUrl when context.HasIssueTargets =>
            $"SELECT NULL AS detail, NULLIF(related_url_id, {IssueHit.NO_OTHER_URL}) AS related_url_id, url_id FROM issue_targets WHERE issue = $check",
        IssueGrouping.PageDetail when context.HasIssueTargets && check.FaultOf is not null =>
            "SELECT fault AS detail, NULL AS related_url_id, url_id FROM issue_faults WHERE issue = $check",
        IssueGrouping.RelatedUrl => "SELECT NULL AS detail, related_url_id, url_id FROM issues WHERE issue = $check",
        _ => "SELECT detail, related_url_id, url_id FROM issues WHERE issue = $check"
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
        var scope = ValidateRows(checkId, query, group);
        return UrlGridReader.Refusing(query.Filters, () => GridReadContext.Read(databasePath, cache, cancellationToken, context => ReadRows(context, checkId, query, scope)));
    }

    /// <summary>
    /// One page of a check's rows, as <see cref="ReadRows(string, string, UrlQuery, IssueGroupKey?, ReadCache?, CancellationToken)"/>
    /// reads them, compared with another crawl of the site (the fix loop): the page gains how far the two can be compared
    /// (<see cref="IssueRowPage.Compared"/>) and how each row moved since (<see cref="IssueRowPage.Changes"/>), by a few
    /// index seeks a row in the other file. The order, the columns and the paging are as without it. Null when this crawl
    /// was never analysed; when the other wasn't, the page is as without it.
    /// </summary>
    /// <param name="compareWith">The other crawl's file.</param>
    /// <inheritdoc cref="UrlGridReader.Query" path="/exception"/>
    public static IssueRowPage? ReadRows(string databasePath, string checkId, UrlQuery query, IssueGroupKey? group, string compareWith, ReadCache? cache = null,
        CancellationToken cancellationToken = default)
    {
        var scope = ValidateRows(checkId, query, group);
        return UrlGridReader.Refusing(query.Filters, () => GridReadContext.Read(databasePath, cache, cancellationToken, context =>
            ReadRows(context, checkId, query, scope) is not { } page ? null
            : page.Page is not { } rows ? page
            : GridReadContext.Read(compareWith, cache, context.Token, other =>
                CrawlComparisonReader.RowChangesOf(context, other, checkId, [.. rows.Rows.Select(row => row.Id)]) is { } changes
                    ? page with { Compared = changes.Compared, Changes = changes.Changes }
                    : page)));
    }

    private static GridScope ValidateRows(string checkId, UrlQuery query, IssueGroupKey? group)
    {
        var grouping = IssueCatalog.Find(checkId)?.Grouping ?? IssueGrouping.None;
        var scope = new GridScope(checkId, group, grouping);
        var errors = UrlQueryCompiler.Validate(query, scope);
        if (group is not null && grouping == IssueGrouping.None)
        {
            errors["group"] = ["This check lists its issues without groups, so there's no group to narrow them to."];
        }

        UrlGridReader.ThrowIfInvalid(errors);
        return scope;
    }

    private static IssueRowPage? ReadRows(GridReadContext context, string checkId, UrlQuery query, GridScope scope)
    {
        if (context.Analysis is null)
        {
            return null;
        }

        // Without an order asked for, the URLs that matter most first.
        var ordered = query.Sort.Count > 0 ? query : query with { Sort = ImportanceOrder(IssueCatalog.Find(checkId), context.Summary.Available) };
        return Flagged(context, checkId) is null
            ? new IssueRowPage(null, context.Summary)
            : new IssueRowPage(UrlGridReader.Read(context, ordered, scope), context.Summary);
    }

    /// <summary>
    /// A group one fix probably clears, as it's in the site's template: it holds at least half of the check's flagged URLs,
    /// and at least <see cref="TEMPLATE_MIN_PAGES"/>, and either they share the other URL involved (the logo link without
    /// text, a canonical every page names) or its detail names a fault in the page's own HTML ("Missing og:image"). How
    /// a URL itself answered ("404 Not Found") is no evidence of a template, however many share it. Where a check's detail takes
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
