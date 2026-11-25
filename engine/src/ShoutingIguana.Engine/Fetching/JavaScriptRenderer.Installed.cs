using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Fetching;

/// <summary>
/// The browser fallback's second browser: the Chrome or Edge this computer has, opened as a person opens it. A
/// site's automatic check ("Just a moment...") clears by itself in that, where it never does in the headless
/// browser, and a check that needs a person can be shown to one. The browser sends its own user agent, since a
/// check's pass is tied to it, and nothing about it is disguised. Each request it makes still takes its turn on the
/// host's gate, and robots.txt and the private-address check apply as they do to a rendered page.
/// </summary>
internal sealed partial class JavaScriptRenderer
{
    /// <summary>
    /// How long an automatic check is given to clear by itself before a person is shown the window. Cloudflare's takes
    /// five to eleven seconds; a person left looking at nothing for longer takes the crawl to have stuck.
    /// </summary>
    internal const int UNATTENDED_CHECK_MS = 12_000;

    /// <summary>How long a refusal with no check in it is watched, in case the page loads itself again.</summary>
    private const int REFUSAL_GRACE_MS = 3_000;

    /// <summary>How many times in a crawl a person is asked to complete one host's check.</summary>
    private const int MAX_PERSON_CHECKS = 2;

    /// <summary>How long a page that has answered is given to be parsed before it is read as it stands.</summary>
    private const int LOADED_MS = 10_000;

    /// <summary>How many of a host's pages read in the installed browser are logged one by one.</summary>
    private const int SAID_PAGES = 5;

    private const string PAGE_HTML =
        "(() => { const t = document.doctype ? '<!DOCTYPE ' + document.doctype.name + '>' : ''; return t + document.documentElement.outerHTML; })()";

    /// <summary>One page at a time in the installed browser, whatever the host: it is one window.</summary>
    private readonly SemaphoreSlim _installedGate = new(1, 1);

    /// <summary>The site's hosts a person is being asked to complete a check for, and when each wait ends.</summary>
    private readonly ConcurrentDictionary<string, DateTimeOffset> _humanChecks = new(StringComparer.Ordinal);

    private InstalledBrowserProcess? _installed;
    private int _installedStarts;
    private FallbackVerdict _installedMissing;

    /// <summary>Finds the browser to open. A test names its own, or none.</summary>
    internal Func<InstalledBrowser?> FindInstalledBrowser { get; set; } = InstalledBrowsers.Find;

    /// <summary>Whether a window can be shown here. A test says no, as a server would.</summary>
    internal Func<bool> HasDisplay { get; set; } = InstalledBrowsers.HasDisplay;

    /// <summary>The hosts waiting for a person to complete a check now, and how long each wait has left.</summary>
    public IReadOnlyList<(string Host, TimeSpan Left)> HumanChecks
    {
        get
        {
            if (_humanChecks.IsEmpty)
            {
                return [];
            }

            var now = timeProvider.GetUtcNow();
            return [.. _humanChecks.Select(check => (check.Key, check.Value > now ? check.Value - now : TimeSpan.Zero))];
        }
    }

    /// <summary>Whether the computer's own browser may be opened, as far as is known before it is tried.</summary>
    private bool CanUseInstalled => options.UseInstalledBrowser && _installedMissing == FallbackVerdict.None && HasDisplay();

    /// <summary>Why <paramref name="hostKey"/> is still blocked after the fallback, for the crawl's alert.</summary>
    public FallbackVerdict VerdictFor(string hostKey) =>
        _fallbackHosts.TryGetValue(hostKey, out var session) ? session.Verdict : FallbackVerdict.None;

    /// <summary>For <see cref="FallbackVerdict.WentElsewhere"/>: where <paramref name="hostKey"/>'s page went once its check had passed.</summary>
    public string? LandedOn(string hostKey) => _fallbackHosts.TryGetValue(hostKey, out var session) ? session.LandedOn : null;

    /// <summary>Whether <paramref name="hostKey"/> is being read in the computer's own browser, not the headless one.</summary>
    public bool ReadsInInstalledBrowser(string hostKey) => _fallbackHosts.TryGetValue(hostKey, out var session) && session.Installed;

    /// <summary>
    /// The crawl has slowed down as far as it goes for <paramref name="hostKey"/>, whose pages this browser read before it
    /// started checking it, and its check still doesn't clear by itself: the next of its pages that meets the check shows
    /// the window to a person. False when this crawl may not ask one, the host has had its asks, or one is already due.
    /// </summary>
    public bool AskAPersonNext(string hostKey)
    {
        if (!MayAskAPerson(hostKey) || _fallbackHosts[hostKey].AskPerson)
        {
            return false;
        }

        _fallbackHosts[hostKey].AskPerson = true;
        return true;
    }

    /// <summary>
    /// Whether a check this browser meets on <paramref name="hostKey"/>, whose pages it has read, may still be shown to a
    /// person in this crawl: it may ask one, and the host hasn't had its asks.
    /// </summary>
    public bool MayAskAPerson(string hostKey) =>
        options.SolveChallenges && _fallbackHosts.TryGetValue(hostKey, out var session) && session.Installed && !session.GaveUp &&
        session.PersonChecks < MAX_PERSON_CHECKS;

