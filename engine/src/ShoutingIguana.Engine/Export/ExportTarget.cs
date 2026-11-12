using ShoutingIguana.Engine.Export.Models;

namespace ShoutingIguana.Engine.Export;

/// <summary>
/// The file an export writes. It's written beside where it's wanted, under another name, and moved into place only once
/// it's whole: an export that's cancelled or fails leaves no file, and one that replaces a file leaves the old one as it
/// was until the new one is ready. What can go wrong with the file itself is told apart, each in plain words
/// (<see cref="ExportFailure"/>): a place that can't be written to, a disk that fills up, and a file of that name held
/// open by another program, as Excel holds a workbook it's showing.
/// </summary>
internal sealed class ExportTarget : IDisposable
{
    private const int BUFFER_BYTES = 1 << 16;

    // Windows' codes for a full disk and for a file someone else has open, in an exception's HResult.
    private const int ERROR_SHARING_VIOLATION = 32;
    private const int ERROR_LOCK_VIOLATION = 33;
    private const int ERROR_HANDLE_DISK_FULL = 39;
    private const int ERROR_DISK_FULL = 112;

    // Elsewhere an IOException carries the errno: no space left, and over quota (Linux's number, then macOS's).
    private const int ENOSPC = 28;
    private const int EDQUOT_LINUX = 122;
    private const int EDQUOT_MACOS = 69;

    private static readonly AsyncLocal<Func<Stream, Stream>?> WRAP = new();

    /// <summary>The characters this system lets no file's name have.</summary>
    private static readonly System.Buffers.SearchValues<char> INVALID_NAME_CHARS = System.Buffers.SearchValues.Create(System.IO.Path.GetInvalidFileNameChars());

    private readonly string _temporary;
    private readonly IExportJournal? _journal;
    private readonly List<string> _beside = [];
    private readonly List<Stream> _open = [];
    private bool _committed;

    private ExportTarget(string path, string temporary, Stream stream, IExportJournal? journal)
    {
        Path = path;
        _temporary = temporary;
        _journal = journal;
        Stream = stream;
        _open.Add(stream);
    }

    /// <summary>Where the file will be, once it's whole.</summary>
    public string Path { get; }

    /// <summary>Where the file is written meanwhile. What goes wrong writing to it is an <see cref="ExportException"/>.</summary>
    public Stream Stream { get; }

    /// <summary>
    /// Tests only: stands in for the files the exports started in this flow (this thread and what it awaits) write to, to
    /// make writing fail as a full disk does.
    /// </summary>
    internal static Func<Stream, Stream>? Wrap
    {
        get => WRAP.Value;
        set => WRAP.Value = value;
    }

