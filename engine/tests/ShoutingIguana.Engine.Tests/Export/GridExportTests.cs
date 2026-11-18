using System.Globalization;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Export;
using ShoutingIguana.Engine.Export.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Tests.Support;
using static ShoutingIguana.Engine.Tests.Support.GridQueries;

namespace ShoutingIguana.Engine.Tests.Export;

/// <summary>
/// Each grid's export, read back from the file and held against the grid's own read, row for row and cell for cell: the
/// URL grid, the links grid, a sitemap's URLs, a check's rows and one group's, an origin's blocked URLs, each as CSV and
/// as a workbook. The grid is read here through its public reader by offset, a page at a time, as the app reads it; the
/// export reads on by cursor, in pages of its own size, which the tests make small so every export crosses many.
/// </summary>
[Collection(GridCrawlCollection.NAME)]
public sealed class GridExportTests : IDisposable
{
    private static readonly string[] URL_COLUMNS =
    [
        "url", "id", "statusCode", "statusClass", "outcome", "indexable", "indexability", "type", "source", "depth", "isInternal", "fetched", "title", "responseMs",
        "sizeBytes", "inlinks", "issues", "inSitemap", "canonicalUrl", "wordCount"
    ];

    private static readonly string[] LINK_COLUMNS =
        ["id", "from.url", "to.url", "kind", "text", "position", "followed", "noFollow", "selfLink", "to.statusCode", "to.indexability", "from.depth", "to.isInternal"];

    private readonly GridCrawl _crawl;
    private readonly string _folder = Directory.CreateTempSubdirectory("si-export-").FullName;
    private readonly ReadCache _cache = new();
    private readonly (int Urls, int Links) _pages = (GridExportReader.UrlPageRows, GridExportReader.LinkPageRows);

    public GridExportTests(GridCrawl crawl)
    {
        _crawl = crawl;

        // Small pages, so that an export of this crawl is read on by cursor many times over.
        (GridExportReader.UrlPageRows, GridExportReader.LinkPageRows) = (97, 211);
    }

    public void Dispose()
    {
        (GridExportReader.UrlPageRows, GridExportReader.LinkPageRows) = _pages;
        Directory.Delete(_folder, recursive: true);
    }

    public static TheoryData<string, string[], UrlFilter[], string?, UrlSort[]> UrlQueries() => new()
    {
        { "everything, as it comes", [], [], null, [] },
        { "every column, in crawl order", URL_COLUMNS, [], null, [] },
        { "pages by inlinks", URL_COLUMNS, [Names("type", FilterOperator.In, "page")], null, [new UrlSort("inlinks", true)] },
        { "by status then URL", ["url", "statusCode", "title"], [], null, [new UrlSort("statusCode"), new UrlSort("url", true)] },
        { "errors only", ["url", "statusCode", "statusClass", "outcome"], [Names("statusClass", FilterOperator.In, "4xx", "5xx", "noResponse")], null, [new UrlSort("url")] },
        { "a search", ["url", "title", "depth"], [], "product", [new UrlSort("depth", true)] },
        {
            COMBINED, ["url", "title", "statusCode", "inlinks", "depth"], [Names("type", FilterOperator.In, "page"), Number("statusCode", FilterOperator.Eq, 200)], "/p/",
            [new UrlSort("inlinks", true), new UrlSort("url")]
        },
        { "nothing matches", ["url", "title"], [Number("statusCode", FilterOperator.Eq, 799)], null, [] }
    };

    /// <summary>A query with filters, a search and a sort at once, as a grid someone has narrowed and ordered is exported.</summary>
    private const string COMBINED = "filters, a search and a sort together";

    [Theory]
    [MemberData(nameof(UrlQueries))]
    public void TheUrlGridIsExportedAsTheGridReadsIt(string name, string[] columns, UrlFilter[] filters, string? search, UrlSort[] sort)
    {
        var query = new UrlQuery { Columns = columns, Filters = filters, Search = search, Sort = sort };
        var shown = Shown(columns.Length == 0 ? ["url"] : columns, UrlGridReader.Columns);
        var expected = Rows(shown, at => Page(UrlGridReader.Query(_crawl.Path, at, _cache)), query);
        Assert.True(name == "nothing matches" ? expected.Count == 0 : expected.Count > (filters.Length == 0 && search is null ? GridExportReader.UrlPageRows * 3 : 0), $"{name}: {expected.Count} rows.");
        if (name == COMBINED)
        {
            // Narrowed, yet more than a page of the export: the cursor carries the filters, the search and the sort on.
            var all = UrlGridReader.Query(_crawl.Path, new UrlQuery { Limit = 1 }).Matching;
            Assert.True(expected.Count > GridExportReader.UrlPageRows && expected.Count < all / 2, $"{expected.Count} of {all} rows.");
            Assert.All(expected, row => Assert.Contains("/p/", (string)row[0]!, StringComparison.Ordinal));
        }

        foreach (var format in new[] { ExportFormat.Csv, ExportFormat.Xlsx })
        {
            var (result, rows) = Export(_crawl.Path, new ExportRequest { Kind = ExportKind.Urls, Format = format, Query = query }, "URLs");
            AssertSame(shown, expected, rows, format);
            Assert.Equal(expected.Count, result.Rows);
            Assert.Empty(result.UnavailableColumns);
            Assert.False(result.CrawlWasRunning);
        }
    }

