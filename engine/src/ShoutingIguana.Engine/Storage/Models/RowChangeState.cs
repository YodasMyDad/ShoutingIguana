namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// How one URL a check flagged in either crawl moved, served in camelCase. The first four are a row flagged now
/// (<see cref="RowChange"/>), the last three a row flagged then and not now (<see cref="ClearedIssue"/>).
/// </summary>
public enum RowChangeState
{
    /// <summary>Flagged then and now.</summary>
    Still,

    /// <summary>Flagged now; the other crawl read the URL in a way the check judges, and didn't flag it.</summary>
    New,

    /// <summary>Flagged now; the other crawl didn't find the URL: a new page, or one newly linked.</summary>
    NewUrl,

    /// <summary>Flagged now; the other crawl found the URL but didn't read it in a way the check judges, or the check didn't run then.</summary>
    NotComparedThen,

    /// <summary>Flagged then; this crawl read the URL in a way the check judges, and didn't flag it.</summary>
    Fixed,

    /// <summary>Flagged then; this crawl didn't find the URL: nothing leads to it any more. Counted as fixed, and told apart.</summary>
    Gone,

    /// <summary>Flagged then; this crawl found it but couldn't judge it, or the check didn't run now. Never counted as fixed.</summary>
    NotChecked
}
