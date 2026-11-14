using System.Text.RegularExpressions;
using ShoutingIguana.Engine.Configuration;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Fetching;

/// <summary>
/// The start URL for an address typed without a scheme ("www.example.co.uk", "127.0.0.1:8080"): https when the site
/// answers there, http when only http answers. Both are asked at once, the site's home page with no body read, so a site
/// on https costs one round trip and a site that only serves http costs no more than https's refusal. Any HTTP status is
/// an answer (a 403 or a 503 is a site there); no answer is a refused connection, a TLS failure or no response in time.
/// Through a proxy, the proxy's own answers are not the site's: a 407 (it wants its password), a refused tunnel, and a
/// 5xx over plain http (it couldn't reach the site, or the site's own error: either way no proof the site is there, and
/// https stays the choice).
/// </summary>
public static partial class StartAddress
{
    /// <summary>The longest either request is given: a guess at a scheme is no place to sit out a slow site.</summary>
    public const int MAX_WAIT_SECONDS = 10;

    public static async Task<StartAddressChoice> ChooseAsync(string address, CrawlOptions options, CancellationToken cancellationToken)
    {
        var trimmed = address.Trim();
        var https = Uri.UriSchemeHttps + Uri.SchemeDelimiter + trimmed;
        if (trimmed.Length == 0 || SchemePattern().IsMatch(trimmed))
        {
            return new StartAddressChoice(trimmed, StartAddressReason.Typed);
        }

        var http = Uri.UriSchemeHttp + Uri.SchemeDelimiter + trimmed;
        if (!Uri.TryCreate(https, UriKind.Absolute, out var httpsUri) || !Uri.TryCreate(http, UriKind.Absolute, out var httpUri))
        {
            // Not an address either way: the crawl's own validation says what is wrong with it.
            return new StartAddressChoice(https, StartAddressReason.Typed);
        }

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var httpsAnswers = AnswersAsync(httpsUri, options, stop.Token);
        var httpAnswers = AnswersAsync(httpUri, options, stop.Token);
        try
        {
            if (await httpsAnswers.ConfigureAwait(false))
            {
                return new StartAddressChoice(https, StartAddressReason.Https);
            }

            return await httpAnswers.ConfigureAwait(false)
                ? new StartAddressChoice(http, StartAddressReason.Http)
                : new StartAddressChoice(https, StartAddressReason.NoAnswer);
        }
        finally
        {
            // https answered first: http's request has nothing left to say.
            await stop.CancelAsync().ConfigureAwait(false);
            await Task.WhenAll(httpsAnswers, httpAnswers).ConfigureAwait(false);
        }
    }

    /// <summary>Whether the site gives any HTTP status of its own. The crawl's proxy, user agent and certificate apply; its extra headers don't.</summary>
    private static async Task<bool> AnswersAsync(Uri uri, CrawlOptions options, CancellationToken cancellationToken)
    {
        // Extra headers can carry a firewall's secret, never sent on a guess (over plain http, anyone could read them).
        var asked = options with
        {
            StartUrl = uri.AbsoluteUri,
            ExtraHeaders = new Dictionary<string, string>(),
            RequestTimeoutSeconds = Math.Min(options.RequestTimeoutSeconds, MAX_WAIT_SECONDS)
        };

        try
        {
            using var fetcher = new HttpFetcher(asked, UserAgents.Resolve(asked.UserAgent, asked.CustomUserAgent),
                new CrawlScope(uri, asked.IncludeSubdomains, asked.StayInStartFolder), TimeProvider.System);
            var response = await fetcher.FetchAsync(uri, FetchBody.None, cancellationToken).ConfigureAwait(false);
            return response.StatusCode is { } status && !IsTheProxysOwnAnswer(uri, status, asked);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// A 407 only ever comes from a proxy. Over https the site answers through the proxy's tunnel (a refused tunnel is an
    /// error, not a status), but over plain http an http proxy answers in the site's place: its 5xx is a gateway error.
    /// </summary>
    private static bool IsTheProxysOwnAnswer(Uri uri, int status, CrawlOptions options) =>
        status == 407 || (status >= 500 && uri.Scheme == Uri.UriSchemeHttp && UsesAnHttpProxy(options));

    private static bool UsesAnHttpProxy(CrawlOptions options) =>
        options.ProxyUrl is { } proxy && Uri.TryCreate(proxy, UriKind.Absolute, out var proxyUri) &&
        proxyUri.Scheme is "http" or "https";

    [GeneratedRegex("^[a-z][a-z0-9+.-]*://", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SchemePattern();
}
