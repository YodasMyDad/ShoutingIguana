namespace ShoutingIguana.Engine.Fetching.Models;

/// <summary>Why a host that blocks plain HTTP is still blocked after the browser fallback, for the crawl's alert.</summary>
internal enum FallbackVerdict
{
    /// <summary>The fallback hasn't tried the host, or read it.</summary>
    None,

    /// <summary>The installed browser was turned off for this crawl, and the headless one was blocked too.</summary>
    InstalledBrowserOff,

    /// <summary>This computer can't show a window, so no browser a person would use could be opened.</summary>
    NoDisplay,

    /// <summary>No Chrome, Edge or Chromium was found, and one couldn't be downloaded or started.</summary>
    NoBrowser,

    /// <summary>The site's check needs a person, and this crawl was not told to ask one.</summary>
    NeedsPerson,

    /// <summary>The window was shown, and the check was not completed in the time it was given.</summary>
    PersonDidNotFinish,

    /// <summary>The window was shown, and it was closed before the check was completed.</summary>
    PersonClosedWindow,

    /// <summary>The person completed the check, and the site then refused the page all the same.</summary>
    RefusedAfterCheck,

    /// <summary>The check passed, and the page's script then left for another address, which is not this URL's answer.</summary>
    WentElsewhere,

    /// <summary>A real browser was refused too, with no check to pass.</summary>
    StillBlocked
}
