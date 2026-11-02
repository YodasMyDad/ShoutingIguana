namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// What a crawl file's grid reads depend on, so a count read once can be kept: the file, the crawl's stamp, the last
/// analysis, the mode. A crawl that's running changes from one read to the next, so what it counts is kept only
/// briefly (<see cref="Live"/>).
/// </summary>
/// <param name="Live">The crawl is running: its counts are kept for <see cref="ReadCache.LIVE_FOR"/>, then counted again.</param>
internal sealed record GridStamp(string Path, long CrawlUpdatedAt, long? AnalyzedAt, GridMode Mode, bool Live = false)
{
    public override string ToString() => $"{Path}|{CrawlUpdatedAt}|{AnalyzedAt}|{Mode}{(Live ? "|live" : string.Empty)}";
}