    /// <summary>
    /// The full path an export may write, from the path asked for: a file, with the format's extension, that isn't the
    /// crawl (or one of the files SQLite keeps beside it).
    /// </summary>
    /// <exception cref="ExportException"><see cref="ExportFailure.InvalidPath"/>, saying what's wrong with it.</exception>
    public static string Check(string path, ExportFormat format, string crawlPath)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ExportException(ExportFailure.InvalidPath, "Give the file to save to.");
        }

        string full;
        try
        {
            full = System.IO.Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ExportException(ExportFailure.InvalidPath, "This isn't a path a file can be saved to.", exception);
        }

        var extensions = Extensions(format);
        if (!extensions.Contains(System.IO.Path.GetExtension(full), StringComparer.OrdinalIgnoreCase))
        {
            throw new ExportException(ExportFailure.InvalidPath, $"The file's name must end in {extensions[0]}, as that is what will be written.");
        }

        // A name the system gives no file: found here, not by the system as the file is opened. On Windows a colon in a
        // name is another stream of the file named before it, so "d.txt:x.csv" ends in .csv and would write into "d.txt".
        if (System.IO.Path.GetFileName(full).AsSpan().ContainsAny(INVALID_NAME_CHARS))
        {
            throw new ExportException(ExportFailure.InvalidPath, OperatingSystem.IsWindows()
                ? "A file’s name can’t have any of these in it: \\ / : * ? \" < > |. Choose another name."
                : "This isn’t a name a file can have. Choose another name.");
        }

        if (Directory.Exists(full))
        {
            throw new ExportException(ExportFailure.InvalidPath, "That is a folder: give a file's name in it.");
        }

        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var crawl = System.IO.Path.GetFullPath(crawlPath);
        if (new[] { crawl, crawl + "-wal", crawl + "-shm", crawl + "-journal", crawl + "-lock" }.Any(file => string.Equals(file, full, comparison)))
        {
            throw new ExportException(ExportFailure.InvalidPath, "That is the crawl's own file: choose another name.");
        }

        return full;
    }

    /// <summary>The extensions a file of a format may have, the usual one first.</summary>
    public static IReadOnlyList<string> Extensions(ExportFormat format) => format switch
    {
        ExportFormat.Csv => [".csv"],
        ExportFormat.Xlsx => [".xlsx"],
        _ => [".html", ".htm"]
    };

    /// <summary>
    /// Opens the file an export writes to, beside <paramref name="path"/> (which <see cref="Check"/> gave). Says at once,
    /// before anything is read or written, when the place can't be written to or a file of that name is open elsewhere.
    /// </summary>
    /// <param name="journal">Told of the part file before it's created, and once it's gone (see <see cref="IExportJournal"/>).</param>
    /// <exception cref="ExportException">The file can't be written there, with why.</exception>
    public static ExportTarget Open(string path, IExportJournal? journal = null)
    {
        var folder = CheckWritable(path);
        var temporary = System.IO.Path.Combine(folder, $".{System.IO.Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        journal?.Opening(temporary);
        try
        {
            var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, BUFFER_BYTES);
            return new ExportTarget(path, temporary, new GuardedStream(Wrap?.Invoke(file) ?? file, path), journal);
        }
        catch (Exception exception) when (IsFileError(exception) || exception is NotSupportedException)
        {
            Gone(journal, temporary);
            throw Failed(exception, path);
        }
    }

    /// <summary>The folder a file at the path would be written in, once it's known to be there and any file of that name to be one this can replace.</summary>
    /// <exception cref="ExportException">The folder isn't there, or the file is read-only or open in another program.</exception>
    public static string CheckWritable(string path)
    {
        var folder = System.IO.Path.GetDirectoryName(path);
        if (folder is null || !Directory.Exists(folder))
        {
            throw new ExportException(ExportFailure.NotWritable, "The folder for this file isn't there. Choose another place.");
        }

        ProbeExisting(path);
        return folder;
    }

    /// <summary>
    /// Another file beside the one being written, for what a format has to set aside as it writes (a workbook's shared
    /// strings). It is read back through the same stream, and goes when the stream closes.
    /// </summary>
    public Stream OpenBeside(string purpose)
    {
        var beside = $"{_temporary}.{purpose}";
        _journal?.Opening(beside);
        _beside.Add(beside);
        try
        {
            var file = new FileStream(beside, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, BUFFER_BYTES, FileOptions.DeleteOnClose);
            var stream = new GuardedStream(Wrap?.Invoke(file) ?? file, Path);
            _open.Add(stream);
            return stream;
        }
        catch (Exception exception) when (IsFileError(exception))
        {
            throw Failed(exception, Path);
        }
    }

    /// <summary>The file is whole: it's moved to where it's wanted, in place of any file there. Its size.</summary>
    /// <exception cref="ExportException">It couldn't be put there, with why. Nothing is left behind.</exception>
    public long Commit()
    {
        try
        {
            if (Stream is GuardedStream { Inner: FileStream file })
            {
                // On the disk, not in the system's cache, before it takes the place of a file that was.
                file.Flush(flushToDisk: true);
            }

            Stream.Dispose();
            File.Move(_temporary, Path, overwrite: true);
            _committed = true;
            Gone(_journal, _temporary);
            return new FileInfo(Path).Length;
        }
        catch (Exception exception) when (IsFileError(exception))
        {
            // Replacing a file another program has open is refused as "access denied": whether it's open says which it was.
            if (exception is UnauthorizedAccessException && File.Exists(Path))
            {
                ProbeExisting(Path);
            }

            throw Failed(exception, Path);
        }
    }

    public void Dispose()
    {
        foreach (var stream in _open)
        {
            try
            {
                stream.Dispose();
            }
            catch (Exception exception) when (exception is ExportException || IsFileError(exception))
            {
                // The export has already failed or been given up: what closing its file says changes nothing.
            }
        }

        if (!_committed)
        {
            Delete(_temporary);
            Gone(_journal, _temporary);
        }

        // Each goes as its stream closes; this is for one a platform left behind.
        foreach (var beside in _beside)
        {
            Delete(beside);
            Gone(_journal, beside);
        }
    }

    /// <summary>
    /// Tells the journal a part file is gone, once it is. One that couldn't be deleted (something else holds it) stays
    /// noted, for whoever keeps the journal to try again.
    /// </summary>
    private static void Gone(IExportJournal? journal, string partFile)
    {
        if (journal is not null && !File.Exists(partFile))
        {
            journal.Closed(partFile);
        }
    }

    /// <summary>What went wrong with the file, in plain words.</summary>
    internal static ExportException Failed(Exception exception, string path)
    {
        var name = System.IO.Path.GetFileName(path);
        return exception switch
        {
            ExportException known => known,
            IOException full when IsDiskFull(full) => new ExportException(ExportFailure.DiskFull,
                "The disk is full: there wasn't room to save this file. Free some space, or save it somewhere else.", exception),
            IOException held when IsHeldOpen(held) => new ExportException(ExportFailure.FileInUse,
                $"{name} is open in another program, such as Excel. Close it there, or save under another name.", exception),
            DirectoryNotFoundException => new ExportException(ExportFailure.NotWritable, "The folder for this file isn't there any more. Choose another place.", exception),
            UnauthorizedAccessException => new ExportException(ExportFailure.NotWritable,
                File.Exists(path) && new FileInfo(path).IsReadOnly
                    ? $"{name} is read-only, so it can't be replaced. Save under another name."
                    : "This folder can't be saved to: you don't have permission to write there. Choose another place.", exception),
            PathTooLongException => new ExportException(ExportFailure.InvalidPath, "This path is too long to save a file at. Choose a shorter name or another place.", exception),
            _ => new ExportException(ExportFailure.NotWritable, $"This file couldn't be saved: {exception.Message}", exception)
        };
    }

    internal static bool IsFileError(Exception exception) => exception is IOException or UnauthorizedAccessException or System.Security.SecurityException;

    private static bool IsDiskFull(IOException exception) => OperatingSystem.IsWindows()
        ? (exception.HResult & 0xFFFF) is ERROR_DISK_FULL or ERROR_HANDLE_DISK_FULL
        : exception.HResult is ENOSPC or EDQUOT_LINUX or EDQUOT_MACOS;

    private static bool IsHeldOpen(IOException exception) =>
        OperatingSystem.IsWindows() && (exception.HResult & 0xFFFF) is ERROR_SHARING_VIOLATION or ERROR_LOCK_VIOLATION;

    /// <summary>A file already at the path must be one this can replace: not open in another program, and not read-only.</summary>
    private static void ProbeExisting(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            using var probe = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        catch (FileNotFoundException)
        {
            // Gone between the two looks: nothing to replace.
        }
        catch (Exception exception) when (IsFileError(exception))
        {
            throw Failed(exception, path);
        }
    }

    private static void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (IsFileError(exception))
        {
            // Left behind only if something else holds it: nothing more can be done from here.
        }
    }

    /// <summary>The file being written, with what goes wrong writing to it said as an export's failure.</summary>
    private sealed class GuardedStream(Stream inner, string path) : Stream
    {
        public Stream Inner => inner;

        public override bool CanRead => inner.CanRead;

        public override bool CanSeek => inner.CanSeek;

        public override bool CanWrite => inner.CanWrite;

        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => Guard(() => inner.Position = value);
        }

        public override void Flush() => Guard(inner.Flush);

        public override int Read(byte[] buffer, int offset, int count) => Guard(() => inner.Read(buffer, offset, count));

        public override int Read(Span<byte> buffer)
        {
            try
            {
                return inner.Read(buffer);
            }
            catch (Exception exception) when (IsFileError(exception))
            {
                throw Failed(exception, path);
            }
        }

        public override long Seek(long offset, SeekOrigin origin) => Guard(() => inner.Seek(offset, origin));

        public override void SetLength(long value) => Guard(() => inner.SetLength(value));

        public override void Write(byte[] buffer, int offset, int count) => Guard(() => inner.Write(buffer, offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            try
            {
                inner.Write(buffer);
            }
            catch (Exception exception) when (IsFileError(exception))
            {
                throw Failed(exception, path);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Guard(inner.Dispose);
            }

            base.Dispose(disposing);
        }

        private void Guard(Action action)
        {
            try
            {
                action();
            }
            catch (Exception exception) when (IsFileError(exception))
            {
                throw Failed(exception, path);
            }
        }

        private T Guard<T>(Func<T> action)
        {
            try
            {
                return action();
            }
            catch (Exception exception) when (IsFileError(exception))
            {
                throw Failed(exception, path);
            }
        }
    }
}
