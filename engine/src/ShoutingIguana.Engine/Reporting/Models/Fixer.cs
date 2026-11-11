namespace ShoutingIguana.Engine.Reporting.Models;

/// <summary>Who usually makes a check's fix.</summary>
internal enum Fixer
{
    /// <summary>The site's owner, in the site's editor.</summary>
    You,

    /// <summary>A developer, or the hosting company.</summary>
    Developer,

    /// <summary>Either.</summary>
    Either
}
