namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>A stretch of time a crawl spent crawling, unbroken by a pause or a stop.</summary>
/// <param name="To">When it ended; for one still going, when the engine last stored it, some seconds ago at most.</param>
public sealed record CrawlingStretch(DateTimeOffset From, DateTimeOffset To);
