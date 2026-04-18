using System.Net;
using System.Net.Sockets;

namespace ShoutingIguana.PluginSdk.Helpers;

/// <summary>
/// Crawl-target safety policy: rejects schemes and address ranges that
/// should not be fetched by a crawler (SSRF defense-in-depth).
/// </summary>
public static class CrawlUrlPolicy
{
    /// <summary>
    /// Validates whether <paramref name="url"/> is safe to fetch as a crawl target.
    /// Only <c>http</c> and <c>https</c> schemes are permitted. When
    /// <paramref name="allowPrivateNetworks"/> is false (the default for crawls),
    /// URLs whose host literal resolves to loopback, private, link-local, unspecified,
    /// or multicast ranges are rejected, along with the hostname <c>localhost</c>.
    /// </summary>
    /// <param name="url">The absolute URL to validate.</param>
    /// <param name="allowPrivateNetworks">
    /// When true, private / loopback / link-local targets are permitted. The crawl
    /// still rejects non-http(s) schemes regardless of this flag.
    /// </param>
    /// <param name="rejectionReason">
    /// On false, a short human-readable reason suitable for logging / surfacing to the user.
    /// </param>
    public static bool IsSafeCrawlTarget(string? url, bool allowPrivateNetworks, out string? rejectionReason)
    {
        rejectionReason = null;

        if (string.IsNullOrWhiteSpace(url))
        {
            rejectionReason = "URL is empty.";
            return false;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            rejectionReason = "URL is not a valid absolute URI.";
            return false;
        }

        if (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            rejectionReason = $"Unsupported scheme '{uri.Scheme}'. Only http and https are allowed.";
            return false;
        }

        if (allowPrivateNetworks)
        {
            return true;
        }

        var host = uri.DnsSafeHost;
        if (string.IsNullOrEmpty(host))
        {
            rejectionReason = "URL has no host component.";
            return false;
        }

        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            rejectionReason = "Host 'localhost' is blocked. Enable AllowPrivateNetworkTargets to crawl local services.";
            return false;
        }

        if (IPAddress.TryParse(host, out var ip))
        {
            if (ip.IsIPv4MappedToIPv6)
            {
                ip = ip.MapToIPv4();
            }

            if (IsBlockedAddress(ip, out var blockReason))
            {
                rejectionReason = $"IP address '{ip}' is {blockReason}. Enable AllowPrivateNetworkTargets to crawl it.";
                return false;
            }
        }

        return true;
    }

    private static bool IsBlockedAddress(IPAddress ip, out string reason)
    {
        if (IPAddress.IsLoopback(ip))
        {
            reason = "loopback";
            return true;
        }

        if (ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any))
        {
            reason = "unspecified (0.0.0.0 / ::)";
            return true;
        }

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = ip.GetAddressBytes();

            // 10.0.0.0/8
            if (bytes[0] == 10)
            {
                reason = "private (RFC1918 10.0.0.0/8)";
                return true;
            }
            // 172.16.0.0/12
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
            {
                reason = "private (RFC1918 172.16.0.0/12)";
                return true;
            }
            // 192.168.0.0/16
            if (bytes[0] == 192 && bytes[1] == 168)
            {
                reason = "private (RFC1918 192.168.0.0/16)";
                return true;
            }
            // 169.254.0.0/16 link-local
            if (bytes[0] == 169 && bytes[1] == 254)
            {
                reason = "link-local (169.254.0.0/16)";
                return true;
            }
            // 100.64.0.0/10 Carrier-grade NAT (RFC 6598)
            if (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127)
            {
                reason = "shared address space (RFC6598 100.64.0.0/10)";
                return true;
            }
            // 224.0.0.0/4 multicast
            if (bytes[0] >= 224 && bytes[0] <= 239)
            {
                reason = "multicast (224.0.0.0/4)";
                return true;
            }
        }
        else if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal)
            {
                reason = "IPv6 link-local (fe80::/10)";
                return true;
            }
            if (ip.IsIPv6Multicast)
            {
                reason = "IPv6 multicast (ff00::/8)";
                return true;
            }

            var bytes = ip.GetAddressBytes();
            // fc00::/7 unique local address
            if ((bytes[0] & 0xfe) == 0xfc)
            {
                reason = "IPv6 unique-local (fc00::/7)";
                return true;
            }
        }

        reason = string.Empty;
        return false;
    }
}
