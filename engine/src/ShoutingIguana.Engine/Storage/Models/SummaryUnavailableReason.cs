namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>Why a crawl has no URL summary to read, the first that applies.</summary>
public enum SummaryUnavailableReason
{
    /// <summary>The crawl is running, in this program or another.</summary>
    Crawling,

    /// <summary>The crawl has never been analysed.</summary>
    NotAnalyzed,

    /// <summary>Another version of the engine analysed it, whose summary this one doesn't read.</summary>
    EngineChanged,

    /// <summary>The crawl was continued after its analysis, so the summary describes an earlier state of it.</summary>
    CrawlChanged
}
