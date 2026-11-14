using System.Net;

namespace ShoutingIguana.Engine.Fetching;

/// <summary>
/// A proxy from the URL the options give (<c>http://user:password@host:port/</c>): the user name and password become its
/// credentials, never part of the address it connects to.
/// </summary>
internal static class ProxyFactory
{
    public static WebProxy? Create(string? proxyUrl)
    {
        if (proxyUrl is null)
        {
            return null;
        }

        var uri = new Uri(proxyUrl);
        var proxy = new WebProxy(new UriBuilder(uri) { UserName = string.Empty, Password = string.Empty }.Uri);
        if (uri.UserInfo.Length > 0)
        {
            var parts = uri.UserInfo.Split(':', 2);
            proxy.Credentials = new NetworkCredential(
                Uri.UnescapeDataString(parts[0]),
                parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty);
        }

        return proxy;
    }

    /// <summary>A proxy address the fetcher can use: absolute, http, https or socks5, with a host.</summary>
    public static bool IsValid(string proxyUrl) =>
        Uri.TryCreate(proxyUrl, UriKind.Absolute, out var uri) &&
        uri.Scheme is "http" or "https" or "socks5" &&
        uri.Host.Length > 0;
}
