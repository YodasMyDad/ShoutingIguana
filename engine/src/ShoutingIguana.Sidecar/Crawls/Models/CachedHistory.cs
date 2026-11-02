using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Sidecar.Library.Models;

namespace ShoutingIguana.Sidecar.Crawls.Models;

/// <summary>A crawl's history as counted back from its file when the file had <paramref name="Stamp"/>.</summary>
/// <param name="LastUse">When it was last read or served, in the cache's own count: the least recent goes first.</param>
internal sealed record CachedHistory(FileStamp Stamp, CrawlHistory History, long LastUse);
