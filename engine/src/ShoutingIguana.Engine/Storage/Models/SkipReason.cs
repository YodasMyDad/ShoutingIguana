namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>Why a discovered URL was recorded but not fetched.</summary>
public enum SkipReason : byte
{
    None = 0,
    NoFollow = 1,

    /// <summary>Canonical, hreflang or pagination target while crawling that link type is switched off.</summary>
    NotFollowedByOption = 2,
    ResourcesDisabled = 3,
    ExternalsDisabled = 4,
    DepthLimit = 5,
    UrlLimit = 6
}
