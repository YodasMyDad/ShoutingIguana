using ShoutingIguana.Engine.Configuration.Models;

namespace ShoutingIguana.Engine.Crawling.Models;

/// <param name="DatabasePath">The crawl's SQLite file. A new crawl needs a path that does not hold a crawl yet.</param>
/// <param name="Resume">Continue the crawl already stored at <paramref name="DatabasePath"/>.</param>
public sealed record CrawlRequest(CrawlOptions Options, string DatabasePath, bool Resume = false);