    /// <summary>
    /// The checks the user hid are left out of the issue counts in the file as in the grid (round 5 review): the URL grid's
    /// and a check's rows', each as CSV and as a workbook.
    /// </summary>
    [Fact]
    public void TheIssueCountsAreExportedWithoutTheHiddenChecks()
    {
        string[] columns = ["id", "url", "issues", "errors", "warnings", "notices"];
        IReadOnlyList<string> hidden =
        [
            .. _crawl.Reference.Issues.Values.SelectMany(issues => issues.Select(issue => issue.Issue)).GroupBy(issue => issue, StringComparer.Ordinal)
                .GroupBy(group => IssueCatalog.Find(group.Key)!.Severity)
                .Select(severity => severity.OrderByDescending(group => group.Count()).ThenBy(group => group.Key, StringComparer.Ordinal).First().Key)
        ];
        var reference = _crawl.Reference.WithoutChecks(hidden);
        var query = new UrlQuery { Columns = columns, Sort = [new UrlSort("errors", true)], HiddenChecks = hidden };
        var shown = Shown(columns, UrlGridReader.Columns);
        var expected = Rows(shown, at => Page(UrlGridReader.Query(_crawl.Path, at, _cache)), query);
        Assert.Equal(reference.Rows.Count, expected.Count);
        Assert.All(expected, row => Assert.Equal(
            new[] { "issues", "errors", "warnings", "notices" }.Select(column => reference.Rows[(int)(long)row[0]!][column]),
            row[2..]));
        Assert.NotEqual(Rows(shown, at => Page(UrlGridReader.Query(_crawl.Path, at, _cache)), query with { HiddenChecks = [] }), expected);

        foreach (var format in new[] { ExportFormat.Csv, ExportFormat.Xlsx })
        {
            var (result, rows) = Export(_crawl.Path, new ExportRequest { Kind = ExportKind.Urls, Format = format, Query = query }, "URLs");
            AssertSame(shown, expected, rows, format);
            Assert.Equal(expected.Count, result.Rows);
        }

        // A check's rows count the URL's other issues the same way: the check shown, the hidden ones left out.
        var check = _crawl.Reference.Issues.Values.SelectMany(issues => issues.Select(issue => issue.Issue)).First(id => !hidden.Contains(id));
        var checkRows = new UrlQuery { Columns = columns, Sort = [new UrlSort("url")], HiddenChecks = hidden };
        var fromGrid = Rows(shown, at => Page(IssueGroupReader.ReadRows(_crawl.Path, check, at, null, _cache)!.Page!), checkRows);
        Assert.All(fromGrid, row => Assert.Equal(reference.Rows[(int)(long)row[0]!]["issues"], row[2]));
        var (_, file) = Export(_crawl.Path, new ExportRequest { Kind = ExportKind.IssueRows, Format = ExportFormat.Csv, Query = checkRows, CheckId = check }, check);
        AssertSame(shown, fromGrid, file, ExportFormat.Csv, check);
    }

    /// <summary>The header's words and a row's, as literals: a grid's own titles and value words, a number's unit after its title.</summary>
    [Fact]
    public void AFileSaysWhatTheGridSaysInTheGridsWords()
    {
        var request = new ExportRequest
        {
            Kind = ExportKind.Urls,
            Format = ExportFormat.Xlsx,
            Query = new UrlQuery
            {
                Columns = ["url", "statusCode", "statusClass", "indexable", "indexability", "type", "source", "isInternal", "responseMs", "sizeBytes", "id"],
                Filters = [Number("id", FilterOperator.Eq, 1)]
            }
        };
        var (_, rows) = Export(_crawl.Path, request, "URLs");
        Assert.Equal(
            ["URL", "Status", "Status class", "Indexable", "Indexability", "Type", "Found by", "On this site", "Response time (ms)", "Size (bytes)", "Crawl order"],
            rows[0]);
        var start = Assert.Single(rows.Skip(1));
        Assert.Equal(_crawl.Server.BaseUri.AbsoluteUri, start[0]);
        Assert.Equal([200d, "2xx", "Yes", "Indexable", "Page", "The start URL", "Yes"], start[1..8]);
        Assert.True(start[8] is double && start[9] is double, "A time and a size are numbers.");
        Assert.Equal(1d, start[10]);

        // And the links grid's: either end's column under "Source" or "Target".
        var links = new ExportRequest
        {
            Kind = ExportKind.Links,
            Format = ExportFormat.Csv,
            Query = new UrlQuery
            {
                Columns = ["from.url", "to.url", "to.statusCode", "to.h1Count", "from.isInternal", "kind", "text", "selfLink", "to.responseMs", "id"],
                Filters = [Names("kind", FilterOperator.In, "hyperlink"), Boolean("from.isInternal", true), Number("from.id", FilterOperator.Eq, 1)]
            }
        };
        var (_, link) = Export(_crawl.Path, links, "Links");
        Assert.Equal(
            ["Source URL", "Target URL", "Target status", "Target H1s", "Source on this site", "Kind", "Anchor or alt text", "Links to its own page", "Target response time (ms)",
                "Crawl order"],
            link[0]);
        Assert.Equal("Yes", link[1][4]);
        Assert.Equal("Hyperlink", link[1][5]);
        Assert.Equal(_crawl.Server.BaseUri.AbsoluteUri, link[1][0]);
    }

    public static TheoryData<string, string[], UrlFilter[], string?, UrlSort[]> LinkQueries() => new()
    {
        { "everything, as it comes", [], [], null, [] },
        { "every column, in crawl order", LINK_COLUMNS, [], null, [] },
        { "newest first", ["id", "from.url", "to.url"], [], null, [new UrlSort("id", true)] },
        { "hyperlinks by target", LINK_COLUMNS, [Names("kind", FilterOperator.In, "hyperlink")], null, [new UrlSort("to.url")] },
        { "to broken URLs", ["from.url", "to.url", "to.statusCode", "text"], [Names("to.statusClass", FilterOperator.In, "4xx")], null, [] },
        { "text contains", ["from.url", "text", "kind"], [Text("text", FilterOperator.Contains, "product")], null, [] },
        { "a search", ["from.url", "to.url", "text"], [], "about", [] },
        {
            COMBINED, ["from.url", "to.url", "kind", "text", "to.statusCode"], [Names("kind", FilterOperator.In, "hyperlink"), Boolean("to.isInternal", true)], "/p/",
            [new UrlSort("to.url", true)]
        }
    };

