using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Engine.Analysis;

public static class IssueCategoryExtensions
{
    public static string DisplayName(this IssueCategory category) => category switch
    {
        IssueCategory.ResponseCodes => "Response codes",
        IssueCategory.MetaDescriptions => "Meta descriptions",
        IssueCategory.StructuredData => "Structured data",
        IssueCategory.Urls => "URLs",
        IssueCategory.Html => "HTML",
        _ => category.ToString()
    };
}
