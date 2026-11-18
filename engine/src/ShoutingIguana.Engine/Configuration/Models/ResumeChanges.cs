namespace ShoutingIguana.Engine.Configuration.Models;

/// <summary>
/// What may change when a crawl is resumed: how it talks to the site, never its scope or rules. A null keeps the stored
/// value. Header values and proxy credentials aren't stored, so <see cref="CrawlOptions.PlanResume"/> says which
/// weren't given again.
/// </summary>
public sealed record ResumeChanges
{
    public int? Concurrency { get; init; }

    public double? MaxRequestsPerSecond { get; init; }

    public int? RequestTimeoutSeconds { get; init; }

    /// <summary>The headers to send from now on. A stored header that isn't given again isn't sent at all, not even empty.</summary>
    public IReadOnlyDictionary<string, string> ExtraHeaders { get; init; } = new Dictionary<string, string>();

    /// <summary>The crawler signatures to send from now on. A crawl file keeps none, so a stored crawl sends none unless given them.</summary>
    public IReadOnlyDictionary<string, CrawlSignature> Signatures { get; init; } = new Dictionary<string, CrawlSignature>();

    /// <summary>Replaces the stored proxy. It must be given again when the stored one had credentials.</summary>
    public string? ProxyUrl { get; init; }
}
