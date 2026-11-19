using System.CommandLine;
using System.Globalization;
using ShoutingIguana.Cli.Output;
using ShoutingIguana.Engine.Export;
using ShoutingIguana.Engine.Export.Models;
using ShoutingIguana.Engine.Reporting.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Cli.Commands;

/// <summary>
/// <c>si export</c> and <c>si report</c>: what the app's Export writes, from the command line, by the same code
/// (<see cref="CrawlExporter"/>). A grid's export takes the grid's own query: its columns, filters, search and sort.
/// </summary>
internal static class ExportCommand
{
    private static readonly Dictionary<string, ExportKind> KINDS = Enum.GetValues<ExportKind>().ToDictionary(kind => Camel(kind.ToString()), StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, FilterOperator> OPERATORS =
        Enum.GetValues<FilterOperator>().ToDictionary(op => Camel(op.ToString()), StringComparer.OrdinalIgnoreCase);

    public static Command Create()
    {
        var database = new Argument<string>("db") { Description = "A .sicrawl file." };
        var kind = new Option<string>("--kind") { Description = $"What to export: {string.Join(", ", KINDS.Keys)}.", Required = true };
        var output = new Option<string>("--out") { Description = "The file to write: .csv or .xlsx for a grid, .xlsx for the workbook, .html for the report.", Required = true };
        var format = new Option<string?>("--format") { Description = "csv, xlsx or html. Taken from the file's name when left out." };
        var columns = new Option<string?>("--columns") { Description = "A grid's columns, by id, comma-separated (url,statusCode,title). The grid's own when left out." };
        var filters = new Option<string[]>("--filter")
        {
            Description = "A grid's filter, as column:operator[:value], any number of times: statusClass:in:4xx,5xx  depth:lte:3  title:contains:garden  title:isEmpty  responseMs:between:500..2000",
            AllowMultipleArgumentsPerToken = false
        };
        var search = new Option<string?>("--search") { Description = "A grid's quick search; for robotsOrigins, the origins whose name contains it." };
        var sort = new Option<string[]>("--sort") { Description = "A grid's sort, as column or column:desc, up to three times." };
        var linkEnds = new Option<string?>("--link-ends")
        {
            Description = "For urls: only the URLs at one end of the links --link-filter and --link-search match: from (the pages they're on) or to (the URLs they lead to)."
        };
        var linkFilters = new Option<string[]>("--link-filter")
        {
            Description = "With --link-ends: a links grid's filter, as --filter takes one, any number of times: kind:in:hyperlink  to.statusClass:in:4xx",
            AllowMultipleArgumentsPerToken = false
        };
        var linkSearch = new Option<string?>("--link-search") { Description = "With --link-ends: the links grid's quick search." };
        var blocked = new Option<bool?>("--blocked") { Description = "For robotsOrigins: true for only the origins that blocked a URL, false for only those that blocked none." };
        var sitemap = new Option<int?>("--sitemap") { Description = "For sitemapUrls: the sitemap's id." };
        var origin = new Option<string?>("--origin") { Description = "For robotsBlocked: the origin, such as https://www.example.com." };
        var line = new Option<int?>("--line") { Description = "For robotsBlocked: only the URLs this line of robots.txt refused." };
        var reason = new Option<string?>("--reason") { Description = "For robotsBlocked: only the URLs refused for this reason: rule, unreachable or unknown." };
        var conflict = new Option<bool?>("--conflict")
        {
            Description = "For robotsBlocked: true for only the URLs a sitemap lists or a menu links to, false for only the others."
        };
        var check = new Option<string?>("--check") { Description = "For issueRows and check: the check's id, such as title-too-long." };
        var cut = new Option<string?>("--cut") { Description = "For report: full (the default) or summary." };
        var examples = new Option<int>("--examples") { Description = "For report: the first pages listed for each problem to fix.", DefaultValueFactory = _ => ReportOptions.DEFAULT_EXAMPLES };
        var lookExamples = new Option<int>("--look-examples")
        {
            Description = "For report: the first pages listed for each thing worth a look.", DefaultValueFactory = _ => ReportOptions.DEFAULT_LOOK_EXAMPLES
        };
        var words = new Option<string?>("--words") { Description = "For report: whose words its rows are in: plain (the default, a site owner's) or technical (a working SEO's)." };
        var hiddenChecks = new Option<string?>("--hidden-checks")
        {
            Description = "For report, workbook, check and the grids: checks hidden in the app, by id, comma-separated, left out of the lists and counts (title-too-long,thin-content)."
        };
        var onlyChecks = new Option<string?>("--checks") { Description = "For workbook: only these checks, by id, comma-separated: the ones an Issues view listed." };
        var command = new Command("export", "Write what a crawl holds to a file: a grid as CSV or Excel, the workbook of everything, or the report.")
        {
            database, kind, output, format, columns, filters, search, sort, linkEnds, linkFilters, linkSearch, blocked, sitemap, origin, line, reason, conflict, check, cut, examples,
            lookExamples, words, hiddenChecks, onlyChecks
        };

        command.SetAction((parse, cancellationToken) => Task.FromResult(CommandErrors.Run(() =>
        {
            if (!KINDS.TryGetValue(parse.GetValue(kind)!, out var exportKind))
            {
                Console.Error.WriteLine($"There's no kind of export called '{Format.Printable(parse.GetValue(kind))}': there are {string.Join(", ", KINDS.Keys)}.");
                return 2;
            }

            var target = parse.GetValue(output)!;
            if (FormatOf(parse.GetValue(format), target, exportKind) is not { } exportFormat)
            {
                Console.Error.WriteLine("--format must be csv, xlsx or html.");
                return 2;
            }

            // An option that belongs to another kind is refused, never ignored, as the API refuses its field: by the same list.
            (Option Option, string Field)[] owned =
            [
                (columns, "columns"), (filters, "filters"), (search, "search"), (sort, "sort"), (linkEnds, "linkEnds"), (linkFilters, "linkEnds"), (linkSearch, "linkEnds"),
                (blocked, "blocked"), (sitemap, "sitemapId"), (origin, "origin"), (line, "line"), (reason, "reason"), (conflict, "conflict"), (check, "checkId"), (cut, "cut"),
                (examples, "examples"),
                (lookExamples, "lookExamples"), (words, "words"), (hiddenChecks, "hiddenChecks"), (onlyChecks, "checks")
            ];
            var misplaced = owned.Where(option => parse.GetResult(option.Option) is { Implicit: false } && !ExportFields.Takes(exportKind, option.Field)).ToList();
            foreach (var (option, field) in misplaced)
            {
                Console.Error.WriteLine(
                    $"{option.Name} isn't for an export of {ExportFields.Name(exportKind)}: it belongs to {string.Join(" and ", ExportFields.Owners(field).Select(ExportFields.Name))}.");
            }

            if (misplaced.Count > 0)
            {
                return 2;
            }

            if (!TryReason(parse.GetValue(reason), out var blockReason) || !TryCut(parse.GetValue(cut), out var reportCut) || !TryWords(parse.GetValue(words), out var wording) ||
                Query(exportKind, parse.GetValue(columns), parse.GetValue(filters) ?? [], parse.GetValue(search), parse.GetValue(sort) ?? []) is not { } query ||
                !TryLinkEnds(parse.GetValue(linkEnds), parse.GetValue(linkFilters) ?? [], parse.GetValue(linkSearch), out var ends))
            {
                return 2;
            }

            var request = new ExportRequest
            {
                Kind = exportKind,
                Format = exportFormat,
                Query = query with
                {
                    LinkEnds = ends,
                    HiddenChecks = ExportFields.Grids.Contains(exportKind) || exportKind == ExportKind.LinkGroups ? Ids(parse.GetValue(hiddenChecks)) ?? [] : []
                },
                Blocked = parse.GetValue(blocked),
                SitemapId = parse.GetValue(sitemap),
                Origin = parse.GetValue(origin),
                Line = parse.GetValue(line),
                Reason = blockReason,
                Conflict = parse.GetValue(conflict),
                CheckId = parse.GetValue(check),
                Report = new ReportOptions
                {
                    Cut = reportCut,
                    Examples = parse.GetValue(examples),
                    LookExamples = parse.GetValue(lookExamples),
                    Wording = wording,
                    HiddenChecks = Ids(parse.GetValue(hiddenChecks)) ?? [],
                    Checks = Ids(parse.GetValue(onlyChecks))
                }
            };
            return Run(parse.GetValue(database)!, request, target, cancellationToken);
        })));

        return command;
    }

    /// <summary><c>si report</c>: the report alone, as <c>si export --kind report</c> writes it.</summary>
    public static Command CreateReport()
    {
        var database = new Argument<string>("db") { Description = "A .sicrawl file." };
        var output = new Option<string>("--out") { Description = "The HTML file to write.", Required = true };
        var cut = new Option<string?>("--cut") { Description = "full (the default) or summary: the report's first page alone." };
        var examples = new Option<int>("--examples") { Description = "The first pages listed for each problem to fix.", DefaultValueFactory = _ => ReportOptions.DEFAULT_EXAMPLES };
        var lookExamples = new Option<int>("--look-examples")
        {
            Description = "The first pages listed for each thing worth a look.", DefaultValueFactory = _ => ReportOptions.DEFAULT_LOOK_EXAMPLES
        };
        var words = new Option<string?>("--words") { Description = "Whose words its rows are in: plain (the default, a site owner's) or technical (a working SEO's)." };
        var command = new Command("report", "Write the report of a crawl's last analysis: one self-contained HTML file, which prints to a PDF.")
        {
            database, output, cut, examples, lookExamples, words
        };

        command.SetAction((parse, cancellationToken) => Task.FromResult(CommandErrors.Run(() =>
        {
            if (!TryCut(parse.GetValue(cut), out var reportCut) || !TryWords(parse.GetValue(words), out var wording))
            {
                return 2;
            }

            var request = new ExportRequest
            {
                Kind = ExportKind.Report,
                Format = ExportFormat.Html,
                Report = new ReportOptions { Cut = reportCut, Examples = parse.GetValue(examples), LookExamples = parse.GetValue(lookExamples), Wording = wording }
            };
            return Run(parse.GetValue(database)!, request, parse.GetValue(output)!, cancellationToken);
        })));

        return command;
    }

    private static int Run(string path, ExportRequest request, string target, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"No crawl file at {path}.");
            return 2;
        }

