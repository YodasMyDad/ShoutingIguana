using System.Collections.Concurrent;
using System.Text;
using ShoutingIguana.Engine.Analysis.Rules;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Favicons;
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
    RetryingFetcher fetcher,
    RobotsCache robots,
    OutLinkFactory links,
    ICrawlObserver observer,
    CrawlScope scope,
    HostGateRegistry gates,
    FaviconFetcher favicon,
    JavaScriptRenderer renderer,
    TimeProvider timeProvider)
{
    private const int FORBIDDEN_SAMPLE_SIZE = 20;

    /// <summary>The crawl's extraction rules, compiled once. Applied to each HTML page as it is parsed.</summary>
    private readonly ExtractionSession _extractions = CustomExtractor.Open(options.ExtractionRules);

    /// <summary>How often the alert that a Shopify store is refusing an unsigned crawl is said again, with its count, while it grows.</summary>
    internal static readonly TimeSpan SHOPIFY_COUNT_EVERY = TimeSpan.FromSeconds(30);

    // What the workers are doing, kept in one value so progress reads all three counts at once: URLs being fetched in
    // the lowest bits, the site's URLs waiting for robots.txt above them, other sites' at the top. Each count is at most
    // the crawl's workers, far below the 21 bits it has.
    private const int WORK_BITS = 21;
    private const long WORK_MASK = (1L << WORK_BITS) - 1;
    private const long FETCHING = 1;
    private const long SITE_WAITING_FOR_ROBOTS_TXT = 1L << WORK_BITS;
    private const long OTHERS_WAITING_FOR_ROBOTS_TXT = 1L << (2 * WORK_BITS);

    private readonly ConcurrentDictionary<string, bool> _blockedHostsReported = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, bool> _rateLimitedHostsReported = new(StringComparer.Ordinal);
    private int _internalFetches;
    private int _internalForbidden;
    private int _forbiddenReported;
    private int _startLeftReported;
    private long _work;

    // The first of the site's hosts that answered 429 to a request without a signature, with what was wrong with the
    // signature the crawl had for it, if it had one; the count of refused URLs last said, and when; and whether a
    // signature Shopify refused has been said.
    private readonly Lock _shopifySync = new();
    private (string Host, string? Detail)? _unsignedRefusal;
    private int _refusedSaid;
    private DateTimeOffset _refusedSaidAt;
    private int _signatureRefusedReported;
    private readonly UrlPatterns _renderPatterns = CompilePatterns(options.RenderPatterns);

    /// <summary>The site's hosts found to be a Shopify store's this run, and whether the crawl can sign its requests to one.</summary>
    public ShopifyWatch Shopify { get; } = new(new SignatureBook(options.Signatures, timeProvider));

    /// <summary>URLs waiting for their origin's robots.txt, and URLs being fetched, as one moment has them.</summary>
    public WorkInProgress Work
    {
        get
        {
            var work = Interlocked.Read(ref _work);
            return new WorkInProgress((int)((work >> WORK_BITS) & WORK_MASK), (int)(work >> (2 * WORK_BITS)), (int)(work & WORK_MASK));
        }
    }

    /// <summary>
    /// A probe of another version of the site (or where one redirects) must never hold up the crawl, so it gets a single
    /// attempt. Where its origin's robots.txt timed out it isn't sent at all: it would only wait out another timeout, so it
    /// is stored with robots.txt's failure, as a version that doesn't answer. One whose robots.txt failed at once (refused,
    /// reset, no TLS) is still asked, since that costs nothing, and a server can drop robots.txt and serve its pages.
    /// </summary>
    /// <param name="isRetry">The URL's one more try at the end of the crawl: a single attempt, as it had its retries.</param>
    public async Task<PageRecord> ProcessAsync(FrontierItem item, CancellationToken cancellationToken, bool isRetry = false)
    {
        var uri = new Uri(item.Url);
        var fetchedAt = timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        var isProbe = item.Source == DiscoverySource.SiteVersion;

        if (options.RespectRobotsTxt)
        {
            var load = robots.GetAsync(uri);
            if (!load.IsCompleted)
            {
                // Counted only while it's still being read: once it has been, every URL on the origin passes straight on.
                // The site's own are counted on their host's gate too, so progress can say whose robots.txt they wait for.
                var waiting = item.IsInternal ? SITE_WAITING_FOR_ROBOTS_TXT : OTHERS_WAITING_FOR_ROBOTS_TXT;
                var gate = item.IsInternal ? gates.For(uri) : null;
                Interlocked.Add(ref _work, waiting);
                gate?.AddRobotsTxtWaiter(1);
                try
                {
                    await load.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    gate?.AddRobotsTxtWaiter(-1);
                    Interlocked.Add(ref _work, -waiting);
                }
            }

            var rules = await load.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (isProbe && rules.NoAnswer is { Outcome: FetchOutcome.Timeout } noAnswer)
            {
                return Failed(item, fetchedAt, noAnswer.Outcome, noAnswer.Error);
            }

            if (!rules.IsAllowed(uri.PathAndQuery))
            {
                // Refused because robots.txt couldn't be read is stored apart from refused by a rule.
                var error = rules.UnreachableReason is { } reason ? $"{RobotsTxt.UNREACHABLE_ERROR}: {reason}" : null;
                return Failed(item, fetchedAt, FetchOutcome.RobotsDisallowed, error);
            }
        }

        var body = item.Mode == FetchMode.Crawl ? FetchBody.Html : FetchBody.None;
        FetchResponse response;
        Interlocked.Add(ref _work, FETCHING);
        try
        {
            // The site's own URLs asked to slow down are held back by the session for its end, not retried here.
            response = await fetcher.FetchAsync(uri, body, cancellationToken, singleAttempt: isRetry || isProbe, holdBackThrottled: item.IsInternal)
                .ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Add(ref _work, -FETCHING);
        }

        // The site's own responses say whether it's a Shopify store, a 429's as much as a page's.
        var shopifySignal = item.IsInternal ? ShopifySignals.FromHeaders(response.Headers) : null;
        string? refusalNote = null;
        if (item.IsInternal)
        {
            ObserveShopify(uri, shopifySignal);
            WatchForBlocking(uri, response);
            if (response.IsRateLimited)
            {
                refusalNote = NoteShopifyRefusal(item, uri, response);
            }
            else
            {
                response = await RenderIfNeededAsync(item, uri, response, cancellationToken).ConfigureAwait(false);
            }
        }

        // A 429 says nothing about the URL: it is stored as not checked, and the site's own get one more try at the end.
        return response.IsRateLimited
            ? Failed(item, fetchedAt, FetchOutcome.NetworkError, $"{RetryingFetcher.RATE_LIMITED_ERROR} ({HttpStatusNames.Describe(429)})", refusalNote)
                with { ShopifySignal = shopifySignal }
            : Build(item, uri, response, fetchedAt, shopifySignal);
    }

    /// <summary>A record for a URL that produced no usable response.</summary>
    /// <param name="refusalNote">For a 429: what the crawl's signature had to do with it, which the writer adds to the error.</param>
    public static PageRecord Failed(FrontierItem item, long fetchedAt, FetchOutcome outcome, string? error, string? refusalNote = null)
    {
        var row = new ResponseRow
        {
            UrlId = item.Id,
            FetchedAt = fetchedAt,
            Outcome = outcome,
            Error = error,
            RefusalNote = refusalNote,
            Indexability = Indexability.NonIndexable,
            IndexabilityReason = outcome == FetchOutcome.RobotsDisallowed ? IndexabilityReason.BlockedByRobots : IndexabilityReason.Unreachable
        };

        return new PageRecord(item, row, [], false, null, null);
    }

    /// <summary>
    /// A page this crawl renders is fetched again in the browser. The HTML that comes back replaces the HTTP body,
    /// and <see cref="Build"/> parses that once. JavaScript mode also keeps the raw body, for links the script removed.
    /// A render that doesn't happen, or fails, leaves the HTTP body to be parsed.
    /// </summary>
    private async Task<FetchResponse> RenderIfNeededAsync(FrontierItem item, Uri uri, FetchResponse response, CancellationToken cancellationToken)
    {
        if (item.Mode != FetchMode.Crawl || !item.IsInternal ||
            response is not { Outcome: FetchOutcome.Success, IsHtml: true, Body: { } html, BodyTruncated: false } ||
            !RenderNeed.ShouldRender(options.Rendering, _renderPatterns, item.Url, html))
        {
            return response;
        }

        var rendered = await renderer.RenderAsync(uri, cancellationToken).ConfigureAwait(false);
        if (rendered is null)
        {
            return response;
        }

        return response with
        {
            Body = rendered,
            RawHtml = options.Rendering == JavaScriptRendering.JavaScript ? html : null,
            BodyBytes = Encoding.UTF8.GetByteCount(rendered),
            ByteOrderMark = null
        };
    }

    /// <summary>
    /// Links that were in the HTTP body and are gone after the script ran. JavaScript mode follows them too, as
    /// Screaming Frog does. The raw body is not what the page's facts come from.
    /// </summary>
    private void AddLinksTheScriptRemoved(Uri page, FetchResponse response, List<OutLink> outLinks)
    {
        if (response.RawHtml is not { } raw)
        {
            return;
        }

        var seen = outLinks.Select(link => (link.Url, link.Kind)).ToHashSet();
        var rawPage = HtmlPageParser.Parse(raw, page, response.Headers, userAgent.RobotsToken, null);
        foreach (var link in rawPage.Links.Select(links.Create))
        {
            if (seen.Add((link.Url, link.Kind)))
            {
                outLinks.Add(link);
            }
        }
    }

    private static UrlPatterns CompilePatterns(IReadOnlyList<string>? patterns)
    {
        UrlPatterns.TryCompile(patterns, out var compiled, out _);
        return compiled;
    }

    private PageRecord Build(FrontierItem item, Uri uri, FetchResponse response, long fetchedAt, string? shopifySignal)
    {
        ParsedPage? parsed = null;
        if (response.Outcome == FetchOutcome.Success && item.IsInternal)
        {
            // Only a page that was read: an error page is not extracted, and the rules read the document this parse builds.
            var extractions = response.StatusCode is >= 200 and < 300 ? _extractions : null;
            parsed = response is { IsHtml: true, Body: { } document }
                ? HtmlPageParser.Parse(document, uri, response.Headers, userAgent.RobotsToken, response.ByteOrderMark, extractions)
                : HtmlPageParser.ParseHeaders(uri, response.Headers, userAgent.RobotsToken);
        }

        List<OutLink> outLinks = [];
        if (response is { Outcome: FetchOutcome.Redirect, Location: { } location })
        {
            var target = links.Create(location, LinkKind.Redirect, LinkFlags.None, LinkPosition.Head, null);
            outLinks.Add(target);
            WarnIfTheStartLeavesTheSite(item, target);
        }

        if (parsed is not null && item.Mode == FetchMode.Crawl)
        {
            outLinks.AddRange(parsed.Links.Select(links.Create));
            AddLinksTheScriptRemoved(uri, response, outLinks);
        }

        var facts = parsed?.Facts;
        var isParsedHtml = facts is not null && response.IsHtml && response.Body is not null;
        if (isParsedHtml)
        {
            // The crawl's first page names the site's icon, which is fetched beside the crawl.
            favicon.Offer(item, uri, facts!);
        }
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
            PixelWidth = response.PixelWidth,
            PixelHeight = response.PixelHeight,
            ShareTitle = facts is null ? null : Analysis.Rules.SocialText.ShareTitle(facts),
            ShareDescription = facts is null ? null : Analysis.Rules.SocialText.ShareDescription(facts),
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
            SimHashLanes = isParsedHtml ? facts!.SimHashLanes : null,
            Language = facts?.Language
        };

        var details = item.IsInternal ? BlobCodec.EncodeDetails(new ResponseDetails(response.Headers, facts)) : null;
        var html = options.StoreHtml && isParsedHtml ? BlobCodec.CompressText(response.Body!) : null;

        // A page's markup can say it's a Shopify store's where its headers didn't.
        if (item.IsInternal && shopifySignal is null && facts?.ShopifyMarkup is { } markup)
        {
            shopifySignal = markup;
            ObserveShopify(uri, shopifySignal);
        }

        return new PageRecord(item, row, outLinks, facts?.Robots.NoFollow ?? false, details, html)
        {
            ShopifySignal = shopifySignal,
            Extractions = parsed?.Extractions ?? []
        };
    }

    /// <summary>Notes what a response said of its host; the first sign of a store may be what makes Shopify's limit worth saying.</summary>
    private void ObserveShopify(Uri uri, string? signal)
    {
        if (Shopify.Observe(uri, signal))
        {
            SayShopifyLimitsUnsignedCrawlers();
        }
    }

    /// <summary>
    /// A 429 from the site, as far as Shopify goes: what the crawl's signature had to do with it (its note, for the stored
    /// error), and the alert that says it as it happens. A signature Shopify refused is said once, with which problem it
    /// is when that can be told. A store refusing an unsigned crawl is said at its first refusal, once the site is known to
    /// be a store, whichever comes second, and said again with its count while that grows, at most every
    /// <see cref="SHOPIFY_COUNT_EVERY"/>: one alert for the crawl, never one a page.
    /// </summary>
    private string? NoteShopifyRefusal(FrontierItem item, Uri uri, FetchResponse response)
    {
        var host = scope.HostName(UrlNormalizer.GetHost(uri));
        var situation = Shopify.Explain(uri);
        if (response.Signed && situation.Kind != SignatureSituationKind.Signed)
        {
            // It expired between the request and now: it was signed.
            situation = situation with { Kind = SignatureSituationKind.Signed, At = null };
        }

        var note = ShopifySignals.Explain(situation, timeProvider.GetUtcNow());
        if (situation.Kind == SignatureSituationKind.Signed)
        {
            if (Interlocked.Exchange(ref _signatureRefusedReported, 1) == 0)
            {
                var why = note![(ShopifySignals.SIGNED_REFUSAL.Length)..].TrimStart(',', ':', ' ');
                observer.OnNotice(new CrawlNotice(
                    CrawlNoticeLevel.Warning,
                    "shopify-signature-refused",
                    $"{host} runs on Shopify, and Shopify refused pages even with the signature: {why}. The crawl asks again for them at its end; any still refused are marked not checked, not broken.")
                {
                    Data = new CrawlNoticeData(Host: host, Reason: why)
                });
            }

            return note;
        }

        var detail = note?[ShopifySignals.UNSIGNED_REFUSAL.Length..].TrimStart(':', ' ');
        lock (_shopifySync)
        {
            _unsignedRefusal ??= (host, detail);
        }

        Shopify.NoteRefused(item.Id);
        SayShopifyLimitsUnsignedCrawlers();
        return note;
    }

    private void SayShopifyLimitsUnsignedCrawlers()
    {
        CrawlNotice notice;
        lock (_shopifySync)
        {
            var now = timeProvider.GetUtcNow();
            var count = Shopify.Refused;
            if (_unsignedRefusal is not { } refusal || !(Shopify.IsDetected || refusal.Detail is not null) || count <= _refusedSaid ||
                _refusedSaid > 0 && now - _refusedSaidAt < SHOPIFY_COUNT_EVERY)
            {
                return;
            }

            _refusedSaid = count;
            _refusedSaidAt = now;
            var pages = count == 1 ? "1 so far" : $"{count:N0} so far";
            var because = refusal.Detail is { } detail ? $"the crawl isn't signed ({detail})" : "the crawl isn't signed";
            notice = new CrawlNotice(
                CrawlNoticeLevel.Warning,
                "shopify-unsigned",
                $"{refusal.Host} runs on Shopify, and Shopify is refusing some of its pages because {because}: {pages}. They'll be marked not checked, not broken. To fix it, in Shopify admin go to Online Store › Preferences › Crawler access, choose Create signature, then add it to the crawl as Shopify crawler access.")
            {
                Data = new CrawlNoticeData(Host: refusal.Host, Reason: refusal.Detail, Count: count)
            };
        }

        observer.OnNotice(notice);
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

    /// <summary>
    /// Redirects keep their depth, so depth 0 is the start URL and its redirects, and the site-version probes, which aren't
    /// the start. One that leads off the site leaves nothing else to crawl, which looks like a broken crawl unless it's said.
    /// </summary>
    private void WarnIfTheStartLeavesTheSite(FrontierItem item, OutLink target)
    {
        if (item is { Depth: 0, IsInternal: true, Source: not DiscoverySource.SiteVersion } && !target.IsInternal &&
            Interlocked.Exchange(ref _startLeftReported, 1) == 0)
        {
            observer.OnNotice(new CrawlNotice(
                CrawlNoticeLevel.Warning,
                "start-redirects-away",
                $"The start URL redirects to {target.Url}, which is outside the site being crawled, so there's nothing more to crawl. Start from {target.Url} instead.")
            {
                Data = new CrawlNoticeData(Url: target.Url)
            });
        }
    }

    /// <summary>
    /// The site's hosts are named as progress names them: the start URL's host for it and its www twin. A 429 is the
    /// site asking the crawler to slow down, which it does by itself, not a block: it is said once per host, with the pace.
    /// </summary>
    private void WatchForBlocking(Uri uri, FetchResponse response)
    {
        // A 429 that limits one section is said by the section's own notice, as its URLs are held back.
        if (response.IsRateLimited && gates.For(uri).LimitedSectionOf(uri) is null)
        {
            var host = scope.HostName(UrlNormalizer.GetHost(uri));
            if (_rateLimitedHostsReported.TryAdd(host, true))
            {
                var state = gates.For(uri).ReadState();
                var pace = state.SlowedTo is { } interval ? $"at about {HostPace.DescribePace(interval)}" : "more slowly";
                var goesOn = state.HeldFor > TimeSpan.Zero
                    ? $"It waits {HostPace.DescribeWait(state.HeldFor)} as asked, then goes on {pace}"
                    : $"It goes on {pace}";
                observer.OnNotice(new CrawlNotice(
                    CrawlNoticeLevel.Info,
                    "rate-limited",
                    $"{host} asked the crawler to slow down ({HttpStatusNames.Describe(429)}). {goesOn}, speeding up slowly while the site answers well, and asks again for the URLs it was refused at the end of the crawl.")
                {
                    Data = new CrawlNoticeData(Host: host, Reason: HttpStatusNames.Describe(429), Duration: state.HeldFor > TimeSpan.Zero ? state.HeldFor : null)
                });
            }
        }
        else if (response is { Outcome: FetchOutcome.Blocked, BlockReason: { } reason } && !response.IsRateLimited)
        {
            // Once per host: the first blocked response raises it, and the file counts the rest when it's read back.
            var host = scope.HostName(UrlNormalizer.GetHost(uri));
            if (_blockedHostsReported.TryAdd(host, true))
            {
                observer.OnNotice(new CrawlNotice(
                    CrawlNoticeLevel.Warning,
                    "blocked",
                    $"{host} is blocking the crawler ({reason}). Try a slower crawl rate or a different user agent, or ask the site owner to allowlist the crawler.")
                {
                    Data = new CrawlNoticeData(Host: host, Reason: reason, Count: 1)
                });
            }
        }

        var fetches = Interlocked.Increment(ref _internalFetches);
        var forbidden = response.StatusCode == 403 ? Interlocked.Increment(ref _internalForbidden) : Volatile.Read(ref _internalForbidden);
        if (fetches == FORBIDDEN_SAMPLE_SIZE && forbidden * 2 >= fetches && Interlocked.Exchange(ref _forbiddenReported, 1) == 0)
        {
            observer.OnNotice(new CrawlNotice(
                CrawlNoticeLevel.Warning,
                "mostly-forbidden",
                $"{forbidden} of the first {fetches} pages returned 403 Forbidden. The site may be blocking crawlers.")
            {
                Data = new CrawlNoticeData(Host: scope.HostName(UrlNormalizer.GetHost(uri)), Count: forbidden, Sampled: fetches)
            });
        }
    }
}
