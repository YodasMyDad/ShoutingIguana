using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Engine.Analysis;

public static class IssueCategoryExtensions
{
    /// <summary>
    /// The category's title, a plain name such as "Meta descriptions", for every host to show as it is. Each category
    /// has its own, so a new one can't reach the UI as an identifier.
    /// </summary>
    public static string DisplayName(this IssueCategory category) => category switch
    {
        IssueCategory.ResponseCodes => "Response codes",
        IssueCategory.Redirects => "Redirects",
        IssueCategory.Links => "Links",
        IssueCategory.Indexability => "Indexability",
        IssueCategory.Canonicals => "Canonicals",
        IssueCategory.Sitemaps => "Sitemaps",
        IssueCategory.Titles => "Titles",
        IssueCategory.MetaDescriptions => "Meta descriptions",
        IssueCategory.Headings => "Headings",
        IssueCategory.Content => "Content",
        IssueCategory.Images => "Images",
        IssueCategory.Hreflang => "Hreflang",
        IssueCategory.StructuredData => "Structured data",
        IssueCategory.Security => "Security",
        IssueCategory.Urls => "URLs",
        IssueCategory.Performance => "Performance",
        IssueCategory.Html => "HTML",
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, null)
    };
}
