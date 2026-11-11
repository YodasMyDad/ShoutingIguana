namespace ShoutingIguana.Engine.Reporting.Models;

/// <summary>How much of the site a check's count is: the band its points come from.</summary>
internal enum ScoreExtent
{
    /// <summary>Under 1 in 100.</summary>
    Few,

    /// <summary>1 in 100 or more.</summary>
    Some,

    /// <summary>1 in 10 or more.</summary>
    Many,

    /// <summary>Half or more.</summary>
    Most,

    /// <summary>A fault of the whole site.</summary>
    Site
}
