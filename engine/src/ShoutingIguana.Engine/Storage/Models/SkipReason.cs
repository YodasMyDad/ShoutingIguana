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
    UrlLimit = 6,

    /// <summary>
    /// Found by a recheck (the fix loop) on a page it fetched one step from what it set out to fetch, and not followed:
    /// a full crawl would. An earlier engine reads it as a skipped URL, which it is.
    /// </summary>
    Recheck = 7,

    /// <summary>
    /// Let go by a recheck: nothing the crawl still has leads to it any more (its last link was removed), so its response,
    /// details, HTML and outgoing links were deleted. The row stays, so ids stay stable; it isn't counted as found.
    /// </summary>
    NoLongerLinked = 8
}
