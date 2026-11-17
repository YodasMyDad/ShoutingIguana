namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>What a recheck found of one URL it set out to judge (<c>recheck_urls.outcome</c>). Stored as its number.</summary>
public enum RecheckOutcome
{
    /// <summary>Read again (or let go as no longer linked), and the check no longer flags it.</summary>
    Fixed = 0,

    /// <summary>The check still flags it.</summary>
    Still = 1,

    /// <summary>The recheck couldn't judge it: no answer, an error that isn't the check's own business, a block, a rate limit, robots.txt refusing it, a redirect away, or the check skipped. Never fixed.</summary>
    NotChecked = 2,

    /// <summary>Past the cap, or the recheck was stopped before it: as the crawl found it.</summary>
    NotRechecked = 3,

    /// <summary>The check didn't flag it before, and does now.</summary>
    New = 4
}
