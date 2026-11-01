namespace ShoutingIguana.Sidecar.Hosting;

/// <summary>
/// One sidecar per state folder: two would write the same library and defaults, and neither would see the other's
/// changes. Held for the process's lifetime on <c>sidecar.lock</c> in the state folder. The operating system drops it
/// when the process ends, however it ends, so a crash never leaves the folder locked.
/// </summary>
internal sealed class StateFolderLock : IDisposable
{
    public const string FILE_NAME = "sidecar.lock";

    private readonly FileStream _file;

    private StateFolderLock(FileStream file) => _file = file;

    /// <exception cref="StateFolderInUseException">Another sidecar holds the lock.</exception>
    public static StateFolderLock Acquire(string stateDirectory)
    {
        Directory.CreateDirectory(stateDirectory);
        var path = Path.Combine(stateDirectory, FILE_NAME);
        try
        {
            // FileShare.None is a sharing lock on Windows and flock(LOCK_EX) on Linux and macOS. The file stays: deleting it
            // on close would let a sidecar starting at that moment lock a file the next one can't see.
            return new StateFolderLock(new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, bufferSize: 1));
        }
        catch (IOException exception) when (exception.GetType() == typeof(IOException))
        {
            throw new StateFolderInUseException(
                $"Another Shouting Iguana engine is already using {stateDirectory}. Close it, or give this one another --state-dir.", exception);
        }
    }

    public void Dispose() => _file.Dispose();
}
