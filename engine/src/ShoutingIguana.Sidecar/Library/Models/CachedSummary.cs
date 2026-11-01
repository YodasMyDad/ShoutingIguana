using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Sidecar.Library.Models;

/// <summary>A crawl file's summary as last read, or why it couldn't be. A null stamp means the file isn't there.</summary>
internal sealed record CachedSummary(FileStamp? Stamp, CrawlFileSummary? Summary, Exception? Error)
{
    public static CachedSummary Missing { get; } = new(null, null, null);

    public bool Exists => Stamp is not null;
}
