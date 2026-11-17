using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Storage;

namespace ShoutingIguana.Engine.Tests.Analysis;

/// <summary>What each check says it shows (step 3b): columns a check's rows can have, and fields a target has.</summary>
public sealed class EvidenceCatalogTests
{
    /// <summary>The fields of a target (and of a group's related URL) a check can name, as the API gives them.</summary>
    private static readonly string[] TARGET_FIELDS =
    [
        "statusCode", "final", "indexability", "canonical", "sizeBytes", "contentType", "pixelWidth", "pixelHeight", "responseMs", "redirectKind", "img",
        "kind", "text", "position", "fragment", "flags", "links"
    ];

    [Fact]
    public void EveryCheckShowsColumnsItsRowsHaveAndFactsItsTargetsHave()
    {
        var columns = UrlGridReader.IssueRowColumns.Select(column => column.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var check in IssueCatalog.All)
        {
            Assert.All(check.Evidence, column => Assert.Contains(column, columns));
            Assert.All(check.TargetFacts, fact => Assert.Contains(fact, TARGET_FIELDS));
            Assert.Equal(check.Evidence.Count, check.Evidence.Distinct().Count());
            Assert.Equal(check.TargetFacts.Count, check.TargetFacts.Distinct().Count());

            // A check names URLs when its pages hold them, or a group is a URL.
            Assert.True(check.TargetFacts.Count == 0 || check.References == IssueReferences.Targets || check.Grouping is IssueGrouping.RelatedUrl or IssueGrouping.ResponseDetail,
                check.Id);
        }

        Assert.Equal(43, IssueCatalog.All.Count(check => check.Evidence.Count > 0));
        Assert.Equal(34, IssueCatalog.All.Count(check => check.TargetFacts.Count > 0));
        Assert.All(IssueCatalog.All.Where(check => check.References == IssueReferences.Targets), check => Assert.NotEmpty(check.TargetFacts));
    }

    [Fact]
    public void TheImageChecksAreGroupedByImage()
    {
        foreach (var check in new[] { IssueCatalog.IMAGE_MISSING_ALT, IssueCatalog.IMAGE_ALT_TOO_LONG, IssueCatalog.IMAGE_MISSING_DIMENSIONS })
        {
            Assert.Equal((IssueReferences.Targets, IssueGrouping.RelatedUrl), (check.References, check.Grouping));
        }
    }

    [Fact]
    public void ACheckThatDoesntGroupIsATemplateFaultOnNearlyEveryPage()
    {
        Assert.True(IssueGroupReader.IsLikelyTemplateCheck(IssueCatalog.VIEWPORT_MISSING, 90, 100));
        Assert.False(IssueGroupReader.IsLikelyTemplateCheck(IssueCatalog.VIEWPORT_MISSING, 89, 100));
        Assert.False(IssueGroupReader.IsLikelyTemplateCheck(IssueCatalog.VIEWPORT_MISSING, 9, 9));

        // Not a grouped check (its groups say it), a check of any URL, or one reported once for the whole site.
        Assert.False(IssueGroupReader.IsLikelyTemplateCheck(IssueCatalog.IMAGE_MISSING_ALT, 100, 100));
        Assert.False(IssueGroupReader.IsLikelyTemplateCheck(IssueCatalog.REDIRECT_LOOP, 100, 100));
        Assert.False(IssueGroupReader.IsLikelyTemplateCheck(IssueCatalog.SECURITY_HEADERS_MISSING, 100, 100));
    }
}
