using ShoutingIguana.Engine.Export.Models;
using ShoutingIguana.Engine.Export.Tables;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Export;

/// <summary>
/// The two lists that aren't grids of URLs or links: the sitemaps the crawl looked for, and the origins it asked for a
/// robots.txt. Each is written as its screen shows it, under the screen's column titles and in its cells' words, in
/// the order its read gives it (<see cref="SitemapListReader.Read"/>, <see cref="RobotsReader.ReadOrigins(string, RobotsOriginQuery, ReadCache?, CancellationToken)"/>).
/// Each is small (a crawl reads at most 1,000 sitemaps, and asks some thousands of origins at most), so it's read
/// whole from one snapshot of the file: a running crawl's file is of one moment of it, and no crawl can change under it.
/// </summary>
internal static class ListExport
{
    /// <summary>The sitemaps' columns: the list's own, then how each was found and what became of it, then the counts the screens show.</summary>
    public static readonly string[] SITEMAP_TITLES = ["Sitemap", "Index", "Listed by", "Found via", "Status", "Error", .. SitemapListWords.COUNT_TITLES];

    /// <summary>The origins' columns: the list's own, with what the answer was beside what it meant.</summary>
    public static readonly string[] ORIGIN_TITLES = ["Origin", "This site", "robots.txt", "Status", "Error", "Blocked URLs", "Read", "Size (bytes)"];

    /// <summary>Every sitemap the crawl looked for, in the order it found them, each with the index that lists it.</summary>
    public static void WriteSitemaps(ExportRun run, ITableWriter table)
    {
        run.Token.ThrowIfCancellationRequested();
        var (list, live) = SitemapListReader.ReadWithMoment(run.CrawlPath, run.Settings.Cache, run.Token);
        Begin(run, table, "Sitemaps", SITEMAP_TITLES, list.Sitemaps.Count, live);
        var urls = list.Sitemaps.ToDictionary(sitemap => sitemap.Id, sitemap => sitemap.Url);
        var cells = new object?[SITEMAP_TITLES.Length];
        foreach (var sitemap in list.Sitemaps)
        {
            var subtree = SitemapListWords.SubtreeOf(list.Sitemaps, sitemap.Id, list.Discovery);
            cells[0] = sitemap.Url;
            cells[1] = sitemap.IsIndex;
            cells[2] = sitemap.ParentId is { } parent ? urls.GetValueOrDefault(parent) : null;
            cells[3] = SitemapListWords.FoundVia(sitemap.FoundVia);
            cells[4] = SitemapListWords.State(sitemap, live, subtree);
            cells[5] = sitemap.Error;
            var counts = SitemapListWords.Counts(sitemap, subtree);
            for (var index = 0; index < SitemapListWords.COUNT_TITLES.Length; index++)
            {
                cells[6 + index] = counts?[index];
            }

            table.WriteRow(cells);
        }

        run.Wrote(list.Sitemaps.Count);
        table.End();
    }

    /// <summary>The origins a search and the filter match (every origin with neither), in the list's order.</summary>
    public static void WriteOrigins(ExportRun run, ITableWriter table)
    {
        run.Token.ThrowIfCancellationRequested();
        var query = new RobotsOriginQuery { Search = run.Request.Query.Search, Blocked = run.Request.Blocked };
        var (page, live) = RobotsReader.ReadEveryOrigin(run.CrawlPath, query, run.Settings.Cache, run.Token);
        Begin(run, table, "robots.txt", ORIGIN_TITLES, page.Origins.Count, live);
        var zone = run.Settings.Clock.LocalTimeZone;
        var cells = new object?[ORIGIN_TITLES.Length];
        foreach (var origin in page.Origins)
        {
            cells[0] = origin.Origin;
            cells[1] = origin.IsInternal;
            cells[2] = RobotsOriginWords.State(origin, page.Respected);
            cells[3] = origin.StatusCode is { } status ? (long)status : null;
            cells[4] = origin.Error;
            cells[5] = origin.Blocked;
            cells[6] = origin.FetchedAt is { } read ? TimeZoneInfo.ConvertTime(read, zone).DateTime : null;
            cells[7] = origin.Bytes;
            table.WriteRow(cells);
        }

        run.Wrote(page.Origins.Count);
        table.End();
    }

    private static void Begin(ExportRun run, ITableWriter table, string name, IReadOnlyList<string> titles, long rows, bool live)
    {
        run.CrawlWasRunning = live;
        run.Expect(rows);
        table.Begin(name, titles);
    }
}
