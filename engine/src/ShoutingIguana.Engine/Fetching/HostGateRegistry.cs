using System.Collections.Concurrent;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Fetching;

/// <summary>
/// One <see cref="HostGate"/> per host, created on first contact. The site's own hosts are keyed by name alone, so
/// its http and https URLs share one rate and concurrency budget, as do example.com and www.example.com (one
/// server, as a rule); other sites are keyed by host and port. Whether an origin answers is kept apart, per scheme,
/// host and port (<see cref="OriginOf"/>), since a gate's origins can fail one at a time.
/// </summary>
internal sealed class HostGateRegistry(CrawlScope scope, Func<bool, HostGate> createGate)
{
    private readonly ConcurrentDictionary<string, HostGate> _gates = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, OriginReachability> _origins = new(StringComparer.Ordinal);

    public HostGate For(Uri uri)
    {
        var isInternal = scope.IsInternal(uri);
        var host = UrlNormalizer.GetHost(uri);
        var key = isInternal ? $"site {CrawlScope.SiteHost(host)}" : $"{host}:{uri.Port}";
        return _gates.GetOrAdd(key, static (_, state) => state.CreateGate(state.IsInternal), (CreateGate: createGate, IsInternal: isInternal));
    }

    /// <summary>Whether the URL's exact origin (scheme, host and port) answers.</summary>
    public OriginReachability OriginOf(Uri uri) =>
        _origins.GetOrAdd($"{uri.Scheme}://{UrlNormalizer.GetHost(uri)}:{uri.Port}", static _ => new OriginReachability());
}
