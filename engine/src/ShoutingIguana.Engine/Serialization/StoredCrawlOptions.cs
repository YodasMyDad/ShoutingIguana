using System.Text.Json;
using ShoutingIguana.Engine.Configuration.Models;

namespace ShoutingIguana.Engine.Serialization;

/// <summary>
/// Reads the options a crawl file keeps, which say how that crawl ran. An option the file doesn't have takes the
/// engine's default (<see cref="StoredJson"/>), except one whose default has changed since crawls were stored without
/// it: that reads as what those crawls did, never as what a new crawl does now. Every read of a crawl file's options
/// goes through here, so a resumed crawl, an analysis, a comparison and "crawl again with these settings" agree.
/// </summary>
internal static class StoredCrawlOptions
{
    /// <summary>
    /// What a crawl file that lacks an option did. No crawl stored before the browser fallback was an option read a
    /// blocked host in a browser, so a file without it reads as off, though a new crawl has it on.
    /// </summary>
    public static CrawlOptions Missing(string startUrl) => new() { StartUrl = startUrl, BrowserFallback = false };

    /// <param name="startUrl">The crawl's start URL, where the caller has it. The stored options must name their own.</param>
    /// <exception cref="JsonException">The JSON isn't a crawl's options.</exception>
    public static CrawlOptions? Deserialize(string json, string startUrl = "") =>
        StoredJson.Deserialize(json, EngineJsonContext.Default.CrawlOptions, Missing(startUrl));
}
