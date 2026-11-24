using System.Globalization;
using Microsoft.Data.Sqlite;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Export;
using ShoutingIguana.Engine.Export.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Tests.Support;
using ShoutingIguana.TestSite;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.Engine.Tests.Export;

/// <summary>
/// An export while the crawl runs. The file is of the crawl as it stood when the export began (nothing found later is in
/// it), and the export reads a page at a time, each on a snapshot it lets go of before the next: between any two pages
/// nothing of the export holds the crawl's write-ahead log, so the crawl's writer can fold the log into the file and
/// start it again, however long the export takes.
/// </summary>
[Collection(GridCrawlCollection.NAME)]
public sealed class RunningCrawlExportTests : IDisposable
{
    private readonly GridCrawl _crawl;
    private readonly CrawlFiles _files = new();
    private readonly string _folder = Directory.CreateTempSubdirectory("si-export-").FullName;
    private readonly (int Urls, int Links) _pages = (GridExportReader.UrlPageRows, GridExportReader.LinkPageRows);

    public RunningCrawlExportTests(GridCrawl crawl)
    {
        _crawl = crawl;
        (GridExportReader.UrlPageRows, GridExportReader.LinkPageRows) = (97, 211);
    }

    public void Dispose()
    {
        (GridExportReader.UrlPageRows, GridExportReader.LinkPageRows) = _pages;
        _files.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    [Theory]
    [InlineData(ExportKind.Links, ExportFormat.Csv)]
    [InlineData(ExportKind.Links, ExportFormat.Xlsx)]
    [InlineData(ExportKind.Urls, ExportFormat.Csv)]
    public void AnExportOfARunningCrawlIsOfTheMomentItBeganAndNeverHoldsItsLog(ExportKind kind, ExportFormat format)
    {
        var (path, writer, held) = Running();
        using var releaseLock = held;
        using var closeWriter = writer;
        string[] columns = kind == ExportKind.Links ? ["id", "from.url", "to.url", "kind", "text"] : ["id", "url", "depth"];
        var query = new UrlQuery { Columns = columns };

        // A reader part-way through the file does keep the log from being emptied: the check below can tell.
        Grow(writer);
        using (var reader = CrawlDatabase.OpenReader(path))
        using (var snapshot = reader.BeginTransaction(deferred: true))
        {
            using var read = reader.CreateCommand();
            read.Transaction = snapshot;
            read.CommandText = "SELECT COUNT(*) FROM urls";
            read.ExecuteScalar();
            Grow(writer);
            Assert.Equal(1, Checkpoint(writer));
        }

        // The grid as it stands now, by its own reader: what the file must hold.
        List<string?[]> before = [];
        for (var offset = 0; ; offset += 1_000)
        {
            var rows = kind == ExportKind.Links
                ? LinkGridReader.Query(path, query with { Offset = offset, Limit = 1_000 }).Rows.Select(row => row.Values).ToList()
                : UrlGridReader.Query(path, query with { Offset = offset, Limit = 1_000 }).Rows.Select(row => row.Values).ToList();
            before.AddRange(rows.Select(row => row.Select(Text).ToArray()));
            if (rows.Count < 1_000)
            {
                break;
            }
        }

        Assert.True(before.Count > 400, $"{before.Count} rows.");

        var pages = 0;
        var settings = new ExportSettings
        {
            Progress = new Heard(_ =>
            {
                // Between two pages the crawl goes on: more URLs and more links, which are no part of the export.
                pages++;
                Grow(writer);

                // And nothing of the export is part-way through the file: the log is folded in whole, and emptied.
                Assert.Equal(0, Checkpoint(writer));
                Assert.Equal(0, new FileInfo(path + "-wal").Length);
            })
        };
        var target = Path.Combine(_folder, format == ExportFormat.Csv ? "out.csv" : "out.xlsx");
        var result = CrawlExporter.Export(path, new ExportRequest { Kind = kind, Format = format, Query = query }, target, settings);

        Assert.True(result.CrawlWasRunning);
        Assert.Equal(before.Count, result.Rows);
        Assert.True(pages > 4, $"{pages} pages.");
        var file = format == ExportFormat.Csv
            ? CsvFile.Read(target).Skip(1).Select(row => row.Select(field => field.Length == 0 ? null : field).ToArray()).ToList()
            : Assert.Single(XlsxFile.Read(target).Sheets).Values.Skip(1).Select(row => row.Select(Text).ToArray()).ToList();
        Assert.Equal(before.Count, file.Count);
        for (var row = 0; row < before.Count; row++)
        {
            var expected = before[row].Select((value, column) => columns[column] == "kind" ? Words(value) : value).ToArray();
            var written = Enumerable.Range(0, expected.Length).Select(column => column < file[row].Length ? file[row][column] : null).ToArray();
            Assert.True(expected.SequenceEqual(written), $"Row {row + 1}: the grid had [{string.Join(" | ", expected)}], the file [{string.Join(" | ", written)}].");
        }

        // The crawl did grow under the export, by more than the export wrote of it.
        var grown = TestCrawls.Count(path, kind == ExportKind.Links ? "SELECT COUNT(*) FROM links" : "SELECT COUNT(*) FROM urls");
        Assert.True(grown >= before.Count + pages * 10, $"{grown} rows after, {before.Count} before, {pages} pages.");
    }

    /// <summary>
    /// A running crawl's rows are counted a moment before they are read, and a count the grid keeps can be older still:
    /// the crawl has more rows by the time the export begins than were counted. An export never says it has written more
    /// rows than it expects: what it expects rises with what it has written.
    /// </summary>
    [Fact]
    public void AnExportOfARunningCrawlNeverWritesMoreRowsThanItSaysItExpects()
    {
        var (path, writer, held) = Running();
        using var releaseLock = held;
        using var closeWriter = writer;
        var cache = new ReadCache();
        var query = new UrlQuery { Columns = ["id", "url"], Sort = [new UrlSort("url")] };

        // The grid counts its rows, and keeps the count; the crawl then goes on.
        var counted = UrlGridReader.Query(path, query with { Limit = 1 }, cache).Matching;
        Grow(writer);
        Grow(writer);

        List<ExportProgress> heard = [];
        var result = CrawlExporter.Export(path, new ExportRequest { Kind = ExportKind.Urls, Format = ExportFormat.Csv, Query = query }, Path.Combine(_folder, "out.csv"),
            new ExportSettings { Cache = cache, Progress = new Heard(heard.Add) });

        Assert.Equal(counted + 20, result.Rows);
        Assert.Equal(counted, heard[0].RowsExpected);
        Assert.All(heard, progress => Assert.True(progress.RowsWritten <= progress.RowsExpected, $"{progress.RowsWritten} of {progress.RowsExpected}."));
        Assert.Equal(new ExportProgress(result.Rows, result.Rows), heard[^1]);
    }

    [Fact]
    public void ARunningCrawlIsExportedInCrawlOrderOrByUrlOnly()
    {
        var (path, writer, held) = Running();
        using var releaseLock = held;
        using var closeWriter = writer;
        var target = Path.Combine(_folder, "out.csv");

        var refused = Assert.Throws<UrlQueryException>(() => CrawlExporter.Export(path,
            new ExportRequest { Kind = ExportKind.Urls, Format = ExportFormat.Csv, Query = new UrlQuery { Sort = [new UrlSort("statusCode", true)] } }, target));
        Assert.Equal(["sort"], refused.Errors.Keys);
        Assert.Contains("still running", refused.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(_folder));

        var links = Assert.Throws<UrlQueryException>(() => CrawlExporter.Export(path,
            new ExportRequest { Kind = ExportKind.Links, Format = ExportFormat.Csv, Query = new UrlQuery { Sort = [new UrlSort("to.statusCode")] } }, target));
        Assert.Equal(["sort"], links.Errors.Keys);

        // A URL keeps its text and its place in crawl order whatever the crawl does, so those sorts hold.
        var byUrl = CrawlExporter.Export(path, new ExportRequest { Kind = ExportKind.Urls, Format = ExportFormat.Csv, Query = new UrlQuery { Sort = [new UrlSort("url", true)] } }, target);
        var urls = CsvFile.Read(target).Skip(1).Select(row => row[0]).ToList();
        Assert.Equal(byUrl.Rows, urls.Count);
        Assert.Equal(urls.OrderByDescending(url => url, StringComparer.Ordinal), urls);
        var newest = CrawlExporter.Export(path,
            new ExportRequest { Kind = ExportKind.Links, Format = ExportFormat.Csv, Query = new UrlQuery { Columns = ["id"], Sort = [new UrlSort("id", true)] } }, target);
        Assert.True(newest.Rows > 1_000);
    }

    /// <summary>
    /// A crawl by the engine itself, exported over and over while it runs. Each file holds the links the crawl had up to
    /// some link, every one of them and no other, in crawl order.
    /// </summary>
    [Fact]
    public async Task EveryExportOfACrawlAsItGrowsIsOfOneMomentOfIt()
    {
        await using var site = await SyntheticSiteServer.StartAsync(new SyntheticSiteOptions { PageCount = 300 });
        var path = _files.NewPath();
        await using var job = TestCrawls.Start(new CrawlOptions { StartUrl = site.BaseUri.AbsoluteUri, Concurrency = 4, MaxRequestsPerSecond = 80, BrowserFallback = false }, path);
        await TestCrawls.WaitUntilAsync(path, "SELECT COUNT(*) FROM links");

        var cache = new ReadCache();
        var request = new ExportRequest { Kind = ExportKind.Links, Format = ExportFormat.Csv, Query = new UrlQuery { Columns = ["id", "from.url", "to.url"] } };
        List<List<string[]>> exports = [];
        var changed = 0;
        while (!job.Completion.IsCompleted)
        {
            var target = Path.Combine(_folder, $"links-{exports.Count}.csv");
            try
            {
                var result = await Task.Run(() => CrawlExporter.Export(path, request, target, new ExportSettings { Cache = cache }));
                if (result.CrawlWasRunning)
                {
                    exports.Add([.. CsvFile.Read(target).Skip(1)]);
                    Assert.Equal(result.Rows, exports[^1].Count);
                }
            }
            catch (ExportException exception) when (exception.Failure == ExportFailure.CrawlChanged)
            {
                // The crawl finished under it: said, and nothing written.
                changed++;
                Assert.False(File.Exists(target));
            }
        }

        Assert.Equal(CrawlStatus.Completed, (await job.Completion.WaitAsync(TestCrawls.TIMEOUT)).Status);
        Assert.True(exports.Count >= 3, $"Only {exports.Count} exports were made while the crawl ran ({changed} found it changed).");
        Assert.True(exports.Select(rows => rows.Count).Distinct().Count() >= 3, "The exports saw the crawl at fewer than three sizes.");
        Assert.DoesNotContain(Directory.GetFiles(_folder), file => file.Contains(".tmp", StringComparison.Ordinal));

        // Against the finished crawl: each file is the links up to its last one, all of them, in order.
        var stored = TestCrawls.Rows(path, "SELECT l.rowid, f.url, t.url FROM links l JOIN urls f ON f.id = l.from_id JOIN urls t ON t.id = l.to_id")
            .Select(row => row.Split(" | ")).OrderBy(row => long.Parse(row[0], CultureInfo.InvariantCulture)).ToList();
        foreach (var rows in exports)
        {
            var last = long.Parse(rows[^1][0], CultureInfo.InvariantCulture);
            var expected = stored.TakeWhile(row => long.Parse(row[0], CultureInfo.InvariantCulture) <= last).ToList();
            Assert.Equal(expected.Count, rows.Count);
            Assert.All(expected.Zip(rows), pair => Assert.Equal(pair.First, pair.Second));
        }
    }

    /// <summary>
    /// A copy of the crawl that reads as running, with its writer: the row says so, someone holds its lock, and it's in
    /// WAL mode with a connection that writes to it, as a crawl's file is while the crawl runs.
    /// </summary>
    private (string Path, SqliteConnection Writer, IDisposable Lock) Running()
    {
        var path = _crawl.Copy();
        var writer = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        writer.Open();
        Execute(writer, "PRAGMA journal_mode = WAL; PRAGMA busy_timeout = 0; PRAGMA wal_autocheckpoint = 0;");
        Execute(writer, $"UPDATE crawl SET status = {(int)CrawlStatus.Running}, updated_at = updated_at + 1");
        return (path, writer, CrawlFileLock.Acquire(path));
    }

    /// <summary>The crawl stores another page: ten new URLs, and a link to each from a page it has.</summary>
    private static void Grow(SqliteConnection writer)
    {
        using var transaction = writer.BeginTransaction();
        using var command = writer.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            WITH RECURSIVE numbers (value) AS (SELECT 1 UNION ALL SELECT value + 1 FROM numbers WHERE value < 10)
            INSERT INTO urls (url, is_internal, fetch_mode, depth, state, skip_reason, source, found_on_id, found_via)
            SELECT 'https://grown.example/' || (SELECT COALESCE(MAX(id), 0) FROM urls) || '/' || value, 0, 0, 3, 0, 0, 1, 1, 0 FROM numbers;
            INSERT INTO links (from_id, to_id, kind, flags, position, text, fragment)
            SELECT 1, id, 0, 0, 0, 'grown', NULL FROM urls ORDER BY id DESC LIMIT 10;
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    /// <summary>Folds the log into the file and empties it, if no reader is part-way through it: 0 when it could, 1 when a reader kept it.</summary>
    private static long Checkpoint(SqliteConnection writer)
    {
        using var command = writer.CreateCommand();
        command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        return reader.GetInt64(0);
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string? Text(object? value) => value switch
    {
        null => null,
        string text => text.Length == 0 ? null : text,
        double number => number.ToString(CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture)
    };

    /// <summary>A link's kind as a file says it.</summary>
    private static string? Words(string? kind) => kind is null ? null : LinkGridReader.Columns.Single(column => column.Id == "kind").ValueTitles[kind];

    private sealed class Heard(Action<ExportProgress> heard) : IProgress<ExportProgress>
    {
        public void Report(ExportProgress value) => heard(value);
    }
}