    /// <summary>What the installed browser is called, once it has been started: "Chrome", "Edge".</summary>
    public string? InstalledBrowserName => _installed?.Browser.Name;

    /// <summary>
    /// The page the installed browser got for <paramref name="uri"/>. Null when no such browser can be used, or the
    /// page didn't load: <paramref name="session"/> then says why. A page still refused comes back as the block.
    /// </summary>
    /// <param name="mayAsk">
    /// False for a check of another version of the site: nobody is asked to complete a check for one, and what
    /// happens to it says nothing of the host's pages, so it leaves the host's verdict alone.
    /// </param>
    private async Task<FetchResponse?> LoadInstalledAsync(Uri uri, FallbackHost session, bool mayAsk, CancellationToken cancellationToken)
    {
        if (!options.UseInstalledBrowser)
        {
            if (mayAsk)
            {
                session.Verdict = FallbackVerdict.InstalledBrowserOff;
            }

            return null;
        }

        await _installedGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var browser = await InstalledAsync(cancellationToken).ConfigureAwait(false);
            if (browser is null)
            {
                if (mayAsk)
                {
                    session.Verdict = _installedMissing;
                }

                return null;
            }

            var response = await NavigateInstalledAsync(browser, uri, session, mayAsk, cancellationToken).ConfigureAwait(false);
            if (mayAsk && response is { Outcome: FetchOutcome.Blocked } && session.Verdict == FallbackVerdict.None)
            {
                session.Verdict = FallbackVerdict.StillBlocked;
            }

            if (response is { Outcome: not FetchOutcome.Blocked })
            {
                // The first few are said, so a log shows the crawl going on in the browser after a check; then the count, at the end.
                var read = ++session.ReadInInstalled;
                logger.Log(read <= SAID_PAGES ? LogLevel.Information : LogLevel.Debug,
                    "Read {Url} in {Browser}: {Status} (page {Count} of this host read there)", uri, browser.Browser.Name, response.StatusCode, read);
            }

            return response;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            logger.LogWarning(ex, "Reading {Url} in the installed browser failed", uri);
            return null;
        }
        finally
        {
            _installedGate.Release();
        }
    }

    /// <summary>The running browser, started the first time it is needed, and again if it was closed.</summary>
    private async Task<InstalledBrowserProcess?> InstalledAsync(CancellationToken cancellationToken)
    {
        if (_installed is { IsRunning: true } running)
        {
            return running;
        }

        if (_installedMissing != FallbackVerdict.None)
        {
            return null;
        }

        if (!HasDisplay())
        {
            _installedMissing = FallbackVerdict.NoDisplay;
            return null;
        }

        if (_installed is not null)
        {
            await _installed.DisposeAsync().ConfigureAwait(false);
            _installed = null;
        }

        if (++_installedStarts > MAX_BROWSER_STARTS)
        {
            _installedMissing = FallbackVerdict.NoBrowser;
            return null;
        }

        var browser = FindInstalledBrowser() ?? await DownloadedChromiumAsync().ConfigureAwait(false);
        if (browser is null)
        {
            _installedMissing = FallbackVerdict.NoBrowser;
            return null;
        }

        try
        {
            _installed = await InstalledBrowserProcess.StartAsync(browser, BrowserProxy()?.Server, logger, cancellationToken).ConfigureAwait(false);
            if (_jar.Count > 0)
            {
                var cookies = _jar.Select(cookie => new { name = cookie.Name, value = cookie.Value, domain = cookie.Domain, path = cookie.Path, secure = cookie.Secure });
                await _installed.Connection.SendAsync("Storage.setCookies", new { cookies }, cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            logger.LogInformation("Started {Browser} for a host the headless browser can't read", browser.Name);
            return _installed;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "{Browser} couldn't be started", browser.Name);
            _installedMissing = FallbackVerdict.NoBrowser;
            return null;
        }
    }

    /// <summary>
    /// With no Chrome or Edge on the computer, the windowed Chromium that Playwright downloads is opened the same way.
    /// It is downloaded the first time it is needed.
    /// </summary>
    private async Task<InstalledBrowser?> DownloadedChromiumAsync()
    {
        try
        {
            MakeDriverRunnable();
            _playwright ??= await CreatePlaywright().ConfigureAwait(false);
            var path = _playwright.Chromium.ExecutablePath;
            if (!File.Exists(path))
            {
                observer.OnNotice(new CrawlNotice(
                    CrawlNoticeLevel.Info,
                    "javascript-rendering",
                    "Downloading the browser that renders pages. This happens once, and the crawl goes on when it is done."));
                if (await Task.Run(() => InstallBrowser(shellOnly: false)).ConfigureAwait(false) != 0 || !File.Exists(path))
                {
                    return null;
                }
            }

            return new InstalledBrowser("Chromium", path);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "No windowed Chromium could be found or downloaded");
            return null;
        }
    }

    private async Task<FetchResponse?> NavigateInstalledAsync(InstalledBrowserProcess browser, Uri uri, FallbackHost session, bool mayAsk,
        CancellationToken cancellationToken)
    {
        var connection = browser.Connection;
        var front = InstalledBrowserProcess.WindowInFront();
        var targetId = await browser.OpenTabAsync(cancellationToken).ConfigureAwait(false);
        var load = new InstalledLoad(targetId);
        Action<string?, string, JsonElement> onEvent = (sessionId, method, parameters) => OnInstalledEvent(connection, load, sessionId, method, parameters);
        connection.Event += onEvent;
        var asking = false;
        var shown = false;
        var hostName = scope.HostName(UrlNormalizer.GetHost(uri));
        void Decide(FallbackVerdict verdict)
        {
            if (mayAsk)
            {
                session.Verdict = verdict;
            }
        }

        try
        {
            await connection.SendAsync("Target.setDiscoverTargets", new { discover = true }, cancellationToken: cancellationToken).ConfigureAwait(false);
            var attached = await connection.SendAsync("Target.attachToTarget", new { targetId, flatten = true }, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var sessionId = attached.GetProperty("sessionId").GetString()!;
            load.SessionId = sessionId;
            await connection.SendAsync("Page.enable", null, sessionId, cancellationToken).ConfigureAwait(false);
            await connection.SendAsync("Network.enable", null, sessionId, cancellationToken).ConfigureAwait(false);

            // A service worker's requests are not paused, so they would miss the host's gate and the private-address check.
            await connection.SendAsync("Network.setBypassServiceWorker", new { bypass = true }, sessionId, cancellationToken).ConfigureAwait(false);
            if (options.TrustedCertificate is not null)
            {
                await connection.SendAsync("Security.setIgnoreCertificateErrors", new { ignore = true }, sessionId, cancellationToken).ConfigureAwait(false);
            }

            await connection.SendAsync("Fetch.enable", new { patterns = new[] { new { urlPattern = "*" } }, handleAuthRequests = true }, sessionId, cancellationToken)
                .ConfigureAwait(false);
            var tree = await connection.SendAsync("Page.getFrameTree", null, sessionId, cancellationToken).ConfigureAwait(false);
            load.FrameId = tree.GetProperty("frameTree").GetProperty("frame").GetProperty("id").GetString()!;

            var began = Stopwatch.StartNew();
            var answerBy = TimeSpan.FromSeconds(options.RequestTimeoutSeconds * HELD_PATIENCE);

            // Answered once the document's request has gone, which waits its turn on the host's gate.
            await connection.SendAsync("Page.navigate", new { url = uri.AbsoluteUri }, sessionId, cancellationToken, answerBy).ConfigureAwait(false);

            // The tab that has just come to the top of its window can take the keyboard a moment after it opened.
            browser.KeepKeyboardWith(front);

            InstalledDocument? judged = null;
            string? reason = null;
            TimeSpan? refusedAt = null;
            TimeSpan? personUntil = null;
            TimeSpan askedAt = default;

            // The person completed the check: said once, in the log and to the crawl.
            void Passed()
            {
                if (!asking)
                {
                    return;
                }

                asking = false;
                var took = began.Elapsed - askedAt;
                logger.LogInformation("Human check: {Host}'s check was completed after {Seconds:F0} s", hostName, took.TotalSeconds);
                observer.OnNotice(new CrawlNotice(
                    CrawlNoticeLevel.Info,
                    "human-check-passed",
                    $"{hostName}'s check was completed. The crawl is reading the site in {browser.Browser.Name}.")
                {
                    Data = new CrawlNoticeData(Host: hostName, Url: uri.AbsoluteUri, Duration: took, Browser: browser.Browser.Name)
                });
            }

            // How long the site's check has had to clear by itself. Time one of its requests spent waiting for the
            // crawl's own pace is not the check's, as it is not a rendered page's.
            var checking = TimeSpan.Zero;
            var last = began.Elapsed;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var now = began.Elapsed;
                if (refusedAt is not null && !load.IsHeld)
                {
                    checking += now - last;
                }

                last = now;
                if (load.Closed || !browser.IsRunning)
                {
                    // The person closed the window, or the browser stopped: the check was not completed.
                    if (asking)
                    {
                        Decide(FallbackVerdict.PersonClosedWindow);
                        logger.LogInformation("Human check: the {Browser} window was closed before {Host}'s check was completed, after {Seconds:F0} s",
                            browser.Browser.Name, hostName, (began.Elapsed - askedAt).TotalSeconds);
                    }

                    return judged is not null && reason is not null ? Refused(uri, judged, reason) : null;
                }

                if (load.Redirect is { } redirect)
                {
                    // Before any check, or after one: a site that lets the browser in can answer its reload with a redirect
                    // (to its site for the visitor's country, say). That is the address's answer, and the crawl follows it.
                    Passed();
                    var answer = Redirected(uri, redirect);
                    if (refusedAt is not null)
                    {
                        Decide(FallbackVerdict.None);
                        logger.LogInformation("{Host}'s check cleared, and {Url} answered {Status}, a redirect to {Location}", hostName, uri,
                            redirect.Status, answer.Location);
                    }
                    else
                    {
                        logger.LogDebug("The installed browser was redirected from {Url} with {Status} to {Location}", redirect.Url, redirect.Status,
                            answer.Location);
                    }

                    return answer;
                }

                var document = load.Document;
                if (document is null)
                {
                    if (began.Elapsed >= answerBy)
                    {
                        logger.LogInformation("The installed browser got no answer for {Url}", uri);
                        return null;
                    }

                    await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (!ReferenceEquals(document, judged))
                {
                    // The headers name most checks. The page is only read for the statuses a firewall refuses with, as the
                    // plain fetch reads it, and not while a check the headers named is running: it is left alone.
                    reason = BlockDetector.Detect(document.Status, document.Headers, null);
                    if (reason is null && document.Status is 401 or 403 or 405 or 503)
                    {
                        var body = await EvaluateAsync(connection, sessionId, PAGE_HTML, cancellationToken).ConfigureAwait(false);
                        if (body is null && began.Elapsed < answerBy)
                        {
                            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                            continue;
                        }

                        reason = BlockDetector.Detect(document.Status, document.Headers, body);
                    }

                    if (reason is not null && reason.StartsWith(BlockDetector.RATE_LIMITED, StringComparison.Ordinal))
                    {
                        // The host asked the crawler to slow down. A browser doesn't change that.
                        return Refused(uri, document, reason);
                    }

                    judged = document;
                    if (reason is not null)
                    {
                        refusedAt ??= began.Elapsed;
                    }
                }

                if (reason is null)
                {
                    var html = await LoadedHtmlAsync(connection, sessionId, load, document, cancellationToken).ConfigureAwait(false);
                    if (!ReferenceEquals(load.Document, document))
                    {
                        continue;
                    }

                    if (html is null || !StayedForFallback(document.Url, uri))
                    {
                        if (html is not null && refusedAt is not null)
                        {
                            // The check passed, and the page's own script then left for another address: no redirect of
                            // the site's to store. The alert names where it went.
                            Passed();
                            session.LandedOn = document.Url;
                            Decide(FallbackVerdict.WentElsewhere);
                        }

                        logger.LogInformation("The installed browser landed on {Final} for {Url}; keeping the HTTP response", document.Url, uri);
                        return null;
                    }

                    Passed();
                    Decide(FallbackVerdict.None);
                    if (refusedAt is not null)
                    {
                        logger.LogInformation("{Host}'s check cleared after {Seconds:F0} s, and {Url} was read: {Status}", hostName,
                            (began.Elapsed - refusedAt.Value).TotalSeconds, uri, document.Status);
                    }

                    return BrowserDocument(uri, document.Status, document.Headers, html, document.TimeToFirstByteMs,
                        document.TimeToFirstByteMs + (int)(began.Elapsed - document.At).TotalMilliseconds);
                }

                var isCheck = BlockDetector.IsCheck(reason);
                var patience = TimeSpan.FromMilliseconds(isCheck ? UNATTENDED_CHECK_MS : REFUSAL_GRACE_MS);
                if (asking && !isCheck && now - document.At >= patience)
                {
                    // The check was completed, and what the site then answered is a plain refusal.
                    asking = false;
                    Decide(FallbackVerdict.RefusedAfterCheck);
                    logger.LogInformation("Human check: {Host}'s check was completed, and the site then refused {Url} ({Reason})", hostName, uri, reason);
                    return Refused(uri, document, reason);
                }

                if (personUntil is null && (checking >= patience || now - refusedAt!.Value >= patience * HELD_PATIENCE))
                {
                    if (!isCheck)
                    {
                        return Refused(uri, document, reason);
                    }

                    if (!mayAsk || !options.SolveChallenges || session.PersonChecks >= MAX_PERSON_CHECKS)
                    {
                        Decide(options.SolveChallenges ? FallbackVerdict.PersonDidNotFinish : FallbackVerdict.NeedsPerson);
                        logger.LogInformation("{Host}'s check ({Reason}) did not clear by itself for {Url}, and nobody is asked: {Why}", hostName, reason, uri,
                            !mayAsk ? "it is a check of another version of the site"
                            : options.SolveChallenges ? "a person was already asked for this host" : "this crawl was not told to ask a person");
                        return Refused(uri, document, reason);
                    }

                    if (session.ReadInInstalled > 0 && !session.AskPerson)
                    {
                        // The host let this browser read its pages, and has started checking it: the crawl slows down for it
                        // first (Crawling.RefusalRetreat), and asks for a person only once that's no use (AskAPersonNext).
                        Decide(FallbackVerdict.NeedsPerson);
                        logger.LogInformation("{Host}'s check ({Reason}) did not clear by itself for {Url}, and nobody is asked yet: the crawl slows down for it first",
                            hostName, reason, uri);
                        return Refused(uri, document, reason);
                    }

                    session.AskPerson = false;
                    session.PersonChecks++;
                    asking = true;
                    shown = true;
                    askedAt = began.Elapsed;
                    var wait = TimeSpan.FromSeconds(Math.Max(1, options.ChallengeWaitSeconds));
                    personUntil = began.Elapsed + wait;
                    _humanChecks[hostName] = timeProvider.GetUtcNow() + wait;
                    logger.LogInformation("Human check: {Host} asks for a person ({Reason}) at {Url}; waiting up to {Seconds} s", hostName, reason, uri,
                        (int)wait.TotalSeconds);
                    var window = await browser.ShowAsync(targetId, cancellationToken).ConfigureAwait(false);
                    logger.Log(window.IsOnScreen ? LogLevel.Information : LogLevel.Warning,
                        "Human check: the {Browser} window is shown at {Left},{Top}, {Width} by {Height}, {State}, on a screen of {ScreenWidth} by {ScreenHeight}; whole on the screen: {OnScreen}; in front: {InFront}",
                        browser.Browser.Name, window.Bounds.Left, window.Bounds.Top, window.Bounds.Width, window.Bounds.Height, window.Bounds.State,
                        window.Screen.Width, window.Screen.Height, window.IsOnScreen, window.InFront?.ToString() ?? "not known");
                    observer.OnNotice(new CrawlNotice(
                        CrawlNoticeLevel.Info,
                        "human-check",
                        $"{hostName} asked to check you're human ({reason}). {InstalledBrowsers.WithArticle(browser.Browser.Name)} window opened on the page for you to complete the check.")
                    {
                        Data = new CrawlNoticeData(Host: hostName, Url: uri.AbsoluteUri, Reason: reason, Browser: browser.Browser.Name)
                    });
                }

                if (personUntil is { } until && began.Elapsed >= until)
                {
                    Decide(FallbackVerdict.PersonDidNotFinish);
                    logger.LogInformation("Human check: nobody completed {Host}'s check in the {Seconds:F0} s it was given; its pages are recorded as blocked",
                        hostName, (began.Elapsed - askedAt).TotalSeconds);
                    return Refused(uri, document, reason);
                }

                await Task.Delay(200, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            connection.Event -= onEvent;
            load.Finish();
            _humanChecks.TryRemove(hostName, out _);
            await CloseInstalledTabAsync(browser, targetId, hide: shown).ConfigureAwait(false);
        }
    }

    private async Task CloseInstalledTabAsync(InstalledBrowserProcess browser, string targetId, bool hide)
    {
        if (!browser.IsRunning)
        {
            return;
        }

        try
        {
            using var patience = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            if (hide)
            {
                await browser.HideAsync(targetId, patience.Token).ConfigureAwait(false);
            }

            await browser.Connection.SendAsync("Target.closeTarget", new { targetId }, cancellationToken: patience.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            logger.LogDebug(ex, "Closing the installed browser's tab failed");
        }
    }

    /// <summary>
    /// The page's HTML once its document is parsed and what it loads has had <see cref="SETTLE_MS"/> to finish, as the
    /// headless browser gives a quiet network. Null when it can't be read.
    /// </summary>
    private async Task<string?> LoadedHtmlAsync(DevToolsConnection connection, string sessionId, InstalledLoad load, InstalledDocument document,
        CancellationToken cancellationToken)
    {
        var waited = Stopwatch.StartNew();
        var limit = Math.Min(LOADED_MS, options.RequestTimeoutSeconds * 1000);
        long? parsedAt = null;
        while (waited.ElapsedMilliseconds < limit && ReferenceEquals(load.Document, document) && !load.Closed)
        {
            var state = await EvaluateAsync(connection, sessionId, "document.readyState", cancellationToken).ConfigureAwait(false);
            if (state == "complete")
            {
                break;
            }

            if (state == "interactive")
            {
                parsedAt ??= waited.ElapsedMilliseconds;
                if (waited.ElapsedMilliseconds - parsedAt >= SETTLE_MS)
                {
                    break;
                }
            }

            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }

        // What the page's own script adds as soon as it has loaded.
        await Task.Delay(300, cancellationToken).ConfigureAwait(false);
        return await EvaluateAsync(connection, sessionId, PAGE_HTML, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>A string the page has, or null while it is between two documents.</summary>
    private static async Task<string?> EvaluateAsync(DevToolsConnection connection, string sessionId, string expression, CancellationToken cancellationToken)
    {
        try
        {
            var result = await connection.SendAsync("Runtime.evaluate", new { expression, returnByValue = true }, sessionId, cancellationToken)
                .ConfigureAwait(false);
            return result.TryGetProperty("result", out var value) && value.TryGetProperty("value", out var text) && text.ValueKind == JsonValueKind.String
                ? text.GetString()
                : null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            return null;
        }
    }

    private void OnInstalledEvent(DevToolsConnection connection, InstalledLoad load, string? sessionId, string method, JsonElement parameters)
    {
        if (sessionId is null)
        {
            if (method is "Target.targetDestroyed" or "Target.targetCrashed" &&
                parameters.TryGetProperty("targetId", out var gone) && gone.GetString() == load.TargetId)
            {
                load.Closed = true;
            }
            else if (method == "Target.detachedFromTarget" && parameters.TryGetProperty("sessionId", out var detached) &&
                     detached.GetString() == load.SessionId)
            {
                load.Closed = true;
            }

            return;
        }

        if (sessionId != load.SessionId)
        {
            return;
        }

        switch (method)
        {
            case "Network.requestWillBeSent" when IsTopDocument(load, parameters):
                load.DocumentAsked = Stopwatch.GetTimestamp();
                // The address's first answer, or its first once a check has let the browser in and loaded the page again.
                if (parameters.TryGetProperty("redirectResponse", out var redirected) && load.Redirect is null && (load.Document is null || load.Refused))
                {
                    var headers = InstalledBrowserProcess.Headers(redirected.GetProperty("headers"));

                    // The browser's own steps are not the site's redirect: from http to https for a host it knows to be
                    // https only, and a 307 to the same address, which Chrome makes of itself when a check reloads the page.
                    var from = redirected.GetProperty("url").GetString();
                    var to = parameters.GetProperty("request").GetProperty("url").GetString();
                    if (!headers.Any(header => header.Name == "non-authoritative-reason") && !string.Equals(from, to, StringComparison.Ordinal))
                    {
                        load.Redirect = new InstalledDocument(redirected.GetProperty("status").GetInt32(), headers,
                            redirected.GetProperty("url").GetString() ?? string.Empty, 0, TimeSpan.Zero);
                    }
                }

                break;
            case "Network.responseReceived" when IsTopDocument(load, parameters):
                var response = parameters.GetProperty("response");
                var status = response.GetProperty("status").GetInt32();
                if (status is 401 or 403 or 405 or 429 or 503)
                {
                    // What a check loads must reach it: its images, and its own paths, which robots.txt often keeps crawlers out of.
                    load.Refused = true;
                }

                load.Document = new InstalledDocument(status, InstalledBrowserProcess.Headers(response.GetProperty("headers")),
                    response.GetProperty("url").GetString() ?? string.Empty,
                    (int)Stopwatch.GetElapsedTime(load.DocumentAsked).TotalMilliseconds, load.Elapsed);
                Report(load, parameters, status);
                break;
            case "Network.responseReceived":
                Report(load, parameters, parameters.GetProperty("response").GetProperty("status").GetInt32());
                break;
            case "Network.loadingFinished" or "Network.loadingFailed":
                load.Release(parameters.GetProperty("requestId").GetString());
                break;
            case "Fetch.requestPaused":
                _ = Task.Run(() => OnInstalledRequestAsync(connection, load, sessionId, parameters));
                break;
            case "Fetch.authRequired":
                _ = Task.Run(() => OnInstalledAuthAsync(connection, sessionId, parameters));
                break;
            case "Page.javascriptDialogOpening":
                _ = connection.SendAsync("Page.handleJavaScriptDialog", new { accept = false }, sessionId)
                    .ContinueWith(static done => done.Exception, TaskScheduler.Default);
                break;
        }
    }

    private static void Report(InstalledLoad load, JsonElement parameters, int status)
    {
        if (parameters.TryGetProperty("requestId", out var id) && load.Lease(id.GetString()) is { } held)
        {
            held.Lease.ReportStatus(status, null, held.Uri);
        }
    }

    private static bool IsTopDocument(InstalledLoad load, JsonElement parameters) =>
        parameters.TryGetProperty("type", out var type) && type.GetString() == "Document" &&
        parameters.TryGetProperty("frameId", out var frame) && frame.GetString() == load.FrameId;

    /// <summary>
    /// Sends one of the page's requests on, or not, by the rules a rendered page's requests are held to
    /// (<see cref="RouteAsync"/>). While the site is refusing the page, everything its check asks for is let through.
    /// </summary>
    private async Task OnInstalledRequestAsync(DevToolsConnection connection, InstalledLoad load, string sessionId, JsonElement parameters)
    {
        var requestId = parameters.GetProperty("requestId").GetString()!;
        try
        {
            var request = parameters.GetProperty("request");
            var resourceType = parameters.TryGetProperty("resourceType", out var kind) ? kind.GetString() : null;
            if (!Uri.TryCreate(request.GetProperty("url").GetString(), UriKind.Absolute, out var uri) || uri.Scheme is not "http" and not "https")
            {
                await connection.SendAsync("Fetch.continueRequest", new { requestId }, sessionId).ConfigureAwait(false);
                return;
            }

            var isTop = resourceType == "Document" && parameters.TryGetProperty("frameId", out var frame) && frame.GetString() == load.FrameId;
            var sent = request.GetProperty("headers");
            var refuse = await guard.RefusesAsync(uri).ConfigureAwait(false) || await ResolvesOnlyPrivatelyAsync(uri).ConfigureAwait(false);
            // Where a redirect leads is the crawl's to ask for, as its own URL.
            refuse |= isTop && load.Redirect is not null;
            if (!refuse && !load.Refused)
            {
                refuse = resourceType is "Image" or "Media" or "Font" or "WebSocket" || IsAskedAhead(sent) ||
                         (!isTop && await RobotsTxtRefusesAsync(uri).ConfigureAwait(false));
            }

            if (refuse || load.Finished)
            {
                await connection.SendAsync("Fetch.failRequest", new { requestId, errorReason = "Aborted" }, sessionId).ConfigureAwait(false);
                return;
            }

            var headers = ExtraHeadersFor(uri, sent);
            var proceed = headers is null ? (object)new { requestId } : new { requestId, headers };

            // A site's check is a dozen requests in a second or two, answered by its firewall, and it fails if they
            // are spread out. They go at the browser's pace, as a visitor's would. The page itself always takes its turn.
            if (load.Refused && !isTop)
            {
                await connection.SendAsync("Fetch.continueRequest", proceed, sessionId).ConfigureAwait(false);
                return;
            }

            var key = parameters.TryGetProperty("networkId", out var network) && network.GetString() is { } networkId ? networkId : requestId;

            // A redirect's next hop is the same request paused again. The hop before it has been answered, so its slot
            // is given back first: on a host read one request at a time, the next would wait for it for good.
            load.Release(key);
            HostGateLease lease;
            load.Waiting(1);
            try
            {
                lease = await gates.For(uri).AcquireAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                load.Waiting(-1);
            }

            if (!load.Hold(key, lease, uri))
            {
                await connection.SendAsync("Fetch.failRequest", new { requestId, errorReason = "Aborted" }, sessionId).ConfigureAwait(false);
                return;
            }

            try
            {
                await connection.SendAsync("Fetch.continueRequest", proceed, sessionId).ConfigureAwait(false);
            }
            catch
            {
                load.Release(key);
                throw;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The tab closed, or the request had already ended. One still paused would hold the page for good.
            logger.LogDebug(ex, "A request of the installed browser's page was not sent on");
            try
            {
                await connection.SendAsync("Fetch.failRequest", new { requestId, errorReason = "Failed" }, sessionId).ConfigureAwait(false);
            }
            catch (Exception again) when (again is not OutOfMemoryException)
            {
            }
        }
    }

    /// <summary>The proxy's sign-in, when the crawl's proxy asks for one. A site's own is not answered here: the headers carry it.</summary>
    private async Task OnInstalledAuthAsync(DevToolsConnection connection, string sessionId, JsonElement parameters)
    {
        try
        {
            var requestId = parameters.GetProperty("requestId").GetString()!;
            var fromProxy = parameters.TryGetProperty("authChallenge", out var challenge) &&
                            challenge.TryGetProperty("source", out var source) && source.GetString() == "Proxy";
            object answer = fromProxy && BrowserProxy() is { Username: { Length: > 0 } user } proxy
                ? new { response = "ProvideCredentials", username = user, password = proxy.Password ?? string.Empty }
                : new { response = "CancelAuth" };
            await connection.SendAsync("Fetch.continueWithAuth", new { requestId, authChallengeResponse = answer }, sessionId).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            logger.LogDebug(ex, "Answering the installed browser's sign-in request failed");
        }
    }

    /// <summary>
    /// The request's headers with the crawl's own added, where the plain fetch would send them: extra headers and the
    /// site's HTTP sign-in to the site, a crawler signature to its host. Null when there is nothing to add, so the
    /// browser's request goes as it made it.
    /// </summary>
    private object[]? ExtraHeadersFor(Uri uri, JsonElement sent)
    {
        Dictionary<string, string> added = new(StringComparer.OrdinalIgnoreCase);
        var mayCarrySecrets = uri.Scheme == Uri.UriSchemeHttps || !_startsOnHttps;
        if (scope.IsInternal(uri) && mayCarrySecrets)
        {
            foreach (var (name, value) in options.ExtraHeaders)
            {
                added[name] = value;
            }
        }

        if (mayCarrySecrets && LoginFor(options, uri) is { } login)
        {
            added["Authorization"] = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{login.Username}:{login.Password}"));
        }

        if (_signatures.For(uri) is { } signature)
        {
            added[CrawlSignature.INPUT_HEADER] = signature.SignatureInput;
            added[CrawlSignature.SIGNATURE_HEADER] = signature.Signature;
            added[CrawlSignature.AGENT_HEADER] = CrawlSignature.SHOPIFY_AGENT;
        }

        if (added.Count == 0)
        {
            return null;
        }

        List<object> headers = [];
        foreach (var header in sent.EnumerateObject())
        {
            if (!added.ContainsKey(header.Name))
            {
                headers.Add(new { name = header.Name, value = header.Value.GetString() ?? string.Empty });
            }
        }

        headers.AddRange(added.Select(header => (object)new { name = header.Key, value = header.Value }));
        return [.. headers];
    }

    private static bool IsAskedAhead(JsonElement headers)
    {
        foreach (var header in headers.EnumerateObject())
        {
            var name = header.Name;
            if (name.Equals("next-router-prefetch", StringComparison.OrdinalIgnoreCase) ||
                ((name.Equals("sec-purpose", StringComparison.OrdinalIgnoreCase) || name.Equals("purpose", StringComparison.OrdinalIgnoreCase)) &&
                 (header.Value.GetString() ?? string.Empty).Contains("prefetch", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<bool> RobotsTxtRefusesAsync(Uri uri)
    {
        if (!options.RespectRobotsTxt || ReadRobotsTxt is not { } read)
        {
            return false;
        }

        var rules = await read(uri).ConfigureAwait(false);
        return !rules.IsAllowed(uri.PathAndQuery);
    }

    /// <summary>The site's redirect, as the plain fetch stores one: the crawl follows it as its own URL.</summary>
    private static FetchResponse Redirected(Uri uri, InstalledDocument redirect)
    {
        var location = redirect.Headers.FirstOrDefault(header => header.Name == "location").Value;
        return new FetchResponse
        {
            RequestUri = uri,
            Outcome = HttpFetcher.Classify(redirect.Status, null),
            StatusCode = redirect.Status,
            Headers = redirect.Headers,
            ContentType = ContentTypeHeader.MediaTypeOf(redirect.Headers.FirstOrDefault(header => header.Name == "content-type").Value),
            Location = location is not null && Uri.TryCreate(uri, location, out var target) ? target : null,
            FromBrowser = true
        };
    }

    private static FetchResponse Refused(Uri uri, InstalledDocument document, string reason) => new()
    {
        RequestUri = uri,
        Outcome = FetchOutcome.Blocked,
        StatusCode = document.Status,
        Headers = document.Headers,
        ContentType = ContentTypeHeader.MediaTypeOf(document.Headers.FirstOrDefault(header => header.Name == "content-type").Value),
        IsHtml = true,
        TimeToFirstByteMs = document.TimeToFirstByteMs,
        TotalMs = document.TimeToFirstByteMs,
        BlockReason = reason,
        FromBrowser = true
    };

    private async Task CloseInstalledAsync()
    {
        if (_installed is not null)
        {
            await _installed.DisposeAsync().ConfigureAwait(false);
            _installed = null;
        }

        _installedGate.Dispose();
    }

    /// <summary>A document the tab was answered with.</summary>
    /// <param name="At">When, from the start of the load.</param>
    private sealed record InstalledDocument(int Status, List<ResponseHeader> Headers, string Url, int TimeToFirstByteMs, TimeSpan At);

    /// <summary>One page being loaded in the installed browser: what the tab was answered, and the gate slots its requests hold.</summary>
    private sealed class InstalledLoad(string targetId)
    {
        private readonly ConcurrentDictionary<string, (HostGateLease Lease, Uri Uri)> _leases = new(StringComparer.Ordinal);
        private readonly long _began = Stopwatch.GetTimestamp();
        private volatile bool _finished;
        private volatile InstalledDocument? _document;
        private volatile InstalledDocument? _redirect;
        private volatile bool _refused;
        private volatile bool _closed;
        private int _waiting;

        public string TargetId { get; } = targetId;
        public string? SessionId { get; set; }
        public string? FrameId { get; set; }
        public long DocumentAsked { get; set; } = Stopwatch.GetTimestamp();

        /// <summary>The last document the tab was answered with.</summary>
        public InstalledDocument? Document { get => _document; set => _document = value; }

        /// <summary>The first redirect the site answered the page's URL with.</summary>
        public InstalledDocument? Redirect { get => _redirect; set => _redirect = value; }

        /// <summary>The site has refused the page in this load, so a check may be running.</summary>
        public bool Refused { get => _refused; set => _refused = value; }

        /// <summary>The tab has gone: the person closed it, or the browser stopped.</summary>
        public bool Closed { get => _closed; set => _closed = value; }

        public bool Finished => _finished;

        /// <summary>One of the page's requests is waiting for the crawl's own pace, not for the site.</summary>
        public bool IsHeld => Volatile.Read(ref _waiting) > 0;

        public void Waiting(int change) => Interlocked.Add(ref _waiting, change);

        public TimeSpan Elapsed => Stopwatch.GetElapsedTime(_began);

        public (HostGateLease Lease, Uri Uri)? Lease(string? key) =>
            key is not null && _leases.TryGetValue(key, out var held) ? held : null;

        /// <summary>Keeps a request's slot until it is answered. False when the load has ended: the slot is given back.</summary>
        public bool Hold(string key, HostGateLease lease, Uri uri)
        {
            Release(key);
            _leases[key] = (lease, uri);
            if (_finished)
            {
                Release(key);
                return false;
            }

            return true;
        }

        public void Release(string? key)
        {
            if (key is not null && _leases.TryRemove(key, out var held))
            {
                held.Lease.Dispose();
            }
        }

        public void Finish()
        {
            _finished = true;
            foreach (var key in _leases.Keys)
            {
                Release(key);
            }
        }
    }
}
