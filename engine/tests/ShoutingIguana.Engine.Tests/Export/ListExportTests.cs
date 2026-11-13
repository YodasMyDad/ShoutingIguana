using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Export;
using ShoutingIguana.Engine.Export.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Tests.Support;

namespace ShoutingIguana.Engine.Tests.Export;

/// <summary>
/// The two lists that aren't grids, exported as their screens list them: the sitemaps (<c>GET /sitemaps</c>'s read) and
/// the origins' robots.txt (<c>POST /robots</c>'s, searched and filtered), each read back from the file and held to the
/// screen's own read row for row, under the screen's column titles and in its cells' words.
/// </summary>
[Collection(GridCrawlCollection.NAME)]
public sealed class ListExportTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("si-export-list-").FullName;
    private readonly ReadCache _cache = new();

    /// <summary>A clock in a time zone of its own, so the "Read" dates are known to be written in the reader's time.</summary>
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero));

    private static readonly TimeZoneInfo ZONE = TimeZoneInfo.CreateCustomTimeZone("si-test", TimeSpan.FromHours(5.5), "si-test", "si-test");

    private readonly GridCrawl _crawl;

    public ListExportTests(GridCrawl crawl)
    {
        _crawl = crawl;
        _clock.SetLocalTimeZone(ZONE);
    }

    public void Dispose()
    {
        Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void TheSitemapsAreExportedAsTheirListReadsThem()
    {
        var list = SitemapListReader.Read(_crawl.Path);
        Assert.True(list.Sitemaps.Count > 1, $"{list.Sitemaps.Count} sitemaps.");
        var urls = list.Sitemaps.ToDictionary(sitemap => sitemap.Id, sitemap => sitemap.Url);
        List<object?[]> expected =
        [
            .. list.Sitemaps.Select(sitemap =>
            {
                var subtree = SitemapListWords.SubtreeOf(list.Sitemaps, sitemap.Id, list.Discovery);
                var known = SitemapListWords.CountsKnown(sitemap, subtree);
                var counts = sitemap.Urls;
                object?[] own =
                [
                    sitemap.Url, sitemap.IsIndex ? "Yes" : "No", sitemap.ParentId is { } parent ? urls[parent] : null, SitemapListWords.FoundVia(sitemap.FoundVia),
                    SitemapListWords.State(sitemap, crawling: false, subtree), sitemap.Error
                ];
                object?[] counted = known
                    ? [counts.Listed, counts.Fetched - counts.RobotsDisallowed, counts.RobotsDisallowed, counts.Listed - counts.Fetched, counts.Indexable, counts.NonIndexable, counts.RedirectingOrBroken]
                    : new object?[7];
                return (object?[])[.. own, .. counted];
            })
        ];

        foreach (var format in new[] { ExportFormat.Csv, ExportFormat.Xlsx })
        {
            var (result, rows) = Export(_crawl.Path, new ExportRequest { Kind = ExportKind.Sitemaps, Format = format }, "Sitemaps");
            Assert.Equal(
                ["Sitemap", "Index", "Listed by", "Found via", "Status", "Error", "Listed", "Crawled", "Blocked by robots.txt", "Not crawled", "Indexable", "Non-indexable", "Redirecting or broken"],
                rows[0]);
            AssertSameCells(expected, rows, format);
            Assert.Equal(list.Sitemaps.Count, result.Rows);
            Assert.False(result.CrawlWasRunning);
        }
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(null, true)]
    [InlineData(null, false)]
    [InlineData("HTTP", null)]
    [InlineData("nothing.matches.this", null)]
    public void TheOriginsAreExportedAsTheirListReadsThemSearchedAndFiltered(string? search, bool? blocked)
    {
        // The list's own read, a page at a time, as the screen reads it.
        List<RobotsOrigin> listed = [];
        RobotsOriginPage page;
        do
        {
            page = RobotsReader.ReadOrigins(_crawl.Path, new RobotsOriginQuery { Search = search, Blocked = blocked, Offset = listed.Count, Limit = 2 }, _cache);
            listed.AddRange(page.Origins);
        }
        while (page.Origins.Count > 0);

        Assert.Equal(page.Matching, listed.Count);
        if (search is null)
        {
            Assert.True(blocked is not null || listed.Count > 2, $"{listed.Count} origins.");
            Assert.True(listed.Count > 0 || blocked == false, $"{listed.Count} origins.");
        }

        List<object?[]> expected =
        [
            .. listed.Select(origin => new object?[]
            {
                origin.Origin, origin.IsInternal ? "Yes" : "No", RobotsOriginWords.State(origin, page.Respected), origin.StatusCode is { } status ? (long)status : null, origin.Error,
                origin.Blocked, origin.FetchedAt is { } read ? read.ToOffset(TimeSpan.FromHours(5.5)).DateTime : null, origin.Bytes
            })
        ];

        foreach (var format in new[] { ExportFormat.Csv, ExportFormat.Xlsx })
        {
            var (result, rows) = Export(_crawl.Path, new ExportRequest { Kind = ExportKind.RobotsOrigins, Format = format, Query = new UrlQuery { Search = search }, Blocked = blocked },
                "robots.txt");
            Assert.Equal(["Origin", "This site", "robots.txt", "Status", "Error", "Blocked URLs", "Read", "Size (bytes)"], rows[0]);
            AssertSameCells(expected, rows, format);
            Assert.Equal(listed.Count, result.Rows);
        }
    }

    [Fact]
    public void TheOriginsSearchIsHeldToWhatTheListTakes()
    {
        var refused = Assert.Throws<UrlQueryException>(() => CrawlExporter.Check(new ExportRequest
        {
            Kind = ExportKind.RobotsOrigins, Format = ExportFormat.Csv, Query = new UrlQuery { Search = new string('a', 1_001) }
        }));
        Assert.Equal(["search"], refused.Errors.Keys);
    }

    /// <summary>
    /// A running crawl's list is of one moment of it, and says so; a sitemap it hasn't read is on its way ("Not read yet"),
    /// where a crawl that stopped never read it.
    /// </summary>
    [Fact]
    public void ARunningCrawlsListsAreOfOneMomentOfItAndSaySo()
    {
        var path = _crawl.Copy();
        var pending = SitemapListReader.Read(path).Sitemaps.First(sitemap => sitemap is { IsIndex: false, ParentId: not null });
        using (var connection = Open(path))
        {
            Execute(connection, $"UPDATE sitemaps SET status_code = NULL, error = NULL WHERE id = {pending.Id}");
        }

        string StateOf(List<object?[]> rows) => (string)rows.Single(row => (string?)row[0] == pending.Url)[4]!;
        var (stopped, stoppedRows) = Export(path, new ExportRequest { Kind = ExportKind.Sitemaps, Format = ExportFormat.Csv }, "Sitemaps");
        Assert.False(stopped.CrawlWasRunning);
        Assert.Equal("Never read", StateOf(stoppedRows));

        using var writer = Open(path);
        Execute(writer, "PRAGMA journal_mode = WAL;");
        Execute(writer, $"UPDATE crawl SET status = {(int)CrawlStatus.Running}, updated_at = updated_at + 1");
        using var held = CrawlFileLock.Acquire(path);
        var (running, runningRows) = Export(path, new ExportRequest { Kind = ExportKind.Sitemaps, Format = ExportFormat.Csv }, "Sitemaps");
        Assert.True(running.CrawlWasRunning);
        Assert.Equal("Not read yet", StateOf(runningRows));

        var (origins, _) = Export(path, new ExportRequest { Kind = ExportKind.RobotsOrigins, Format = ExportFormat.Csv }, "robots.txt");
        Assert.True(origins.CrawlWasRunning);
        Assert.Equal(RobotsReader.ReadOrigins(path, limit: 1_000).Matching, origins.Rows);
    }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    /// <summary>Exports to a file in the test's folder and reads it back: the header, then the rows, a number as a double and text as text.</summary>
    private (ExportResult Result, List<object?[]> Rows) Export(string path, ExportRequest request, string sheet)
    {
        var target = Path.Combine(_folder, request.Format == ExportFormat.Csv ? "out.csv" : "out.xlsx");
        var result = CrawlExporter.Export(path, request, target, new ExportSettings { Cache = new ReadCache(), Clock = _clock });
        Assert.DoesNotContain(Directory.GetFiles(_folder), file => file.Contains(".tmp", StringComparison.Ordinal));
        if (request.Format == ExportFormat.Csv)
        {
            return (result, [.. CsvFile.Read(target).Select(row => row.Select(field => field.Length == 0 ? null : (object)field).ToArray())]);
        }

        XlsxFile.AssertValid(target);
        var file = Assert.Single(XlsxFile.Read(target).Sheets);
        Assert.Equal(sheet, file.Name);
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
                    (DateTime moment, ExportFormat.Csv) => moment.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    (DateTime moment, _) => moment.ToOADate(),
                    (_, ExportFormat.Csv) => Convert.ToString(value, CultureInfo.InvariantCulture),
                    _ => Convert.ToDouble(value, CultureInfo.InvariantCulture)
                };
                Assert.True(Equals(wanted, written), $"Row {row + 1}, column {column + 1}: the list has '{wanted}', the file '{written}'.");
            }
        }
    }
}
