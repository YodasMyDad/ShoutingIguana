namespace ShoutingIguana.Engine.Analysis.Models;

/// <summary>Who usually makes a check's fix, for a reader who has to hand it on.</summary>
public enum IssueFixer
{
    /// <summary>The site's owner, in the site's editor.</summary>
    You,

    /// <summary>A developer, or the hosting company.</summary>
    Developer,

    /// <summary>Either: it depends on how the site is built.</summary>
    Either
}
