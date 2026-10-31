using System.Collections.Concurrent;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Fetching;

/// <summary>
/// One <see cref="HostGate"/> per host, created on first contact. The site's own hosts are keyed by name alone, so
/// its http and https URLs share one rate and concurrency budget, as do example.com and www.example.com (one
/// server, as a rule); other sites are keyed by host and port.
/// </summary>
internal sealed class HostGateRegistry(CrawlScope scope, Func<bool, HostGate> createGate)
{
    private readonly ConcurrentDictionary<string, HostGate> _gates = new(StringComparer.Ordinal);

    public HostGate For(Uri uri)
    {
        var isInternal = scope.IsInternal(uri);
        var host = UrlNormalizer.GetHost(uri);
        var key = isInternal ? $"site {CrawlScope.SiteHost(host)}" : $"{host}:{uri.Port}";
        return _gates.GetOrAdd(key, static (_, state) => state.CreateGate(state.IsInternal), (CreateGate: createGate, IsInternal: isInternal));
    }
}
