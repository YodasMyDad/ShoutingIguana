using ShoutingIguana.Engine.Export;
using ShoutingIguana.Engine.Export.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Tests.Support;
using static ShoutingIguana.Engine.Tests.Support.GridQueries;

namespace ShoutingIguana.Engine.Tests.Export;

/// <summary>
/// The file an export writes: where it may go, that it's whole or not there at all, and what's said when it can't be
/// written. An export that's cancelled, that fails, or whose crawl changes under it leaves nothing behind, and leaves a
/// file it was to replace as it was.
/// </summary>
[Collection(GridCrawlCollection.NAME)]
public sealed class ExportFileTests : IDisposable
{
    private const string BEFORE = "the file that was here before";

    private static readonly ExportRequest LINKS = new() { Kind = ExportKind.Links, Format = ExportFormat.Csv };

    private readonly GridCrawl _crawl;
    private readonly string _folder = Directory.CreateTempSubdirectory("si-export-").FullName;
    private readonly (int Urls, int Links) _pages = (GridExportReader.UrlPageRows, GridExportReader.LinkPageRows);

    public ExportFileTests(GridCrawl crawl)
    {
        _crawl = crawl;
        (GridExportReader.UrlPageRows, GridExportReader.LinkPageRows) = (97, 211);
    }

    public void Dispose()
    {
        (GridExportReader.UrlPageRows, GridExportReader.LinkPageRows) = _pages;
        ExportTarget.Wrap = null;
        foreach (var file in Directory.GetFiles(_folder))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(_folder, recursive: true);
    }

