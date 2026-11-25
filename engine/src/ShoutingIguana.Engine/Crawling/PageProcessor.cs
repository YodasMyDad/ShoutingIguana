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

    /// <summary>Hosts whose plain HTTP was blocked and whose browser fetch got past it. Later pages skip HTTP.</summary>
    private readonly ConcurrentDictionary<string, byte> _browserHosts = new(StringComparer.Ordinal);
    private int _internalFetches;
    private int _internalForbidden;
    private int _forbiddenReported;
    private int _startLeftReported;

    /// <summary>
    /// Done once the crawl's first page has its answer, by whatever way it was read. A check of another version of the
    /// site that plain HTTP was refused waits for it, to be asked in the browser if that is how the site is read.
    /// </summary>
    private volatile Task _startRead = Task.CompletedTask;
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

    /// <summary>
    /// The crawl slowing down for a host that starts refusing it after pages were read: such a host's <c>blocked</c> alert
    /// is said as its refused URLs are recorded (<see cref="SayBlocked(PageRecord, RetreatFacts?, FallbackVerdict?)"/>), with what was done
    /// about them first.
    /// </summary>
    public RefusalRetreat? Retreat { get; set; }

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
            response = await FetchWithFallbackAsync(item, uri, body, cancellationToken, isRetry, isProbe).ConfigureAwait(false);
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
            WatchForBlocking(uri, response, isProbe, item.Mode == FetchMode.Crawl);
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
    /// Plain HTTP first. When this crawl's browser fallback is on and the host answers with a block that isn't a 429,
    /// the page is fetched in the browser. If that gets past the block, the host's later pages skip HTTP. A 429 is the
    /// host asking the crawler to slow down, which the browser doesn't change. A browser fetch that is still blocked,
    /// or that fails, leaves the HTTP response, and the host stays on HTTP.
    /// </summary>
    private async Task<FetchResponse> FetchWithFallbackAsync(FrontierItem item, Uri uri, FetchBody body, CancellationToken cancellationToken,
        bool isRetry, bool isProbe)
    {
        var http = () => fetcher.FetchAsync(uri, body, cancellationToken, singleAttempt: isRetry || isProbe, holdBackThrottled: item.IsInternal);
        var host = HostKey(uri);
        if (!FallsBack(item))
        {
            var plain = await http().ConfigureAwait(false);
            return isProbe ? await ProbeInTheBrowserAsync(uri, host, plain, cancellationToken).ConfigureAwait(false) : plain;
        }

        if (item is { Depth: 0, Source: not DiscoverySource.SiteVersion })
        {
            TaskCompletionSource read = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _startRead = read.Task;
            try
            {
                return await FetchInTheBrowserIfBlockedAsync(uri, host, http, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                read.TrySetResult();
            }
        }

        return await FetchInTheBrowserIfBlockedAsync(uri, host, http, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The site's other versions (http, without www) are asked for once each, to see where they lead. Where the site's
    /// firewall refused one and a browser is reading the site, it is asked for there, with the pass the browser has:
    /// otherwise the versions of a protected site could never be compared. Nobody is asked to complete a check for one.
    /// </summary>
    private async Task<FetchResponse> ProbeInTheBrowserAsync(Uri uri, string host, FetchResponse plain, CancellationToken cancellationToken)
    {
        if (!options.BrowserFallback || plain is not { Outcome: FetchOutcome.Blocked } || plain.IsRateLimited)
        {
            return plain;
        }

        await _startRead.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (!_browserHosts.ContainsKey(host))
        {
            return plain;
        }

        var viaBrowser = await renderer.LoadAsync(uri, host, cancellationToken, mayAsk: false).ConfigureAwait(false);

        // A probe is a status, as plain HTTP's is: the page it may have come with is not read.
        return viaBrowser is { FromBrowser: true, Outcome: not FetchOutcome.Blocked, IsRateLimited: false }
            ? viaBrowser with { Body = null, BodyBytes = null, BodyTruncated = false }
            : plain;
    }

    private async Task<FetchResponse> FetchInTheBrowserIfBlockedAsync(Uri uri, string host, Func<Task<FetchResponse>> http,
        CancellationToken cancellationToken)
    {
        if (_browserHosts.ContainsKey(host))
        {
            var again = await renderer.LoadAsync(uri, host, cancellationToken).ConfigureAwait(false);
            if (again is { FromBrowser: true })
            {
                return again;
            }
        }

        var response = await http().ConfigureAwait(false);
        if (response is not { Outcome: FetchOutcome.Blocked } || response.IsRateLimited)
        {
            return response;
        }

        var viaBrowser = await renderer.LoadAsync(uri, host, cancellationToken, response.BlockReason).ConfigureAwait(false);
        if (viaBrowser is not { FromBrowser: true } || viaBrowser.Outcome == FetchOutcome.Blocked || viaBrowser.IsRateLimited)
        {
            return response;
        }

        if (_browserHosts.TryAdd(host, 0))
        {
            var name = scope.HostName(UrlNormalizer.GetHost(uri));
            var browser = viaBrowser.Outcome != FetchOutcome.Blocked && renderer.ReadsInInstalledBrowser(host) ? renderer.InstalledBrowserName : null;
            observer.OnNotice(new CrawlNotice(
                CrawlNoticeLevel.Info,
                "browser-fallback",
                browser is not null
                    ? $"{name} blocked plain HTTP ({response.BlockReason}), so its pages are being read in {browser}, as itself: it sends its own user agent, not the crawler's."
                    : $"{name} blocked plain HTTP ({response.BlockReason}), so its pages are being read in a browser.")
            {
                Data = new CrawlNoticeData(Host: name, Reason: response.BlockReason, Browser: browser)
            });
        }

        return viaBrowser;
    }

    /// <summary>What the site's owner can do, in the words of the firewall most sites have. The same in every blocked alert.</summary>
    internal const string OWNER_ADVICE =
        "Or ask the site's owner to let the crawler in, with a firewall rule that allows the crawler's user agent or this computer's IP address. " +
        "In Cloudflare that is a custom rule in the firewall that skips the check: its \"verified bots\" setting does not cover this crawler.";

    /// <summary>
    /// What to do about a host that blocks the crawler. With the browser fallback off, turning it on comes first: it
    /// is the one thing here that needs nobody else. With it on, the advice is for what stopped the browser too. A host
    /// the crawl already slowed down for (<paramref name="retreat"/>) isn't told to go slower: it says what was done.
    /// </summary>
    /// <param name="verdict">What the browser did about the host when the page was refused.</param>
    private string BlockedAdvice(Uri uri, string host, string reason, RetreatFacts? retreat, FallbackVerdict verdict)
    {
        var slowed = retreat is null
            ? null
            : $"The crawl had slowed down by itself to one page every {HostPace.DescribeWait(retreat.Slowest)} and asked " +
              $"{(retreat.Retried == 1 ? "1 refused URL" : $"{retreat.Retried:N0} refused URLs")} again, and the site still refused them. ";
        if (!options.BrowserFallback)
        {
            return $"{host} is blocking the crawler ({reason}). {slowed}In the crawl's settings, turn on \"If a site blocks the check\" (\"Browser fallback\" in Advanced) and crawl again: " +
                   $"the site is then read in a real browser, which most checks let through. {OWNER_ADVICE}";
        }

        var slower = retreat is null ? " Try a slower crawl rate." : string.Empty;
        var what = verdict switch
        {
            FallbackVerdict.NeedsPerson =>
                $"{host} is asking to check you're human ({reason}), and that needs a person, whom this crawl was not told to ask. From the command line, crawl again with --solve-challenges: " +
                "a browser window opens on the page, you complete the check, and the crawl carries on.",
            FallbackVerdict.PersonDidNotFinish =>
                $"{host} asked to check you're human ({reason}). {Browser()} window was shown for you, and the check was not completed in the browser window in the time it was given, so its pages are recorded as blocked. Crawl again to be asked once more.",
            FallbackVerdict.PersonClosedWindow =>
                $"{host} asked to check you're human ({reason}). {Browser()} window was shown for you, and it was closed before the check was completed, so its pages are recorded as blocked. Crawl again to be asked once more.",
            FallbackVerdict.RefusedAfterCheck =>
                $"{host} asked to check you're human ({reason}). The check was completed in the {TheBrowser()} window, and the site then refused the page all the same, so its pages are recorded as blocked.",
            FallbackVerdict.WentElsewhere =>
                $"{host} asked to check you're human ({reason}). The check passed in {TheBrowser()}, and the page then went to {renderer.LandedOn(HostKey(uri)) ?? "another address"}, which is not this address's own answer, so this address is recorded as blocked. Crawl from where it went instead.",
            FallbackVerdict.NoDisplay =>
                $"{host} is blocking the crawler ({reason}). Getting past it needs a browser window, and this computer has no screen to show one on. Crawl from a desktop computer.",
            FallbackVerdict.NoBrowser =>
                $"{host} is blocking the crawler ({reason}). Getting past it needs Chrome or Edge, and none could be started on this computer. Install one and crawl again.",
            FallbackVerdict.InstalledBrowserOff =>
                $"{host} is blocking the crawler ({reason}), and the browser the crawler downloads was blocked too. This crawl was told not to use the computer's own Chrome or Edge, which most checks let through.",
            FallbackVerdict.StillBlocked => $"{host} is blocking the crawler ({reason}), and it refused a real browser too.{slower}",
            _ => $"{host} is blocking the crawler ({reason}).{slower}"
        };
        return $"{what} {slowed}{OWNER_ADVICE}";

        // "A Chrome", "An Edge".
        string Browser() => InstalledBrowsers.WithArticle(renderer.InstalledBrowserName ?? "browser");
        string TheBrowser() => renderer.InstalledBrowserName ?? "browser";
    }

    /// <summary>What the browser fallback did about a blocked host, as the one word the alert's facts carry (<see cref="CrawlNoticeData"/>).</summary>
    private string TriedFor(FallbackVerdict verdict) => !options.BrowserFallback
        ? "off"
        : verdict switch
        {
            FallbackVerdict.NeedsPerson => "needsPerson",
            FallbackVerdict.PersonDidNotFinish => "notCompleted",
            FallbackVerdict.PersonClosedWindow => "windowClosed",
            FallbackVerdict.RefusedAfterCheck => "refusedAfterCheck",
            FallbackVerdict.WentElsewhere => "wentElsewhere",
            FallbackVerdict.NoDisplay => "noDisplay",
            FallbackVerdict.NoBrowser => "noBrowser",
            FallbackVerdict.InstalledBrowserOff => "ownBrowserOff",
            FallbackVerdict.StillBlocked => "refused",
            _ => "notTried"
        };

    /// <summary>Internal pages only. A status check is not parsed into page facts, and a 429 is not a firewall block.</summary>
    private bool FallsBack(FrontierItem item) =>
        options.BrowserFallback && item is { Mode: FetchMode.Crawl, IsInternal: true };

    /// <summary>example.com and www.example.com share a gate, and they share this too.</summary>
    private static string HostKey(Uri uri) => CrawlScope.SiteHost(UrlNormalizer.GetHost(uri));

    /// <summary>
    /// A page this crawl renders is fetched again in the browser. The HTML that comes back replaces the HTTP body,
    /// and <see cref="Build"/> parses that once. JavaScript mode also keeps the raw body, for links the script removed.
    /// A render that doesn't happen, or fails, leaves the HTTP body to be parsed. A page the browser fallback already
    /// fetched is that HTML: it is not rendered again.
    /// </summary>
    private async Task<FetchResponse> RenderIfNeededAsync(FrontierItem item, Uri uri, FetchResponse response, CancellationToken cancellationToken)
    {
        if (response.FromBrowser || item.Mode != FetchMode.Crawl || !item.IsInternal ||
            response is not { Outcome: FetchOutcome.Success, IsHtml: true, Body: { } html, BodyTruncated: false } ||
            !RenderNeed.ShouldRender(options.Rendering, _renderPatterns, item.Url, html))
        {
            return response;
        }

        var result = await renderer.RenderAsync(uri, cancellationToken).ConfigureAwait(false);
        if (result.LeftFor is { } target)
        {
            // The script left for another page. This one is kept as it was sent, and forwards there.
            return response with { ScriptRedirect = target };
        }

        if (result.Html is not { } rendered)
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
        if (facts is { MetaRefresh: null } && response.ScriptRedirect is { } scripted && item.Mode == FetchMode.Crawl)
        {
            // A redirect the page's script made is an instant refresh nothing in the HTML announces: the same forward,
            // followed and judged the same way.
            facts = facts with { MetaRefresh = new MetaRefreshFact(0, scripted.AbsoluteUri, FromScript: true) };
            var forward = links.Create(scripted, LinkKind.MetaRefresh, LinkFlags.None, LinkPosition.Head, null);
            if (!outLinks.Any(link => link.Url == forward.Url && link.Kind == LinkKind.MetaRefresh))
            {
                outLinks.Add(forward);
            }
        }

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

    /// <summary>Only canonicals Google honours count: in the head or in an HTTP header, and without an alternate's attributes.</summary>
    private static bool CanonicalisedElsewhere(string url, PageFacts? facts) =>
        facts?.Canonicals.FirstOrDefault(canonical => canonical.Url is not null && canonical.IsHonoured)
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
    /// A refused URL of a host some of whose pages were read is being recorded: the host's <c>blocked</c> alert is said
    /// now, once, with what the crawl did about it first (<paramref name="retreat"/>: it slowed down and asked again).
    /// </summary>
    /// <param name="verdict">
    /// What the browser did about the host when the page was refused, which a page read since may have changed; null for
    /// what it says now.
    /// </param>
    public void SayBlocked(PageRecord record, RetreatFacts? retreat, FallbackVerdict? verdict)
    {
        if (record.Response is { Outcome: FetchOutcome.Blocked, BlockReason: { } reason })
        {
            SayBlocked(new Uri(record.Item.Url), reason, retreat, verdict);
        }
    }

    /// <summary>The host's <c>blocked</c> alert, once per host: the file counts the rest of its refusals when it's read back.</summary>
    private void SayBlocked(Uri uri, string reason, RetreatFacts? retreat, FallbackVerdict? verdict = null)
    {
        var host = scope.HostName(UrlNormalizer.GetHost(uri));
        if (!_blockedHostsReported.TryAdd(host, true))
        {
            return;
        }

        var said = verdict ?? renderer.VerdictFor(HostKey(uri));
        var tried = TriedFor(said);
        observer.OnNotice(new CrawlNotice(
            CrawlNoticeLevel.Warning,
            "blocked",
            BlockedAdvice(uri, host, reason, retreat, said))
        {
            Data = new CrawlNoticeData(Host: host, Reason: reason, Count: 1, Tried: tried,
                Browser: tried is "notCompleted" or "windowClosed" or "refusedAfterCheck" or "wentElsewhere" ? renderer.InstalledBrowserName : null,
                Url: tried == "wentElsewhere" ? renderer.LandedOn(HostKey(uri)) : null,
                Duration: retreat?.Slowest, Retried: retreat?.Retried)
        });
    }

    /// <summary>
    /// The site's hosts are named as progress names them: the start URL's host for it and its www twin. A 429 is the
    /// site asking the crawler to slow down, which it does by itself, not a block: it is said once per host, with the pace.
    /// </summary>
    private void WatchForBlocking(Uri uri, FetchResponse response, bool isProbe, bool isPage)
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
            // A host some of whose pages were read is said as its refused URLs are recorded, once the crawl has done what
            // it could about them; and its refusals are no sample of the site's first pages. With the browser fallback on,
            // a file's status check refused over plain HTTP (an icon, a script) is no block of the site: its pages are read
            // in the browser, and a page refused there says so itself.
            if (!isProbe && (Retreat?.WasRead(uri) == true || (options.BrowserFallback && !isPage)))
            {
                return;
            }

            // Once per host: the first blocked response raises it, and the file counts the rest when it's read back.
            // A host a browser is reading is not blocking the crawl: what was refused is a check of another version of it.
            // With the browser fallback on, such a check never raises the alert: it is answered while the browser is
            // still at the site's first page, before anyone can say what the browser did, and the host's alert is said once.
            if (!(isProbe && options.BrowserFallback) &&
                !(_browserHosts.ContainsKey(HostKey(uri)) && renderer.VerdictFor(HostKey(uri)) == FallbackVerdict.None))
            {
                SayBlocked(uri, reason, null);
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
