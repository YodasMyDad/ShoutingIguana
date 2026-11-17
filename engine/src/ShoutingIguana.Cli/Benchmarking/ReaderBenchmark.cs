using System.Diagnostics;
using System.Runtime.InteropServices;
using ShoutingIguana.Cli.Benchmarking.Models;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Cli.Benchmarking;

/// <summary>
/// Times the grids' readers on a real crawl file, as the UI calls them: the first page, the page half way down and the one
/// just past it (read from the far end), for every sort, for filters common and rare (up to three at once), for text
/// filters, regular expressions and search (common and rare, up to three), for facets, the inspector, its links (of the
/// most linked URLs, by kind), a check's groups and rows, and again without the URL summary, as a crawl running or
/// continued since its analysis is read. Each read runs on a fresh connection with nothing cached, as the first read of a
/// filter set does; the file itself is warm, as it is once a crawl is open.
/// </summary>
/// <param name="only">Run only the reads whose name contains this text (ignoring case); all of them when null.</param>
internal sealed class ReaderBenchmark(string path, int runs, Action<string> progress, string? only = null)
{
    /// <summary>
    /// Each class of read's gate at 100k URLs, p95 in ms: what CI holds the 100k benchmark crawl to. A read grows with the
    /// crawl as its class does, so a read at its gate on CI's runner would take about the 1M budget on 1M URLs:
    /// <list type="bullet">
    /// <item>gate = 1,000 ms ÷ the class's growth × 2. The growth is its slowest read's p95 at 1M over its slowest at 100k,
    /// measured on a 32-core desktop, and at least 10, linear, as a read that regresses to a scan grows. The runner's core
    /// is taken as twice as slow as the desktop's.</item>
    /// <item>Two classes are slower than 1 s at 1M today, and held to that instead, so they can't get slower: the facets
    /// without the summary (1.35 s at 1M, a ceiling of 1.5 s), and links of a kind to a URL that has a million of them
    /// (1.8 s, a ceiling of 2 s, until the links' indexes hold their kind).</item>
    /// <item>The links grid's reads that pass over every link once per filter set (<c>link-pass</c>: a filter on a link's
    /// text or on the page it's on, a search, and any filter without the link summary) are over the budget at 100k
    /// already, by what they are: held to 2.5 s there, so they can't get slower.</item>
    /// <item>The crawl's headline numbers (<c>stats</c>) pass over the URLs once and the responses once: inside the 300 ms
    /// budget at 100k on the desktop (174 ms at worst), and held to twice that for the runner. Read again, they're kept
    /// (<c>stats-kept</c>, under a millisecond): held to 10 ms, so a cache that stopped keeping them fails here.</item>
    /// <item>Without the link summary (<c>stats-pass</c>: a crawl running, never analysed or continued since) the links are
    /// counted and the pages a sitemap led to looked up among them, and that <b>misses the 300 ms budget</b> at 100k on
    /// the desktop: 209 to 366 ms at p95 over nine runs of 20, 188 to 320 ms at p50. It's held to 450 ms, the worst
    /// measured and a quarter, so it can't get slower unseen; a runner slower than the desktop needs its own figure.</item>
    /// <item>The links grid's reads deep among the links of more targets than are listed for their index
    /// (<c>link-targets</c>) read an index entry for every matching link, once per filter set: inside the 300 ms budget
    /// at 100k on the desktop (265 ms at worst), which the runner's 550 ms is, and held to it so they can't get slower.
    /// They grow with the links, so they'd miss at 1M. A filter most links pass isn't one of them any more: it's read
    /// from the blocks but for the others (<c>link-grid</c>).</item>
    /// <item>The same passes split by link id over the connections a host lends (<c>link-pass-split</c>: eight in all at
    /// most), and a view's pages counted the same way. Timed in turn with the engine from before the split, three rounds
    /// on a desktop other work was loading: a pass takes a quarter of the time on four connections and a sixth on eight
    /// (a text filter 113 to 123 ms at p50 where one connection took 665 to 934, a search 183 to 211 for 1,233 to
    /// 1,592, a filter on the source's depth 210 to 228 for 819 to 1,536). Their worst p95 was 489 ms on eight and 532
    /// on four, with one of 1,295 as the machine was busiest. Held to 1,500 ms: CI's 4-core runner has three helpers to
    /// lend and is taken as twice as slow a core, so over twice the worst on four connections, and above the busy run.
    /// <b>No CI run has yet confirmed it.</b> As every one of these passes takes less than that on one connection too, a
    /// pass that stopped splitting would pass it: <see cref="SplitGate"/> holds each to being split, and faster for it.</item>
    /// <item>Search as it's typed (<c>search</c>) passes over the crawl's URLs and titles in memory: 1.3 to 4.5 ms at 100k
    /// on the desktop whatever is typed, against the palette's 50 ms, and held to 25 ms: a search that read the file
    /// again (the text no longer kept) takes 50 to 70 ms on the desktop and would fail it, where a gate of 50 might not.
    /// The first search of a crawl reads that text from the file (<c>search-first</c>: 55 to 89 ms at p95 on the
    /// desktop, <b>over the palette's 50 ms</b>, which is why the palette asks an empty search as it opens): held to
    /// 200 ms for the runner, so it can't get slower unseen.</item>
    /// </list>
    /// engine/README.md has the measurements.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, double> GATE_MS = new Dictionary<string, double>(StringComparer.Ordinal)
    {
        ["indexed"] = 200,
        ["scan"] = 200,
        ["filter"] = 110,
        ["text"] = 190,
        ["check"] = 75,
        ["facets"] = 200,
        ["inspector"] = 200,
        ["links"] = 200,
        ["links-kind"] = 240,
        ["crawl"] = 185,
        ["crawl-facets"] = 245,
        ["link-grid"] = 200,
        ["link-targets"] = 550,
        ["link-pass"] = 2_500,
        ["sitemaps"] = 200,
        ["robots"] = 200,
        ["stats"] = 350,
        ["stats-kept"] = 10,
        ["stats-pass"] = 450,
        ["link-pass-split"] = 1_500,
        ["search"] = 25,
        ["search-first"] = 200
    };