    [Theory]
    [MemberData(nameof(LinkQueries))]
    public void TheLinksGridIsExportedAsTheGridReadsIt(string name, string[] columns, UrlFilter[] filters, string? search, UrlSort[] sort)
    {
        var query = new UrlQuery { Columns = columns, Filters = filters, Search = search, Sort = sort };
        var shown = Shown(columns.Length == 0 ? ["from.url", "to.url", "kind", "text"] : columns, LinkGridReader.Columns);
        var expected = Rows(shown, at => Page(LinkGridReader.Query(_crawl.Path, at, _cache)), query);
        Assert.True(expected.Count > 0, $"{name}: no rows.");
        if (name == COMBINED)
        {
            var all = LinkGridReader.Query(_crawl.Path, new UrlQuery { Limit = 1 }).Matching;
            Assert.True(expected.Count > GridExportReader.LinkPageRows && expected.Count < all, $"{expected.Count} of {all} rows.");
            Assert.All(expected, row => Assert.Equal("hyperlink", row[2]));
        }

        foreach (var format in new[] { ExportFormat.Csv, ExportFormat.Xlsx })
        {
            var (result, rows) = Export(_crawl.Path, new ExportRequest { Kind = ExportKind.Links, Format = format, Query = query }, "Links");
            AssertSame(shown, expected, rows, format);
            Assert.Equal(expected.Count, result.Rows);
        }
    }

    /// <summary>
    /// The URL grid narrowed to a links view's pages or targets (<c>linkEnds</c>) is exported as the grid reads it under
    /// the same links, beside its own filter, search and sort, and the list of ends agrees on how many there are.
    /// </summary>
    [Theory]
    [InlineData(LinkEnd.To, false)]
    [InlineData(LinkEnd.From, false)]
    [InlineData(LinkEnd.To, true)]
    [InlineData(LinkEnd.From, true)]
    public void TheUrlGridNarrowedToALinksViewsEndsIsExportedAsTheGridReadsIt(LinkEnd end, bool narrowed)
    {
        UrlLinkEnds ends = new(end, [Names("kind", FilterOperator.In, "hyperlink"), Text("to.url", FilterOperator.Contains, "/p/")]);
        var query = narrowed
            ? new UrlQuery { Columns = ["url", "statusCode", "title", "depth"], Filters = [Number("depth", FilterOperator.Gte, 1)], Search = "p", Sort = [new UrlSort("url", true)], LinkEnds = ends }
            : new UrlQuery { Columns = ["url", "statusCode", "inlinks"], LinkEnds = ends };
        var shown = Shown(query.Columns, UrlGridReader.Columns);
        var expected = Rows(shown, at => Page(UrlGridReader.Query(_crawl.Path, at, _cache)), query);
        var all = UrlGridReader.Query(_crawl.Path, new UrlQuery { Limit = 1 }).Matching;
        Assert.True(expected.Count > GridExportReader.UrlPageRows && expected.Count < all, $"{expected.Count} of {all} rows.");
        if (!narrowed)
        {
            Assert.Equal(LinkGridReader.Ends(_crawl.Path, new LinkEndQuery { End = end, Filters = ends.Filters }, _cache).Total, expected.Count);
        }

        foreach (var format in new[] { ExportFormat.Csv, ExportFormat.Xlsx })
        {
            var (result, rows) = Export(_crawl.Path, new ExportRequest { Kind = ExportKind.Urls, Format = format, Query = query }, "URLs");
            AssertSame(shown, expected, rows, format);
            Assert.Equal(expected.Count, result.Rows);
        }
    }

    [Fact]
    public void ASitemapsUrlsAreExportedAsItsGridReadsThem()
    {
        var sitemap = SitemapListReader.Read(_crawl.Path).Sitemaps.MaxBy(listed => listed.Urls.Listed)!;
        var query = new UrlQuery { Columns = ["url", "statusCode", "indexable", "indexability"], Sort = [new UrlSort("url")] };
        var shown = Shown(query.Columns, UrlGridReader.Columns);
        var expected = Rows(shown, at => Page(UrlGridReader.QuerySitemap(_crawl.Path, sitemap.Id, at, _cache)!), query);
        Assert.Equal(sitemap.Urls.Listed, expected.Count);
        Assert.True(expected.Count > GridExportReader.UrlPageRows);

        foreach (var format in new[] { ExportFormat.Csv, ExportFormat.Xlsx })
        {
            var (_, rows) = Export(_crawl.Path, new ExportRequest { Kind = ExportKind.SitemapUrls, Format = format, Query = query, SitemapId = sitemap.Id }, "Sitemap URLs");
            AssertSame(shown, expected, rows, format);
        }

        var missing = Assert.Throws<ExportException>(() => Export(_crawl.Path,
            new ExportRequest { Kind = ExportKind.SitemapUrls, Format = ExportFormat.Csv, Query = query, SitemapId = 9_999 }, "Sitemap URLs"));
        Assert.Equal(ExportFailure.SitemapNotFound, missing.Failure);
    }

