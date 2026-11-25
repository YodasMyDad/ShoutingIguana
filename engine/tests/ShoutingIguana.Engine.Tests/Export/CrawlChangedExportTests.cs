using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling;
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
/// What a crawl does under an export of it, by the engine's own crawler and not by a change made to the file by hand.
/// A crawl that finishes, and one that is continued, end the export: its pages would be of two different crawls. A
/// crawl that is paused and resumed is the same crawl, and its export goes on to its end.
/// </summary>
[Collection(GridCrawlCollection.NAME)]
public sealed class CrawlChangedExportTests : IDisposable
{
    /// <summary>What an export that found its crawl changed says, word for word: it names what does end one, and no more.</summary>
    private const string CHANGED = "The crawl changed while it was being exported: it was analysed again, continued or it finished. Export it again.";

    private static readonly ExportRequest URLS = new() { Kind = ExportKind.Urls, Format = ExportFormat.Csv, Query = new UrlQuery { Columns = ["id", "url"] } };

    private readonly CrawlFiles _files = new();
    private readonly string _folder = Directory.CreateTempSubdirectory("si-export-").FullName;
    private readonly (int Urls, int Links) _pages = (GridExportReader.UrlPageRows, GridExportReader.LinkPageRows);

    // Small pages, so that an export of a few hundred URLs is read on by cursor several times.
    public CrawlChangedExportTests() => (GridExportReader.UrlPageRows, GridExportReader.LinkPageRows) = (97, 211);

