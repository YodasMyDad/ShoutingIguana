namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>Why a recheck can't run, as the engine can tell from the crawl file. The sidecar adds its own (the site busy, not the latest crawl).</summary>
public enum RecheckRefusalCode
{
    /// <summary>The crawl is open in another process or run (a crawl, an analysis or a recheck).</summary>
    CrawlBusy,

    /// <summary>The crawl was never analysed, so there is nothing to recheck.</summary>
    NotAnalyzed,

    /// <summary>The crawl was interrupted (its file says running or paused): it must be continued first.</summary>
    NotAnalysable,

    /// <summary>A recheck can't settle the check (its <c>Recheck</c> is empty), or the analysis skipped it.</summary>
    Unsupported
}
