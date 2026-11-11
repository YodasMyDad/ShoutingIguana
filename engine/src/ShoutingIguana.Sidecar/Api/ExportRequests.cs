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
    private static readonly HashSet<ExportKind> GRIDS = [ExportKind.Urls, ExportKind.Links, ExportKind.SitemapUrls, ExportKind.IssueRows];

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
        void Only(bool given, string field, params ExportKind[] kinds)
        {
            if (given && !kinds.Contains(kind))
            {
                errors[field] = [$"An export of {kindName} doesn't take {field}: it belongs to {string.Join(" and ", kinds.Select(owner => Camel(owner.ToString())))}."];
            }
        }

        var grids = GRIDS.ToArray();
        Only(body.Columns is not null, "columns", grids);
        Only(body.Filters is not null, "filters", grids);
        Only(body.Search is not null, "search", grids);
        Only(body.Sort is not null, "sort", grids);
        Only(body.Group is not null, "group", ExportKind.IssueRows);
        Only(body.SitemapId is not null, "sitemapId", ExportKind.SitemapUrls);
        Only(body.Origin is not null, "origin", ExportKind.RobotsBlocked);
        Only(body.Line is not null, "line", ExportKind.RobotsBlocked);
        Only(body.Reason is not null, "reason", ExportKind.RobotsBlocked);
        Only(body.CheckId is not null, "checkId", ExportKind.IssueRows, ExportKind.Check);
        Only(body.Cut is not null, "cut", ExportKind.Report);
        Only(body.Examples is not null, "examples", ExportKind.Report);
        Only(body.LookExamples is not null, "lookExamples", ExportKind.Report);

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
            Query = GRIDS.Contains(kind)
                ? GridRequests.Query(new GridQueryRequest { Columns = body.Columns, Filters = body.Filters, Search = body.Search, Sort = body.Sort })
                : new UrlQuery(),
            SitemapId = sitemap,
            Origin = body.Origin,
            Line = body.Line,
            Reason = reason,
            CheckId = body.CheckId,
            Group = body.Group is { } group ? new IssueGroupKey(group.Detail, group.RelatedUrlId) : null,
            Report = Report(cut, body.Examples, body.LookExamples)
        };
    }

    /// <summary>The report's options from a query string: <c>cut</c>, <c>examples</c> and <c>lookExamples</c>, each optional.</summary>
    public static ReportOptions Report(IQueryCollection query)
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
        if (query.Keys.FirstOrDefault(key => key is not ("cut" or "examples" or "lookExamples")) is { } unknown)
        {
            errors[unknown] = ["The report takes cut, examples and lookExamples."];
        }

        return errors.Count > 0 ? throw Problems.InvalidExport(errors) : Report(cut, examples, lookExamples);
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
