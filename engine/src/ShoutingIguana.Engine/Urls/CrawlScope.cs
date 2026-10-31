namespace ShoutingIguana.Engine.Urls;

/// <summary>
/// Decides which URLs belong to the site being crawled. Internal means the start URL's host or its www twin
/// (example.com and www.example.com are one site: one almost always redirects to the other), on the start URL's
/// port (http and https count as the same site), optionally widened to every subdomain of that domain. Only a
/// leading www. is taken off, so subdomains of shop.example.com are shop.example.com's, not example.com's. The
/// start folder narrows what gets parsed and followed; anything outside it on the same site is still status-checked.
/// </summary>
internal sealed class CrawlScope
{
    private const string WWW = "www.";

    private readonly string _rootHost;
    private readonly string _wwwHost;
    private readonly string? _subdomainSuffix;
    private readonly bool _isDefaultPort;
    private readonly int _port;
    private readonly string _startFolder;

    public CrawlScope(Uri startUri, bool includeSubdomains, bool stayInStartFolder)
    {
        _rootHost = SiteHost(UrlNormalizer.GetHost(startUri));
        _wwwHost = WWW + _rootHost;
        _subdomainSuffix = includeSubdomains ? "." + _rootHost : null;
        _isDefaultPort = startUri.IsDefaultPort;
        _port = startUri.Port;
        _startFolder = stayInStartFolder ? GetFolder(startUri.AbsolutePath) : "/";
        StartOrigin = new Uri(startUri.GetLeftPart(UriPartial.Authority));
    }

    /// <summary>The start URL's scheme, host and port: the origin that stands for the site when another of its origins goes quiet.</summary>
    public Uri StartOrigin { get; }

    public bool IsInternal(Uri uri)
    {
        var samePort = uri.IsDefaultPort ? _isDefaultPort : !_isDefaultPort && uri.Port == _port;
        if (!samePort)
        {
            return false;
        }

        var host = UrlNormalizer.GetHost(uri);
        if (host == _rootHost || host == _wwwHost)
        {
            return true;
        }

        return _subdomainSuffix is not null && host.EndsWith(_subdomainSuffix, StringComparison.Ordinal);
    }

    /// <summary>The host without a leading www., which names the site: www.example.com and example.com are one.</summary>
    public static string SiteHost(string host) => host.StartsWith(WWW, StringComparison.Ordinal) ? host[WWW.Length..] : host;

    /// <summary>The folder pages are parsed and followed in: "/" for the whole site.</summary>
    public string StartFolder => _startFolder;

    public bool IsInStartFolder(Uri uri) =>
        _startFolder == "/" || uri.AbsolutePath.StartsWith(_startFolder, StringComparison.Ordinal);

    private static string GetFolder(string path)
    {
        var lastSlash = path.LastIndexOf('/');
        return lastSlash <= 0 ? "/" : path[..(lastSlash + 1)];
    }
}
