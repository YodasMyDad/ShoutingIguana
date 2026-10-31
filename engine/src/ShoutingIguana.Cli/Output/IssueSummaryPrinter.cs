using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Cli.Output;

/// <summary>Prints issue counts grouped by category, worst first. Checks that found nothing are left out.</summary>
internal static class IssueSummaryPrinter
{
    public static void Print(IReadOnlyList<IssueCount> issues)
    {
        var found = issues.Where(issue => issue.Count > 0).ToList();
        var bySeverity = found.GroupBy(issue => issue.Issue.Severity).ToDictionary(group => group.Key, group => group.Sum(issue => issue.Count));
        Console.WriteLine($"  {bySeverity.GetValueOrDefault(IssueSeverity.Error):N0} errors, " +
                          $"{bySeverity.GetValueOrDefault(IssueSeverity.Warning):N0} warnings, " +
                          $"{bySeverity.GetValueOrDefault(IssueSeverity.Notice):N0} notices " +
                          $"({found.Count} of {issues.Count} checks found something)");

        foreach (var category in found.GroupBy(issue => issue.Issue.Category).OrderBy(group => group.Key))
        {
            Console.WriteLine();
            Console.WriteLine($"  {category.Key.DisplayName()}");
            foreach (var issue in category.OrderBy(issue => issue.Issue.Severity).ThenByDescending(issue => issue.Count))
            {
                Console.WriteLine($"    {issue.Issue.Severity,-8} {issue.Issue.Title,-46} {issue.Count,10:N0}   {issue.Issue.Id}");
            }
        }

        if (found.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("  List the URLs for one issue with: si issues <crawl file> <issue id>");
        }
    }
}