    /// <summary>The Overview columns, which every grid read fetches.</summary>
    private static readonly string[] COLUMNS = ["url", "statusCode", "indexability", "issues", "title", "wordCount", "responseMs", "depth", "inlinks", "sizeBytes"];

    private readonly List<ReaderBenchCase> _cases = [];
    private readonly Dictionary<string, IReadOnlyList<int>> _connections = new(StringComparer.Ordinal);

    /// <summary>
    /// The connections each pass of each case read split over several reported, by case name (<see cref="SplitGate"/>):
    /// one entry a pass over every link, from <c>LinkGridReader.Split</c>.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<int>> Connections => _connections;

    public IReadOnlyList<ReaderBenchCase> Run()
    {
        var all = UrlGridReader.Query(path, new UrlQuery { Columns = ["url"], Limit = 1 });
        foreach (var column in UrlGridReader.Columns.Where(column => column.Sortable))
        {
            foreach (var descending in new[] { false, true })
            {
                Grid($"Sort by {column.Id} {(descending ? "desc" : "asc")}", Indexed(column.Id) ? "indexed" : "scan", new UrlQuery { Sort = [new UrlSort(column.Id, descending)] });
            }
        }

        Grid("Pages by response time (a common filter)", "filter", Query([In("type", "page")], "responseMs", descending: true));
        Grid("Status 500 by response time (a rare filter)", "filter", Query([Number("statusCode", FilterOperator.Eq, 500)], "responseMs", descending: true));
        Grid("4xx by linking pages", "filter", Query([In("statusClass", "4xx")], "linkingPages", descending: true));
        Grid("Noindex by words", "filter", Query([In("indexability", "noindex")], "wordCount", descending: false));
        Grid("Indexable internal pages, thinnest first (three common filters)", "filter",
            Query([Boolean("isInternal", true), In("type", "page"), Boolean("indexable", true)], "wordCount", descending: false));
        Grid("Responses of 500 ms or more by size", "filter", Query([Number("responseMs", FilterOperator.Gte, 500)], "sizeBytes", descending: true));
        Grid("Depth 8 or more by size", "filter", Query([new UrlFilter("depth", FilterOperator.Between) { Min = 8 }], "sizeBytes", descending: true));
        Grid("Images by size (a rare type)", "filter", Query([In("type", "image")], "sizeBytes", descending: true));
        Grid("Redirects by final status", "filter", Query([In("statusClass", "3xx")], "finalStatusCode", descending: true));
        Grid("URLs with errors, most first", "filter", Query([Number("errors", FilterOperator.Gte, 1)], "errors", descending: true));
        Grid("Titles over 60 by length", "filter", Query([Number("titleLength", FilterOperator.Gt, 60)], "titleLength", descending: true));
        Grid("Not in a sitemap, internal, by inlinks", "filter", Query([Boolean("inSitemap", false), Boolean("isInternal", true)], "inlinks", descending: false));
        Grid("Internal 404 pages by response time (three filters, one rare)", "filter",
            Query([In("type", "page"), Boolean("isInternal", true), Number("statusCode", FilterOperator.Eq, 404)], "responseMs", descending: true));
        Grid("Not 2xx, by URL (a negative filter)", "filter", Query([new UrlFilter("statusClass", FilterOperator.NotIn) { Values = [FilterValue.Of("2xx")] }], "url", descending: false));

        // A selective filter on a column without an index, against an indexed sort: the walk reads most of the index.
        Grid("External URLs by size (the slowest walk)", "filter", Query([Boolean("isInternal", false)], "sizeBytes", descending: true));

        Grid("Title contains text, by issues (rare)", "text", Query([Text("title", FilterOperator.Contains, "Product 12")], "issues", descending: true));
        Grid("Title contains a common word, by depth", "text", Query([Text("title", FilterOperator.Contains, "product")], "depth", descending: false));
        Grid("Title doesn't contain a common word, by depth", "text", Query([Text("title", FilterOperator.NotContains, "product")], "depth", descending: false));
        Grid("URL matches a regular expression, by depth (rare)", "text", Query([Text("url", FilterOperator.Regex, "/p/12[0-9]{3}$")], "depth", descending: false));
        Grid("URL matches a common regular expression, by depth", "text", Query([Text("url", FilterOperator.Regex, @"/p/\d+$")], "depth", descending: false));
        Grid("Title matches a common regular expression, by followed inlinks (no index)", "text",
            Query([Text("title", FilterOperator.Regex, @"Product \d+")], "followedInlinks", descending: true));
        Grid("Three common text filters, by depth", "text",
            Query([Text("title", FilterOperator.Contains, "product"), Text("url", FilterOperator.Contains, "/p/"), Number("h1Count", FilterOperator.Eq, 1)], "depth", descending: false));
        Grid("Search, by URL (rare)", "text", new UrlQuery { Search = "kestrel", Sort = [new UrlSort("url")] });
        Grid("Search for a common word, by URL", "text", new UrlQuery { Search = "product", Sort = [new UrlSort("url")] });
        Grid("Search for one letter, by issues", "text", new UrlQuery { Search = "p", Sort = [new UrlSort("issues", true)] });
        Grid("Search for two letters, in crawl order", "text", new UrlQuery { Search = "pr" });
        Grid("Search that matches nothing", "text", new UrlQuery { Search = "zzzqqq" });
        Grid("Search for forty characters, by URL", "text", new UrlQuery { Search = FORTY_CHARACTERS, Sort = [new UrlSort("url")] });

        var checks = IssueReader.ReadCounts(path).Where(check => check.Value > 0).OrderByDescending(check => check.Value).ToList();
        if (checks.Count > 0)
        {
            var biggest = checks[0].Key;
            Grid($"Flagged by {biggest}, by inlinks", "check", Query([new UrlFilter("check", FilterOperator.In) { Values = [FilterValue.Of(biggest)] }], "inlinks", descending: true));
            Timed($"Groups of {biggest}", "groups", "check", null, () => IssueGroupReader.ReadGroups(path, biggest, 0, UrlGridReader.DEFAULT_LIMIT));
            var rows = IssueGroupReader.ReadRows(path, biggest, new UrlQuery { Limit = 1 })?.Page?.Matching ?? 0;
            Paged($"Rows of {biggest}, by inlinks", "rows", "check", rows,
                offset => IssueGroupReader.ReadRows(path, biggest, new UrlQuery { Columns = [.. COLUMNS, "detail"], Sort = [new UrlSort("inlinks", true)], Offset = offset }));

            // With no order asked for, the rows come by importance: the links to each, clicks from the start, the URL.
            Paged($"Rows of {biggest}, by importance", "rows", "check", rows,
                offset => IssueGroupReader.ReadRows(path, biggest, new UrlQuery { Columns = [.. COLUMNS, "detail"], Offset = offset }));
        }

        Timed("Facets of every URL", "facets", "facets", all.Total, () => UrlGridReader.Facets(path, new UrlFacetQuery()));
        Timed("Facets of the pages", "facets", "facets", null, () => UrlGridReader.Facets(path, new UrlFacetQuery { Filters = [In("type", "page")] }));
        Timed("Facets of a common title filter", "facets", "facets", null, () => UrlGridReader.Facets(path, new UrlFacetQuery { Filters = [Text("title", FilterOperator.Contains, "product")] }));

        var start = all.Rows.Count > 0 ? all.Rows[0].Id : 1;
        var startUrl = all.Rows.Count > 0 ? (string)all.Rows[0].Values[1]! : string.Empty;
        Timed("Look up a URL", "lookup", "inspector", null, () => UrlGridReader.Lookup(path, startUrl));
        Timed("Inspect the start URL", "inspector", "inspector", null, () => UrlInspectorReader.Read(path, start));
        Timed("Inspect a URL half way through the crawl", "inspector", "inspector", null, () => UrlInspectorReader.Read(path, (int)Math.Max(1, all.Total / 2)));
        if (MostNamed() is { } named)
        {
            Timed("Inspect the URL most issues name", "inspector", "inspector", null, () => UrlInspectorReader.Read(path, named));
        }

        Links("Inlinks of the start URL", start, []);
        Links("Hyperlinks to the start URL", start, [LinkKind.Hyperlink]);
        Links("Canonical links to the start URL (a few among a million)", start, [LinkKind.Canonical]);
        Timed("Outlinks of the start URL", "links", "links", null, () => UrlInspectorReader.ReadLinks(path, start, LinkDirection.Outlinks, new LinkQuery()));

        // The other URLs every page links to: a menu's targets, the stylesheet.
        if (First(new UrlQuery { Sort = [new UrlSort("inlinks", true)], Limit = 2 }, other: start) is { } linked)
        {
            Links("Hyperlinks to the most linked page", linked, [LinkKind.Hyperlink]);
        }

        if (First(new UrlQuery { Filters = [In("type", "stylesheet")], Limit = 1 }) is { } stylesheet)
        {
            Links("Stylesheet links to the stylesheet", stylesheet, [LinkKind.Stylesheet]);
        }

        LinksGrid();
        LinkEnds();
        LinkGroups();
        SitemapsAndRobots();
        WithoutSummary();
        Stats();
        Search();
        return _cases;
    }