    [Theory]
    [InlineData("out.txt", ExportFormat.Csv, ".csv")]
    [InlineData("out.csv", ExportFormat.Xlsx, ".xlsx")]
    [InlineData("out.xlsx", ExportFormat.Csv, ".csv")]
    [InlineData("out", ExportFormat.Csv, ".csv")]
    [InlineData("out.exe", ExportFormat.Html, ".html")]
    [InlineData("out.csv.bat", ExportFormat.Csv, ".csv")]
    public void AFileMustHaveItsFormatsExtension(string name, ExportFormat format, string extension)
    {
        var failure = Assert.Throws<ExportException>(() => CrawlExporter.CheckPath(Path.Combine(_folder, name), format, _crawl.Path));
        Assert.Equal(ExportFailure.InvalidPath, failure.Failure);
        Assert.Contains(extension, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void APathIsMadeFullAndMayNotBeAFolderOrTheCrawl()
    {
        Assert.Equal(Path.Combine(_folder, "a.CSV"), CrawlExporter.CheckPath(Path.Combine(_folder, "sub", "..", "a.CSV"), ExportFormat.Csv, _crawl.Path));
        Assert.Equal(Path.Combine(_folder, "report.htm"), CrawlExporter.CheckPath(Path.Combine(_folder, "report.htm"), ExportFormat.Html, _crawl.Path));

        Directory.CreateDirectory(Path.Combine(_folder, "folder.csv"));
        Assert.Equal(ExportFailure.InvalidPath, Assert.Throws<ExportException>(() => CrawlExporter.CheckPath(Path.Combine(_folder, "folder.csv"), ExportFormat.Csv, _crawl.Path)).Failure);
        Assert.Equal(ExportFailure.InvalidPath, Assert.Throws<ExportException>(() => CrawlExporter.CheckPath(" ", ExportFormat.Csv, _crawl.Path)).Failure);

        // A crawl named as an export's file would be overwritten: refused, however the name is spelt where case doesn't tell files apart.
        var crawl = Path.Combine(_folder, "crawl.csv");
        Assert.Equal(ExportFailure.InvalidPath, Assert.Throws<ExportException>(() => CrawlExporter.CheckPath(crawl, ExportFormat.Csv, crawl)).Failure);
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
        {
            Assert.Equal(ExportFailure.InvalidPath, Assert.Throws<ExportException>(() => CrawlExporter.CheckPath(crawl.ToUpperInvariant(), ExportFormat.Csv, crawl)).Failure);
        }
    }

    [Fact]
    public void AFolderThatIsNotThereIsSaidPlainly()
    {
        var failure = Assert.Throws<ExportException>(() => CrawlExporter.Export(_crawl.Path, LINKS, Path.Combine(_folder, "missing", "links.csv")));
        Assert.Equal(ExportFailure.NotWritable, failure.Failure);
        Assert.Contains("folder", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Excel holds a workbook it's showing open: an export to that name says so at once, before reading a row.</summary>
    [Fact]
    public void AFileOpenInAnotherProgramIsSaidPlainlyAndLeftAsItWas()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var path = Path.Combine(_folder, "links.csv");
        File.WriteAllText(path, BEFORE);
        List<ExportProgress> heard = [];
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var failure = Assert.Throws<ExportException>(() => CrawlExporter.Export(_crawl.Path, LINKS, path, new ExportSettings { Progress = new Heard(heard.Add) }));
            Assert.Equal(ExportFailure.FileInUse, failure.Failure);
            Assert.Contains("links.csv is open in another program", failure.Message, StringComparison.Ordinal);
        }

        Assert.Empty(heard);
        AssertUntouched(path);
    }

    [Fact]
    public void AReadOnlyFileIsNotReplaced()
    {
        var path = Path.Combine(_folder, "links.csv");
        File.WriteAllText(path, BEFORE);
        File.SetAttributes(path, FileAttributes.ReadOnly);
        var failure = Assert.Throws<ExportException>(() => CrawlExporter.Export(_crawl.Path, LINKS, path));
        Assert.Equal(ExportFailure.NotWritable, failure.Failure);
        Assert.Contains("read-only", failure.Message, StringComparison.Ordinal);
        AssertUntouched(path);
    }

    [Theory]
    [InlineData(ExportFormat.Csv)]
    [InlineData(ExportFormat.Xlsx)]
    public void ACancelledExportLeavesNothing(ExportFormat format)
    {
        var path = Path.Combine(_folder, format == ExportFormat.Csv ? "links.csv" : "links.xlsx");
        File.WriteAllText(path, BEFORE);
        using var cancel = new CancellationTokenSource();
        var pages = 0;
        var settings = new ExportSettings { Progress = new Heard(_ => { if (++pages == 4) { cancel.Cancel(); } }) };
        Assert.ThrowsAny<OperationCanceledException>(() => CrawlExporter.Export(_crawl.Path, LINKS with { Format = format }, path, settings, cancel.Token));
        Assert.Equal(4, pages);
        AssertUntouched(path);
    }

    /// <summary>The disk fills part-way through: the export says so, and neither the half file nor anything beside it is left.</summary>
    [Theory]
    [InlineData(ExportFormat.Csv)]
    [InlineData(ExportFormat.Xlsx)]
    public void ADiskThatFillsUpIsSaidPlainlyAndLeavesNothing(ExportFormat format)
    {
        var path = Path.Combine(_folder, format == ExportFormat.Csv ? "links.csv" : "links.xlsx");
        File.WriteAllText(path, BEFORE);
        ExportTarget.Wrap = file => new FullDisk(file, roomBytes: 40_000);
        var failure = Assert.Throws<ExportException>(() => CrawlExporter.Export(_crawl.Path, LINKS with { Format = format }, path));
        Assert.Equal(ExportFailure.DiskFull, failure.Failure);
        Assert.StartsWith("The disk is full", failure.Message, StringComparison.Ordinal);
        AssertUntouched(path);
    }

    [Fact]
    public void WhatGoesWrongWithTheFileIsToldApart()
    {
        var path = Path.Combine(_folder, "links.xlsx");
        if (OperatingSystem.IsWindows())
        {
            Assert.Equal(ExportFailure.DiskFull, ExportTarget.Failed(new IOException("There is not enough space on the disk.", unchecked((int)0x80070070)), path).Failure);
            Assert.Equal(ExportFailure.DiskFull, ExportTarget.Failed(new IOException("The disk is full.", unchecked((int)0x80070027)), path).Failure);
            var held = ExportTarget.Failed(new IOException("The process cannot access the file because it is being used by another process.", unchecked((int)0x80070020)), path);
            Assert.Equal(ExportFailure.FileInUse, held.Failure);
            Assert.StartsWith("links.xlsx is open in another program, such as Excel.", held.Message, StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal(ExportFailure.DiskFull, ExportTarget.Failed(new IOException("No space left on device", 28), path).Failure);
        }

        Assert.Equal(ExportFailure.NotWritable, ExportTarget.Failed(new UnauthorizedAccessException("Access to the path is denied."), path).Failure);
        Assert.Equal(ExportFailure.NotWritable, ExportTarget.Failed(new DirectoryNotFoundException("Could not find a part of the path."), path).Failure);
        Assert.Equal(ExportFailure.NotWritable, ExportTarget.Failed(new IOException("Something else."), path).Failure);
    }

    /// <summary>
    /// A crawl analysed again, or continued, while it's exported: the pages already written are of a crawl that no longer
    /// is, so the export stops and says so, rather than write the rows of two crawls into one file.
    /// </summary>
    [Fact]
    public void ACrawlThatChangesUnderAnExportEndsIt()
    {
        var copy = _crawl.Copy();
        var path = Path.Combine(_folder, "urls.csv");
        File.WriteAllText(path, BEFORE);
        var pages = 0;
        var settings = new ExportSettings { Progress = new Heard(_ => { if (++pages == 3) { Stamp(copy); } }) };
        var failure = Assert.Throws<ExportException>(() => CrawlExporter.Export(copy, new ExportRequest { Kind = ExportKind.Urls, Format = ExportFormat.Csv }, path, settings));
        Assert.Equal(ExportFailure.CrawlChanged, failure.Failure);

        // It names what ends an export, and no more: a crawl paused or resumed under one is the same crawl, and doesn't.
        Assert.Equal("The crawl changed while it was being exported: it was analysed again, continued or it finished. Export it again.", failure.Message);
        Assert.Equal(3, pages);
        AssertUntouched(path);
    }

    /// <summary>
    /// A name a file can't have is refused as the path it is, before anything is written. On Windows a colon in a
    /// file's name is another stream of the file before it: "d.txt:x.csv" ends in .csv, and writing it left an empty
    /// "d.txt" (the part file's name begins with a dot: ".d.txt") behind a message from the system.
    /// </summary>
    [Fact]
    public void AFileNameWithACharacterAFileCannotHaveIsRefusedBeforeAnythingIsWritten()
    {
        if (!OperatingSystem.IsWindows())
        {
            // Elsewhere a file's name may hold any character but a slash and a null, which no path gets this far with.
            return;
        }

        foreach (var name in new[] { "d.txt:x.csv", "what?.csv", "a*b.csv", "say \"hi\".csv", "a<b>.csv", "pipe|d.csv" })
        {
            var failure = Assert.Throws<ExportException>(() => CrawlExporter.Export(_crawl.Path, new ExportRequest { Kind = ExportKind.Urls, Format = ExportFormat.Csv },
                Path.Combine(_folder, name)));
            Assert.Equal(ExportFailure.InvalidPath, failure.Failure);
            Assert.Equal("A file’s name can’t have any of these in it: \\ / : * ? \" < > |. Choose another name.", failure.Message);
            Assert.Empty(Directory.GetFileSystemEntries(_folder));
            Assert.Equal(ExportFailure.InvalidPath, Assert.Throws<ExportException>(() => CrawlExporter.CheckPath(Path.Combine(_folder, name), ExportFormat.Csv, _crawl.Path)).Failure);
        }
    }

    /// <summary>
    /// Whoever runs exports is told of each file one is part-way through writing, before the file is there and once it
    /// has gone, however the export ends: a workbook's two (its sheets, and the strings it sets aside), a CSV's one.
    /// </summary>
    [Theory]
    [InlineData(ExportFormat.Csv, 1, false)]
    [InlineData(ExportFormat.Xlsx, 2, false)]
    [InlineData(ExportFormat.Csv, 1, true)]
    [InlineData(ExportFormat.Xlsx, 2, true)]
    public void EveryPartFileIsNotedBeforeItIsThereAndOnceItHasGone(ExportFormat format, int parts, bool cancelled)
    {
        var journal = new Journal();
        using var cancel = new CancellationTokenSource();
        var pages = 0;
        var settings = new ExportSettings
        {
            Journal = journal,
            Progress = new Heard(_ =>
            {
                if (++pages == 3)
                {
                    // Part-way: each part file is noted, and there.
                    Assert.Equal(parts, journal.Open.Count);
                    Assert.All(journal.Open, part => Assert.True(File.Exists(part), part));
                    if (cancelled)
                    {
                        cancel.Cancel();
                    }
                }
            })
        };
        var path = Path.Combine(_folder, format == ExportFormat.Csv ? "noted.csv" : "noted.xlsx");
        var request = new ExportRequest { Kind = ExportKind.Urls, Format = format };
        if (cancelled)
        {
            Assert.Throws<OperationCanceledException>(() => CrawlExporter.Export(_crawl.Path, request, path, settings, cancel.Token));
            Assert.Empty(Directory.GetFiles(_folder));
        }
        else
        {
            CrawlExporter.Export(_crawl.Path, request, path, settings, cancel.Token);
            Assert.Equal([path], Directory.GetFiles(_folder));
        }

        Assert.True(pages >= 3, $"{pages} pages.");
        Assert.Equal(parts, journal.Opened.Count);
        Assert.All(journal.Opened, part => Assert.Equal(_folder, Path.GetDirectoryName(part)));
        Assert.All(journal.Opened, part => Assert.StartsWith("." + Path.GetFileName(path) + ".", Path.GetFileName(part), StringComparison.Ordinal));
        Assert.Empty(journal.ThereWhenNoted);
        Assert.Empty(journal.Open);
        Assert.Equal(journal.Opened.Order(StringComparer.Ordinal), journal.ClosedParts.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void AnExportReplacesTheFileThatWasThere()
    {
        var path = Path.Combine(_folder, "urls.csv");
        File.WriteAllText(path, BEFORE);
        var result = CrawlExporter.Export(_crawl.Path, new ExportRequest { Kind = ExportKind.Urls, Format = ExportFormat.Csv }, path);
        Assert.Equal(result.Rows + 1, CsvFile.Read(path).Count);
        Assert.Equal([path], Directory.GetFiles(_folder));
    }

    /// <summary>Nothing is in the folder but the file that was there, as it was.</summary>
    private void AssertUntouched(string path)
    {
        Assert.Equal([path], Directory.GetFiles(_folder));
        Assert.Equal(BEFORE, File.ReadAllText(path));
    }

    private sealed class Heard(Action<ExportProgress> heard) : IProgress<ExportProgress>
    {
        public void Report(ExportProgress value) => heard(value);
    }

    /// <summary>A journal that remembers what it was told, and whether a part file was already there when it was noted.</summary>
    private sealed class Journal : IExportJournal
    {
        public List<string> Opened { get; } = [];

        public List<string> ClosedParts { get; } = [];

        public List<string> ThereWhenNoted { get; } = [];

        public HashSet<string> Open { get; } = new(StringComparer.Ordinal);

        public void Opening(string partFile)
        {
            if (File.Exists(partFile))
            {
                ThereWhenNoted.Add(partFile);
            }

            Opened.Add(partFile);
            Open.Add(partFile);
        }

        public void Closed(string partFile)
        {
            Assert.False(File.Exists(partFile), $"{partFile} is still there.");
            ClosedParts.Add(partFile);
            Open.Remove(partFile);
        }
    }

    /// <summary>A file on a disk with room for only so many bytes more: writing past them fails as Windows, or Linux, fails it.</summary>
    private sealed class FullDisk(Stream file, long roomBytes) : Stream
    {
        private long _written;

        public override bool CanRead => file.CanRead;

        public override bool CanSeek => file.CanSeek;

        public override bool CanWrite => file.CanWrite;

        public override long Length => file.Length;

        public override long Position
        {
            get => file.Position;
            set => file.Position = value;
        }

        public override void Flush() => file.Flush();

        public override int Read(byte[] buffer, int offset, int count) => file.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) => file.Seek(offset, origin);

        public override void SetLength(long value) => file.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count)
        {
            _written += count;
            if (_written > roomBytes)
            {
                throw OperatingSystem.IsWindows()
                    ? new IOException("There is not enough space on the disk.", unchecked((int)0x80070070))
                    : new IOException("No space left on device", 28);
            }

            file.Write(buffer, offset, count);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                file.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