    [Fact]
    public void AChecksRowsAndOneGroupsAreExportedAsItsGridReadsThem()
    {
        // The check with the most rows that groups, and its largest group.
        var counts = IssueReader.ReadCounts(_crawl.Path);
        var check = IssueCatalog.All.Where(issue => issue.Grouping != IssueGrouping.None && counts.GetValueOrDefault(issue.Id) > 0)
            .MaxBy(issue => counts[issue.Id])!;
        var group = IssueGroupReader.ReadGroups(_crawl.Path, check.Id, 0, 10)!.Groups[0];
        var key = new IssueGroupKey(group.Detail, group.RelatedUrlId);
        var query = new UrlQuery { Columns = ["url", "detail", "relatedUrl", "title", "inlinks"], Sort = [new UrlSort("inlinks", true)] };
        var shown = Shown(query.Columns, UrlGridReader.IssueRowColumns);

        foreach (var narrowed in new[] { null, key })
        {
            var expected = Rows(shown, at => Page(IssueGroupReader.ReadRows(_crawl.Path, check.Id, at, narrowed, _cache)!.Page!), query);
            Assert.Equal(narrowed is null ? counts[check.Id] : group.Pages, expected.Count);
            foreach (var format in new[] { ExportFormat.Csv, ExportFormat.Xlsx })
            {
                var request = new ExportRequest { Kind = ExportKind.IssueRows, Format = format, Query = query, CheckId = check.Id, Group = narrowed };
                var (_, rows) = Export(_crawl.Path, request, XlsxSheetName(check.Title));
                AssertSame(shown, expected, rows, format, check.Id);
            }
        }

        // With no columns asked for, a check's rows are its URL, detail, its evidence (what kind of URL a 404 is) and other URL,
        // then where to fix it where its URLs are referred to (this check's are: a 404 is linked from pages).
        var (_, plain) = Export(_crawl.Path, new ExportRequest { Kind = ExportKind.IssueRows, Format = ExportFormat.Csv, CheckId = check.Id }, check.Title);
        Assert.Equal(IssueReferences.Sources, check.References);
        Assert.Equal(["URL", "Detail", "Type", "Related URL", "Linked from (pages)", "Linked from", "Listed in sitemaps"], plain[0]);
        Assert.Equal(counts[check.Id], plain.Count - 1);
    }

    [Fact]
    public void ACheckThatDidNotRunOrIsNotOneHasNothingToExport()
    {
        var never = _crawl.Copy();
        Change(never, "DELETE FROM issue_types WHERE id = 'title-missing'");
        var skipped = Assert.Throws<ExportException>(() => Export(never,
            new ExportRequest { Kind = ExportKind.IssueRows, Format = ExportFormat.Csv, CheckId = "title-missing" }, "x"));
        Assert.Equal(ExportFailure.CheckSkipped, skipped.Failure);

        var unknown = Assert.Throws<ExportException>(() => Export(_crawl.Path,
            new ExportRequest { Kind = ExportKind.IssueRows, Format = ExportFormat.Csv, CheckId = "no-such-check" }, "x"));
        Assert.Equal(ExportFailure.CheckNotFound, unknown.Failure);
        Assert.Empty(Directory.GetFiles(_folder));
    }

    [Fact]
    public void AnOriginsBlockedUrlsAreExportedAsItsListReadsThem()
    {
        var origin = RobotsReader.ReadOrigins(_crawl.Path, limit: 1_000).Origins.MaxBy(listed => listed.Blocked)!;
        Assert.True(origin.Blocked > 0, "The synthetic site blocks nothing.");
        var blocked = RobotsReader.ReadBlocked(_crawl.Path, origin.Origin, limit: 1_000)!;
        Assert.Equal(origin.Blocked, blocked.Rows.Count);
        List<object?[]> expected =
        [
            .. blocked.Rows.Select(url => new object?[]
            {
                url.Url,
                url.Reason switch { RobotsBlockReason.Rule => "A rule", RobotsBlockReason.Unreachable => "robots.txt couldn’t be read", _ => "No matching rule" },
                url.Line is { } line ? (long)line : null,
                url.Rule,
                url.Error,
                url.LinkingPages,
                url.InSitemap is { } listed ? listed ? "Yes" : "No" : null,
                url.Sitemap?.Url,
                url.InMenu is { } menu ? menu ? "Yes" : "No" : null,
                url.Conflict != true
                    ? null
                    : (url.InSitemap == true, url.InMenu == true) switch
                    {
                        (true, true) => "In your sitemap and menu but blocked",
                        (true, false) => "In your sitemap but blocked",
                        _ => "In your menu but blocked"
                    }
            })
        ];
        Assert.Contains(expected, row => row[9] is "In your sitemap but blocked" && row[7] is string);
        Assert.Contains(expected, row => (long?)row[5] > 0);

        foreach (var format in new[] { ExportFormat.Csv, ExportFormat.Xlsx })
        {
            var (result, rows) = Export(_crawl.Path, new ExportRequest { Kind = ExportKind.RobotsBlocked, Format = format, Origin = origin.Origin }, "Blocked URLs");
            Assert.Equal(["Blocked URL", "Blocked by", "Line", "Rule", "Error", "Linked from (pages)", "In a sitemap", "Sitemap", "In a menu", "Conflict"], rows[0]);
            AssertSameCells(expected, rows, format);
            Assert.Equal(origin.Blocked, result.Rows);
        }

        // The conflicts alone, as the list narrows to them; a crawl changed since its analysis has no such columns' values.
        var (_, conflicts) = Export(_crawl.Path, new ExportRequest { Kind = ExportKind.RobotsBlocked, Format = ExportFormat.Csv, Origin = origin.Origin, Conflict = true }, "c");
        Assert.Equal(expected.Where(row => row[9] is not null).Select(row => (string?)row[0]), conflicts.Skip(1).Select(row => (string?)row[0]));
        var (_, changed) = Export(_crawl.Changed, new ExportRequest { Kind = ExportKind.RobotsBlocked, Format = ExportFormat.Csv, Origin = origin.Origin }, "d");
        Assert.Equal(expected.Count, changed.Count - 1);
        Assert.All(changed.Skip(1), row => Assert.All(row.Skip(5), cell => Assert.True(cell is null or "")));

        // Narrowed to one line's, as the list narrows.
        var first = blocked.Rows.First(url => url.Line is not null);
        var narrowed = RobotsReader.ReadBlocked(_crawl.Path, origin.Origin, first.Line, limit: 1_000)!;
        var (_, lines) = Export(_crawl.Path, new ExportRequest { Kind = ExportKind.RobotsBlocked, Format = ExportFormat.Csv, Origin = origin.Origin, Line = first.Line }, "x");
        Assert.Equal(narrowed.Matching, lines.Count - 1);
        Assert.Equal(narrowed.Rows.Select(url => url.Url), lines.Skip(1).Select(row => (string)row[0]!));

        var missing = Assert.Throws<ExportException>(() => Export(_crawl.Path,
            new ExportRequest { Kind = ExportKind.RobotsBlocked, Format = ExportFormat.Csv, Origin = "https://nowhere.example" }, "x"));
        Assert.Equal(ExportFailure.OriginNotFound, missing.Failure);
    }

