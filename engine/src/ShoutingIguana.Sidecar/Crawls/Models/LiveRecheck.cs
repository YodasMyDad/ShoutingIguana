namespace ShoutingIguana.Sidecar.Crawls.Models;

/// <summary>A recheck this process runs on a crawl (the fix loop): what its events say. <see cref="CrawlManager"/> changes it under its lock.</summary>
/// <param name="id">The recheck's number in the crawl file, as its run will store it.</param>
internal sealed class LiveRecheck(int id)
{
    public int Id { get; set; } = id;

    /// <summary>Its run has started: the analysis first, when there was one, is over.</summary>
    public bool Started { get; set; }

    /// <summary>When its last <c>recheck</c> event went out, which the next waits half a second after.</summary>
    public DateTime LastEventAt { get; set; }
}