    /// <summary>A search no URL or title of the benchmark site matches, forty characters long.</summary>
    private const string FORTY_CHARACTERS = "this text is forty characters long, no m";

    /// <summary>
    /// Search as it's typed, for the palette: a keystroke with the crawl's text kept, as every keystroke but the first has
    /// it (<c>search</c>: one letter, which every URL matches, to forty characters that none does), and the first, which
    /// reads the crawl's URLs and titles into memory (<c>search-first</c>).
    /// </summary>
    private void Search()
    {
        ReadCache kept = new();
        foreach (var text in new[] { "p", "pr", "pro", "product", "/p/123", "kestrel", "Product 12345", "zzzqqq", FORTY_CHARACTERS })
        {
            Timed($"Search as typed: '{text}'", "search", "search", UrlSearchReader.Search(path, text, cache: kept).Matching, () => UrlSearchReader.Search(path, text, cache: kept));
        }

        Timed("Search as typed: the first keystroke, the crawl's text read", "search", "search-first", null, () => UrlSearchReader.Search(path, "p"));
        Timed("Search as typed: the first keystroke, a text nothing matches", "search", "search-first", null, () => UrlSearchReader.Search(path, "zzzqqq"));
    }

    /// <summary>
    /// The Sitemaps and robots.txt screens: the list of sitemaps with what each lists, counted; the biggest sitemap's URLs
    /// as a URL grid, sorted and filtered; the origins with their robots.txt; the start URL's file with what each line
    /// blocked; and its blocked URLs.
    /// </summary>
    private void SitemapsAndRobots()
    {
        Timed("Sitemaps: the list, each with its URLs counted", "sitemaps", "sitemaps", null, () => SitemapListReader.Read(path));
        if (Wanted("Sitemaps: ") && SitemapListReader.Read(path).Sitemaps.MaxBy(sitemap => sitemap.Urls.Listed) is { Urls.Listed: > 0 } biggest)
        {
            UrlPage Urls(UrlQuery query) => UrlGridReader.QuerySitemap(path, biggest.Id, query with { Columns = COLUMNS })!;
            Paged("Sitemaps: the biggest sitemap's URLs, by URL", "grid", "sitemaps", biggest.Urls.Listed, offset => Urls(new UrlQuery { Sort = [new UrlSort("url")], Offset = offset }));
            Paged("Sitemaps: its non-indexable URLs, by inlinks", "grid", "sitemaps", biggest.Urls.NonIndexable,
                offset => Urls(new UrlQuery { Filters = [Boolean("indexable", false)], Sort = [new UrlSort("inlinks", true)], Offset = offset }));
        }

        Timed("robots.txt: the origins, with what each blocked", "robots", "robots", null, () => RobotsReader.ReadOrigins(path));
        Timed("robots.txt: the origins that blocked something, found by text", "robots", "robots", null,
            () => RobotsReader.ReadOrigins(path, new RobotsOriginQuery { Search = ".", Blocked = true }));
        if (Wanted("robots.txt: ") && RobotsReader.ReadOrigins(path, limit: 1).Origins.FirstOrDefault() is { } site)
        {
            Timed("robots.txt: the site's file, with what each line blocked", "robots", "robots", site.Blocked, () => RobotsReader.ReadFile(path, site.Origin));
            Paged("robots.txt: the site's blocked URLs", "robots", "robots", site.Blocked, offset => RobotsReader.ReadBlocked(path, site.Origin, offset: offset));

            // What blocking each costs (the pages linking to it, a sitemap, a menu) is counted for every blocked URL of the
            // origin, as the conflicts are listed alone: two lookups a URL in the summaries, never a pass over the links.
            Timed("robots.txt: the site's blocked URLs a sitemap lists or a menu links to", "robots", "robots", null,
                () => RobotsReader.ReadBlocked(path, site.Origin, conflict: true));

        }

        // What the inspector links by: one blocked URL's row, and the URL inspected, with the line that refused it. Of the
        // origin that blocked the most, which needn't be the site's own.
        if (Wanted("robots.txt: ") && RobotsReader.ReadOrigins(path, new RobotsOriginQuery { Blocked = true, Limit = 1_000 }).Origins.MaxBy(origin => origin.Blocked) is { } blocking &&
            RobotsReader.ReadBlocked(path, blocking.Origin, limit: 1)?.Rows.FirstOrDefault() is { } blocked)
        {
            Timed("robots.txt: one blocked URL's row", "robots", "robots", 1, () => RobotsReader.ReadBlocked(path, blocking.Origin, urlId: blocked.Id));
            Timed("Inspect a URL robots.txt refused (its origin and line)", "inspector", "inspector", null, () => UrlInspectorReader.Read(path, blocked.Id));
        }

        if (Wanted("Inspect a URL sitemaps list") && First(new UrlQuery { Filters = [Boolean("inSitemap", true)], Limit = 1 }) is { } listed)
        {
            Timed("Inspect a URL sitemaps list (the sitemaps named)", "inspector", "inspector", null, () => UrlInspectorReader.Read(path, listed));
        }
    }

