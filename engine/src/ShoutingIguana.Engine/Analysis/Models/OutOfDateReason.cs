namespace ShoutingIguana.Engine.Analysis.Models;

/// <summary>Why a crawl's last analysis no longer stands, from <see cref="AnalysisFreshness.Check"/>.</summary>
public enum OutOfDateReason
{
    /// <summary>The crawl changed after it was analysed: it was continued, or another crawl ran into the file.</summary>
    CrawlChanged,

    /// <summary>
    /// This engine would judge the crawl differently: its checks, or how they work, changed since the analysis. Every
    /// analysis by an engine from before the URL summary is out of date this way.
    /// </summary>
    EngineChanged,

    /// <summary>The analysis used other thresholds than the ones it would run with now.</summary>
    SettingsChanged
}
