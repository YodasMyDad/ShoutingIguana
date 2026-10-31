using System.Reflection;
using System.Text.RegularExpressions;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Engine.Tests.Analysis;

public sealed class IssueCatalogTests
{
    [Fact]
    public void EveryDeclaredIssueIsInTheCatalogOnce()
    {
        var declared = typeof(IssueCatalog).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(IssueDefinition))
            .Select(field => (IssueDefinition)field.GetValue(null)!)
            .ToList();

        Assert.Equal(declared.Count, IssueCatalog.All.Count);
        Assert.All(declared, issue => Assert.Contains(issue, IssueCatalog.All));
        Assert.Equal(IssueCatalog.All.Count, IssueCatalog.All.Select(issue => issue.Id).Distinct().Count());
    }

    [Fact]
    public void IdsAreKebabCaseAndTextsAreFilledIn()
    {
        Assert.All(IssueCatalog.All, issue =>
        {
            Assert.Matches(new Regex("^[a-z0-9]+(-[a-z0-9]+)*$"), issue.Id);
            Assert.False(string.IsNullOrWhiteSpace(issue.Title));
            Assert.EndsWith(".", issue.Description);
            Assert.EndsWith(".", issue.Fix);
            Assert.Same(issue, IssueCatalog.Find(issue.Id));
        });
        Assert.Null(IssueCatalog.Find("no-such-issue"));
    }

    [Fact]
    public void DefaultSettingsAreValidAndBadOnesAreRejected()
    {
        Assert.Empty(new AnalysisSettings().Validate());
        Assert.NotEmpty(new AnalysisSettings { TitleMinLength = 70, TitleMaxLength = 60 }.Validate());
        Assert.NotEmpty(new AnalysisSettings { NearDuplicateMaxDistance = 9 }.Validate());
        Assert.NotEmpty(new AnalysisSettings { LargeImageBytes = 0 }.Validate());
    }
}
