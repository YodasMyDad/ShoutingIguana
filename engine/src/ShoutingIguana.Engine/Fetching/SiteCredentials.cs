using System.Net;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Fetching;

/// <summary>
/// The crawl's HTTP logins and the cookies it sends from the start. Logins are registered for Basic and Digest, and
/// only for the host they name, so a password is never sent to a different host. www and the bare host share a login
/// that was given for the site.
/// </summary>
internal static class SiteCredentials
{
    /// <summary>Basic and Digest for each login that has a password. Empty when the crawl signs in no other way.</summary>
    public static CredentialCache Cache(CrawlOptions options)
    {
        var cache = new CredentialCache();
        if (!Uri.TryCreate(options.StartUrl, UriKind.Absolute, out var start))
        {
            return cache;
        }

        var startHost = UrlNormalizer.GetHost(start);
        var port = start.IsDefaultPort ? (int?)null : start.Port;
        foreach (var login in options.HttpLogins)
        {
            if (login.Username.Length == 0 || login.Password.Length == 0)
            {
                continue;
            }

            var named = login.Host.Trim();
            var forSite = named.Length == 0 || CrawlScope.SiteHost(named) == CrawlScope.SiteHost(startHost);
            var hosts = forSite ? Twins(named.Length == 0 ? startHost : named) : [named.ToLowerInvariant()];
            var credential = new NetworkCredential(login.Username, login.Password);
            foreach (var host in hosts)
            {
                Add(cache, host, forSite ? port : null, credential);
            }
        }

        return cache;
    }

    /// <summary>Puts <paramref name="cookies"/> into the jar the crawl sends. A cookie with no value is the file's redacted copy, and is not sent.</summary>
    public static void Add(CookieContainer jar, Uri start, IEnumerable<SentCookie> cookies)
    {
        foreach (var cookie in cookies)
        {
            if (cookie.Name.Length == 0 || cookie.Value.Length == 0)
            {
                continue;
            }

            var domain = cookie.Domain.Length == 0 ? start.Host : cookie.Domain.Trim();
            var path = cookie.Path.Length == 0 ? "/" : cookie.Path;
            jar.Add(new Cookie(cookie.Name, cookie.Value, path, domain) { Secure = cookie.Secure });
        }
    }

    /// <summary>The host and its www twin. An address that is already an IP is only itself.</summary>
    private static IEnumerable<string> Twins(string host)
    {
        host = host.ToLowerInvariant();
        yield return host;
        if (IPAddress.TryParse(host, out _))
        {
            yield break;
        }

        var site = CrawlScope.SiteHost(host);
        if (!string.Equals(host, site, StringComparison.Ordinal))
        {
            yield return site;
        }

        var www = "www." + site;
        if (!string.Equals(host, www, StringComparison.Ordinal))
        {
            yield return www;
        }
    }

    private static void Add(CredentialCache cache, string host, int? port, NetworkCredential credential)
    {
        foreach (var scheme in new[] { Uri.UriSchemeHttp, Uri.UriSchemeHttps })
        {
            var prefix = new UriBuilder(scheme, host) { Path = "/" };
            if (port is { } given)
            {
                prefix.Port = given;
            }

            cache.Add(prefix.Uri, "Basic", credential);
            cache.Add(prefix.Uri, "Digest", credential);
        }
    }
}
