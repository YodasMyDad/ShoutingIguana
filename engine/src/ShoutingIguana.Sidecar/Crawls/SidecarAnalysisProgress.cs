using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Sidecar.Crawls;

/// <summary>
/// Hands one analysis's progress to the manager, straight from the analysis's thread, so its events go out in the order
/// it reports them (Progress&lt;T&gt; would hand each to the thread pool, which can reorder them).
/// </summary>
internal sealed class SidecarAnalysisProgress(CrawlManager manager, LiveCrawl crawl) : IProgress<AnalysisProgress>
{
    public void Report(AnalysisProgress value) => manager.OnAnalysisProgress(crawl, value);
}
