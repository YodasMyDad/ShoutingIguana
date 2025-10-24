namespace ShoutingIguana.PluginSdk;

/// <summary>
/// Service for enqueueing new URLs discovered during analysis.
/// </summary>
public interface IUrlEnqueue
{
    /// <summary>
    /// Enqueue a URL for crawling if not already crawled.
    /// </summary>
    /// <param name="url">URL to enqueue</param>
    /// <param name="depth">Depth of the URL from the start URL</param>
    /// <param name="priority">Priority (lower = higher priority)</param>
    Task EnqueueAsync(string url, int depth, int priority = 100);
}

