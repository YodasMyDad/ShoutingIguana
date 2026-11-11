using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Engine.Tests.Analysis;

/// <summary>
/// The order to fix checks in: the most severe; within a severity a fault of the whole site first; then the most URLs
/// flagged; then catalog order. Where no check is site-wide it is the order the app's "Fix first" has always had
/// (app/src/screens/issues/checkList.ts, <c>rankChecksToFix</c>), whose cases the first three here are.
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

    /// <summary>
    /// A fault of the whole site is one issue, so by its count it would come last of its severity, and on a site with five
    /// other serious checks it would be outside the first five to fix. It leads its severity however small its count.
    /// </summary>
    [Fact]
    public void AFaultOfTheWholeSiteLeadsItsSeverityHoweverSmallItsCount()
    {
        Dictionary<string, long> counts = new()
        {
            ["broken-internal-links"] = 700,
            ["title-missing"] = 600,
            ["image-broken"] = 500,
            ["mixed-content"] = 400,
            ["redirect-loop"] = 300,
            ["internal-client-error"] = 200,
            ["no-https"] = 1,
            ["title-duplicate"] = 100,
            ["duplicate-site-version"] = 1,
            ["empty-anchor-text"] = 900
        };

        Assert.Equal(
        [
            "no-https", "broken-internal-links", "title-missing", "image-broken", "mixed-content", "redirect-loop", "internal-client-error",
            // The same among the warnings; a notice on 900 pages is still last.
            "duplicate-site-version", "title-duplicate",
            "empty-anchor-text"
        ], FixFirstOrder.Of(counts));

        // So it is among the first five problems, which the score takes away to say what fixing them would leave.
        var score = ScoreCalculator.Calculate([.. counts.Select(count => new IssueCount(IssueCatalog.Find(count.Key)!, count.Value))], 0, 1_000, 5_000, null, 0)!;
        Assert.Equal(["no-https", "broken-internal-links", "title-missing", "image-broken", "mixed-content"], score.IfFixed.CheckIds);
        Assert.Equal("no-https", score.Lines[0].CheckId);
    }

    /// <summary>Several faults of the whole site lead in catalog order among themselves, whatever their counts.</summary>
    [Fact]
    public void FaultsOfTheWholeSiteLeadInCatalogOrder()
    {
        Dictionary<string, long> counts = new()
        {
            ["no-https"] = 1,
            ["site-hidden-from-search"] = 1,
            ["robots-txt-unreachable"] = 1,
            ["broken-internal-links"] = 900,
            ["duplicate-site-version"] = 3,
            ["robots-txt-unreachable-other-origin"] = 1,
            ["title-duplicate"] = 800
        };

        Assert.Equal(
        [
            "robots-txt-unreachable", "site-hidden-from-search", "no-https", "broken-internal-links",
            // The duplicate site has three issues and the other origin's robots.txt one: catalog order all the same.
            "robots-txt-unreachable-other-origin", "duplicate-site-version", "title-duplicate"
        ], FixFirstOrder.Of(counts));

        // Every site-wide check that found something is before every other check of its severity.
        List<IssueCount> every = [.. IssueCatalog.All.Select((check, index) => new IssueCount(check, 1 + index % 7))];
        var ordered = FixFirstOrder.Of(every).Select(check => check.Issue).ToList();
        Assert.All(Enum.GetValues<IssueSeverity>(), severity =>
        {
            var ofSeverity = ordered.Where(check => check.Severity == severity).ToList();
            var siteWide = ofSeverity.Count(check => check.SiteWide);
            Assert.All(ofSeverity.Take(siteWide), check => Assert.True(check.SiteWide, check.Id));
        });
        Assert.Equal(5, ordered.Count(check => check.SiteWide));
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
