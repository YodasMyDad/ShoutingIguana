using ShoutingIguana.Engine.Export;
using ShoutingIguana.Engine.Export.Models;
using ShoutingIguana.Engine.Reporting.Models;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Sidecar.Api.Models;
using ShoutingIguana.Sidecar.Errors;

namespace ShoutingIguana.Sidecar.Api;

/// <summary>
/// An export's request from JSON to the engine's. A name the API doesn't know (a kind, a format, a reason, a cut) and a
/// field given to a kind that doesn't take it are <c>invalid-export</c> errors against that field; what the grid's query
/// can't say is <c>invalid-query</c>, as on the grid's own call.
/// </summary>
internal static class ExportRequests
{
    private static readonly Dictionary<string, ExportKind> KINDS = Enum.GetValues<ExportKind>().ToDictionary(kind => Camel(kind.ToString()), StringComparer.Ordinal);

    private static readonly Dictionary<string, ExportFormat> FORMATS = Enum.GetValues<ExportFormat>().ToDictionary(format => Camel(format.ToString()), StringComparer.Ordinal);

    /// <summary>The kinds whose export is a grid's query.</summary>
    private static readonly HashSet<ExportKind> GRIDS = [.. ExportFields.Grids];

    public static ExportRequest Request(ExportRequestDto body)
    {
        Dictionary<string, IReadOnlyList<string>> errors = new(StringComparer.Ordinal);
        if (body.Kind is not { } kindName || !KINDS.TryGetValue(kindName, out var kind))
        {
            throw Problems.InvalidExport("kind", $"Give what to export: {string.Join(", ", KINDS.Keys)}.");
        }

        if (body.Format is not { } formatName || !FORMATS.TryGetValue(formatName, out var format))
        {
            throw Problems.InvalidExport("format", $"Give the format: {string.Join(", ", FORMATS.Keys)}.");
        }

        // A field that belongs to another kind: refused, as the grids refuse a group or a sitemap that isn't theirs.
        // Which kinds take which field is the engine's to say (ExportFields), as the command line refuses the same.
        void Only(bool given, string field)
        {
            if (given && !ExportFields.Takes(kind, field))
            {
                errors[field] = [$"An export of {kindName} doesn't take {field}: it belongs to {string.Join(" and ", ExportFields.Owners(field).Select(ExportFields.Name))}."];
            }
        }

        Only(body.Columns is not null, "columns");
        Only(body.Filters is not null, "filters");
        Only(body.Search is not null, "search");
        Only(body.Sort is not null, "sort");
        Only(body.LinkEnds is not null, "linkEnds");
        Only(body.Blocked is not null, "blocked");
        Only(body.Group is not null, "group");
        Only(body.SitemapId is not null, "sitemapId");
        Only(body.Origin is not null, "origin");
        Only(body.Line is not null, "line");
        Only(body.Reason is not null, "reason");
        Only(body.Conflict is not null, "conflict");
        Only(body.CheckId is not null, "checkId");
        Only(body.Cut is not null, "cut");
        Only(body.Examples is not null, "examples");
        Only(body.LookExamples is not null, "lookExamples");
        Only(body.HiddenChecks is not null, "hiddenChecks");
        Only(body.Checks is not null, "checks");
        // A grid's are its query's, checked as the grid's call checks them (invalid-query); the report's and the workbook's here.
        if (!GRIDS.Contains(kind))
        {
            CheckIds(body.HiddenChecks, "hiddenChecks", errors);
        }

        CheckIds(body.Checks, "checks", errors);

        // The fix loop's comparison is the sidecar's to resolve (the library knows the crawls), so it's checked here.
        if (body.CompareWith is not null && kind is not (ExportKind.Report or ExportKind.Workbook))
        {
            errors["compareWith"] = [$"An export of {kindName} doesn't take compareWith: it belongs to report and workbook."];
        }
        else if (body.CompareWith is "")
        {
            errors["compareWith"] = ["compareWith is previous, none, or a crawl's id."];
        }

        RobotsBlockReason? reason = body.Reason switch
        {
            null => null,
            "rule" => RobotsBlockReason.Rule,
            "unreachable" => RobotsBlockReason.Unreachable,
            "unknown" => RobotsBlockReason.Unknown,
            var other => Refuse<RobotsBlockReason?>(errors, "reason", $"There's no reason called '{other}': there are rule, unreachable and unknown.")
        };
        var cut = Cut(body.Cut, errors);
        var sitemap = body.SitemapId switch
        {
            null => (int?)null,
            < 1 or > int.MaxValue => 0,
            var id => (int)id
        };
        if (errors.Count > 0)
        {
            throw Problems.InvalidExport(errors);
        }

        return new ExportRequest
        {
            Kind = kind,
            Format = format,
            // A grid's query, and the grouped links' (the links grid's filters, search, sort and hidden checks).
            Query = GRIDS.Contains(kind) || kind == ExportKind.LinkGroups
                ? GridRequests.Query(new GridQueryRequest
                {
                    Columns = body.Columns, Filters = body.Filters, Search = body.Search, Sort = body.Sort, LinkEnds = body.LinkEnds, HiddenChecks = body.HiddenChecks
                })
                : new UrlQuery { Search = body.Search },
            Blocked = body.Blocked,
            SitemapId = sitemap,
            Origin = body.Origin,
            Line = body.Line,
            Reason = reason,
            Conflict = body.Conflict,
            CheckId = body.CheckId,
            Group = body.Group is { } group ? new IssueGroupKey(group.Detail, group.RelatedUrlId) : null,
            Report = Report(cut, body.Examples, body.LookExamples) with { HiddenChecks = body.HiddenChecks ?? [], Checks = body.Checks }
        };
    }