    /// <summary>
    /// From a view of the links to its URLs: the view's ends as a list, and the URL grid narrowed to them. The targets come
    /// from what the link summary counts (<c>link-grid</c>) where it holds what the view's filters test; the pages the
    /// links are on, and either end of a view beyond the summary, take a pass over every link, on one connection
    /// (<c>link-pass</c>) and split over those a host lends (<c>link-pass-split</c>).
    /// </summary>
    private void LinkEnds()
    {
        (string Name, UrlFilter[] Filters, bool Summarised)[] views =
        [
            ("every link", [], true),
            ("the hyperlinks to 4xx URLs", [In("kind", "hyperlink"), In("to.statusClass", "4xx")], true),
            ("the links whose text contains a common word", [Text("text", FilterOperator.Contains, "product")], false)
        ];
        foreach (var (name, filters, summarised) in views)
        {
            if (summarised)
            {
                Ends($"Link ends: the targets of {name}", "link-grid", LinkEnd.To, filters);
            }
            else
            {
                Ends($"Link ends: the targets of {name} (every link read)", "link-pass", LinkEnd.To, filters);
                Ends($"Link ends: the targets of {name} (every link read, on several connections)", "link-pass-split", LinkEnd.To, filters, HELPERS);
            }

            Ends($"Link ends: the pages of {name} (every link read)", "link-pass", LinkEnd.From, filters);
            Ends($"Link ends: the pages of {name} (every link read, on several connections)", "link-pass-split", LinkEnd.From, filters, HELPERS);
        }
    }

