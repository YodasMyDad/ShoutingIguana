namespace ShoutingIguana.Sidecar.Events.Models;

/// <summary>
/// What a live crawl is doing as a whole, as a <c>progress</c> event says it: the engine's phases
/// (<see cref="Engine.Crawling.Models.CrawlPhase"/>), and <see cref="Rechecking"/> for a recheck's run (the fix loop).
/// </summary>
internal enum ProgressPhase
{
    Starting,
    ReadingRobotsTxt,
    Crawling,
    ReadingSitemaps,
    Retrying,
    Finishing,

    /// <summary>A recheck is fetching some of the crawl's URLs again.</summary>
    Rechecking
}
