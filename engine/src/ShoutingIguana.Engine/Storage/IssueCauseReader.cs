using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// The last analysis's checks as work to do, rather than rows: how many separate things each check's issues come to, how
/// many of those are likely in the site's template, and what a fix of it also clears among the checks the catalog says it
/// does (<see cref="IssueDefinition.Clears"/>). Counted from the issues the analysis stored, grouped as the Issues screen
/// groups them (<see cref="IssueGroupReader"/>), so "about 40 things to change, 3 in the template" and the groups shown
/// never disagree.
/// </summary>
public static class IssueCauseReader
{
    /// <summary>
    /// Every check of the catalog that found something on the last analysis, by id. Null when the crawl has never been
    /// analysed. A check the catalog doesn't know (a newer engine's) is left out: how its issues group isn't known.
    /// </summary>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public static AnalysisCauses? Read(string databasePath, ReadCache? cache = null, CancellationToken cancellationToken = default) =>
        GridReadContext.Read(databasePath, cache, cancellationToken, context => context.Analysis is { } analysis
            ? context.Cached("causes", () => new AnalysisCauses(analysis.AnalyzedAt, Read(context)))
            : null);

    private static Dictionary<string, CheckCauses> Read(GridReadContext context)
    {
        Dictionary<string, long> counts = new(StringComparer.Ordinal);
        using (var command = context.Command("SELECT id, url_count FROM issue_types WHERE url_count > 0", []))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                counts[reader.GetString(0)] = reader.GetInt64(1);
            }
        }

        Dictionary<string, CheckCauses> causes = new(StringComparer.Ordinal);
        foreach (var check in IssueCatalog.All.Where(check => counts.ContainsKey(check.Id)))
        {
            var (things, template) = Causes(context, check, counts[check.Id]);
            causes[check.Id] = new CheckCauses(things, template, Clears(context, check, counts));
        }

        return causes;
    }

    /// <summary>
    /// The separate things to change among a check's flagged URLs, and how many of them are likely its template's. A check
    /// that doesn't group is one thing when it flagged nearly every page (<see cref="IssueGroupReader.IsLikelyTemplateCheck"/>),
    /// else a thing a URL; so is a check grouped by how each URL answered, as two 404s are two fixes. A grouped check's
    /// group is one thing when its URLs share a cause (the other URL each names, or a template fault,
    /// <see cref="IssueGroupReader.IsLikelyTemplate"/>), and a thing a URL when they don't; never more than the URLs it
    /// flagged, as a page in several groups (each image it names, each broken URL) is one page to change at most.
    /// </summary>
    private static (long Causes, long InTemplate) Causes(GridReadContext context, IssueDefinition check, long flagged)
    {
        switch (check.Grouping)
        {
            case IssueGrouping.None:
                return IssueGroupReader.IsLikelyTemplateCheck(check, flagged, context.Analysis!.PagesAnalyzed) ? (1, 1) : (flagged, 0);
            case IssueGrouping.ResponseDetail:
                return (flagged, 0);
        }

        // As IssueGroupReader decides a group's likely template: 10 URLs or more, half the check's or more, and a shared
        // other URL, or a fault in the page's own HTML. A link to a section missing on its own page shares its cause too.
        var shares = check == IssueCatalog.BROKEN_FRAGMENT_LINKS ? "1" : "(related_url_id IS NOT NULL)";
        var ownFault = check.Grouping == IssueGrouping.PageDetail ? "1" : "0";
        using var command = context.Command($"""
            SELECT COALESCE(SUM(CASE WHEN template OR shares THEN 1 ELSE pages END), 0), COALESCE(SUM(template), 0)
            FROM (SELECT pages, shares,
                         (pages >= {IssueGroupReader.TEMPLATE_MIN_PAGES} AND pages * 2 >= $total AND (shares OR {ownFault})) AS template
                  FROM (SELECT pages, {shares} AS shares FROM ({IssueGroupReader.Counted(context, check, check.Grouping)})))
            """, [new("$check", check.Id), new("$total", flagged)]);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return (flagged, 0);
        }

        // Never more things than URLs flagged: a page naming many images without dimensions is one page's markup to
        // change, and its images' templates are often one, which no group shows; a page linking to many broken URLs has
        // them counted by the check of those URLs.
        var things = Math.Min(reader.GetInt64(0), flagged);
        return (things, Math.Min(reader.GetInt64(1), things));
    }

    /// <summary>
    /// The checks a fix of <paramref name="check"/> clears, as the catalog names them, each with the pages it clears there:
    /// those whose every target is one of the check's URLs. Counted from every target each page names, which an analysis
    /// keeps from <see cref="CrawlSchema"/>'s issue targets on; an earlier one's keeps only the first, which can't tell a
    /// page with a second broken target, so it's said of none.
    /// </summary>
    private static IReadOnlyList<ClearedCheck> Clears(GridReadContext context, IssueDefinition check, IReadOnlyDictionary<string, long> counts)
    {
        if (check.Clears.Count == 0 || !context.HasIssueTargets)
        {
            return [];
        }

        List<ClearedCheck> cleared = [];
        foreach (var other in check.Clears)
        {
            if (!counts.TryGetValue(other, out var flagged))
            {
                continue;
            }

            var pages = context.Scalar("""
                SELECT COUNT(*) FROM (
                    SELECT t.url_id FROM issue_targets t WHERE t.issue = $other
                    GROUP BY t.url_id
                    HAVING MIN(EXISTS (SELECT 1 FROM issues i WHERE i.issue = $check AND i.url_id = t.related_url_id)) = 1)
                """, [new("$other", other), new("$check", check.Id)]);
            if (pages > 0)
            {
                cleared.Add(new ClearedCheck(other, pages, pages == flagged));
            }
        }

        return cleared;
    }
}