    /// <summary>
    /// A view of the links grouped by target (step 3b): its first page and the page half way down. Every grouped view takes
    /// one pass over every link in crawl order, which counts the pages a target's links are on: cold on one connection
    /// (<c>link-pass</c>) and split over those a host lends (<c>link-pass-split</c>). Then, the pass kept, the same pages
    /// again and in other orders, which read only their rows (<c>link-grid</c>): each target's links' text, on the links'
    /// index by target, and its facts.
    /// </summary>
    private void LinkGroups()
    {
        (string Name, UrlFilter[] Filters, UrlSort? Sort)[] views =
        [
            ("every link, the most linked from pages first", [], null),
            ("the hyperlinks to 4xx URLs", [In("kind", "hyperlink"), In("to.statusClass", "4xx")], null),
            ("the images, the largest first", [In("kind", "image")], new UrlSort("to.sizeBytes", true)),
            ("the links whose text contains a common word", [Text("text", FilterOperator.Contains, "product")], null),
            ("every link, by target URL", [], new UrlSort("to.url"))
        ];
        foreach (var (name, filters, sort) in views)
        {
            Groups($"Link groups: {name} (every link read)", "link-pass", filters, sort, null);
            Groups($"Link groups: {name} (every link read, on several connections)", "link-pass-split", filters, sort, () => new ReadCache { Helpers = HELPERS });
            if (Wanted($"Link groups: {name} (counted)"))
            {
                ReadCache kept = new();
                LinkGridReader.Groups(path, new LinkGroupQuery { Filters = filters, Sort = sort, Limit = 1 }, kept);
                Groups($"Link groups: {name} (counted)", "link-grid", filters, sort, () => kept);
            }
        }
    }

    /// <summary>A grouped view's first page and the page half way down, each read with the cache given, or none.</summary>
    private void Groups(string name, string readClass, UrlFilter[] filters, UrlSort? sort, Func<ReadCache>? cache)
    {
        if (!Wanted(name))
        {
            return;
        }

        var total = LinkGridReader.Groups(path, new LinkGroupQuery { Filters = filters, Sort = sort, Limit = 1 }).Total;
        Paged(name, "link-groups", readClass, total, offset => LinkGridReader.Groups(path, new LinkGroupQuery { Filters = filters, Sort = sort, Offset = offset }, cache?.Invoke()));
    }

    /// <summary>One end of a view's links: the list's first page and the page half way down, then the URL grid narrowed to them.</summary>
    private void Ends(string name, string readClass, LinkEnd end, UrlFilter[] filters, ReadHelpers? helpers = null)
    {
        if (!Wanted(name))
        {
            return;
        }

        ReadCache? Cache() => helpers is null ? null : new ReadCache { Helpers = helpers };
        long total;
        try
        {
            total = LinkGridReader.Ends(path, new LinkEndQuery { End = end, Filters = filters, Limit = 1 }).Total;
        }
        catch (NeedsAnalysisException)
        {
            progress($"  skipped    {name}: it needs the URL summary");
            return;
        }

        Paged(name, "link-ends", readClass, total, offset => LinkGridReader.Ends(path, new LinkEndQuery { End = end, Filters = filters, Offset = offset }, Cache()));
        Timed($"{name}: the URL grid narrowed to them", "grid", readClass, total,
            () => UrlGridReader.Query(path, new UrlQuery { Columns = COLUMNS, LinkEnds = new UrlLinkEnds(end, filters) }, Cache()));
    }

