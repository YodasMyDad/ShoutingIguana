namespace ShoutingIguana.Engine.Analysis.Models;

/// <summary>How much of the site a check's count is: the band its points go by (<see cref="ScoreCalculator.ExtentOf"/>).</summary>
public enum ScoreExtent
{
    /// <summary>A few: under 1 in 100 of the pages, or of the URLs.</summary>
    Few,

    /// <summary>Some: 1 in 100 or more.</summary>
    Some,

    /// <summary>Many: 1 in 10 or more.</summary>
    Many,

    /// <summary>Most: half or more.</summary>
    Most,

    /// <summary>The whole site: a site-wide fault (<see cref="IssueDefinition.SiteWide"/>), whatever its count.</summary>
    Site
}
