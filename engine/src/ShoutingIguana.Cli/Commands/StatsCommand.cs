using System.CommandLine;
using ShoutingIguana.Cli.Output;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Storage;

namespace ShoutingIguana.Cli.Commands;

internal static class StatsCommand
{
    public static Command Create()
    {
        var database = new Argument<string>("db") { Description = "A .sicrawl file." };
        var command = new Command("stats", "Summarise a stored crawl.") { database };

        command.SetAction(parse => CommandErrors.Run(() =>
        {
            var path = parse.GetValue(database)!;
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"No crawl file at {path}.");
                return 2;
            }

            var stats = CrawlStatsReader.Read(path);
            Console.WriteLine($"Crawl of {stats.StartUrl}");
            Console.WriteLine($"  Status        {stats.Status}  (engine {stats.EngineVersion}, started {stats.StartedAt.ToLocalTime():g})");
            Console.WriteLine($"  URLs          {stats.UrlCount:N0} known, {stats.InternalUrlCount:N0} internal, {stats.QueuedCount:N0} still queued");
            Console.WriteLine($"  Pages         {Format.Count(stats.HtmlPageCount, "HTML page", "HTML pages")}, {stats.IndexablePageCount:N0} indexable, {stats.UnlinkedPageCount:N0} only in sitemaps");
            Console.WriteLine($"  Links         {stats.LinkCount:N0}");
            var sitemapErrors = stats.SitemapErrorCount > 0 ? $", {stats.SitemapErrorCount:N0} couldn't be read" : string.Empty;
            Console.WriteLine(stats.SitemapCount + stats.SitemapErrorCount == 0
                ? "  Sitemaps      none found"
                : $"  Sitemaps      {stats.SitemapCount:N0} read, listing {Format.Count(stats.SitemapUrlCount, "URL", "URLs")}{sitemapErrors}");
            Console.WriteLine($"  Speed         {stats.AverageResponseMs:N0} ms average page response, {Format.Bytes(stats.TotalBytes)} of HTML");
            Console.WriteLine($"  Database      {Format.Bytes(new FileInfo(path).Length)}");

            Print("Outcomes", stats.Outcomes.OrderByDescending(pair => pair.Value).Select(pair => (pair.Key.ToString(), pair.Value)));
            Print("Status codes", stats.StatusCodes.OrderBy(pair => pair.Key).Select(pair => (pair.Key.ToString(), pair.Value)));
            Print("Not indexable", stats.NonIndexableReasons.OrderByDescending(pair => pair.Value).Select(pair => (pair.Key.ToString(), pair.Value)));
            Print("Not fetched", stats.SkipReasons.OrderByDescending(pair => pair.Value).Select(pair => (pair.Key.ToString(), pair.Value)));
            if (stats.NotCheckedCount > 0)
            {
                Console.WriteLine($"  Not checked   {Format.Count(stats.NotCheckedCount, "URL", "URLs")}: their site had stopped answering, or kept asking the crawler to slow down");
            }
            if (stats.UnlinkedPageCount > 0)
            {
                Console.WriteLine($"  (pages only in sitemaps have depth {CrawlDepth.UNLINKED:N0} in the urls table)");
            }

            return 0;
        }));

        return command;
    }

    private static void Print(string heading, IEnumerable<(string Label, long Count)> rows)
    {
        var list = rows.ToList();
        if (list.Count == 0)
        {
            return;
        }

        Console.WriteLine($"  {heading}");
        foreach (var (label, count) in list)
        {
            Console.WriteLine($"    {label,-22}{count,12:N0}");
        }
    }
}
