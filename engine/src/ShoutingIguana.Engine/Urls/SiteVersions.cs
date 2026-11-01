namespace ShoutingIguana.Engine.Urls;

/// <summary>
/// The other versions of a site, which search engines treat as separate sites: the bare domain and www, each over https
/// and http. The crawler probes them at the start of a crawl, and the analysis checks that each one redirects to the
/// main one. An IP address, or a host without a dot such as localhost, has no www twin. On the default port each scheme
/// keeps its own default port; on any other port both schemes are asked there, as on a server that answers http and
/// https on one port.
/// </summary>
internal static class SiteVersions
{
    private const string WWW = "www.";

    /// <summary>The start URL's path and query on every other version of its site, normalised: https before http, the bare domain before www.</summary>
    public static IReadOnlyList<string> Of(Uri start)
    {
        var host = UrlNormalizer.GetHost(start);
        var root = CrawlScope.SiteHost(host);
        string[] hosts = HasWwwTwin(root) ? [root, WWW + root] : [host];
        var port = start.IsDefaultPort ? -1 : start.Port;
        var self = UrlNormalizer.Normalize(start);
        return
        [
            .. from scheme in new[] { Uri.UriSchemeHttps, Uri.UriSchemeHttp }
               from versionHost in hosts
               let url = UrlNormalizer.Normalize(new UriBuilder(start) { Scheme = scheme, Host = versionHost, Port = port }.Uri)
               where url != self
               select url
        ];
    }

    /// <summary>The scheme, host and port of a URL, as robots.txt rows and origins are keyed: https://example.com.</summary>
    public static string OriginOf(string url) => new Uri(url).GetLeftPart(UriPartial.Authority);

    private static bool HasWwwTwin(string root) => Uri.CheckHostName(root) == UriHostNameType.Dns && root.Contains('.');
}
