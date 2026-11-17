namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>Why a recheck has a URL in its record (<c>recheck_urls.role</c>). Stored as its number.</summary>
public enum RecheckRole
{
    /// <summary>A URL whose result the recheck gives: the check's flagged URLs, the group's, or the list's.</summary>
    Judged = 0,

    /// <summary>A URL the judged pages' issues name, for a check whose pages hold the bad reference: either end may hold the fix.</summary>
    Named = 1,

    /// <summary>A page referring to a judged URL that still failed after the first fetch, for a check of what others refer to.</summary>
    Referring = 2,

    /// <summary>A URL the crawl didn't have, which a page the recheck fetched now links to: fetched one step, no further.</summary>
    New = 3,

    /// <summary>Let go: nothing the crawl still has leads to it any more.</summary>
    LetGo = 4
}
