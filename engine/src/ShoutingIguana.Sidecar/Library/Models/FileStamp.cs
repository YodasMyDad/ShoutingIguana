namespace ShoutingIguana.Sidecar.Library.Models;

/// <summary>
/// The sizes and modified times of a crawl file and its write-ahead log. While a crawl writes, its changes reach the log
/// before the file, so a summary is read again whenever either changes.
/// </summary>
internal sealed record FileStamp(long Length, DateTime LastWriteUtc, long LogLength, DateTime LogLastWriteUtc)
{
    /// <summary>The crawl file and its log on disk, in bytes.</summary>
    public long SizeBytes => Length + LogLength;

    /// <summary>Null when the file doesn't exist.</summary>
    public static FileStamp? Of(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            return null;
        }

        var log = new FileInfo(path + "-wal");
        return log.Exists
            ? new FileStamp(file.Length, file.LastWriteTimeUtc, log.Length, log.LastWriteTimeUtc)
            : new FileStamp(file.Length, file.LastWriteTimeUtc, 0, default);
    }
}
