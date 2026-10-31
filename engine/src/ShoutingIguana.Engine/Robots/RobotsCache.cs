using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Fetching;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Robots;

/// <summary>
/// Fetches each origin's robots.txt once, on first contact, and shares the result with every worker.
/// Status handling follows RFC 9309: 4xx means no restrictions, while 5xx or an unreachable server means
/// a full disallow. That second case is reported as a notice so it never looks like a silent empty crawl.
/// </summary>
internal sealed class RobotsCache(
    HttpFetcher fetcher,
    HostGateRegistry gates,
    CrawlWriter writer,
    string robotsToken,
    bool respectCrawlDelay,
    ICrawlObserver observer,
    TimeProvider timeProvider,
    ILogger logger,
    CancellationToken stopping)
{
    private const int MAX_ROBOTS_BYTES = 512 * 1024;
    private const int MAX_REDIRECTS = 5;

    private readonly ConcurrentDictionary<string, Lazy<Task<RobotsTxt>>> _origins = new(StringComparer.Ordinal);

    public Task<RobotsTxt> GetAsync(Uri uri) =>
        _origins.GetOrAdd(uri.GetLeftPart(UriPartial.Authority), origin => new Lazy<Task<RobotsTxt>>(() => LoadAsync(origin))).Value;

    private async Task<RobotsTxt> LoadAsync(string origin)
    {
        var location = new Uri(origin + "/robots.txt");
        for (var hop = 0; hop <= MAX_REDIRECTS; hop++)
        {
            FetchResponse response;
            using (await gates.For(location).AcquireAsync(stopping).ConfigureAwait(false))
            {
                response = await fetcher.FetchAsync(location, FetchBody.Text, stopping, MAX_ROBOTS_BYTES).ConfigureAwait(false);
            }

            if (response.Outcome == FetchOutcome.Redirect && response.Location is { } next)
            {
                location = next;
                continue;
            }

            var robots = Interpret(origin, response);
            if (respectCrawlDelay && robots.CrawlDelaySeconds is { } delay && delay > 0)
            {
                gates.For(new Uri(origin)).RaiseMinimumInterval(TimeSpan.FromSeconds(delay));
            }

            await RecordAsync(origin, response.StatusCode, response.Body, response.Error).ConfigureAwait(false);
            logger.LogInformation("robots.txt for {Origin}: HTTP {Status}", origin, response.StatusCode);
            return robots;
        }

        // RFC 9309 lets crawlers treat a robots.txt behind too many redirects as unavailable.
        await RecordAsync(origin, null, null, $"More than {MAX_REDIRECTS} redirects").ConfigureAwait(false);
        return RobotsTxt.AllowAll;
    }

    private RobotsTxt Interpret(string origin, FetchResponse response)
    {
        if (response.Outcome == FetchOutcome.Success)
        {
            return RobotsTxtParser.Parse(response.Body ?? string.Empty, robotsToken);
        }

        if (response.StatusCode is >= 400 and < 500)
        {
            return RobotsTxt.AllowAll;
        }

        var reason = response.StatusCode is { } status ? $"HTTP {status}" : response.Error ?? response.Outcome.ToString();
        observer.OnNotice(new CrawlNotice(
            CrawlNoticeLevel.Warning,
            "robots-unreachable",
            $"robots.txt for {origin} could not be read ({reason}). The site is treated as fully disallowed, as RFC 9309 requires. Re-run with robots.txt ignored to crawl it anyway."));
        return RobotsTxt.DisallowAll;
    }

    private ValueTask RecordAsync(string origin, int? statusCode, string? content, string? error) =>
        writer.RecordRobotsAsync(
            new RobotsRecord(origin, statusCode, content, error, timeProvider.GetUtcNow().ToUnixTimeMilliseconds()),
            stopping);
}