    /// <summary>
    /// A crawl continued since its analysis has no URL summary: the columns that need one are in the file with no values,
    /// and the result names them, as the grid lists them unavailable; a sort on one is refused, as the grid refuses it.
    /// </summary>
    [Fact]
    public void ACrawlWithoutItsSummaryIsExportedAsItsGridReadsIt()
    {
        var query = new UrlQuery { Columns = ["url", "statusCode", "inlinks", "title", "issues"], Sort = [new UrlSort("url")] };
        var page = UrlGridReader.Query(_crawl.Changed, query with { Limit = 1 }, _cache);
        Assert.Equal(["inlinks", "issues"], page.Unavailable);
        var shown = Shown(["url", "statusCode", "title"], UrlGridReader.Columns);
        var expected = Rows(shown, at => Page(UrlGridReader.Query(_crawl.Changed, at, _cache)), query);

        var (result, rows) = Export(_crawl.Changed, new ExportRequest { Kind = ExportKind.Urls, Format = ExportFormat.Csv, Query = query }, "URLs");
        Assert.Equal(["inlinks", "issues"], result.UnavailableColumns);
        Assert.Equal(["URL", "Status", "Inlinks", "Title", "Issues"], rows[0]);
        Assert.All(rows.Skip(1), row => Assert.True(row[2] is null && row[4] is null));
        AssertSameCells([.. expected], [rows[0], .. rows.Skip(1).Select(row => new[] { row[0], row[1], row[3] })], ExportFormat.Csv, shown);

        Assert.Throws<NeedsAnalysisException>(() => Export(_crawl.Changed,
            new ExportRequest { Kind = ExportKind.Urls, Format = ExportFormat.Csv, Query = query with { Sort = [new UrlSort("inlinks")] } }, "URLs"));

        // The refused export left nothing, and the file from before is as it was.
        Assert.Equal(["out.csv"], Directory.GetFiles(_folder).Select(Path.GetFileName));
        Assert.Equal(rows.Count, CsvFile.Read(Path.Combine(_folder, "out.csv")).Count);
    }

    /// <summary>
    /// A site's text in the crawl file: titles and URLs that are formulas, hold quotes, commas and line breaks, control
    /// characters and emoji. Each comes out of both files as the grid has it, the CSV's guarded.
    /// </summary>
    [Fact]
    public void HostileTitlesAndUrlsComeThroughBothFiles()
    {
        var hostile = _crawl.Copy();
        var texts = TableWriterTests.HOSTILE;
        for (var index = 0; index < texts.Length; index++)
        {
            Change(hostile, "UPDATE responses SET title = $title WHERE url_id = (SELECT url_id FROM responses WHERE title IS NOT NULL ORDER BY url_id LIMIT 1 OFFSET $offset)",
                ("$title", texts[index]), ("$offset", index));
            Change(hostile, "UPDATE urls SET url = $url WHERE id = (SELECT MAX(id) FROM urls) - $offset",
                ("$url", $"https://other.example/{index}?q={texts[index]}"), ("$offset", index));
        }

        var query = new UrlQuery { Columns = ["title", "url", "depth"] };
        var shown = Shown(query.Columns, UrlGridReader.Columns);
        var expected = Rows(shown, at => Page(UrlGridReader.Query(hostile, at)), query);
        var stored = texts.Select(Encodable).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(texts.Length, expected.Count(row => row[0] is string title && stored.Contains(title)));
        foreach (var format in new[] { ExportFormat.Csv, ExportFormat.Xlsx })
        {
            var (_, rows) = Export(hostile, new ExportRequest { Kind = ExportKind.Urls, Format = format, Query = query }, "URLs");
            AssertSame(shown, expected, rows, format);
        }

        // As a spreadsheet reads the CSV: no title that starts as a formula does, and none is lost.
        var csv = CsvFile.Read(Path.Combine(_folder, "out.csv"));
        Assert.DoesNotContain(csv.Skip(1), row => row[0].Length > 0 && "=+-@\t\r".Contains(row[0][0], StringComparison.Ordinal));
        Assert.Contains(csv, row => row[0] == "'=cmd|' /C calc'!A0");
    }

    /// <summary>
    /// A site's URL longer than a spreadsheet's cell (nothing caps a URL's length): cut in both files, to what each
    /// reads as one cell, and the result says how many cells were cut. The CSV is still a row a URL.
    /// </summary>
    [Fact]
    public void AUrlLongerThanACellIsCutInBothFilesAndTheResultSaysSo()
    {
        var copy = _crawl.Copy();
        var url = "https://other.example/?q=" + new string('L', 40_000) + "=1+1";
        Change(copy, "UPDATE urls SET url = $url WHERE id = (SELECT MAX(id) FROM urls)", ("$url", url));
        var request = new ExportRequest { Kind = ExportKind.Urls, Format = ExportFormat.Csv, Query = new UrlQuery { Columns = ["url", "id"] } };
        var matching = UrlGridReader.Query(copy, new UrlQuery { Limit = 1 }).Matching;

        var (csv, csvRows) = Export(copy, request, "URLs");
        Assert.Equal(1, csv.TruncatedCells);
        Assert.Equal(matching + 1, csvRows.Count);
        Assert.All(csvRows, row => Assert.Equal(2, row.Length));
        Assert.Equal(url[..32_758], csvRows[^1][0]);

        var (xlsx, xlsxRows) = Export(copy, request with { Format = ExportFormat.Xlsx }, "URLs");
        Assert.Equal(1, xlsx.TruncatedCells);
        Assert.Equal(url[..32_767], xlsxRows[^1][0]);
    }

