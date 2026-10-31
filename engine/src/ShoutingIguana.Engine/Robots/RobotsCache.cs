using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Fetching;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Robots.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Robots;

/// <summary>
/// Fetches each origin's robots.txt on first contact and shares the result with every worker. Status handling follows
/// RFC 9309 and Google: a 4xx means no restrictions; a 5xx, a 429 or no answer means a full disallow. Those failures
/// are retried like page fetches first, then asked again after a cool-down, and on the site's own origins they raise a
/// notice so they never look like a silent empty crawl. Another site that doesn't answer at all gets no verdict, so
/// links to it are fetched and show their real network error.
/// </summary>
internal sealed class RobotsCache(
    RetryingFetcher fetcher,
    HostGateRegistry gates,
    CrawlWriter writer,
    CrawlScope scope,
    CrawlOptions options,
    string robotsToken,
    ICrawlObserver observer,
    TimeProvider timeProvider,
    ILogger logger,
    CancellationToken stopping)
{
    private const int MAX_ROBOTS_BYTES = 512 * 1024;
    private const int MAX_REDIRECTS = 5;

    /// <summary>How long a robots.txt that couldn't be read keeps its verdict before the next URL asks again.</summary>
    internal static readonly TimeSpan RETRY_AFTER = TimeSpan.FromMinutes(2);

    private readonly ConcurrentDictionary<string, RobotsCacheEntry> _origins = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, bool> _reported = new(StringComparer.Ordinal);

    public Task<RobotsTxt> GetAsync(Uri uri)
    {
        var origin = uri.GetLeftPart(UriPartial.Authority);
        var entry = _origins.GetOrAdd(origin, NewEntry);
        if (entry.CanRetry(timeProvider.GetUtcNow()))
        {
            // The last attempt failed and its cool-down is over. One caller swaps in a fresh attempt; the rest share it.
            var fresh = NewEntry(origin);
            entry = _origins.TryUpdate(origin, fresh, entry) ? fresh : _origins[origin];
        }

        return entry.Load;
    }

    /// <summary>Lets every robots.txt that couldn't be read be fetched again on next use, cool-down or not.</summary>
    public void ExpireFailures()
    {
        foreach (var entry in _origins.Values.Where(entry => entry.IsFailed))
        {
            entry.RetryFrom(DateTimeOffset.MinValue);
        }
    }

    private RobotsCacheEntry NewEntry(string origin) => new(entry => LoadAsync(origin, entry));

    private async Task<RobotsTxt> LoadAsync(string origin, RobotsCacheEntry entry)
    {
        var location = new Uri(origin + "/robots.txt");
        for (var hop = 0; hop <= MAX_REDIRECTS; hop++)
        {
            var response = await fetcher.FetchAsync(location, FetchBody.Text, stopping, MAX_ROBOTS_BYTES).ConfigureAwait(false);
            if (response.Outcome == FetchOutcome.Redirect && response.Location is { } next)
            {
                location = next;
                continue;
            }

            await RecordAsync(origin, response.StatusCode, response.Body, response.Error).ConfigureAwait(false);
            logger.LogInformation("robots.txt for {Origin}: HTTP {Status}", origin, response.StatusCode);
            return Interpret(origin, response, entry);
        }

        // RFC 9309 lets crawlers treat a robots.txt behind too many redirects as unavailable.
        await RecordAsync(origin, null, null, $"More than {MAX_REDIRECTS} redirects").ConfigureAwait(false);
        return RobotsTxt.AllowAll;
    }

    private RobotsTxt Interpret(string origin, FetchResponse response, RobotsCacheEntry entry)
    {
        if (response.Outcome == FetchOutcome.Success)
        {
            var robots = RobotsTxtParser.Parse(response.Body ?? string.Empty, robotsToken);
            if (options.RespectCrawlDelay && robots.CrawlDelaySeconds is { } delay && delay > 0)
            {
                gates.For(new Uri(origin)).RaiseMinimumInterval(TimeSpan.FromSeconds(delay));
            }

            return robots;
        }

        // Any other 4xx means there are no rules. Google treats 429 like a server error, and so does the analysis.
        if (response.StatusCode is >= 400 and < 500 and not 429)
        {
            return RobotsTxt.AllowAll;
        }

        // Still unreadable after the retries: keep the verdict for a while, not for the rest of the crawl.
        entry.RetryFrom(timeProvider.GetUtcNow() + RETRY_AFTER);
        var isInternal = scope.IsInternal(new Uri(origin));
        if (response.StatusCode is null && !isInternal)
        {
            return RobotsTxt.AllowAll;
        }

        var reason = response.StatusCode is { } status ? $"HTTP {status}" : response.Error ?? response.Outcome.ToString();
        if (isInternal && options.RespectRobotsTxt && _reported.TryAdd(origin, true))
        {
            observer.OnNotice(new CrawlNotice(
                CrawlNoticeLevel.Warning,
                "robots-unreachable",
                $"robots.txt for {origin} could not be read ({reason}). Until it can, the site is treated as fully disallowed, as RFC 9309 requires; the crawler asks again every {RETRY_AFTER.TotalMinutes:0} minutes. Re-run with robots.txt ignored to crawl it anyway."));
        }

        return RobotsTxt.Unreachable(reason);
    }

    private ValueTask RecordAsync(string origin, int? statusCode, string? content, string? error) =>
        writer.RecordRobotsAsync(
            new RobotsRecord(origin, statusCode, content, error, timeProvider.GetUtcNow().ToUnixTimeMilliseconds()),
            stopping);
}
