using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Sidecar.Library.Models;

/// <summary>A crawl file's summary as read, kept across launches for as long as the file's stamp stays the same.</summary>
internal sealed record PersistedSummary(string Id, string Path, FileStamp Stamp, CrawlFileSummary Summary);
