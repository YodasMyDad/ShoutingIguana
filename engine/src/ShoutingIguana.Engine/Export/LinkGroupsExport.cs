using ShoutingIguana.Engine.Export.Models;
using ShoutingIguana.Engine.Export.Tables;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Export;

/// <summary>
/// The links grid grouped by target, as a file (step 3b): one row a target of the links the query matches, in the order
/// the grouped view shows them, with what its row says: how many pages and links lead to it, how it answered, the text
/// most of its links have, where they sit, whether that's the template, and what the file is (its type, size and pixels
/// after any redirects, and where those end). Read from one snapshot of the file, a page of targets at a time
/// (<see cref="LinkGridReader.EveryGroup"/>): a running crawl's are of one moment of it.
/// </summary>
internal static class LinkGroupsExport
{
    /// <summary>The file's columns, in the grouped view's words.</summary>
    public static readonly string[] TITLES =
    [
        "Target", "Status", "Outcome", "Linking pages", "Links", "Text", "Other texts", "Positions", "Likely template", "Content type", "Size (bytes)",
        "Pixel width", "Pixel height", "Redirects to"
    ];

    /// <summary>What can be checked without the crawl: the grouped view's filters, search, one sort key, hidden checks.</summary>
    /// <exception cref="UrlQueryException">The query can't be run: the errors say why, by field.</exception>
    public static void Check(ExportRequest request)
    {
        var errors = LinkGridReader.ValidateGroups(QueryOf(request));
        if (request.Query.Sort.Count > 1)
        {
            UrlQueryCompiler.Add(errors, "sort", "The links grouped by target sort by one key.");
        }

        UrlGridReader.ThrowIfInvalid(errors);
    }

    public static void Write(ExportRun run, ITableWriter table)
    {
        run.Token.ThrowIfCancellationRequested();
        table.Begin("Links by target", TITLES);
        var cells = new object?[TITLES.Length];
        var live = LinkGridReader.EveryGroup(run.CrawlPath, QueryOf(run.Request), run.Settings.Cache, run.Token, run.Expect, rows =>
        {
            foreach (var row in rows)
            {
                var target = row.Target;
                cells[0] = row.Url;
                cells[1] = target.StatusCode is { } status ? (long)status : null;
                cells[2] = target.Outcome is { } outcome ? ColumnWords.OutcomeTitle(outcome) : null;
                cells[3] = row.Pages;
                cells[4] = row.Links;
                cells[5] = row.Text;
                // No text is one of the texts counted: with none shown, there's no other.
                cells[6] = row.Text is null ? 0L : (long)Math.Max(0, row.Texts - 1);
                cells[7] = string.Join(", ", row.Positions.Select(ColumnWords.PositionTitle));
                cells[8] = row.LikelyTemplate;
                cells[9] = target.ContentType;
                cells[10] = target.SizeBytes;
                cells[11] = target.PixelWidth is { } width ? (long)width : null;
                cells[12] = target.PixelHeight is { } height ? (long)height : null;
                cells[13] = target.Final?.Url;
                table.WriteRow(cells);
            }

            run.Wrote(rows.Count);
        });
        run.CrawlWasRunning = live;
        table.End();
    }

    /// <summary>The grouped view's query from an export's: its filters, search, first sort key and hidden checks.</summary>
    public static LinkGroupQuery QueryOf(ExportRequest request) => new()
    {
        Filters = request.Query.Filters,
        Search = request.Query.Search,
        Sort = request.Query.Sort.Count == 0 ? null : request.Query.Sort[0],
        HiddenChecks = request.Query.HiddenChecks
    };
}
