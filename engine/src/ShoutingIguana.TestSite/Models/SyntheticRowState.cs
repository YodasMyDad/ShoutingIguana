namespace ShoutingIguana.TestSite.Models;

/// <summary>
/// How one URL a check flagged in either crawl moved. The first four are a row flagged now, the last three one flagged
/// only then. The names are the engine's comparison's, so tests match them by name.
/// </summary>
public enum SyntheticRowState
{
    /// <summary>Flagged then and now.</summary>
    Still,

    /// <summary>Flagged now; the crawl then judged the URL and didn't flag it.</summary>
    New,

    /// <summary>Flagged now; the crawl then didn't have the URL.</summary>
    NewUrl,

    /// <summary>Flagged now; the crawl then had the URL and couldn't judge it, or the check didn't run.</summary>
    NotComparedThen,

    /// <summary>Flagged then; judged now and not flagged.</summary>
    Fixed,

    /// <summary>Flagged then; the crawl now doesn't have the URL: nothing leads to it any more.</summary>
    Gone,

    /// <summary>Flagged then; the crawl now couldn't judge it, or the check didn't run. Never fixed.</summary>
    NotChecked
}
