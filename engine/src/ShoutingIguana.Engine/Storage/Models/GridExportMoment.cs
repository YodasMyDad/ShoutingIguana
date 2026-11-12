namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// The crawl as a page of an export found it: what tells one state of it from the next, whether it was running, and the
/// highest URL and link ids it had. An export keeps its first page's: a later page whose <see cref="Stamp"/> differs was
/// read from a crawl that has changed since (analysed again, continued, finished), and the ids are the bounds of what a
/// running crawl had when the export began.
/// </summary>
/// <param name="Stamp">The crawl row's stamp, the last analysis and how the grid reads, as text: equal while the crawl stands as it did.</param>
/// <param name="Live">The crawl is running, so it gains URLs and links from one page to the next.</param>
internal sealed record GridExportMoment(string Stamp, bool Live, long MaxUrlId, long MaxLinkId);
