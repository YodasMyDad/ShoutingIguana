using ShoutingIguana.Engine.Export.Models;

namespace ShoutingIguana.Engine.Export;

/// <summary>
/// Which kinds of export take which of a request's fields. A field that belongs to another kind is refused, never
/// ignored: an export that quietly left out a filter, a check or a cut someone asked for would be a file of something
/// else than they asked for. The API and the command line both ask here, so they refuse the same things.
/// </summary>
public static class ExportFields
{
    private static readonly ExportKind[] GRIDS = [ExportKind.Urls, ExportKind.Links, ExportKind.SitemapUrls, ExportKind.IssueRows];

    private static readonly ExportKind[] REPORT = [ExportKind.Report];

    private static readonly ExportKind[] BLOCKED = [ExportKind.RobotsBlocked];

    private static readonly Dictionary<string, ExportKind[]> OWNERS = new(StringComparer.Ordinal)
    {
        ["columns"] = GRIDS,
        ["filters"] = GRIDS,
        ["search"] = [.. GRIDS, ExportKind.RobotsOrigins],
        ["sort"] = GRIDS,
        ["linkEnds"] = [ExportKind.Urls],
        ["blocked"] = [ExportKind.RobotsOrigins],
        ["group"] = [ExportKind.IssueRows],
        ["sitemapId"] = [ExportKind.SitemapUrls],
        ["origin"] = BLOCKED,
        ["line"] = BLOCKED,
        ["reason"] = BLOCKED,
        ["checkId"] = [ExportKind.IssueRows, ExportKind.Check],
        ["cut"] = REPORT,
        ["examples"] = REPORT,
        ["lookExamples"] = REPORT,
        // The report and the workbook leave the hidden checks out; a check's file leaves them out and is numbered as the
        // workbook is; a grid leaves them out of its issue counts.
        ["hiddenChecks"] = [ExportKind.Report, ExportKind.Workbook, ExportKind.Check, .. GRIDS],
        ["checks"] = [ExportKind.Workbook]
    };

    /// <summary>The kinds whose export is a grid's query: they take <c>columns</c>, <c>filters</c>, <c>search</c> and <c>sort</c>.</summary>
    public static IReadOnlyList<ExportKind> Grids => GRIDS;

    /// <summary>The fields that belong to some kinds only, by the names the API gives them.</summary>
    public static IReadOnlyCollection<string> Names => OWNERS.Keys;

    /// <summary>The kinds that take a field.</summary>
    /// <exception cref="KeyNotFoundException">The field isn't one of <see cref="Names"/>.</exception>
    public static IReadOnlyList<ExportKind> Owners(string field) => OWNERS[field];

    /// <summary>Whether an export of a kind takes a field.</summary>
    public static bool Takes(ExportKind kind, string field) => OWNERS[field].Contains(kind);

    /// <summary>A kind as the API and the command line name it: <c>sitemapUrls</c>.</summary>
    public static string Name(ExportKind kind)
    {
        var name = kind.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }
}
