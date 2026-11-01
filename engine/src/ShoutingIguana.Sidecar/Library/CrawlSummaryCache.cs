using System.Collections.Concurrent;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Sidecar.Errors;
using ShoutingIguana.Sidecar.Library.Models;

namespace ShoutingIguana.Sidecar.Library;

/// <summary>
/// Each crawl file's summary, read once and kept until the file or its write-ahead log changes size or modified time,
/// so listing many crawls stays cheap. A file that can't be read is remembered too, until it changes.
/// </summary>
internal sealed class CrawlSummaryCache(ILogger<CrawlSummaryCache> logger)
{
    private readonly ConcurrentDictionary<string, CachedSummary> _summaries = new(StringComparer.Ordinal);

    public CachedSummary Get(string id, string path)
    {
        // A file that has gone keeps what was last known of it, for the library to show beside "missing".
        var stamp = FileStamp.Of(path);
        if (stamp is null)
        {
            return _summaries.TryGetValue(id, out var last) ? last with { Stamp = null } : CachedSummary.Missing;
        }

        if (_summaries.TryGetValue(id, out var cached) && cached.Stamp == stamp)
        {
            return cached;
        }

        CachedSummary read;
        try
        {
            read = new CachedSummary(stamp, CrawlSummaryReader.Read(path), null);
        }
        catch (Exception exception) when (EngineErrors.IsExpected(exception))
        {
            logger.LogDebug(exception, "Could not read the summary of {Path}", path);
            read = new CachedSummary(stamp, null, exception);
        }

        _summaries[id] = read;
        return read;
    }

    /// <summary>For changes this process made, which a modified time may be too coarse to show.</summary>
    public void Invalidate(string id) => _summaries.TryRemove(id, out _);
}
