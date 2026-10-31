namespace ShoutingIguana.Engine.Urls;

/// <summary>
/// Decides which URLs belong to the site being crawled. Internal means the same host and port as the
/// start URL (http and https count as the same site), optionally widened to every subdomain of the
/// root domain. The start folder narrows what gets parsed and followed; anything outside it on the same
/// site is still status-checked.
/// </summary>
internal sealed class CrawlScope
{
    private readonly string _host;
    private readonly string _rootHost;
    private readonly string? _subdomainSuffix;
    private readonly bool _isDefaultPort;
    private readonly int _port;
    private readonly string _startFolder;

    public CrawlScope(Uri startUri, bool includeSubdomains, bool stayInStartFolder)
    {
        _host = UrlNormalizer.GetHost(startUri);
        _rootHost = _host.StartsWith("www.", StringComparison.Ordinal) ? _host[4..] : _host;
        _subdomainSuffix = includeSubdomains ? "." + _rootHost : null;
        _isDefaultPort = startUri.IsDefaultPort;
        _port = startUri.Port;
        _startFolder = stayInStartFolder ? GetFolder(startUri.AbsolutePath) : "/";
    }

    public bool IsInternal(Uri uri)
    {
        var samePort = uri.IsDefaultPort ? _isDefaultPort : !_isDefaultPort && uri.Port == _port;
        if (!samePort)
        {
            return false;
        }

        var host = UrlNormalizer.GetHost(uri);
        if (host == _host)
        {
            return true;
        }

        return _subdomainSuffix is not null &&
               (host == _rootHost || host.EndsWith(_subdomainSuffix, StringComparison.Ordinal));
    }

    public bool IsInStartFolder(Uri uri) =>
        _startFolder == "/" || uri.AbsolutePath.StartsWith(_startFolder, StringComparison.Ordinal);

    private static string GetFolder(string path)
    {
        var lastSlash = path.LastIndexOf('/');
        return lastSlash <= 0 ? "/" : path[..(lastSlash + 1)];
    }
}