    [Fact]
    public void AnExportSaysHowFarItHasGotAndEndsOnItsCount()
    {
        List<ExportProgress> heard = [];
        var settings = new ExportSettings { Cache = _cache, Progress = new Heard(heard) };
        var path = Path.Combine(_folder, "progress.csv");
        var result = CrawlExporter.Export(_crawl.Path, new ExportRequest { Kind = ExportKind.Links, Format = ExportFormat.Csv }, path, settings);

        Assert.True(heard.Count > 10, $"Heard {heard.Count} times.");
        Assert.Equal(new ExportProgress(0, result.Rows), heard[0]);
        Assert.Equal(new ExportProgress(result.Rows, result.Rows), heard[^1]);
        Assert.All(heard.Zip(heard.Skip(1)), pair => Assert.True(pair.First.RowsWritten <= pair.Second.RowsWritten));
        Assert.Equal(new FileInfo(path).Length, result.Bytes);
        Assert.Equal(path, result.Path);
    }

    [Theory]
    [InlineData(ExportKind.Urls, ExportFormat.Html, "format")]
    [InlineData(ExportKind.Workbook, ExportFormat.Csv, "format")]
    [InlineData(ExportKind.Report, ExportFormat.Xlsx, "format")]
    [InlineData(ExportKind.SitemapUrls, ExportFormat.Csv, "sitemapId")]
    [InlineData(ExportKind.RobotsBlocked, ExportFormat.Csv, "origin")]
    [InlineData(ExportKind.IssueRows, ExportFormat.Csv, "checkId")]
    [InlineData(ExportKind.Check, ExportFormat.Xlsx, "checkId")]
    public void ARequestThatLacksWhatItsKindNeedsIsRefusedByField(ExportKind kind, ExportFormat format, string field)
    {
        var failure = Assert.Throws<ExportException>(() => CrawlExporter.Check(new ExportRequest { Kind = kind, Format = format }));
        Assert.Equal(ExportFailure.InvalidRequest, failure.Failure);
        Assert.Equal(field, failure.Field);
    }

    [Fact]
    public void AQueryTheGridWouldRefuseIsRefusedBeforeAnythingIsWritten()
    {
        var unknown = Assert.Throws<UrlQueryException>(() => CrawlExporter.Check(new ExportRequest
        {
            Kind = ExportKind.Urls, Format = ExportFormat.Csv, Query = new UrlQuery { Columns = ["url", "nope"], Sort = [new UrlSort("title")] }
        }));
        Assert.Equal(["columns[1]", "sort[0].column"], unknown.Errors.Keys.Order());

        // A check's own columns are its rows' alone.
        Assert.Throws<UrlQueryException>(() => CrawlExporter.Check(new ExportRequest { Kind = ExportKind.Urls, Format = ExportFormat.Csv, Query = new UrlQuery { Columns = ["detail"] } }));
        Assert.Throws<UrlQueryException>(() => CrawlExporter.Check(new ExportRequest
        {
            Kind = ExportKind.Links, Format = ExportFormat.Csv, Query = new UrlQuery { Sort = [new UrlSort("from.url")] }
        }));
    }

    // ---- Reading the grid, and the files.

    private sealed class Heard(List<ExportProgress> heard) : IProgress<ExportProgress>
    {
        public void Report(ExportProgress value) => heard.Add(value);
    }