    /// <summary>A list of checks' ids: at most <see cref="ReportOptions.MAX_CHECKS"/>, none empty or longer than an id is.</summary>
    private static void CheckIds(IReadOnlyList<string>? ids, string field, Dictionary<string, IReadOnlyList<string>> errors)
    {
        if (ids is null || errors.ContainsKey(field))
        {
            return;
        }

        if (ids.Count > ReportOptions.MAX_CHECKS)
        {
            errors[field] = [$"{field} takes at most {ReportOptions.MAX_CHECKS} checks."];
        }
        else if (ids.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 64))
        {
            errors[field] = [$"Each of {field} is a check's id, such as title-too-long."];
        }
    }

    /// <summary>
    /// The report's options from a query string: <c>cut</c>, <c>examples</c>, <c>lookExamples</c>, <c>hiddenChecks</c> and
    /// <c>compareWith</c> (the fix loop: <c>previous</c>, <c>none</c> or a crawl's id), each optional.
    /// </summary>
    public static ReportOptions Report(IQueryCollection query, out string? compareWith)
    {
        Dictionary<string, IReadOnlyList<string>> errors = new(StringComparer.Ordinal);
        int? Number(string name)
        {
            var values = query[name];
            if (values.Count == 0)
            {
                return null;
            }

            return values.Count == 1 && int.TryParse(values[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number)
                ? number
                : Refuse<int?>(errors, name, $"{name} must be a whole number from 0 to {ReportOptions.MAX_EXAMPLES}.");
        }

        var cut = Cut(query["cut"].Count == 0 ? null : query["cut"].ToString(), errors);
        var (examples, lookExamples) = (Number("examples"), Number("lookExamples"));
        // The checks the user hid, comma-separated, as the export takes them: the report shown is the report saved.
        IReadOnlyList<string> hidden = query["hiddenChecks"].Count == 0
            ? []
            : [.. string.Join(',', query["hiddenChecks"].Select(value => value ?? string.Empty)).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
        CheckIds(hidden, "hiddenChecks", errors);
        compareWith = query["compareWith"] switch
        {
            { Count: 0 } => null,
            { Count: 1 } one when !string.IsNullOrEmpty(one[0]) => one[0],
            _ => Refuse<string?>(errors, "compareWith", "compareWith is previous, none, or one crawl's id.")
        };
        if (query.Keys.FirstOrDefault(key => key is not ("cut" or "examples" or "lookExamples" or "hiddenChecks" or "compareWith")) is { } unknown)
        {
            errors[unknown] = ["The report takes cut, examples, lookExamples, hiddenChecks and compareWith."];
        }

        return errors.Count > 0 ? throw Problems.InvalidExport(errors) : Report(cut, examples, lookExamples) with { HiddenChecks = hidden };
    }

    private static ReportOptions Report(ReportCut cut, int? examples, int? lookExamples) => new()
    {
        Cut = cut,
        Examples = examples ?? ReportOptions.DEFAULT_EXAMPLES,
        LookExamples = lookExamples ?? ReportOptions.DEFAULT_LOOK_EXAMPLES
    };

    private static ReportCut Cut(string? cut, Dictionary<string, IReadOnlyList<string>> errors) => cut switch
    {
        null or "full" => ReportCut.Full,
        "summary" => ReportCut.Summary,
        _ => Refuse<ReportCut>(errors, "cut", $"There's no cut called '{cut}': there are full and summary.")
    };

    private static T Refuse<T>(Dictionary<string, IReadOnlyList<string>> errors, string field, string message)
    {
        errors[field] = [message];
        return default!;
    }

    private static string Camel(string name) => char.ToLowerInvariant(name[0]) + name[1..];
}
