using System.Diagnostics;
using System.Runtime.InteropServices;
using ShoutingIguana.Cli.Benchmarking.Models;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Cli.Benchmarking;

/// <summary>
/// Times the grids' readers on a real crawl file, as the UI calls them: the first page and a page half way down, for every
/// sort, for filters common and rare (up to three at once), for search, facets, the inspector, its links, and a check's
/// groups and rows. Each read runs on a fresh connection with nothing cached, as the first read of a filter set does; the
/// file itself is warm, as it is once a crawl is open.
/// </summary>
/// <param name="only">Run only the reads whose name contains this text (ignoring case); all of them when null.</param>
internal sealed class ReaderBenchmark(string path, int runs, Action<string> progress, string? only = null)
{
    /// <summary>The Overview columns, which every grid read fetches.</summary>
    private static readonly string[] COLUMNS = ["url", "statusCode", "indexability", "issues", "title", "wordCount", "responseMs", "depth", "inlinks", "sizeBytes"];

    private readonly List<ReaderBenchCase> _cases = [];

    public IReadOnlyList<ReaderBenchCase> Run()
    {
        var all = UrlGridReader.Query(path, new UrlQuery { Columns = ["url"], Limit = 1 });
        foreach (var column in UrlGridReader.Columns.Where(column => column.Sortable))
        {
            foreach (var descending in new[] { false, true })
            {
                Grid($"Sort by {column.Id} {(descending ? "desc" : "asc")}", new UrlQuery { Sort = [new UrlSort(column.Id, descending)] });
            }
        }

        Grid("Pages by response time (a common filter)", Query([In("type", "page")], "responseMs", descending: true));
        Grid("Status 500 by response time (a rare filter)", Query([Number("statusCode", FilterOperator.Eq, 500)], "responseMs", descending: true));
        Grid("4xx by linking pages", Query([In("statusClass", "4xx")], "linkingPages", descending: true));
        Grid("Noindex by words", Query([In("indexability", "noindex")], "wordCount", descending: false));
        Grid("Indexable internal pages, thinnest first (three common filters)",
            Query([Boolean("isInternal", true), In("type", "page"), Boolean("indexable", true)], "wordCount", descending: false));
        Grid("Responses of 500 ms or more by size", Query([Number("responseMs", FilterOperator.Gte, 500)], "sizeBytes", descending: true));
        Grid("Depth 8 or more by size", Query([new UrlFilter("depth", FilterOperator.Between) { Min = 8 }], "sizeBytes", descending: true));
        Grid("Images by size (a rare type)", Query([In("type", "image")], "sizeBytes", descending: true));
        Grid("Redirects by final status", Query([In("statusClass", "3xx")], "finalStatusCode", descending: true));
        Grid("URLs with errors, most first", Query([Number("errors", FilterOperator.Gte, 1)], "errors", descending: true));
        Grid("Titles over 60 by length", Query([Number("titleLength", FilterOperator.Gt, 60)], "titleLength", descending: true));
        Grid("Not in a sitemap, internal, by inlinks", Query([Boolean("inSitemap", false), Boolean("isInternal", true)], "inlinks", descending: false));
        Grid("Internal 404 pages by response time (three filters, one rare)",
            Query([In("type", "page"), Boolean("isInternal", true), Number("statusCode", FilterOperator.Eq, 404)], "responseMs", descending: true));
        Grid("Not 2xx, by URL (a negative filter)", Query([new UrlFilter("statusClass", FilterOperator.NotIn) { Values = [FilterValue.Of("2xx")] }], "url", descending: false));
        Grid("Title contains text, by issues", Query([Text("title", FilterOperator.Contains, "Product 12")], "issues", descending: true));
        Grid("URL matches a regular expression, by depth", Query([Text("url", FilterOperator.Regex, "/p/12[0-9]{3}$")], "depth", descending: false));
        Grid("Search, by URL", new UrlQuery { Search = "kestrel", Sort = [new UrlSort("url")] });

        var checks = IssueReader.ReadCounts(path).Where(check => check.Value > 0).OrderByDescending(check => check.Value).ToList();
        if (checks.Count > 0)
        {
            var biggest = checks[0].Key;
            Grid($"Flagged by {biggest}, by inlinks", Query([new UrlFilter("check", FilterOperator.In) { Values = [FilterValue.Of(biggest)] }], "inlinks", descending: true));
            Timed($"Groups of {biggest}", "groups", null, () => IssueGroupReader.ReadGroups(path, biggest, 0, UrlGridReader.DEFAULT_LIMIT));
            var rows = IssueGroupReader.ReadRows(path, biggest, new UrlQuery { Limit = 1 })?.Page?.Matching ?? 0;
            Paged($"Rows of {biggest}, by inlinks", "rows", rows,
                offset => IssueGroupReader.ReadRows(path, biggest, new UrlQuery { Columns = [.. COLUMNS, "detail"], Sort = [new UrlSort("inlinks", true)], Offset = offset }));
        }

        Timed("Facets of every URL", "facets", all.Total, () => UrlGridReader.Facets(path, new UrlFacetQuery()));
        Timed("Facets of the pages", "facets", null, () => UrlGridReader.Facets(path, new UrlFacetQuery { Filters = [In("type", "page")] }));

        var start = all.Rows.Count > 0 ? all.Rows[0].Id : 1;
        var startUrl = all.Rows.Count > 0 ? (string)all.Rows[0].Values[1]! : string.Empty;
        Timed("Look up a URL", "lookup", null, () => UrlGridReader.Lookup(path, startUrl));
        Timed("Inspect the start URL", "inspector", null, () => UrlInspectorReader.Read(path, start));
        Timed("Inspect a URL half way through the crawl", "inspector", null, () => UrlInspectorReader.Read(path, (int)Math.Max(1, all.Total / 2)));
        var inlinks = UrlInspectorReader.ReadLinks(path, start, LinkDirection.Inlinks, new LinkQuery { Limit = 1 })?.Total ?? 0;
        Paged("Inlinks of the start URL", "links", inlinks, offset => UrlInspectorReader.ReadLinks(path, start, LinkDirection.Inlinks, new LinkQuery { Offset = offset }));
        var hyperlinks = UrlInspectorReader.ReadLinks(path, start, LinkDirection.Inlinks, new LinkQuery { Kinds = [LinkKind.Hyperlink], Limit = 1 })?.Total ?? 0;
        Paged("Hyperlinks to the start URL", "links", hyperlinks,
            offset => UrlInspectorReader.ReadLinks(path, start, LinkDirection.Inlinks, new LinkQuery { Kinds = [LinkKind.Hyperlink], Offset = offset }));
        Timed("Outlinks of the start URL", "links", null, () => UrlInspectorReader.ReadLinks(path, start, LinkDirection.Outlinks, new LinkQuery()));
        return _cases;
    }

