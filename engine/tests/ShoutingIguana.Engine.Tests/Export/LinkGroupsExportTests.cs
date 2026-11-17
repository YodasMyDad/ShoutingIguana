using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Export;
using ShoutingIguana.Engine.Export.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Tests.Support;
using static ShoutingIguana.Engine.Tests.Support.GridQueries;

namespace ShoutingIguana.Engine.Tests.Export;

/// <summary>
/// The links grouped by target as a file (step 3b): one row a target, in the grouped view's order, with what its row
/// says, read back from the file and held to the grouped view's own read; as CSV and as a workbook. And whether a crawl
/// measured images' pixel sizes, which the grouped view, the links grid and a page's images say.
/// </summary>
[Collection(GridCrawlCollection.NAME)]
public sealed class LinkGroupsExportTests(GridCrawl crawl) : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("si-export-groups-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    public static TheoryData<string, UrlFilter[], string?, UrlSort[]> Views() => new()
    {
        { "every link, the most linked from pages first", [], null, [] },
        { "the broken links", [Names("to.statusClass", FilterOperator.In, "4xx", "5xx")], null, [] },
        { "the images, the largest first", [Names("kind", FilterOperator.In, "image")], null, [new UrlSort("to.sizeBytes", true)] },
        { "a search, by target URL", [], "about", [new UrlSort("to.url")] }
    };

    [Theory]
    [MemberData(nameof(Views))]
    public void TheGroupedViewIsExportedOneRowATargetAsItReadsThem(string name, UrlFilter[] filters, string? search, UrlSort[] sort)
    {
        // The grouped view's own read, a page at a time.
        List<LinkGroupRow> groups = [];
        var query = new LinkGroupQuery { Filters = filters, Search = search, Sort = sort.FirstOrDefault(), Limit = 37 };
        for (var offset = 0; ; offset += 37)
        {
            var page = LinkGridReader.Groups(crawl.Path, query with { Offset = offset });
            groups.AddRange(page.Rows);
            if (page.Rows.Count < 37)
            {
                break;
            }
        }

        Assert.True(groups.Count > 0, $"{name}: no targets.");
        List<object?[]> expected =
        [
            .. groups.Select(row => new object?[]
            {
                row.Url, row.Target.StatusCode, row.Target.Outcome is { } outcome ? ColumnWords.OutcomeTitle(outcome) : null, row.Pages, row.Links, row.Text,
                row.Text is null ? 0 : Math.Max(0, row.Texts - 1), string.Join(", ", row.Positions.Select(ColumnWords.PositionTitle)), row.LikelyTemplate ? "Yes" : "No",
                row.Target.ContentType, row.Target.SizeBytes, row.Target.PixelWidth, row.Target.PixelHeight, row.Target.Final?.Url
            })
        ];
        foreach (var format in new[] { ExportFormat.Csv, ExportFormat.Xlsx })
        {
            var request = new ExportRequest { Kind = ExportKind.LinkGroups, Format = format, Query = new UrlQuery { Filters = filters, Search = search, Sort = sort } };
            var (result, rows) = Export(request);
            Assert.Equal(LinkGroupsExport.TITLES, rows[0].Select(title => (string)title!));
            Assert.Equal(groups.Count, result.Rows);
            AssertSameCells(expected, rows, format);
        }
    }

    [Fact]
    public void AGroupedExportThatCantBeReadIsRefusedByField()
    {
        void Refused(UrlQuery query, string field)
        {
            var refused = Assert.Throws<UrlQueryException>(() => CrawlExporter.Check(new ExportRequest { Kind = ExportKind.LinkGroups, Format = ExportFormat.Csv, Query = query }));
            Assert.Contains(field, refused.Errors.Keys);
        }

        Refused(new UrlQuery { Sort = [new UrlSort("kind")] }, "sort.column");
        Refused(new UrlQuery { Sort = [new UrlSort("pages", true), new UrlSort("links")] }, "sort");
        Refused(new UrlQuery { Filters = [Text("nothing", FilterOperator.Contains, "x")] }, "filters[0].column");
        Assert.True(ExportFields.Takes(ExportKind.LinkGroups, "filters"));
        Assert.True(ExportFields.Takes(ExportKind.LinkGroups, "hiddenChecks"));
        Assert.False(ExportFields.Takes(ExportKind.LinkGroups, "columns"));
        Assert.Equal([ExportFormat.Csv, ExportFormat.Xlsx], CrawlExporter.Formats(ExportKind.LinkGroups));
    }

    /// <summary>
    /// A crawl made before images' pixel sizes were read has no columns for them: the links grid, the grouped view and a
    /// page's images say it wasn't measured, where a crawl that has them says it was.
    /// </summary>
    [Fact]
    public void TheReadsSayWhetherTheCrawlMeasuredImagesPixelSizes()
    {
        var page = int.Parse(TestCrawls.Rows(crawl.Path, $"SELECT printf('%09d', from_id) FROM links WHERE kind = {(int)ShoutingIguana.Engine.Parsing.Models.LinkKind.Image} LIMIT 1")[0], CultureInfo.InvariantCulture);
        Assert.True(LinkGridReader.Query(crawl.Path, new UrlQuery { Limit = 1 }).PixelSizes);
        Assert.True(LinkGridReader.Groups(crawl.Path, new LinkGroupQuery { Limit = 1 }).PixelSizes);
        Assert.True(PageImagesReader.Read(crawl.Path, page)!.PixelSizes);

        var earlier = crawl.Copy();
        var connection = CrawlDatabase.OpenWriter(earlier, allowNew: false);
        try
        {
            CrawlDatabase.Execute(connection, "ALTER TABLE responses DROP COLUMN pixel_width; ALTER TABLE responses DROP COLUMN pixel_height;");
        }
        finally
        {
            CrawlDatabase.CloseWriter(connection, NullLogger.Instance);
        }

        Assert.False(LinkGridReader.Query(earlier, new UrlQuery { Columns = ["to.url", "to.pixelWidth"], Limit = 1 }).PixelSizes);
        Assert.False(LinkGridReader.Groups(earlier, new LinkGroupQuery { Limit = 1 }).PixelSizes);
        Assert.False(PageImagesReader.Read(earlier, page)!.PixelSizes);
    }

    private (ExportResult Result, List<object?[]> Rows) Export(ExportRequest request)
    {
        var target = Path.Combine(_folder, request.Format == ExportFormat.Csv ? "out.csv" : "out.xlsx");
        var result = CrawlExporter.Export(crawl.Path, request, target, new ExportSettings { Cache = new ReadCache() });
        if (request.Format == ExportFormat.Csv)
        {
            return (result, [.. CsvFile.Read(target).Select(row => row.Select(field => field.Length == 0 ? null : (object)field).ToArray())]);
        }

        XlsxFile.AssertValid(target);
        var file = Assert.Single(XlsxFile.Read(target).Sheets);
        Assert.Equal("Links by target", file.Name);
        Assert.Equal(1, file.FrozenRows);
        return (result, file.Values);
    }

    private static void AssertSameCells(List<object?[]> expected, List<object?[]> file, ExportFormat format)
    {
        Assert.Equal(expected.Count, file.Count - 1);
        for (var row = 0; row < expected.Count; row++)
        {
            for (var column = 0; column < expected[row].Length; column++)
            {
                var value = expected[row][column];
                var written = column < file[row + 1].Length ? file[row + 1][column] : null;
                object? wanted = (value, format) switch
                {
                    (null, _) => null,
                    (string text, _) => text.Length == 0 ? null : text,
                    (_, ExportFormat.Csv) => Convert.ToString(value, CultureInfo.InvariantCulture),
                    _ => Convert.ToDouble(value, CultureInfo.InvariantCulture)
                };
                Assert.True(Equals(wanted, written), $"Row {row + 1}, column {column + 1}: the view has '{wanted}', the file '{written}'.");
            }
        }
    }
}