        try
        {
            var result = CrawlExporter.Export(path, request, target, cancellationToken: cancellationToken);
            var rows = request.Kind == ExportKind.Report ? string.Empty : $"{Format.Count(result.Rows, "row", "rows")} to ";
            var sheets = result.Sheets > 1 ? $", on {result.Sheets} sheets" : string.Empty;
            Console.WriteLine($"Wrote {rows}{result.Path} ({Format.Bytes(result.Bytes)}{sheets})");
            if (result.UnavailableColumns.Count > 0)
            {
                Console.WriteLine($"  These columns are empty, as they need an analysis of the crawl as it stands: {string.Join(", ", result.UnavailableColumns)}.");
            }

            if (result.TruncatedCells > 0)
            {
                Console.WriteLine(request.Format == ExportFormat.Csv
                    ? $"  {Format.Count(result.TruncatedCells, "field was", "fields were")} cut to the 32,758 characters Excel reads of a CSV field."
                    : $"  {Format.Count(result.TruncatedCells, "cell was", "cells were")} cut to the 32,767 characters an Excel cell holds.");
            }

            if (result.CrawlWasRunning)
            {
                Console.WriteLine("  The crawl was running: the file holds what it had when the export began.");
            }

            return 0;
        }
        catch (ExportException exception)
        {
            Console.Error.WriteLine(exception.Failure == ExportFailure.NotAnalyzed ? $"{exception.Message} Run: si analyze \"{path}\"" : exception.Message);
            return 2;
        }
        catch (UrlQueryException exception)
        {
            foreach (var (field, messages) in exception.Errors)
            {
                Console.Error.WriteLine($"{field}: {Format.Printable(string.Join(" ", messages))}");
            }

            return 2;
        }
        catch (NeedsAnalysisException exception)
        {
            Console.Error.WriteLine($"{exception.Message} Run: si analyze \"{path}\"");
            return 2;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Console.Error.WriteLine("Export cancelled; nothing was written.");
            return AnalyzeCommand.CANCELLED;
        }
    }

    /// <summary>The format asked for, or the one the file's name says; a kind with one format takes it whatever the name.</summary>
    private static ExportFormat? FormatOf(string? format, string target, ExportKind kind) => (format ?? Path.GetExtension(target).TrimStart('.')).ToLowerInvariant() switch
    {
        "csv" => ExportFormat.Csv,
        "xlsx" => ExportFormat.Xlsx,
        "html" or "htm" => ExportFormat.Html,
        _ when format is null => CrawlExporter.Formats(kind)[0],
        _ => null
    };

    private static bool TryReason(string? text, out RobotsBlockReason? reason)
    {
        reason = text?.ToLowerInvariant() switch
        {
            "rule" => RobotsBlockReason.Rule,
            "unreachable" => RobotsBlockReason.Unreachable,
            "unknown" => RobotsBlockReason.Unknown,
            _ => null
        };
        if (text is not null && reason is null)
        {
            Console.Error.WriteLine("--reason must be rule, unreachable or unknown.");
            return false;
        }

        return true;
    }

    private static bool TryWords(string? text, out ReportWording wording)
    {
        wording = string.Equals(text, "technical", StringComparison.OrdinalIgnoreCase) ? ReportWording.Technical : ReportWording.Plain;
        if (text is not null && wording == ReportWording.Plain && !string.Equals(text, "plain", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("--words must be plain or technical.");
            return false;
        }

        return true;
    }

    private static bool TryCut(string? text, out ReportCut cut)
    {
        cut = string.Equals(text, "summary", StringComparison.OrdinalIgnoreCase) ? ReportCut.Summary : ReportCut.Full;
        if (text is not null && cut == ReportCut.Full && !string.Equals(text, "full", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("--cut must be full or summary.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// The links view whose pages or targets the URL grid is narrowed to, as the grid's <c>linkEnds</c> takes it: none
    /// without <c>--link-ends</c>; false, with why on stderr, when an option can't be read.
    /// </summary>
    private static bool TryLinkEnds(string? end, string[] filters, string? search, out UrlLinkEnds? ends)
    {
        ends = null;
        if (end is null)
        {
            if (filters.Length > 0 || search is not null)
            {
                Console.Error.WriteLine("--link-filter and --link-search narrow the links of --link-ends: give it, as from or to.");
                return false;
            }

            return true;
        }

        LinkEnd? which = end.ToLowerInvariant() switch { "from" => LinkEnd.From, "to" => LinkEnd.To, _ => null };
        if (which is null)
        {
            Console.Error.WriteLine("--link-ends must be from (the pages the links are on) or to (the URLs they lead to).");
            return false;
        }

        List<UrlFilter> parsed = [];
        foreach (var text in filters)
        {
            if (Filter(text, LinkGridReader.Columns) is not { } filter)
            {
                Console.Error.WriteLine($"--link-filter '{Format.Printable(text)}' isn't column:operator[:value], as --filter takes one.");
                return false;
            }

            parsed.Add(filter);
        }

        ends = new UrlLinkEnds(which.Value, parsed, search);
        return true;
    }

    /// <summary>A grid's query from the options; null, with why on stderr, when one of them can't be read.</summary>
    private static UrlQuery? Query(ExportKind kind, string? columns, string[] filters, string? search, string[] sorts)
    {
        var known = kind is ExportKind.Links or ExportKind.LinkGroups ? LinkGridReader.Columns : UrlGridReader.IssueRowColumns;
        List<UrlFilter> parsed = [];
        foreach (var text in filters)
        {
            if (Filter(text, known) is not { } filter)
            {
                Console.Error.WriteLine(
                    $"--filter '{Format.Printable(text)}' isn't column:operator[:value]. The operators are {string.Join(", ", OPERATORS.Keys)}; in and notIn take a,b,c and between takes min..max.");
                return null;
            }

            parsed.Add(filter);
        }

        return new UrlQuery
        {
            Columns = columns is null ? [] : [.. columns.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)],
            Filters = parsed,
            Search = search,
            Sort =
            [
                .. sorts.Select(sort => sort.EndsWith(":desc", StringComparison.OrdinalIgnoreCase)
                    ? new UrlSort(sort[..^5], Descending: true)
                    : new UrlSort(sort.EndsWith(":asc", StringComparison.OrdinalIgnoreCase) ? sort[..^4] : sort))
            ]
        };
    }

    /// <summary>
    /// <c>column:operator[:value]</c>. A value is of its column's type, not of what it looks like: a title that is
    /// "404" is text, as a status of 404 is a number, and true or false is a boolean for a column of them. What isn't of
    /// the column's type is passed as the text it is, and the engine says what the column takes; so is the value for a
    /// column this grid doesn't have, which the engine names.
    /// </summary>
    private static UrlFilter? Filter(string text, IReadOnlyList<UrlColumnInfo> known)
    {
        var parts = text.Split(':', 3);
        if (parts.Length < 2 || parts[0].Length == 0 || !OPERATORS.TryGetValue(parts[1], out var op))
        {
            return null;
        }

        var value = parts.Length == 3 ? parts[2] : null;
        var type = known.FirstOrDefault(column => column.Id == parts[0])?.Type;
        FilterValue Value(string given) => type switch
        {
            UrlColumnType.Number when double.TryParse(given, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) => FilterValue.Of(number),
            UrlColumnType.Boolean when bool.TryParse(given, out var flag) => FilterValue.Of(flag),
            _ => FilterValue.Of(given)
        };

        switch (op)
        {
            case FilterOperator.IsEmpty or FilterOperator.IsNotEmpty:
                return value is null ? new UrlFilter(parts[0], op) : null;
            case FilterOperator.In or FilterOperator.NotIn:
                return value is null ? null : new UrlFilter(parts[0], op) { Values = [.. value.Split(',', StringSplitOptions.TrimEntries).Select(Value)] };
            case FilterOperator.Between:
                var bounds = value?.Split("..", 2);
                if (bounds is not { Length: 2 })
                {
                    return null;
                }

                double? Bound(string bound) => double.TryParse(bound, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : null;
                return (bounds[0].Length > 0 && Bound(bounds[0]) is null) || (bounds[1].Length > 0 && Bound(bounds[1]) is null)
                    ? null
                    : new UrlFilter(parts[0], op) { Min = Bound(bounds[0]), Max = Bound(bounds[1]) };
            default:
                return value is null ? null : new UrlFilter(parts[0], op) { Value = Value(value) };
        }
    }

    /// <summary>A comma-separated list of checks' ids, or null for none given.</summary>
    private static string[]? Ids(string? list) =>
        list is null ? null : list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string Camel(string name) => char.ToLowerInvariant(name[0]) + name[1..];
}
