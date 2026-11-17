using System.Globalization;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Export.Models;
using ShoutingIguana.Engine.Reporting.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Reporting;

/// <summary>
/// A problem's first pages for the report, read as the check's grid reads them: the most important first (by the links to
/// each, then clicks from the start), with what was found, the other URL, and the check's evidence, each value as a line of
/// the report says it.
/// </summary>
internal static class ReportRows
{
    /// <summary>The evidence a detail already says: a title's or description's length is in "78 characters, over 60".</summary>
    private static readonly HashSet<string> SAID_BY_THE_DETAIL = new(StringComparer.Ordinal) { "titleLength", "descriptionLength" };

    private static readonly Dictionary<string, UrlColumnInfo> COLUMNS = UrlGridReader.IssueRowColumns.ToDictionary(column => column.Id, StringComparer.Ordinal);

    public static IReadOnlyList<ReportRow> Read(string crawlPath, IssueDefinition check, int listed, ExportSettings export, CancellationToken cancellationToken)
    {
        List<string> evidence = [.. check.Evidence.Where(column => !SAID_BY_THE_DETAIL.Contains(column) && COLUMNS.ContainsKey(column))];
        var page = IssueGroupReader.ReadRows(crawlPath, check.Id, new UrlQuery { Columns = ["url", "detail", "relatedUrl", .. evidence], Limit = listed },
            cache: export.Cache, cancellationToken: cancellationToken)?.Page;
        if (page is null)
        {
            return [];
        }

        int At(string column) => page.Columns.ToList().IndexOf(column);
        var (url, detail, related) = (At("url"), At("detail"), At("relatedUrl"));
        var shown = evidence.Select(column => (Column: COLUMNS[column], At: At(column))).Where(column => column.At >= 0).ToList();
        return
        [
            .. page.Rows.Select(row => new ReportRow(
                row.Id,
                (string)row.Values[url]!,
                detail < 0 ? null : row.Values[detail] as string,
                related < 0 ? null : row.Values[related] as string,
                [.. shown.Select(column => Said(column.Column, row.Values[column.At])).OfType<string>()]))
        ];
    }

    /// <summary>
    /// One value of a check's evidence as the report says it: a page's text quoted, a URL or a directive with what it is,
    /// a number with its column's name and unit. Null for no value.
    /// </summary>
    internal static string? Said(UrlColumnInfo column, object? value) => (column.Id, value) switch
    {
        (_, null) => null,
        ("title" or "description" or "h1", string text) => $"“{text}”",
        ("metaRobots" or "xRobotsTag", string directive) => $"{directive} ({column.Title})",
        (_, string text) when column.Type == UrlColumnType.Enum => $"{column.Title}: {column.ValueTitles.GetValueOrDefault(text, text)}",
        (_, string text) => $"{column.Title}: {text}",
        (_, long number) => string.Create(CultureInfo.InvariantCulture, $"{column.Title}: {number:N0}{(column.Unit is { } unit ? " " + unit : string.Empty)}"),
        (_, bool flag) => $"{column.Title}: {(flag ? "yes" : "no")}",
        _ => UrlComputedColumns.Text(value)
    };
}
