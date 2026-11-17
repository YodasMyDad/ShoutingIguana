using System.Globalization;
using ShoutingIguana.Engine.Analysis;
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
    /// <summary>The evidence a detail already says: a title's or description's width and length are in "612 px, over 580 (64 characters)".</summary>
    private static readonly HashSet<string> SAID_BY_THE_DETAIL = new(StringComparer.Ordinal) { "titleLength", "descriptionLength", "titleWidth", "descriptionWidth" };

    private static readonly Dictionary<string, UrlColumnInfo> COLUMNS = UrlGridReader.IssueRowColumns.ToDictionary(column => column.Id, StringComparer.Ordinal);

    /// <summary>
    /// Checks whose detail plain words say otherwise, as Simple does, from one of the check's evidence columns: where Google
    /// will likely cut a title, not "612 px, over 580 (64 characters)".
    /// </summary>
    private static readonly Dictionary<string, (string Column, Func<object?, string?> Say)> PLAIN_DETAIL = new(StringComparer.Ordinal)
    {
        [IssueCatalog.TITLE_TOO_LONG.Id] = ("titleInResults", value => ReportWords.CutOff("title", value as string)),
        [IssueCatalog.DESCRIPTION_TOO_LONG.Id] = ("descriptionInResults", value => ReportWords.CutOff("description", value as string)),
        [IssueCatalog.TITLE_TOO_SHORT.Id] = ("titleLength", value => value is long characters ? ReportWords.OnlyCharacters(characters) : null),
        [IssueCatalog.DESCRIPTION_TOO_SHORT.Id] = ("descriptionLength", value => value is long characters ? ReportWords.OnlyCharacters(characters) : null)
    };

    /// <param name="plainWords">The problem is in plain words, so a detail that has plain words (<see cref="PLAIN_DETAIL"/>) is said in them.</param>
    public static IReadOnlyList<ReportRow> Read(string crawlPath, IssueDefinition check, int listed, ExportSettings export, bool plainWords,
        CancellationToken cancellationToken)
    {
        var plain = plainWords && PLAIN_DETAIL.TryGetValue(check.Id, out var said) ? said : default;
        List<string> evidence = [.. check.Evidence.Where(column => !SAID_BY_THE_DETAIL.Contains(column) && column != plain.Column && COLUMNS.ContainsKey(column))];
        var page = IssueGroupReader.ReadRows(crawlPath, check.Id,
            new UrlQuery { Columns = ["url", "detail", "relatedUrl", .. evidence, .. plain.Column is { } column ? [column] : Array.Empty<string>()], Limit = listed },
            cache: export.Cache, cancellationToken: cancellationToken)?.Page;
        if (page is null)
        {
            return [];
        }

        int At(string column) => page.Columns.ToList().IndexOf(column);
        var (url, detail, related, plainAt) = (At("url"), At("detail"), At("relatedUrl"), plain.Column is { } plainColumn ? At(plainColumn) : -1);
        var shown = evidence.Select(column => (Column: COLUMNS[column], At: At(column))).Where(column => column.At >= 0).ToList();
        return
        [
            .. page.Rows.Select(row => new ReportRow(
                row.Id,
                (string)row.Values[url]!,
                (plainAt < 0 ? null : plain.Say(row.Values[plainAt])) ?? (detail < 0 ? null : row.Values[detail] as string),
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
