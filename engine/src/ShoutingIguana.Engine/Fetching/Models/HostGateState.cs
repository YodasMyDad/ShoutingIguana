namespace ShoutingIguana.Engine.Fetching.Models;

/// <summary>What holds a host's requests up right now, beyond the crawl's own settings.</summary>
/// <param name="CrawlDelay">The robots.txt Crawl-delay in force, when it is longer than the interval the crawl was set to.</param>
/// <param name="Backoff">The extra spacing the host's signals have asked for, as it has eased by now.</param>
/// <param name="HeldFor">How long until requests may go again, after a Retry-After or while the host doesn't answer.</param>
/// <param name="WaitingForRobotsTxt">The host's URLs waiting for their origin's robots.txt, before their first request.</param>
internal readonly record struct HostGateState(TimeSpan? CrawlDelay, TimeSpan Backoff, TimeSpan HeldFor, int WaitingForRobotsTxt);
