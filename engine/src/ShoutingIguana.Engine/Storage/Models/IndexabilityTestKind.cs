namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>The four tests a URL passes to be indexable, in the order they decide it.</summary>
public enum IndexabilityTestKind
{
    /// <summary>It answered 2xx and doesn't move on with an instant meta refresh.</summary>
    Answered,

    /// <summary>robots.txt allowed the request.</summary>
    RobotsTxt,

    /// <summary>No noindex in a robots meta tag or the X-Robots-Tag header.</summary>
    Noindex,

    /// <summary>No canonical, one to itself, or one that leads back to it or nowhere.</summary>
    Canonical
}