    public void Dispose()
    {
        (GridExportReader.UrlPageRows, GridExportReader.LinkPageRows) = _pages;
        _files.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public async Task ACrawlThatFinishesUnderAnExportEndsIt()
    {
        await using var site = await SyntheticSiteServer.StartAsync(new SyntheticSiteOptions { PageCount = 300 });
        var path = _files.NewPath();
        await using var job = TestCrawls.Start(Options(site), path);
        await TestCrawls.WaitUntilAsync(path, "SELECT COUNT(*) FROM urls WHERE id > 400");
        Assert.False(job.Completion.IsCompleted);

        // The export has written two pages when the crawl finishes; its third is of a crawl that is no longer running.
        var pages = 0;
        CrawlStatus? ended = null;
        var settings = new ExportSettings
        {
            Progress = new Heard(progress =>
            {
                if (progress.RowsWritten > 0 && ++pages == 2)
                {
                    ended = job.Completion.WaitAsync(TestCrawls.TIMEOUT).GetAwaiter().GetResult().Status;
                }
            })
        };
        var target = Path.Combine(_folder, "finished.csv");
        var failure = await Assert.ThrowsAsync<ExportException>(() => Task.Run(() => CrawlExporter.Export(path, URLS, target, settings)));

        Assert.Equal(CrawlStatus.Completed, ended);
        Assert.Equal(ExportFailure.CrawlChanged, failure.Failure);
        Assert.Equal(CHANGED, failure.Message);
        Assert.Equal(2, pages);
        Assert.Empty(Directory.GetFiles(_folder));

        // Exported again, it is the crawl as it now is: all of it.
        var whole = CrawlExporter.Export(path, URLS, target);
        Assert.False(whole.CrawlWasRunning);
        Assert.Equal(TestCrawls.Count(path, "SELECT COUNT(*) FROM urls"), whole.Rows);
    }

    [Fact]
    public async Task ACrawlThatIsContinuedUnderAnExportEndsIt()
    {
        await using var site = await SyntheticSiteServer.StartAsync(new SyntheticSiteOptions { PageCount = 300 });
        var path = _files.NewPath();
        await using (var first = TestCrawls.Start(Options(site), path))
        {
            await TestCrawls.WaitUntilAsync(path, "SELECT COUNT(*) FROM urls WHERE id > 400");
            Assert.Equal(CrawlStatus.Stopped, (await first.StopAsync()).Status);
        }

        var stopped = TestCrawls.Count(path, "SELECT COUNT(*) FROM urls");
        Assert.True(TestCrawls.Count(path, "SELECT COUNT(*) FROM urls WHERE state = 0") > 0, "Nothing was left to continue with.");

        // The export of the stopped crawl has written two pages when the crawl is continued, by the engine's own crawler.
        var pages = 0;
        CrawlJob? continued = null;
        var settings = new ExportSettings
        {
            Progress = new Heard(progress =>
            {
                if (progress.RowsWritten > 0 && ++pages == 2)
                {
                    continued = TestCrawls.Start(Options(site), path, resume: true);
                    TestCrawls.WaitUntilAsync(path, $"SELECT COUNT(*) FROM crawl WHERE status = {(int)CrawlStatus.Running}").GetAwaiter().GetResult();
                }
            })
        };
        var target = Path.Combine(_folder, "continued.csv");
        try
        {
            var failure = await Assert.ThrowsAsync<ExportException>(() => Task.Run(() => CrawlExporter.Export(path, URLS, target, settings)));
            Assert.Equal(ExportFailure.CrawlChanged, failure.Failure);
            Assert.Equal(CHANGED, failure.Message);
            Assert.Equal(2, pages);
            Assert.Empty(Directory.GetFiles(_folder));
        }
        finally
        {
            if (continued is not null)
            {
                await continued.StopAsync();
                await continued.DisposeAsync();
            }
        }

        // The first run's export, had it not been continued, would have held what it had: the crawl as it then stood.
        Assert.True(stopped > 400);
    }

    /// <summary>
    /// A pause is no change to the crawl: the export of a crawl paused under it, and resumed, goes on and ends with its
    /// file, which holds the crawl as it stood when the export began.
    /// </summary>
    [Fact]
    public async Task ACrawlPausedAndResumedUnderAnExportDoesNotEndIt()
    {
        await using var site = await SyntheticSiteServer.StartAsync(new SyntheticSiteOptions { PageCount = 300 });
        var path = _files.NewPath();
        await using var job = TestCrawls.Start(Options(site) with { MaxRequestsPerSecond = 40 }, path);
        await TestCrawls.WaitUntilAsync(path, "SELECT COUNT(*) FROM urls WHERE id > 400");
        Assert.False(job.Completion.IsCompleted);

        var pages = 0;
        var settings = new ExportSettings
        {
            Progress = new Heard(progress =>
            {
                if (progress.RowsWritten == 0)
                {
                    return;
                }

                switch (++pages)
                {
                    case 1:
                        // Paused: what is in flight settles, and nothing more is asked for until it is resumed.
                        job.Pause();
                        Assert.True(job.IsPaused);
                        break;
                    case 3:
                        job.Resume();
                        Assert.False(job.IsPaused);
                        break;
                }
            })
        };
        var target = Path.Combine(_folder, "paused.csv");
        var result = await Task.Run(() => CrawlExporter.Export(path, URLS, target, settings));

        Assert.True(pages >= 4, $"{pages} pages.");
        Assert.True(result.CrawlWasRunning);
        var rows = CsvFile.Read(target).Skip(1).ToList();
        Assert.Equal(result.Rows, rows.Count);
        Assert.True(rows.Count > 400, $"{rows.Count} rows.");

        // In crawl order, every URL up to the last the crawl had when the export began.
        Assert.Equal(Enumerable.Range(1, rows.Count).Select(id => id.ToString(System.Globalization.CultureInfo.InvariantCulture)), rows.Select(row => row[0]));
        Assert.Equal([target], Directory.GetFiles(_folder));
    }

    private static CrawlOptions Options(SyntheticSiteServer site) => new() { StartUrl = site.BaseUri.AbsoluteUri, Concurrency = 4, MaxRequestsPerSecond = 80, BrowserFallback = false };

    private sealed class Heard(Action<ExportProgress> heard) : IProgress<ExportProgress>
    {
        public void Report(ExportProgress value) => heard(value);
    }
}
