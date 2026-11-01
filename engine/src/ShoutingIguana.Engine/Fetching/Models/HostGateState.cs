namespace ShoutingIguana.Engine.Fetching.Models;

/// <summary>What spaces a host's requests out right now, beyond the crawl's own settings.</summary>
/// <param name="CrawlDelay">The robots.txt Crawl-delay in force, when it is longer than the interval the crawl was set to.</param>
/// <param name="Backoff">The extra spacing the host's signals have asked for, as it has eased by now.</param>
/// <param name="HeldFor">How long until requests may go again, after a Retry-After or while the host doesn't answer.</param>
internal readonly record struct HostGateState(TimeSpan? CrawlDelay, TimeSpan Backoff, TimeSpan HeldFor);
