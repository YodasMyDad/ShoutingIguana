namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// What a crawl file's grid reads depend on, so a count read once can be kept: the file, the crawl's stamp, the last
/// analysis, the mode. Null for a crawl that's running, whose counts change from one read to the next.
/// </summary>
internal sealed record GridStamp(string Path, long CrawlUpdatedAt, long? AnalyzedAt, GridMode Mode)
{
    public override string ToString() => $"{Path}|{CrawlUpdatedAt}|{AnalyzedAt}|{Mode}";
}
