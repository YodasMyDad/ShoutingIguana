using System.Collections.Concurrent;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Fetching;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Robots;
using ShoutingIguana.Engine.Serialization;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Crawling;

/// <summary>
/// Handles one URL from claim to record: robots check, polite fetch with retries, a single parse, and
/// the record the writer stores. Nothing here touches the database, so many of these run in parallel.
/// </summary>
internal sealed class PageProcessor(
    CrawlOptions options,
    ResolvedUserAgent userAgent,
    HttpFetcher fetcher,
    HostGateRegistry gates,
    RobotsCache robots,
    OutLinkFactory links,
    ICrawlObserver observer,
    TimeProvider timeProvider)
{
    private const int FORBIDDEN_SAMPLE_SIZE = 20;
    private static readonly TimeSpan MAX_RETRY_WAIT = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<string, bool> _blockedHostsReported = new(StringComparer.Ordinal);
    private int _internalFetches;
    private int _internalForbidden;
    private int _forbiddenReported;

    public async Task<PageRecord> ProcessAsync(FrontierItem item, CancellationToken cancellationToken)
    {
        var uri = new Uri(item.Url);
        var fetchedAt = timeProvider.GetUtcNow().ToUnixTimeMilliseconds();

        if (options.RespectRobotsTxt)
        {
            var rules = await robots.GetAsync(uri).WaitAsync(cancellationToken).ConfigureAwait(false);
            if (!rules.IsAllowed(uri.PathAndQuery))
            {
                return Failed(item, fetchedAt, FetchOutcome.RobotsDisallowed, null);
            }
        }

        var response = await FetchWithRetriesAsync(uri, item, cancellationToken).ConfigureAwait(false);
        if (item.IsInternal)
        {
            WatchForBlocking(uri, response);
        }

        return Build(item, uri, response, fetchedAt);
    }

    /// <summary>A record for a URL that produced no usable response.</summary>
    public static PageRecord Failed(FrontierItem item, long fetchedAt, FetchOutcome outcome, string? error)
    {
        var row = new ResponseRow
        {
            UrlId = item.Id,
            FetchedAt = fetchedAt,
            Outcome = outcome,
            Error = error,
            Indexability = Indexability.NonIndexable,
            IndexabilityReason = outcome == FetchOutcome.RobotsDisallowed ? IndexabilityReason.BlockedByRobots : IndexabilityReason.Unreachable
        };

        return new PageRecord(item, row, [], false, null, null);
    }

    private async Task<FetchResponse> FetchWithRetriesAsync(Uri uri, FrontierItem item, CancellationToken cancellationToken)
    {
        var gate = gates.For(uri);
        var body = item.Mode == FetchMode.Crawl ? FetchBody.Html : FetchBody.None;

        for (var attempt = 0; ; attempt++)
        {
            FetchResponse response;
            using (await gate.AcquireAsync(cancellationToken).ConfigureAwait(false))
            {
                response = await fetcher.FetchAsync(uri, body, cancellationToken).ConfigureAwait(false);
            }

            gate.Report(SignalFor(response), response.RetryAfter);
            if (attempt >= options.MaxRetries || !IsRetryable(response))
            {
                return response;
            }

            await Task.Delay(RetryDelay(attempt, response.RetryAfter), timeProvider, cancellationToken).ConfigureAwait(false);
        }
    }

    private PageRecord Build(FrontierItem item, Uri uri, FetchResponse response, long fetchedAt)
    {
        ParsedPage? parsed = null;
        if (response.Outcome == FetchOutcome.Success && item.IsInternal)
        {
            parsed = response is { IsHtml: true, Body: { } document }
                ? HtmlPageParser.Parse(document, uri, response.Headers, userAgent.RobotsToken)
                : HtmlPageParser.ParseHeaders(uri, response.Headers, userAgent.RobotsToken);
        }

        List<OutLink> outLinks = [];
        if (response is { Outcome: FetchOutcome.Redirect, Location: { } location })
        {
            outLinks.Add(links.Create(location, LinkKind.Redirect, LinkFlags.None, LinkPosition.Head, null));
        }

        if (parsed is not null && item.Mode == FetchMode.Crawl)
        {
            outLinks.AddRange(parsed.Links.Select(links.Create));
        }

        var facts = parsed?.Facts;
        var isParsedHtml = facts is not null && response.IsHtml && response.Body is not null;
        var reason = IndexabilityOf(item.Url, response.Outcome, facts);
        var h1s = facts?.Headings.Where(heading => heading.Level == 1).ToList() ?? [];

        var row = new ResponseRow
        {
            UrlId = item.Id,
            FetchedAt = fetchedAt,
            Outcome = response.Outcome,
            StatusCode = response.StatusCode,
            ContentType = response.ContentType,
            SizeBytes = response.TransferBytes ?? response.ContentLength,
            BodyBytes = response.BodyBytes,
            TimeToFirstByteMs = response.TimeToFirstByteMs,
            ResponseMs = response.TotalMs,
            HttpVersion = response.HttpVersion,
            BlockReason = response.BlockReason,
            Error = response.Error,
            IsHtml = response.IsHtml,
            Title = facts?.Titles.FirstOrDefault(),
            TitleCount = facts?.Titles.Count ?? 0,
            MetaDescription = facts?.MetaDescriptions.FirstOrDefault(),
            MetaDescriptionCount = facts?.MetaDescriptions.Count ?? 0,
            H1 = h1s.FirstOrDefault()?.Text,
            H1Count = h1s.Count,
            IsNoindex = facts?.Robots.NoIndex ?? false,
            IsNofollow = facts?.Robots.NoFollow ?? false,
            Indexability = reason == IndexabilityReason.None ? Indexability.Indexable : Indexability.NonIndexable,
            IndexabilityReason = reason,
            WordCount = isParsedHtml ? facts!.WordCount : null,
            ContentHash = isParsedHtml ? facts!.ContentHash : null,
            SimHash = isParsedHtml ? facts!.SimHash : null,
            Language = facts?.Language
        };

        var details = item.IsInternal ? BlobCodec.EncodeDetails(new ResponseDetails(response.Headers, facts)) : null;
        var html = options.StoreHtml && isParsedHtml ? BlobCodec.CompressText(response.Body!) : null;
        return new PageRecord(item, row, outLinks, facts?.Robots.NoFollow ?? false, details, html);
    }

    private static IndexabilityReason IndexabilityOf(string url, FetchOutcome outcome, PageFacts? facts) => outcome switch
    {
        FetchOutcome.Redirect => IndexabilityReason.Redirected,
        FetchOutcome.ClientError => IndexabilityReason.ClientError,
        FetchOutcome.ServerError => IndexabilityReason.ServerError,
        FetchOutcome.Blocked => IndexabilityReason.Blocked,
        FetchOutcome.RobotsDisallowed => IndexabilityReason.BlockedByRobots,
        FetchOutcome.Timeout or FetchOutcome.NetworkError or FetchOutcome.ProcessingError => IndexabilityReason.Unreachable,
        _ when facts?.Robots.NoIndex == true => IndexabilityReason.Noindex,
        _ when CanonicalisedElsewhere(url, facts) => IndexabilityReason.Canonicalised,
        _ => IndexabilityReason.None
    };

    /// <summary>Only canonicals Google honours count: in the head or in an HTTP header.</summary>
    private static bool CanonicalisedElsewhere(string url, PageFacts? facts) =>
        facts?.Canonicals.FirstOrDefault(canonical => canonical.Url is not null && (canonical.InHead || canonical.FromHttpHeader))
            is { Url: { } target } && target != url;

    private void WatchForBlocking(Uri uri, FetchResponse response)
    {
        if (response is { Outcome: FetchOutcome.Blocked, BlockReason: { } reason } &&
            _blockedHostsReported.TryAdd(UrlNormalizer.GetHost(uri), true))
        {
            observer.OnNotice(new CrawlNotice(
                CrawlNoticeLevel.Warning,
                "blocked",
                $"{uri.Host} is blocking the crawler ({reason}). Try a slower crawl rate or a different user agent, or ask the site owner to allowlist the crawler."));
        }

        var fetches = Interlocked.Increment(ref _internalFetches);
        var forbidden = response.StatusCode == 403 ? Interlocked.Increment(ref _internalForbidden) : Volatile.Read(ref _internalForbidden);
        if (fetches == FORBIDDEN_SAMPLE_SIZE && forbidden * 2 >= fetches && Interlocked.Exchange(ref _forbiddenReported, 1) == 0)
        {
            observer.OnNotice(new CrawlNotice(
                CrawlNoticeLevel.Warning,
                "mostly-forbidden",
                $"{forbidden} of the first {fetches} pages returned 403 Forbidden. The site may be blocking crawlers."));
        }
    }

    private static HostSignal SignalFor(FetchResponse response) => response switch
    {
        { Outcome: FetchOutcome.Blocked } or { StatusCode: 429 or 503 } => HostSignal.Throttled,
        { Outcome: FetchOutcome.Timeout or FetchOutcome.NetworkError } or { StatusCode: 502 or 504 } => HostSignal.Failed,
        _ => HostSignal.Success
    };

    private static bool IsRetryable(FetchResponse response) =>
        response.IsTransientFailure || response.StatusCode is 429 or 502 or 503 or 504;

    private static TimeSpan RetryDelay(int attempt, TimeSpan? retryAfter)
    {
        if (retryAfter is { } wait && wait > TimeSpan.Zero)
        {
            return wait < MAX_RETRY_WAIT ? wait : MAX_RETRY_WAIT;
        }

        return TimeSpan.FromSeconds(Math.Pow(2, attempt)) + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 250));
    }
}
