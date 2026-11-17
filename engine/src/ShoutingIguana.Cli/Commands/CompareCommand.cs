using System.CommandLine;
using System.Text.Json;
using ShoutingIguana.Cli.Output;
using ShoutingIguana.Cli.Output.Models;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Cli.Commands;

/// <summary>
/// A crawl compared with another crawl of the site (the fix loop): each check fixed, new, fewer or more, and how its URLs
/// moved; or one check's rows and causes. The same comparison the app shows, for tests, agents and scripts.
/// </summary>
internal static class CompareCommand
{
    private const int PAGE = UrlGridReader.MAX_LIMIT;

    public static Command Create()
    {
        var database = new Argument<string>("db") { Description = "A .sicrawl file: the crawl to compare." };
        var other = new Argument<string>("other") { Description = "Another crawl of the site to compare it with, usually the one before." };
        var check = new Option<string?>("--check") { Description = "A check's id, e.g. title-too-long, to list how each of its URLs and causes moved." };
        var json = new Option<bool>("--json") { Description = "Write the comparison as JSON, for scripts and agents." };
        var command = new Command("compare", "Compare a crawl with another crawl of the site: what was fixed, what is new, and what wasn't checked.")
        {
            database, other, check, json
        };

        command.SetAction(parse => CommandErrors.Run(() =>
        {
            var now = parse.GetValue(database)!;
            var then = parse.GetValue(other)!;
            foreach (var path in new[] { now, then })
            {
                if (!File.Exists(path))
                {
                    Console.Error.WriteLine($"No crawl file at {path}.");
                    return 2;
                }

                if (IssueReader.ReadAnalysis(path) is null)
                {
                    Console.Error.WriteLine($"This crawl hasn't been analysed yet, so it can't be compared. Run: si analyze \"{path}\"");
                    return 2;
                }
            }

            var cache = new ReadCache();
            var comparison = CrawlComparisonReader.ReadSummary(now, then, cache)
                             ?? throw new InvalidOperationException("A crawl's analysis went missing while it was read.");
            var checkId = parse.GetValue(check);
            if (checkId is null)
            {
                if (parse.GetValue(json))
                {
                    Console.WriteLine(JsonSerializer.Serialize(comparison, CompareJsonContext.Default.CrawlComparison));
                }
                else
                {
                    ComparisonPrinter.Print(comparison, then, Console.Out);
                }

                return 0;
            }

            if (IssueCatalog.Find(checkId) is null && comparison.Checks.All(change => change.CheckId != checkId))
            {
                Console.Error.WriteLine($"Unknown check '{Format.Printable(checkId)}'. Run si issues \"{now}\" to see the ids.");
                return 2;
            }

            var detail = Read(now, then, comparison, checkId, cache);
            if (parse.GetValue(json))
            {
                Console.WriteLine(JsonSerializer.Serialize(detail, CompareJsonContext.Default.CheckComparison));
            }
            else
            {
                ComparisonPrinter.Print(comparison, then, checkId, detail, Console.Out);
            }

            return 0;
        }));

        return command;
    }

    /// <summary>All of one check's comparison, a page at a time from the readers: its rows now and then, and its causes.</summary>
    private static CheckComparison Read(string now, string then, CrawlComparison comparison, string checkId, ReadCache cache)
    {
        List<ComparedRow> rows = [];
        for (var offset = 0; ; offset += PAGE)
        {
            var page = IssueGroupReader.ReadRows(now, checkId, new UrlQuery { Columns = ["url", "detail"], Offset = offset, Limit = PAGE }, null, then, cache);
            if (page?.Page is not { } urls)
            {
                break;
            }

            rows.AddRange(urls.Rows.Select((row, index) => new ComparedRow(row.Id, (string)row.Values[1]!, row.Values[2] as string, page.Changes?[index])));
            if (urls.Rows.Count < PAGE)
            {
                break;
            }
        }

        List<ClearedIssue> cleared = [];
        for (var offset = 0; ; offset += PAGE)
        {
            var page = CrawlComparisonReader.ReadCleared(now, then, checkId, new ClearedQuery(Offset: offset, Limit: PAGE), cache)!;
            cleared.AddRange(page.Rows);
            if (page.Rows.Count < PAGE)
            {
                break;
            }
        }

        List<ComparedGroup> groups = [];
        List<ClearedGroup> fixedGroups = [];
        if (IssueCatalog.Find(checkId)?.Grouping is { } grouping && grouping != IssueGrouping.None)
        {
            for (var offset = 0; ; offset += PAGE)
            {
                var page = IssueGroupReader.ReadGroups(now, checkId, offset, PAGE, null, then, cache);
                groups.AddRange(page?.Groups.Select(group => new ComparedGroup(group.Detail, group.RelatedUrl, group.Urls, group.Change)) ?? []);
                if (page is null || page.Groups.Count < PAGE)
                {
                    break;
                }
            }

            for (var offset = 0; ; offset += PAGE)
            {
                var page = CrawlComparisonReader.ReadCleared(now, then, checkId, new ClearedQuery(ClearedLevel.Groups, Offset: offset, Limit: PAGE), cache)!;
                fixedGroups.AddRange(page.Groups);
                if (page.Groups.Count < PAGE)
                {
                    break;
                }
            }
        }

        return new CheckComparison(comparison.Comparable, comparison.Checks.FirstOrDefault(change => change.CheckId == checkId), rows, cleared, groups, fixedGroups);
    }
}