    /// <summary>
    /// Every column these tests export, under the title a person reads, and every value in words, written out here: the
    /// comparisons with the grid above ask the engine for its words, so they would follow a word that changed by mistake.
    /// </summary>
    [Fact]
    public void EveryColumnsTitleAndEveryValuesWordIsWrittenOutHere()
    {
        string[] urlTitles =
        [
            "URL", "Crawl order", "Status", "Status class", "Outcome", "Indexable", "Indexability", "Type", "Found by", "Depth", "On this site", "Crawled", "Title",
            "Response time (ms)", "Size (bytes)", "Inlinks", "Issues", "In a sitemap", "Canonical", "Word count"
        ];
        string[] linkTitles =
        [
            "Crawl order", "Source URL", "Target URL", "Kind", "Anchor or alt text", "Position", "Followed", "Nofollow", "Links to its own page", "Target status",
            "Target indexability", "Source depth", "Target on this site"
        ];
        Dictionary<string, string> indexability = new()
        {
            ["none"] = "Indexable", ["noindex"] = "Noindex", ["canonicalised"] = "Canonicalised", ["redirected"] = "Redirected", ["clientError"] = "Client error",
            ["serverError"] = "Server error", ["blockedByRobots"] = "Blocked by robots.txt", ["blocked"] = "Blocked", ["unreachable"] = "Unreachable"
        };
        Dictionary<string, Dictionary<string, string>> urlWords = new()
        {
            ["statusClass"] = new() { ["2xx"] = "2xx", ["3xx"] = "3xx", ["4xx"] = "4xx", ["5xx"] = "5xx", ["other"] = "Another code", ["noResponse"] = "No response" },
            ["outcome"] = new()
            {
                ["success"] = "Answered", ["redirect"] = "Redirect", ["clientError"] = "Client error", ["serverError"] = "Server error", ["blocked"] = "Blocked",
                ["robotsDisallowed"] = "Blocked by robots.txt", ["timeout"] = "Timed out", ["networkError"] = "Network error", ["processingError"] = "Couldn’t be read",
                ["notChecked"] = "Not checked"
            },
            ["indexability"] = indexability,
            ["type"] = new()
            {
                ["page"] = "Page", ["image"] = "Image", ["script"] = "Script", ["stylesheet"] = "Stylesheet", ["font"] = "Font", ["media"] = "Media file", ["pdf"] = "PDF",
                ["other"] = "File"
            },
            ["source"] = new() { ["seed"] = "The start URL", ["link"] = "A link", ["sitemap"] = "A sitemap", ["siteVersion"] = "A probe of the site’s other versions" }
        };
        Dictionary<string, Dictionary<string, string>> linkWords = new()
        {
            ["kind"] = new()
            {
                ["hyperlink"] = "Hyperlink", ["image"] = "Image", ["script"] = "Script", ["stylesheet"] = "Stylesheet", ["iframe"] = "Frame", ["media"] = "Media",
                ["icon"] = "Icon", ["canonical"] = "Canonical", ["hreflang"] = "Hreflang", ["pagination"] = "Pagination", ["metaRefresh"] = "Meta refresh",
                ["redirect"] = "Redirect"
            },
            ["position"] = new()
            {
                ["content"] = "Content", ["navigation"] = "Navigation", ["header"] = "Header", ["footer"] = "Footer", ["sidebar"] = "Sidebar", ["head"] = "Head"
            },
            ["to.indexability"] = indexability
        };

        Written(ExportKind.Urls, "URLs", URL_COLUMNS, urlTitles, urlWords, at => Page(UrlGridReader.Query(_crawl.Path, at, _cache)), UrlGridReader.Columns);
        Written(ExportKind.Links, "Links", LINK_COLUMNS, linkTitles, linkWords, at => Page(LinkGridReader.Query(_crawl.Path, at, _cache)), LinkGridReader.Columns);

        void Written(ExportKind kind, string sheet, string[] columns, string[] titles, Dictionary<string, Dictionary<string, string>> words,
            Func<UrlQuery, (IReadOnlyList<string> Columns, List<IReadOnlyList<object?>> Rows, long Matching)> read, IReadOnlyList<UrlColumnInfo> known)
        {
            var query = new UrlQuery { Columns = columns };
            var grid = Rows(Shown(columns, known), read, query);
            foreach (var format in new[] { ExportFormat.Csv, ExportFormat.Xlsx })
            {
                var (_, file) = Export(_crawl.Path, new ExportRequest { Kind = kind, Format = format, Query = query }, sheet);
                Assert.Equal(titles, file[0]);
                Assert.Equal(grid.Count, file.Count - 1);
                for (var column = 0; column < columns.Length; column++)
                {
                    // What the grid's reader gives for the column, and what the file says for it.
                    var pairs = grid.Select((row, index) => (Value: row[column], Said: column < file[index + 1].Length ? file[index + 1][column] : null)).ToList();
                    if (words.TryGetValue(columns[column], out var said))
                    {
                        Assert.All(pairs, pair => Assert.Equal(pair.Value is null ? null : said[(string)pair.Value], pair.Said));
                        Assert.True(pairs.Select(pair => pair.Value).Distinct().Count() > 1, $"{columns[column]} has one value in this crawl.");
                    }
                    else if (pairs.Any(pair => pair.Value is bool))
                    {
                        Assert.All(pairs, pair => Assert.Equal(pair.Value switch { true => "Yes", false => "No", _ => null }, pair.Said));
                    }
                }
            }
        }
    }

    /// <summary>
    /// More rows than a workbook holds are refused before anything is written, and nothing is left: said of a grid by its
    /// count, and of an origin's blocked URLs by theirs. The same rows go to CSV, which has no limit.
    /// </summary>
    [Fact]
    public void MoreRowsThanAWorkbookHoldsAreRefusedBeforeAnythingIsWritten()
    {
        var urls = UrlGridReader.Query(_crawl.Path, new UrlQuery { Limit = 1 }).Matching;
        var links = LinkGridReader.Query(_crawl.Path, new UrlQuery { Limit = 1 }).Matching;
        var origin = RobotsReader.ReadOrigins(_crawl.Path).Origins.First(candidate => candidate.Blocked > 1);
        var blocked = new ExportRequest { Kind = ExportKind.RobotsBlocked, Format = ExportFormat.Xlsx, Origin = origin.Origin };
        (ExportRequest Request, long Rows)[] exports =
        [
            (new ExportRequest { Kind = ExportKind.Urls, Format = ExportFormat.Xlsx }, urls),
            (new ExportRequest { Kind = ExportKind.Links, Format = ExportFormat.Xlsx }, links),
            (blocked, origin.Blocked)
        ];
        var path = Path.Combine(_folder, "large.xlsx");
        foreach (var (request, rows) in exports)
        {
            // One row more than it may hold: refused, by a sentence that says how many and what to do, and no file or part of one.
            var failure = Assert.Throws<ExportException>(() => CrawlExporter.Export(_crawl.Path, request, path, new ExportSettings { Cache = _cache, MaxXlsxRows = rows - 1 }));
            Assert.Equal(ExportFailure.TooLarge, failure.Failure);
            Assert.Contains("Export", failure.Message, StringComparison.Ordinal);
            Assert.Contains("CSV, which has no limit", failure.Message, StringComparison.Ordinal);
            Assert.Empty(Directory.GetFiles(_folder));

            // Exactly what it may hold is written, and CSV takes the rows whatever a workbook holds.
            Assert.Equal(rows, CrawlExporter.Export(_crawl.Path, request, path, new ExportSettings { Cache = _cache, MaxXlsxRows = rows }).Rows);
            var csv = Path.Combine(_folder, "large.csv");
            Assert.Equal(rows, CrawlExporter.Export(_crawl.Path, request with { Format = ExportFormat.Csv }, csv, new ExportSettings { Cache = _cache, MaxXlsxRows = rows - 1 }).Rows);
            File.Delete(path);
            File.Delete(csv);
        }
    }

