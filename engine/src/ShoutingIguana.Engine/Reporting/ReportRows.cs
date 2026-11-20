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

    /// <summary>
    /// Evidence the plain report leaves out, as it says nothing a site owner can act on: that an address "is a page", how
    /// long checking it took (the audit's S-17); and, where the row lists the pages that link to an address, the counts of
    /// them.
    /// </summary>
    private static readonly HashSet<string> NOT_SAID_PLAINLY = new(StringComparer.Ordinal) { "type", "responseMs" };

    private static readonly HashSet<string> SAID_BY_WHERE_IT_IS_LINKED = new(StringComparer.Ordinal) { "inlinks", "linkingPages" };

    /// <param name="plainWords">The problem is in plain words, so a detail that has plain words (<see cref="PLAIN_DETAIL"/>) is said in them.</param>
    /// <param name="wording">
    /// Whose words each row is in: Simple's, where the detail and the evidence are said as Simple's screens say them
    /// (<see cref="ReportPlainDetails"/>, <see cref="ReportPlainWords"/>), or the engine's own, a fact with its unit.
    /// </param>
    /// <param name="origin">The site's own origin, which a plain sentence leaves off an address.</param>
    public static IReadOnlyList<ReportRow> Read(string crawlPath, IssueDefinition check, int listed, ExportSettings export, bool plainWords,
        CancellationToken cancellationToken, ReportWording wording = ReportWording.Technical, string? origin = null)
    {
        var simple = wording == ReportWording.Plain;
        var plain = plainWords && simple && PLAIN_DETAIL.TryGetValue(check.Id, out var said) ? said : default;
        List<string> evidence =
        [
            .. check.Evidence.Where(column => !SAID_BY_THE_DETAIL.Contains(column) && column != plain.Column && COLUMNS.ContainsKey(column)
                && !(simple && (NOT_SAID_PLAINLY.Contains(column) || (check.References == IssueReferences.Sources && SAID_BY_WHERE_IT_IS_LINKED.Contains(column)))))
        ];
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
        string? Detail(IReadOnlyList<object?> values)
        {
            var written = detail < 0 ? null : values[detail] as string;
            return (plainAt < 0 ? null : plain.Say(values[plainAt])) ?? (simple && written is not null ? ReportPlainDetails.Say(check.Id, written) : null) ?? written;
        }

        return
        [
            .. page.Rows.Select(row =>
            {
                var said = Detail(row.Values);
                return new ReportRow(
                    row.Id,
                    (string)row.Values[url]!,
                    said,
                    related < 0 ? null : row.Values[related] as string,
                    [
                        .. shown.Select(column => simple ? SaidPlainly(column.Column, row.Values[column.At], origin, (string)row.Values[url]!) : Said(column.Column, row.Values[column.At]))
                            .OfType<string>().Where(evidence => !(simple && said is not null && SaidByTheDetail(said, evidence)))
                    ]);
            })
        ];
    }

    /// <summary>
    /// Whether a line of evidence only repeats the row's plain detail: it is in the detail word for word, or every number
    /// and quoted text it holds is. "5 clicks from the home page; at most 4 is advised" needs no "5 clicks from the home
    /// page" after it, nor "The main heading is a picture: “Logo”" a "Main heading: “Logo”" (the audit's S-17: lines that
    /// say one thing three times). The technical row keeps each column, as its grid does.
    /// </summary>
    internal static bool SaidByTheDetail(string detail, string evidence)
    {
        if (detail.Contains(evidence, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var held = System.Text.RegularExpressions.Regex.Matches(evidence, @"“([^”]*)”|\d[\d,.]*\d|\d");
        return held.Count > 0 && held.All(match => match.Groups[1].Success
            ? detail.Contains(match.Groups[1].Value, StringComparison.Ordinal)
            : System.Text.RegularExpressions.Regex.IsMatch(detail, $@"(?<![\d,.]){System.Text.RegularExpressions.Regex.Escape(match.Value)}(?![\d,]|\.\d)"));
    }

    /// <summary>
    /// One value of a check's evidence as Simple says it. The page's own words stay quoted in full, as the technical row
    /// has them (the report names a page by its address, so its title isn't said elsewhere, and a text too long is shown
    /// whole beside where it's cut); everything else is the app's sentence (<see cref="ReportPlainWords.Evidence"/>).
    /// </summary>
    internal static string? SaidPlainly(UrlColumnInfo column, object? value, string? origin, string? url) => (column.Id, value) switch
    {
        (_, null) => null,
        ("title" or "description", string text) => $"“{text}”",
        // After where a chain of forwards ends, "It forwards to another page" says nothing.
        ("redirectKind", Urls.RedirectKinds.OTHER) => null,
        _ => ReportPlainWords.Evidence(column.Id, value, origin, url)
    };

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