    /// <summary>
    /// The links grid: its pages in crawl order and sorted by the target, unfiltered and under filters common and rare on
    /// what a link is and on its target, and its facets, which the link summary answers (<c>link-grid</c>): among them a
    /// few targets with many links between them, a menu's, whose pages are found on the links' index by target at any
    /// depth, and a filter on the target that most links of their kind pass, read from the blocks but for the others. Then
    /// the reads that go through many targets' links on that index once per filter set (<c>link-targets</c>), and those
    /// that pass over every link once per filter set (<c>link-pass</c>): a filter on the link's text or on the page it's
    /// on, a search, a filter on the target with one on a link's position that the fewer links pass, deep among many
    /// links, and everything without the link summary.
    /// </summary>
    private void LinksGrid()
    {
        Linked("Links grid: crawl order", "link-grid", new UrlQuery());
        Linked("Links grid: crawl order, last first", "link-grid", new UrlQuery { Sort = [new UrlSort("id", true)] });
        Linked("Links grid: hyperlinks (a common filter)", "link-grid", new UrlQuery { Filters = [In("kind", "hyperlink")] });
        Linked("Links grid: in the footer", "link-grid", new UrlQuery { Filters = [In("position", "footer")] });
        Linked("Links grid: nofollow (a rare filter)", "link-grid", new UrlQuery { Filters = [Boolean("noFollow", true)] });
        Linked("Links grid: not followed", "link-grid", new UrlQuery { Filters = [Boolean("followed", false)] });
        Linked("Links grid: to 4xx URLs", "link-grid", new UrlQuery { Filters = [In("to.statusClass", "4xx")] });
        Linked("Links grid: hyperlinks to 404s in the content (three filters)", "link-grid",
            new UrlQuery { Filters = [In("kind", "hyperlink"), In("position", "content"), Number("to.statusCode", FilterOperator.Eq, 404)] });
        Linked("Links grid: to other sites", "link-grid", new UrlQuery { Filters = [Boolean("to.isInternal", false)] });
        Linked("Links grid: to URLs containing text", "link-grid", new UrlQuery { Filters = [Text("to.url", FilterOperator.Contains, "/p/12")] });
        Linked("Links grid: to non-indexable pages, hyperlinks", "link-grid", new UrlQuery { Filters = [In("kind", "hyperlink"), Boolean("to.indexable", false)] });
        Linked("Links grid: images without alt text", "link-grid", new UrlQuery { Filters = [In("kind", "image"), new UrlFilter("text", FilterOperator.IsEmpty)] });
        Linked("Links grid: by target URL", "link-grid", new UrlQuery { Sort = [new UrlSort("to.url")] });
        Linked("Links grid: by target status, highest first", "link-grid", new UrlQuery { Sort = [new UrlSort("to.statusCode", true)] });
        Linked("Links grid: hyperlinks by the target's inlinks", "link-grid", new UrlQuery { Filters = [In("kind", "hyperlink")], Sort = [new UrlSort("to.inlinks", true)] });
        Linked("Links grid: to 4xx URLs by target URL", "link-grid", new UrlQuery { Filters = [In("to.statusClass", "4xx")], Sort = [new UrlSort("to.url")] });
        Timed("Links grid: facets of every link", "facets", "link-grid", null, () => LinkGridReader.Facets(path, new UrlFacetQuery()));
        Timed("Links grid: facets of the hyperlinks", "facets", "link-grid", null, () => LinkGridReader.Facets(path, new UrlFacetQuery { Filters = [In("kind", "hyperlink")] }));
        Timed("Links grid: facets of links to other sites", "facets", "link-grid", null, () => LinkGridReader.Facets(path, new UrlFacetQuery { Filters = [Boolean("to.isInternal", false)] }));

        var start = UrlGridReader.Query(path, new UrlQuery { Limit = 1 }).Rows.Select(row => row.Id).FirstOrDefault(1);
        Linked("Links grid: from one page", "link-grid", new UrlQuery { Filters = [Number("from.id", FilterOperator.Eq, start)] });
        Linked("Links grid: to one page (the start URL's inlinks)", "link-grid", new UrlQuery { Filters = [Number("to.id", FilterOperator.Eq, start)] });

        // A few targets with links from every page between them (a menu's): half of a crawl's links, to a few dozen URLs.
        var mostLinked = Linked(new UrlQuery { Sort = [new UrlSort("inlinks", true)], Limit = 12 });
        if (mostLinked.Count > 1)
        {
            Linked("Links grid: to the twelve most linked pages (few targets, many links)", "link-grid",
                new UrlQuery { Filters = [new UrlFilter("to.id", FilterOperator.In) { Values = [.. mostLinked.Select(id => FilterValue.Of(id))] }] });
        }

        Linked("Links grid: to URLs under /c/ (few targets, many links)", "link-grid", new UrlQuery { Filters = [Text("to.url", FilterOperator.Contains, "/c/")] });
        Linked("Links grid: to URLs with over 50,000 inlinks (few targets, many links)", "link-grid", new UrlQuery { Filters = [Number("to.inlinks", FilterOperator.Gt, 50_000)] });
        Linked("Links grid: to pages a click from the start (few targets, many links)", "link-grid", new UrlQuery { Filters = [Number("to.depth", FilterOperator.Lte, 1)] });
        Linked("Links grid: hyperlinks to pages a click from the start (few targets, many links, one kind)", "link-grid",
            new UrlQuery { Filters = [In("kind", "hyperlink"), Number("to.depth", FilterOperator.Lte, 1)] });

        // More targets than are listed for their index, with many links between them; and a filter most links pass.
        Linked("Links grid: to pages four clicks from the start (many targets, many links)", "link-targets", new UrlQuery { Filters = [Number("to.depth", FilterOperator.Eq, 4)] });
        // A filter on the target that most of the links it could match pass: counted by block but for the others.
        Linked("Links grid: to the site's own URLs (most links)", "link-grid", new UrlQuery { Filters = [Boolean("to.isInternal", true)] });
        Linked("Links grid: hyperlinks to the site's own URLs (most links, one kind)", "link-grid", new UrlQuery { Filters = [In("kind", "hyperlink"), Boolean("to.isInternal", true)] });
        Linked("Links grid: navigation links to pages a click from the start (a target and a position, most pass)", "link-grid",
            new UrlQuery { Filters = [In("position", "navigation"), Number("to.depth", FilterOperator.Lte, 1)] });

        // The reads that pass over every link: on one connection, as a running crawl's are read and as any is when every
        // read slot is taken (link-pass); and split over the connections a host with cores to spare lends (link-pass-split).
        (string Name, UrlQuery Query)[] passes =
        [
            ("text contains a common word", new UrlQuery { Filters = [Text("text", FilterOperator.Contains, "product")] }),
            ("text contains rare text", new UrlQuery { Filters = [Text("text", FilterOperator.Contains, "Product 12345")] }),
            ("text matches a pattern", new UrlQuery { Filters = [Text("text", FilterOperator.Regex, @"Product 1\d{3}$")] }),
            ("from pages three clicks from the start", new UrlQuery { Filters = [Number("from.depth", FilterOperator.Eq, 3)] }),
            ("from pages four clicks from the start", new UrlQuery { Filters = [Number("from.depth", FilterOperator.Eq, 4)] }),
            ("search", new UrlQuery { Search = "/p/123" }),
            ("search for a common word", new UrlQuery { Search = "product" }),
            ("content links to pages four clicks from the start (a target and a position, the fewer pass: deep in)",
                new UrlQuery { Filters = [In("position", "content"), Number("to.depth", FilterOperator.Eq, 4)] })
        ];
        Linked("Links grid: from pages at depth 2 (few pages: their links read through their index)", "link-pass", new UrlQuery { Filters = [Number("from.depth", FilterOperator.Eq, 2)] });
        foreach (var (name, query) in passes)
        {
            Linked($"Links grid: {name} (every link read)", "link-pass", query);
            Linked($"Links grid: {name} (every link read, on several connections)", "link-pass-split", query, HELPERS);
        }

        Timed("Links grid: facets of a text filter (every link read)", "facets", "link-pass", null,
            () => LinkGridReader.Facets(path, new UrlFacetQuery { Filters = [Text("text", FilterOperator.Contains, "product")] }));
        Timed("Links grid: facets of a text filter (every link read, on several connections)", "facets", "link-pass-split", null,
            () => LinkGridReader.Facets(path, new UrlFacetQuery { Filters = [Text("text", FilterOperator.Contains, "product")] }, new ReadCache { Helpers = HELPERS }));

        GridReadContext.ReadWithoutLinkSummary = true;
        try
        {
            Linked("Without the link summary: crawl order", "link-grid", new UrlQuery());
            Linked("Without the link summary: hyperlinks", "link-pass", new UrlQuery { Filters = [In("kind", "hyperlink")] });
            Linked("Without the link summary: nofollow", "link-pass", new UrlQuery { Filters = [Boolean("noFollow", true)] });
            Linked("Without the link summary: by target URL", "link-pass", new UrlQuery { Sort = [new UrlSort("to.url")] });
            Timed("Without the link summary: facets of every link", "facets", "link-pass", null, () => LinkGridReader.Facets(path, new UrlFacetQuery()));
        }
        finally
        {
            GridReadContext.ReadWithoutLinkSummary = false;
        }
    }

