using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Engine.Tests.Analysis;

/// <summary>
/// The order to fix checks in, which is the app's (app/src/screens/issues/checkList.ts, <c>rankChecksToFix</c>): the most
/// severe, then the most URLs flagged, then catalog order. Its cases there are these.
/// </summary>
public sealed class FixFirstOrderTests
{
    [Fact]
    public void TheOrderIsBySeverityThenByCountThenByTheCatalog()
    {
        Dictionary<string, long> counts = new()
        {
            ["title-too-long"] = 86,
            ["empty-anchor-text"] = 265,
            ["title-duplicate"] = 86,
            ["description-duplicate"] = 86,
            ["broken-internal-links"] = 5,
            ["internal-client-error"] = 9
        };

        Assert.Equal(
        [
            "internal-client-error", "broken-internal-links",
            // Level on severity and count: catalog order, titles before meta descriptions.
            "title-duplicate", "description-duplicate",
            "empty-anchor-text", "title-too-long"
        ], FixFirstOrder.Of(counts));
    }

    /// <summary>A notice on every page never comes before a warning on one: severity leads.</summary>
    [Fact]
    public void SeverityLeadsWhateverTheCounts()
    {
        Dictionary<string, long> counts = new() { ["empty-anchor-text"] = 265, ["title-too-long"] = 86, ["title-duplicate"] = 2 };

        Assert.Equal(["title-duplicate", "empty-anchor-text", "title-too-long"], FixFirstOrder.Of(counts));
    }

    /// <summary>A check that ran and found nothing isn't in it, nor one the catalog doesn't know, which has no severity to place it by.</summary>
    [Fact]
    public void ChecksThatFoundNothingAndChecksTheCatalogDoesntKnowAreLeftOut()
    {
        Dictionary<string, long> counts = new() { ["title-missing"] = 0, ["future-check"] = 86, ["h1-missing"] = 1 };

        Assert.Equal(["h1-missing"], FixFirstOrder.Of(counts));
        Assert.Empty(FixFirstOrder.Of(new Dictionary<string, long>()));
    }

    /// <summary>The order doesn't depend on the order the checks arrive in.</summary>
    [Fact]
    public void TheOrderIsTheSameHoweverTheChecksArrive()
    {
        List<IssueCount> checks = [.. IssueCatalog.All.Select((check, index) => new IssueCount(check, 1 + index % 3))];
        var ordered = FixFirstOrder.Of(checks).Select(check => check.Issue.Id).ToList();

        Assert.Equal(ordered, FixFirstOrder.Of(Enumerable.Reverse(checks)).Select(check => check.Issue.Id));
        Assert.Equal(ordered, FixFirstOrder.Of(checks.ToDictionary(check => check.Issue.Id, check => check.Count)));
        Assert.Equal(IssueCatalog.All.Count, ordered.Count);

        // Errors, then warnings, then notices; within one severity, the larger count first.
        var severities = ordered.Select(id => IssueCatalog.Find(id)!.Severity).ToList();
        Assert.Equal(severities.Order(), severities);
    }
}