    private static string XlsxSheetName(string title) => ShoutingIguana.Engine.Export.Xlsx.XlsxWriter.SheetName(title, []);

    private static List<UrlColumnInfo> Shown(IReadOnlyList<string> ids, IReadOnlyList<UrlColumnInfo> known) => [.. ids.Select(id => known.Single(column => column.Id == id))];

    private static (IReadOnlyList<string> Columns, List<IReadOnlyList<object?>> Rows, long Matching) Page(UrlPage page) =>
        (page.Columns, [.. page.Rows.Select(row => row.Values)], page.Matching);

    private static (IReadOnlyList<string> Columns, List<IReadOnlyList<object?>> Rows, long Matching) Page(LinkGridPage page) =>
        (page.Columns, [.. page.Rows.Select(row => row.Values)], page.Matching);

    /// <summary>Every row the grid shows for a query, read by offset a page at a time, each cell as the grid's reader gives it for the columns shown.</summary>
    private static List<object?[]> Rows(IReadOnlyList<UrlColumnInfo> shown, Func<UrlQuery, (IReadOnlyList<string> Columns, List<IReadOnlyList<object?>> Rows, long Matching)> read,
        UrlQuery query)
    {
        const int LIMIT = 1_000;
        List<object?[]> rows = [];
        for (var offset = 0; ; offset += LIMIT)
        {
            var page = read(query with { Offset = offset, Limit = LIMIT });
            var places = shown.Select(column => page.Columns.ToList().IndexOf(column.Id)).ToList();
            Assert.DoesNotContain(-1, places);
            rows.AddRange(page.Rows.Select(row => places.Select(place => row[place]).ToArray()));
            if (rows.Count >= page.Matching || page.Rows.Count == 0)
            {
                Assert.Equal(page.Matching, rows.Count);
                return rows;
            }
        }
    }

    /// <summary>Exports to a file in the test's folder and reads it back: the header, then the rows, a number as a double and text as text.</summary>
    private (ExportResult Result, List<object?[]> Rows) Export(string crawl, ExportRequest request, string sheet)
    {
        var path = Path.Combine(_folder, request.Format == ExportFormat.Csv ? "out.csv" : "out.xlsx");
        var result = CrawlExporter.Export(crawl, request, path, new ExportSettings { Cache = _cache });
        Assert.Equal([Path.GetFileName(path)], Directory.GetFiles(_folder).Select(Path.GetFileName).Where(name => name == Path.GetFileName(path)));
        Assert.DoesNotContain(Directory.GetFiles(_folder), file => file.EndsWith(".tmp", StringComparison.Ordinal) || file.Contains(".tmp.", StringComparison.Ordinal));
        if (request.Format == ExportFormat.Csv)
        {
            Assert.Equal(0, result.Sheets);
            return (result, [.. CsvFile.Read(path).Select(row => row.Select(field => field.Length == 0 ? null : (object)field).ToArray())]);
        }

        var file = Assert.Single(XlsxFile.Read(path).Sheets);
        Assert.Equal(1, result.Sheets);
        Assert.Equal(sheet.Length > 31 ? XlsxSheetName(sheet) : sheet, file.Name);
        Assert.Equal(1, file.FrozenRows);
        Assert.All(file.Rows[0], cell => Assert.Equal(1u, cell.Style));
        return (result, file.Values);
    }

    /// <summary>The file's rows are the grid's, in its order, under the grid's titles.</summary>
    /// <param name="checkId">A check's rows: after the grid's columns, where to fix each URL, for a check whose URLs are referred to.</param>
    private static void AssertSame(IReadOnlyList<UrlColumnInfo> shown, List<object?[]> grid, List<object?[]> file, ExportFormat format, string? checkId = null)
    {
        string[] where = (checkId is null ? null : IssueCatalog.Find(checkId)?.References) switch
        {
            IssueReferences.Sources => ["Linked from (pages)", "Linked from", "Listed in sitemaps"],
            IssueReferences.Targets => ["Links to (addresses)", "Links to"],
            _ => []
        };
        Assert.Equal([.. shown.Select(GridExport.Title), .. where], file[0].Select(title => (string)title!));
        AssertSameCells(grid, file, format, shown);
    }

    private static void AssertSameCells(List<object?[]> grid, List<object?[]> file, ExportFormat format, IReadOnlyList<UrlColumnInfo>? shown = null)
    {
        Assert.Equal(grid.Count, file.Count - 1);
        for (var row = 0; row < grid.Count; row++)
        {
            for (var column = 0; column < grid[row].Length; column++)
            {
                var value = shown is null ? grid[row][column] : GridExport.Cell(shown[column], grid[row][column]);
                var written = column < file[row + 1].Length ? file[row + 1][column] : null;
                object? expected = (value, format) switch
                {
                    (null, _) => null,
                    (string text, ExportFormat.Csv) => text.Length == 0 ? null : Guarded(Encodable(text)),
                    (string text, _) => text.Length == 0 ? null : Encodable(text),
                    (_, ExportFormat.Csv) => Convert.ToString(value, CultureInfo.InvariantCulture),
                    _ => Convert.ToDouble(value, CultureInfo.InvariantCulture)
                };
                Assert.True(Equals(expected, written), $"Row {row + 1}, column {column + 1}: the grid has '{expected}', the file '{written}'.");
            }
        }
    }

    private static string Guarded(string text) => "=+-@\t\r".Contains(text[0], StringComparison.Ordinal) ? "'" + text : text;

    /// <summary>Text as any file holds it: a surrogate with no partner can't be encoded, and is written as U+FFFD.</summary>
    private static string Encodable(string text) => System.Text.Encoding.UTF8.GetString(System.Text.Encoding.UTF8.GetBytes(text));
}