    /// <summary>
    /// Overview's headline numbers: read for the first time, read again from what was kept, and as a crawl running or never
    /// analysed has them, without the link summary (<c>stats-pass</c>: the links are counted, and each page a sitemap led to
    /// looked up among them).
    /// </summary>
    private void Stats()
    {
        Timed("Stats: the crawl's headline numbers", "stats", "stats", null, () => CrawlStatsReader.Read(path));
        ReadCache kept = new();
        Timed("Stats: again, kept", "stats", "stats-kept", null, () => CrawlStatsReader.Read(path, kept));
        GridReadContext.ReadWithoutLinkSummary = true;
        try
        {
            Timed("Stats: without the link summary", "stats", "stats-pass", null, () => CrawlStatsReader.Read(path));
        }
        finally
        {
            GridReadContext.ReadWithoutLinkSummary = false;
        }
    }

    /// <summary>
    /// The connections a read may use beside its own, as a host with every core free would lend them: every other core. A
    /// read with them still keeps nothing between runs: each is given a cache of its own, empty, that only carries them.
    /// </summary>
    private static readonly ReadHelpers HELPERS = ReadHelpers.UpTo(Environment.ProcessorCount - 1);

    /// <summary>A links grid read: its first page, and the page half way down and the one just past it.</summary>
    /// <param name="helpers">Connections the read may split a pass over, beside its own; none reads on one.</param>
    private void Linked(string name, string readClass, UrlQuery query, ReadHelpers? helpers = null)
    {
        if (!Wanted(name))
        {
            return;
        }

        var shown = query with { Columns = ["from.url", "to.url", "kind", "text", "position", "followed", "to.statusCode"] };
        long matching;
        try
        {
            matching = LinkGridReader.Query(path, shown with { Limit = 1 }).Matching;
        }
        catch (NeedsAnalysisException)
        {
            progress($"  skipped    {name}: it needs the URL summary");
            return;
        }

        Paged(name, "links-grid", readClass, matching, offset => LinkGridReader.Query(path, shown with { Offset = offset }, helpers is null ? null : new ReadCache { Helpers = helpers }));
    }

    /// <summary>
    /// The grids' reads as a crawl without its URL summary has them, from the crawl's own tables: one running, or continued
    /// since its analysis. The file isn't changed: the reads only take it as having none.
    /// </summary>
    private void WithoutSummary()
    {
        GridReadContext.ReadWithoutSummary = true;
        try
        {
            Grid("Without the summary: crawl order", "crawl", new UrlQuery());
            Grid("Without the summary: sort by status", "crawl", new UrlQuery { Sort = [new UrlSort("statusCode")] });
            Grid("Without the summary: sort by response time desc", "crawl", new UrlQuery { Sort = [new UrlSort("responseMs", true)] });
            Grid("Without the summary: pages by response time", "crawl", Query([In("type", "page")], "responseMs", descending: true));
            Grid("Without the summary: 4xx by depth", "crawl", Query([In("statusClass", "4xx")], "depth", descending: false));
            Grid("Without the summary: title contains a common word, by URL", "crawl", Query([Text("title", FilterOperator.Contains, "product")], "url", descending: false));
            Grid("Without the summary: search for a common word", "crawl", new UrlQuery { Search = "product" });
            Timed("Without the summary: facets of every URL", "facets", "crawl-facets", null, () => UrlGridReader.Facets(path, new UrlFacetQuery()));
            Timed("Without the summary: facets of the pages", "facets", "crawl-facets", null, () => UrlGridReader.Facets(path, new UrlFacetQuery { Filters = [In("type", "page")] }));
        }
        finally
        {
            GridReadContext.ReadWithoutSummary = false;
        }
    }

