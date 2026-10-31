using System.CommandLine;
using ShoutingIguana.Cli.Output;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Storage;

namespace ShoutingIguana.Cli.Commands;

internal static class IssuesCommand
{
    public static Command Create()
    {
        var database = new Argument<string>("db") { Description = "A .sicrawl file." };
        var issueId = new Argument<string?>("issue") { Description = "An issue id, e.g. title-too-long, to list its URLs.", Arity = ArgumentArity.ZeroOrOne };
        var limit = new Option<int>("--limit") { Description = "URLs to list for one issue.", DefaultValueFactory = _ => 50 };
        var csv = new Option<string?>("--csv") { Description = "Write the flagged URLs (all issues, or the one given) to this CSV file." };
        var command = new Command("issues", "Show what the last analysis found.") { database, issueId, limit, csv };

        command.SetAction(parse =>
        {
            var path = parse.GetValue(database)!;
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"No crawl file at {path}.");
                return 2;
            }

            var analysis = IssueReader.ReadAnalysis(path);
            if (analysis is null)
            {
                Console.Error.WriteLine($"This crawl hasn't been analysed yet. Run: si analyze \"{path}\"");
                return 2;
            }

            var id = parse.GetValue(issueId);
            if (id is not null && IssueCatalog.Find(id) is null)
            {
                Console.Error.WriteLine($"Unknown issue '{id}'. Run si issues \"{path}\" to see the ids.");
                return 2;
            }

            if (parse.GetValue(csv) is { } csvPath)
            {
                var written = ExportCsv(path, id, csvPath);
                Console.WriteLine($"Wrote {written:N0} rows to {Path.GetFullPath(csvPath)}");
                return 0;
            }

            Console.WriteLine($"Analysed {analysis.AnalyzedAt.ToLocalTime():g} by engine {analysis.EngineVersion}: {analysis.PagesAnalyzed:N0} pages");
            if (id is null)
            {
                var counts = IssueReader.ReadCounts(path);
                IssueSummaryPrinter.Print(IssueCatalog.All.Select(issue => new IssueCount(issue, counts.GetValueOrDefault(issue.Id))).ToList());
                return 0;
            }

            PrintIssue(path, IssueCatalog.Find(id)!, parse.GetValue(limit));
            return 0;
        });

        return command;
    }

    private static void PrintIssue(string path, IssueDefinition issue, int limit)
    {
        var total = IssueReader.ReadCounts(path).GetValueOrDefault(issue.Id);
        Console.WriteLine();
        Console.WriteLine($"  {issue.Severity}: {issue.Title} ({total:N0} URLs)");
        Console.WriteLine($"  {issue.Description}");
        Console.WriteLine($"  Fix: {issue.Fix}");
        Console.WriteLine();
        foreach (var row in IssueReader.ReadIssues(path, issue.Id, Math.Max(1, limit)))
        {
            var detail = row.Detail is null ? string.Empty : $"  {row.Detail}";
            var related = row.RelatedUrl is null ? string.Empty : $"  -> {row.RelatedUrl}";
            Console.WriteLine($"  {row.Url}{detail}{related}");
        }

        if (total > limit)
        {
            Console.WriteLine($"  ... and {total - limit:N0} more (use --limit or --csv)");
        }
    }

    private static long ExportCsv(string path, string? issueId, string csvPath)
    {
        var csv = CsvWriter.Create(csvPath);
        try
        {
            csv.WriteRow("issue", "severity", "category", "title", "url", "detail", "related_url");
            long rows = 0;
            foreach (var row in IssueReader.ReadAll(path).Where(row => issueId is null || row.Issue == issueId))
            {
                var issue = IssueCatalog.Find(row.Issue);
                csv.WriteRow(row.Issue, issue?.Severity.ToString(), issue?.Category.DisplayName(), issue?.Title, row.Url, row.Detail, row.RelatedUrl);
                rows++;
            }

            return rows;
        }
        finally
        {
            csv.Close();
        }
    }
}