    private static UrlQuery Query(UrlFilter[] filters, string sort, bool descending) => new() { Filters = filters, Sort = [new UrlSort(sort, descending)] };

    private static UrlFilter In(string column, string value) => new(column, FilterOperator.In) { Values = [FilterValue.Of(value)] };

    private static UrlFilter Number(string column, FilterOperator op, double value) => new(column, op) { Value = FilterValue.Of(value) };

    private static UrlFilter Boolean(string column, bool value) => new(column, FilterOperator.Is) { Value = FilterValue.Of(value) };

    private static UrlFilter Text(string column, FilterOperator op, string value) => new(column, op) { Value = FilterValue.Of(value) };

    private void Grid(string name, UrlQuery query)
    {
        if (!Wanted(name))
        {
            return;
        }

        var shown = query with { Columns = COLUMNS };
        long matching;
        try
        {
            matching = UrlGridReader.Query(path, shown with { Limit = 1 }).Matching;
        }
        catch (NeedsAnalysisException)
        {
            progress($"  skipped    {name}: it needs the URL summary");
            return;
        }

        Paged(name, "grid", matching, offset => UrlGridReader.Query(path, shown with { Offset = offset }));
    }

    /// <summary>A paged read: its first page, and the page half way down when there's more than one.</summary>
    private void Paged(string name, string kind, long rows, Func<int, object?> read)
    {
        if (!Wanted(name))
        {
            return;
        }

        var first = Time(() => read(0));
        (double P50, double P95)? half = rows > UrlGridReader.DEFAULT_LIMIT ? Time(() => read((int)(rows / 2))) : null;
        Add(new ReaderBenchCase(name, kind, rows, first.P50, first.P95, half?.P50, half?.P95));
    }

    private void Timed(string name, string kind, long? rows, Func<object?> read)
    {
        if (!Wanted(name))
        {
            return;
        }

        var (p50, p95) = Time(() => read());
        Add(new ReaderBenchCase(name, kind, rows, p50, p95, null, null));
    }

    private bool Wanted(string name) => only is null || name.Contains(only, StringComparison.OrdinalIgnoreCase);

    private void Add(ReaderBenchCase result)
    {
        _cases.Add(result);
        var half = result.HalfP95Ms is { } halfP95 ? $"  half way {result.HalfP50Ms:N1} / {halfP95:N1} ms" : string.Empty;
        progress($"  {result.FirstP95Ms,8:N1} ms  {result.Name} ({result.Matching?.ToString("N0") ?? "-"} rows): first {result.FirstP50Ms:N1} / {result.FirstP95Ms:N1} ms{half}");
    }

    /// <summary>p50 and p95 over the runs, after one untimed read that warms the file and the code.</summary>
    private (double P50, double P95) Time(Action read)
    {
        read();
        var times = new double[runs];
        for (var run = 0; run < runs; run++)
        {
            var clock = Stopwatch.StartNew();
            read();
            times[run] = clock.Elapsed.TotalMilliseconds;
        }

        Array.Sort(times);
        return (Math.Round(Percentile(times, 0.5), 1), Math.Round(Percentile(times, 0.95), 1));
    }

    /// <summary>The nearest-rank percentile of sorted times.</summary>
    private static double Percentile(double[] sorted, double percentile) => sorted[Math.Max(0, (int)Math.Ceiling(percentile * sorted.Length) - 1)];

    public static string Machine => $"{Environment.ProcessorCount} cores, {RuntimeInformation.OSDescription}";
}
