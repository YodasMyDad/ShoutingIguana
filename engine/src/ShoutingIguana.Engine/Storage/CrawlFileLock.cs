using System.Diagnostics;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// Makes "one writer per crawl file" a guarantee rather than a status check. A crawl or an analysis holds an exclusive
/// lock on a file beside the crawl for as long as it writes, so a second writer is refused, in this process or another.
/// The operating system drops the lock when a process ends, so a crashed crawl stays resumable, and a lock file that a
/// crash leaves behind is simply reused. On Linux and macOS the lock file stays beside the crawl.
/// </summary>
internal sealed class CrawlFileLock : IDisposable
{
    /// <summary>
    /// How long <see cref="Acquire"/> keeps asking before it refuses. Asking for the lock is the only way to see whether
    /// it is held, so a probe in another process holds it for an instant (on Linux and macOS it takes the lock itself),
    /// and a writer must not mistake that instant for a crawl.
    /// </summary>
    private static readonly TimeSpan PATIENCE = TimeSpan.FromMilliseconds(200);

    private static readonly TimeSpan RETRY_INTERVAL = TimeSpan.FromMilliseconds(10);

    /// <summary>Probes and acquires in this process take turns, so a probe here never makes a writer here give up.</summary>
    private static readonly Lock GATE = new();

    private readonly FileStream _file;

    private CrawlFileLock(FileStream file) => _file = file;

    /// <exception cref="CrawlFileInUseException">Another crawl or analysis is writing to the file.</exception>
    public static CrawlFileLock Acquire(string databasePath) => Acquire(databasePath, PATIENCE);

    /// <param name="patience">How long to keep asking; tests give none, to see a single attempt.</param>
    internal static CrawlFileLock Acquire(string databasePath, TimeSpan patience)
    {
        var started = Stopwatch.GetTimestamp();
        while (true)
        {
            try
            {
                lock (GATE)
                {
                    // FileShare.None is a sharing lock on Windows and flock(LOCK_EX) on Linux and macOS. There, deleting the file on
                    // close would unlink it before unlocking it: a writer that had just opened it would then lock a file nobody else
                    // can see, while a third created a new one and locked that, and two would write at once.
                    var options = OperatingSystem.IsWindows() ? FileOptions.DeleteOnClose : FileOptions.None;
                    return new CrawlFileLock(new FileStream(databasePath + "-lock", FileMode.OpenOrCreate, FileAccess.ReadWrite,
                        FileShare.None, bufferSize: 1, options));
                }
            }
            catch (IOException exception) when (exception.GetType() == typeof(IOException))
            {
                // A sharing violation; a missing folder or a bad path throws a subclass and surfaces as it is.
                if (Stopwatch.GetElapsedTime(started) >= patience)
                {
                    throw new CrawlFileInUseException(
                        $"Another crawl or analysis is writing to '{databasePath}'. Wait for it to finish, or stop it, and try again.", exception);
                }
            }
            catch (UnauthorizedAccessException) when (Stopwatch.GetElapsedTime(started) < patience)
            {
                // On Windows, the lock file of a writer that has just let go, still being deleted. Past the patience, a lock
                // file this user may not open surfaces as it is.
            }

            Thread.Sleep(RETRY_INTERVAL);
        }
    }

    /// <summary>
    /// Whether a crawl or an analysis holds the lock, in this process or another. It asks for the lock as
    /// <see cref="Acquire"/> does and lets go at once. A lock file that doesn't exist isn't held: writers create it before
    /// they lock it, and this never does. Neither is one this user can't open (no writer could share it with us), nor one
    /// Windows is deleting because its writer has just let go.
    /// </summary>
    /// <exception cref="IOException">The path can't be used at all, e.g. it is too long.</exception>
    public static bool IsHeld(string databasePath)
    {
        try
        {
            lock (GATE)
            {
                // Read access is enough to ask, on every system, and works on a lock file this user can't write.
                using var probe = new FileStream(databasePath + "-lock", FileMode.Open, FileAccess.Read, FileShare.None, bufferSize: 1);
            }

            return false;
        }
        catch (IOException exception) when (exception.GetType() == typeof(IOException))
        {
            // A sharing violation: someone holds it.
            return true;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void Dispose() => _file.Dispose();
}