    /// <summary>A URL many issues name as the other URL involved: the largest group of a check grouped by the other URL.</summary>
    private int? MostNamed()
    {
        var best = IssueReader.ReadCounts(path)
            .Where(check => check.Value > 0 && IssueCatalog.Find(check.Key)?.Grouping == IssueGrouping.RelatedUrl)
            .Select(check => IssueGroupReader.ReadGroups(path, check.Key, 0, 1)?.Groups.FirstOrDefault())
            .OfType<IssueGroup>()
            .Where(group => group.RelatedUrlId is not null)
            .MaxBy(group => group.Pages);
        return best?.RelatedUrlId;
    }

    /// <summary>The URLs the query lists, by id; none without the URL summary.</summary>
    private List<int> Linked(UrlQuery query)
    {
        try
        {
            return [.. UrlGridReader.Query(path, query).Rows.Select(row => row.Id)];
        }
        catch (NeedsAnalysisException)
        {
            return [];
        }
    }

    /// <summary>The first URL the query lists, but for <paramref name="other"/>; null without the URL summary.</summary>
    private int? First(UrlQuery query, int? other = null)
    {
        try
        {
            return UrlGridReader.Query(path, query).Rows.Select(row => (int?)row.Id).FirstOrDefault(id => id != other);
        }
        catch (NeedsAnalysisException)
        {
            return null;
        }
    }

    /// <summary>A URL's inlinks of some kinds (every kind when none), a page at a time.</summary>
    private void Links(string name, int urlId, IReadOnlyList<LinkKind> kinds)
    {
        if (!Wanted(name))
        {
            return;
        }

        var total = UrlInspectorReader.ReadLinks(path, urlId, LinkDirection.Inlinks, new LinkQuery { Kinds = kinds, Limit = 1 })?.Total ?? 0;
        Paged(name, "links", kinds.Count == 0 ? "links" : "links-kind", total,
            offset => UrlInspectorReader.ReadLinks(path, urlId, LinkDirection.Inlinks, new LinkQuery { Kinds = kinds, Offset = offset }));
    }

    /// <summary>An index of the URL summary gives the column's order, the row id's and the URL's included.</summary>
    private static bool Indexed(string column) => column is "id" or "url" || UrlColumns.Find(column, issueColumns: false) is { Indexed: true };

    private static UrlQuery Query(UrlFilter[] filters, string sort, bool descending) => new() { Filters = filters, Sort = [new UrlSort(sort, descending)] };

    private static UrlFilter In(string column, string value) => new(column, FilterOperator.In) { Values = [FilterValue.Of(value)] };

    private static UrlFilter Number(string column, FilterOperator op, double value) => new(column, op) { Value = FilterValue.Of(value) };

    private static UrlFilter Boolean(string column, bool value) => new(column, FilterOperator.Is) { Value = FilterValue.Of(value) };

    private static UrlFilter Text(string column, FilterOperator op, string value) => new(column, op) { Value = FilterValue.Of(value) };

    private void Grid(string name, string readClass, UrlQuery query)
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

        Paged(name, "grid", readClass, matching, offset => UrlGridReader.Query(path, shown with { Offset = offset }));
    }

    /// <summary>A paged read: its first page, and the page half way down and the one just past it when there's more than one.</summary>
    private void Paged(string name, string kind, string readClass, long rows, Func<int, object?> read)
    {
        if (!Wanted(name))
        {
            return;
        }

        var watched = Watch(name);
        try
        {
            var first = Time(() => read(0));
            (double P50, double P95)? half = null, pastHalf = null;
            if (rows > UrlGridReader.DEFAULT_LIMIT)
            {
                half = Time(() => read((int)(rows / 2)));
                pastHalf = Time(() => read((int)(rows / 2) + 1));
            }

            Add(new ReaderBenchCase(name, kind, readClass, rows, first.P50, first.P95, half?.P50, half?.P95, pastHalf?.P50, pastHalf?.P95));
        }
        finally
        {
            if (watched)
            {
                LinkGridReader.Split = null;
            }
        }
    }

    private void Timed(string name, string kind, string readClass, long? rows, Func<object?> read)
    {
        if (!Wanted(name))
        {
            return;
        }

        var watched = Watch(name);
        try
        {
            var (p50, p95) = Time(() => read());
            Add(new ReaderBenchCase(name, kind, readClass, rows, p50, p95, null, null, null, null));
        }
        finally
        {
            if (watched)
            {
                LinkGridReader.Split = null;
            }
        }
    }

    /// <summary>For a case read split over several connections: hears how many each of its passes was split over.</summary>
    private bool Watch(string name)
    {
        if (!name.Contains(SplitGate.SPLIT, StringComparison.Ordinal))
        {
            return false;
        }

        List<int> reported = [];
        _connections[name] = reported;
        LinkGridReader.Split = connections =>
        {
            lock (reported)
            {
                reported.Add(connections);
            }
        };
        return true;
    }

    private bool Wanted(string name) => only is null || name.Contains(only, StringComparison.OrdinalIgnoreCase);

    private void Add(ReaderBenchCase result)
    {
        _cases.Add(result);
        var half = result.HalfP95Ms is { } halfP95 ? $"  half way {result.HalfP50Ms:N1} / {halfP95:N1} ms, past it {result.PastHalfP50Ms:N1} / {result.PastHalfP95Ms:N1} ms" : string.Empty;
        progress($"  {result.WorstP95Ms,8:N1} ms  {result.Name} ({result.Matching?.ToString("N0") ?? "-"} rows): first {result.FirstP50Ms:N1} / {result.FirstP95Ms:N1} ms{half}");
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
