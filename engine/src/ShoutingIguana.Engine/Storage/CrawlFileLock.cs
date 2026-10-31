namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// Makes "one writer per crawl file" a guarantee rather than a status check. A crawl or an analysis holds an exclusive
/// lock on a file beside the crawl for as long as it writes, so a second writer is refused at once, in this process or
/// another. The operating system drops the lock when a process ends, so a crashed crawl stays resumable, and a lock
/// file that a crash leaves behind is simply reused.
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
            // FileShare.None is a sharing lock on Windows and flock(LOCK_EX) on Linux and macOS.
            return new CrawlFileLock(new FileStream(databasePath + "-lock", FileMode.OpenOrCreate, FileAccess.ReadWrite,
                FileShare.None, bufferSize: 1, FileOptions.DeleteOnClose));
        }
        catch (IOException exception) when (exception.GetType() == typeof(IOException))
        {
            // A sharing violation; a missing folder or a bad path throws a subclass and surfaces as it is.
            throw new InvalidOperationException(
                $"Another crawl or analysis is writing to '{databasePath}'. Wait for it to finish, or stop it, and try again.", exception);
        }
    }

    public void Dispose() => _file.Dispose();
}
