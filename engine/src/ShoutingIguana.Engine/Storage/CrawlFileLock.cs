namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// Makes "one writer per crawl file" a guarantee rather than a status check. A crawl or an analysis holds an exclusive
/// lock on a file beside the crawl for as long as it writes, so a second writer is refused at once, in this process or
/// another. The operating system drops the lock when a process ends, so a crashed crawl stays resumable, and a lock
/// file that a crash leaves behind is simply reused. On Linux and macOS the lock file stays beside the crawl.
/// </summary>
internal sealed class CrawlFileLock : IDisposable
{
    private readonly FileStream _file;

    private CrawlFileLock(FileStream file) => _file = file;

    /// <exception cref="InvalidOperationException">Another crawl or analysis is writing to the file.</exception>
    public static CrawlFileLock Acquire(string databasePath)
    {
        try
        {
            // FileShare.None is a sharing lock on Windows and flock(LOCK_EX) on Linux and macOS. There, deleting the file on
            // close would unlink it before unlocking it: a writer that had just opened it would then lock a file nobody else
            // can see, while a third created a new one and locked that, and two would write at once.
            var options = OperatingSystem.IsWindows() ? FileOptions.DeleteOnClose : FileOptions.None;
            return new CrawlFileLock(new FileStream(databasePath + "-lock", FileMode.OpenOrCreate, FileAccess.ReadWrite,
                FileShare.None, bufferSize: 1, options));
        }
        catch (IOException exception) when (exception.GetType() == typeof(IOException))
        {
            // A sharing violation; a missing folder or a bad path throws a subclass and surfaces as it is.
            throw new InvalidOperationException(
                $"Another crawl or analysis is writing to '{databasePath}'. Wait for it to finish, or stop it, and try again.", exception);
        }
    }

    /// <summary>
    /// Whether a crawl or an analysis holds the lock, in this process or another. It asks for the lock as
    /// <see cref="Acquire"/> does and lets go at once, so a writer starting in that instant is refused as if one were
    /// running. A lock file that doesn't exist isn't held: writers create it before they lock it, and this never does.
    /// </summary>
    public static bool IsHeld(string databasePath)
    {
        try
        {
            using var probe = new FileStream(databasePath + "-lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None, bufferSize: 1);
            return false;
        }
        catch (IOException exception) when (exception.GetType() == typeof(IOException))
        {
            // A sharing violation: someone holds it. Its subclasses (not found, path too long...) mean nobody does.
            return true;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
    }

    public void Dispose() => _file.Dispose();
}
