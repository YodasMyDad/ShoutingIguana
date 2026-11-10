using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Tests.Support;

namespace ShoutingIguana.Engine.Tests.Analysis;

/// <summary>
/// Checks an analysed crawl against what the catalog says each check flags (<see cref="IssueDefinition.Subject"/>): a
/// check that says pages must have flagged nothing else, as a page is counted everywhere
/// (<see cref="CrawlSchema.PAGE"/>). Every end-to-end analysis of the synthetic site runs it, so each planted check is held
/// to what it declares on every variant of the site that plants it.
/// </summary>
internal static class IssueSubjectAgreement
{
    /// <summary>Each check with the URLs it flagged that aren't pages.</summary>
    public static Dictionary<string, List<string>> FlaggedNonPages(string path) =>
        TestCrawls.Rows(path, $"""
                SELECT i.issue, u.url FROM issues i JOIN urls u ON u.id = i.url_id LEFT JOIN responses r ON r.url_id = u.id
                WHERE r.url_id IS NULL OR NOT ({CrawlSchema.PAGE})
                """)
            .Select(row => row.Split(" | ", 2))
            .GroupBy(parts => parts[0], parts => parts[1], StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

    public static void AssertPageChecksFlagOnlyPages(string path)
    {
        var nonPages = FlaggedNonPages(path);
        Assert.All(IssueCatalog.All.Where(check => check.Subject == IssueSubject.Page), check =>
            Assert.True(!nonPages.TryGetValue(check.Id, out var urls),
                $"{check.Id} says it flags pages, but flagged {string.Join(", ", (urls ?? []).Take(5))}"));
    }
}
