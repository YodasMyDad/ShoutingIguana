using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Sidecar.Errors;

namespace ShoutingIguana.Sidecar.Library.Models;

/// <summary>A crawl file's summary as last read, or why it couldn't be. A null stamp means the file isn't there.</summary>
internal sealed record CachedSummary(FileStamp? Stamp, CrawlFileSummary? Summary, Exception? Error)
{
    public static CachedSummary Missing { get; } = new(null, null, null);

    public bool Exists => Stamp is not null;

    /// <summary>The summary has its counts: it wasn't read for a crawl still running here.</summary>
    public bool HasCounts => Summary is { FetchedUrlCount: not null, PageCount: not null };

    /// <summary>The file isn't a crawl this engine reads, so the library doesn't list it.</summary>
    public bool IsNotACrawl => Error is not null && EngineErrors.IsNotACrawl(Error);

    /// <summary>The file couldn't be read (too slow, damaged, locked), but may well be a crawl: listed as unreadable.</summary>
    public bool IsUnreadable => Error is not null && !EngineErrors.IsNotACrawl(Error);
}
