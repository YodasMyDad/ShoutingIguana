using System.Net;
using System.Net.Sockets;
using ShoutingIguana.Engine.Fetching.Models;

namespace ShoutingIguana.Engine.Fetching;

/// <summary>
/// Keeps a crawl of a public site off the user's own machine and network: a link to localhost, 127.x, ::1 or a private
/// range (10/8, 172.16/12, 192.168/16, 100.64/10, fc00::/7, link-local) would have the crawler request whatever answers
/// there. Such addresses are asked only when the crawl's start URL is itself on one (a staging or intranet site, the
/// synthetic test site on 127.0.0.1); otherwise they are stored as not checked, which no check judges. A literal address
/// in the URL is refused before any request. A name that resolves to one is refused as it connects, when the crawl
/// connects by itself; through a proxy the proxy resolves names, so only literal addresses are refused.
/// </summary>
internal sealed class PrivateNetworkGuard
{
    /// <summary>The error of a URL refused: "Not checked", so the analysis counts it as never fetched.</summary>
    internal const string ERROR = RetryingFetcher.NOT_CHECKED_ERROR + ": private network address; not checked from a public site";

    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _resolve;
    private readonly Uri? _start;
    private readonly Lazy<Task<bool>> _startIsPrivate;

    /// <param name="resolve">Looks a host name up; DNS unless a test gives its own.</param>
    public PrivateNetworkGuard(string startUrl, Func<string, CancellationToken, Task<IPAddress[]>>? resolve = null)
    {
        _resolve = resolve ?? Dns.GetHostAddressesAsync;
        _start = Uri.TryCreate(startUrl, UriKind.Absolute, out var start) ? start : null;
        _startIsPrivate = new Lazy<Task<bool>>(StartIsPrivateAsync);
    }

    /// <summary>
    /// Whether the crawl refuses <paramref name="uri"/> without a request: its host is a private address or a loopback
    /// name, and the crawl didn't start on one. The start's own name is looked up once, only when a URL like that turns up.
    /// </summary>
    public async ValueTask<bool> RefusesAsync(Uri uri) => IsPrivateLiteral(uri) && !await _startIsPrivate.Value.ConfigureAwait(false);

    /// <summary>
    /// The addresses the crawl may connect to for <paramref name="host"/>: all of them when the crawl started on a private
    /// address or when none is private, else the public ones. None left refuses the connection.
    /// </summary>
    /// <exception cref="PrivateNetworkAddressException">Every address of the host is private, and the crawl started on a public one.</exception>
    public async Task<IPAddress[]> AddressesToConnectToAsync(string host, CancellationToken cancellationToken)
    {
        var addresses = await _resolve(host, cancellationToken).ConfigureAwait(false);
        if (!addresses.Any(IsPrivate) || await _startIsPrivate.Value.ConfigureAwait(false))
        {
            return addresses;
        }

        IPAddress[] allowed = [.. addresses.Where(address => !IsPrivate(address))];
        return allowed.Length > 0 ? allowed : throw new PrivateNetworkAddressException(host);
    }

    /// <summary>A response for a URL refused, stored as not checked.</summary>
    public static FetchResponse NotChecked(Uri uri) => new()
    {
        RequestUri = uri,
        Outcome = FetchOutcome.NetworkError,
        Error = ERROR
    };

    public static bool IsRefusal(FetchResponse response) => response.Error == ERROR;

    /// <summary>Loopback, private, shared (carrier-grade NAT), link-local, unique-local or unspecified.</summary>
    public static bool IsPrivate(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] is 0 or 10 ||
                   bytes[0] == 172 && (bytes[1] & 0xF0) == 16 ||
                   bytes[0] == 192 && bytes[1] == 168 ||
                   bytes[0] == 169 && bytes[1] == 254 ||
                   bytes[0] == 100 && (bytes[1] & 0xC0) == 64;
        }

        return address.Equals(IPAddress.IPv6Any) || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal;
    }

    /// <summary>A private address written into the URL, or a loopback name: localhost and its subdomains (RFC 6761).</summary>
    public static bool IsPrivateLiteral(Uri uri)
    {
        if (uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6)
        {
            return IPAddress.TryParse(uri.DnsSafeHost, out var address) && IsPrivate(address);
        }

        var host = uri.IdnHost.TrimEnd('.');
        return host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The start URL's host is private: written as such, or its name resolves to a private address (an intranet site). A
    /// name that can't be looked up counts as public, the safe side.
    /// </summary>
    private async Task<bool> StartIsPrivateAsync()
    {
        if (_start is null)
        {
            return false;
        }

        if (IsPrivateLiteral(_start))
        {
            return true;
        }

        if (_start.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6)
        {
            return false;
        }

        try
        {
            var addresses = await _resolve(_start.IdnHost, CancellationToken.None).ConfigureAwait(false);
            return addresses.Any(IsPrivate);
        }
        catch (SocketException)
        {
            return false;
        }
    }
}
