using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using ShoutingIguana.Engine.Analysis.Rules;
using ShoutingIguana.Engine.Configuration;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Fetching.Models;

namespace ShoutingIguana.Engine.Fetching;

/// <summary>
/// Fetches one address through a proxy, as a crawl would (no redirects followed, the body never read), and says what
/// happened in plain words: for the proxy test in the app's settings. Only the proxy given is used, never the system's.
/// </summary>
public static class ProxyTester
{
    /// <summary>How long a connection to the proxy may take.</summary>
    public static readonly TimeSpan CONNECT_TIMEOUT = TimeSpan.FromSeconds(10);

    /// <summary>How long the whole fetch may take, the connection included.</summary>
    public static readonly TimeSpan TIMEOUT = TimeSpan.FromSeconds(15);

    /// <summary>A proxy address the crawler can use: absolute, http, https or socks5, with a host.</summary>
    public static bool IsValidProxy(string proxyUrl) => ProxyFactory.IsValid(proxyUrl);

    public static Task<ProxyTestResult> TestAsync(string proxyUrl, Uri url, CancellationToken cancellationToken) =>
        TestAsync(proxyUrl, url, CONNECT_TIMEOUT, TIMEOUT, cancellationToken);

    internal static async Task<ProxyTestResult> TestAsync(
        string proxyUrl, Uri url, TimeSpan connectTimeout, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (!IsValidProxy(proxyUrl))
        {
            throw new ArgumentException("The proxy must be an absolute http, https or socks5 address.", nameof(proxyUrl));
        }

        if (!url.IsAbsoluteUri || url.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException("The address must be an absolute http or https address.", nameof(url));
        }

        var proxy = ProxyFactory.Create(proxyUrl)!;
        var hasCredentials = proxy.Credentials is not null;
        using var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false,
            ConnectTimeout = connectTimeout,
            UseProxy = true,
            Proxy = proxy
        };
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgents.Resolve(UserAgentPreset.ShoutingIguana, null).Header);

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, limit.Token).ConfigureAwait(false);
            return Answered((int)response.StatusCode, Elapsed(stopwatch), hasCredentials);
        }
        catch (HttpRequestException exception)
        {
            return Failed(exception, Elapsed(stopwatch), hasCredentials);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            var elapsed = Elapsed(stopwatch);
            return HasTimeout(exception)
                ? new(ProxyTestOutcome.ProxyUnreachable, null, elapsed, $"The proxy didn't answer within {Seconds(connectTimeout)}.")
                : new(ProxyTestOutcome.Timeout, null, elapsed, $"The site didn't reply through the proxy within {Seconds(timeout)}.");
        }
    }

    private static ProxyTestResult Answered(int status, int elapsed, bool hasCredentials) => status switch
    {
        407 => ProxyAuthentication(status, elapsed, hasCredentials),
        // A gateway error is what a proxy answers when it can't reach the site, though a site can answer it too.
        502 or 504 => new(ProxyTestOutcome.SiteUnreachable, status, elapsed,
            $"The proxy answered {HttpStatusNames.Describe(status)}: it usually means the proxy couldn't reach the site."),
        _ => new(ProxyTestOutcome.Ok, status, elapsed,
            $"The proxy answered and the site replied {HttpStatusNames.Describe(status)} in {Milliseconds(elapsed)}.")
    };

    private static ProxyTestResult ProxyAuthentication(int? status, int elapsed, bool hasCredentials) => hasCredentials
        ? new(ProxyTestOutcome.ProxyAuthRequired, status, elapsed, "The proxy didn't accept the user name and password (407).")
        : new(ProxyTestOutcome.ProxyAuthRequired, status, elapsed, "The proxy asks for a user name and password (407).");

    private static ProxyTestResult Failed(HttpRequestException exception, int elapsed, bool hasCredentials)
    {
        if (exception.StatusCode == HttpStatusCode.ProxyAuthenticationRequired)
        {
            return ProxyAuthentication(407, elapsed, hasCredentials);
        }

        // A SOCKS5 proxy's refusal comes as a failed tunnel, whatever it was: which one it was is in its own words.
        if (SocksRefusals.Of(exception) is { } socks)
        {
            return socks.Refusal switch
            {
                SocksRefusal.LoginRefused or SocksRefusal.LoginNeeded or SocksRefusal.NoUsableMethod => new(ProxyTestOutcome.ProxyAuthRequired, null, elapsed,
                    SocksRefusals.Describe(socks.Refusal, socks.Message, hasCredentials) + "."),
                SocksRefusal.SiteUnreachable => new(ProxyTestOutcome.SiteUnreachable, null, elapsed, "The SOCKS5 proxy let the test in, but couldn't reach the site."),
                _ => new(ProxyTestOutcome.Failed, null, elapsed, SocksRefusals.Describe(socks.Refusal, socks.Message, hasCredentials).TrimEnd('.') + ".")
            };
        }

        return exception.HttpRequestError switch
        {
            HttpRequestError.NameResolutionError => new(ProxyTestOutcome.ProxyRefused, null, elapsed, "The proxy's address couldn't be found."),
            HttpRequestError.ConnectionError when IsRefused(exception) =>
                new(ProxyTestOutcome.ProxyRefused, null, elapsed, "Nothing answered at the proxy's address."),
            HttpRequestError.ConnectionError =>
                new(ProxyTestOutcome.ProxyRefused, null, elapsed, "A connection to the proxy couldn't be made."),
            HttpRequestError.ProxyTunnelError => new(ProxyTestOutcome.SiteUnreachable, (int?)exception.StatusCode, elapsed,
                exception.StatusCode is { } status
                    ? $"The proxy answered {HttpStatusNames.Describe((int)status)}: it couldn't reach the site."
                    : "The proxy answered, but it couldn't reach the site."),
            HttpRequestError.SecureConnectionError => new(ProxyTestOutcome.Tls, null, elapsed,
                "A secure connection couldn't be set up, so nothing was fetched. A certificate may not be trusted."),
            _ => new(ProxyTestOutcome.Failed, null, elapsed, $"The test didn't work: {exception.Message}")
        };
    }

    private static bool IsRefused(Exception exception)
    {
        for (var inner = exception.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is SocketException { SocketErrorCode: SocketError.ConnectionRefused })
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A connection that took longer than its connect timeout is cancelled with a <see cref="TimeoutException"/> inside.</summary>
    private static bool HasTimeout(Exception exception)
    {
        for (var inner = exception.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is TimeoutException)
            {
                return true;
            }
        }

        return false;
    }

    private static int Elapsed(Stopwatch stopwatch) => (int)stopwatch.ElapsedMilliseconds;

    private static string Milliseconds(int elapsed) => $"{elapsed.ToString("N0", CultureInfo.InvariantCulture)} ms";

    private static string Seconds(TimeSpan span) =>
        span.TotalSeconds == 1 ? "1 second" : $"{span.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture)} seconds";
}
