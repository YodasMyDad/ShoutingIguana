using System.Collections.Concurrent;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Fetching;

/// <summary>
/// One <see cref="HostGate"/> per host, created on first contact. The site's own hosts are keyed by name alone, so
/// its http and https URLs share one rate and concurrency budget, as do example.com and www.example.com (one
/// server, as a rule); other sites are keyed by host and port. Whether an origin answers is kept apart, per scheme,
/// host and port (<see cref="OriginOf"/>), since a gate's origins can fail one at a time, and so are the site's
/// origins that are down, which the crawl gives up on after a while.
/// </summary>
internal sealed class HostGateRegistry(CrawlScope scope, Func<bool, HostGate> createGate)
{
    private readonly ConcurrentDictionary<string, HostGate> _gates = new(StringComparer.Ordinal);

    // The site's own gates again, by site host, so its progress reads a handful of them rather than every site's.
    private readonly ConcurrentDictionary<string, HostGate> _siteGates = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, OriginReachability> _origins = new(StringComparer.Ordinal);

    // The site's origins that are down, by name, for the outage notices.
    private readonly ConcurrentDictionary<OriginReachability, string> _down = new();
    private int _downCount;

    public HostGate For(Uri uri)
    {
        var isInternal = scope.IsInternal(uri);
        var host = UrlNormalizer.GetHost(uri);
        if (!isInternal)
        {
            return _gates.GetOrAdd($"{host}:{uri.Port}", static (_, create) => create(false), createGate);
        }

        var siteHost = CrawlScope.SiteHost(host);
        if (_siteGates.TryGetValue(siteHost, out var known))
        {
            return known;
        }

        var gate = _gates.GetOrAdd($"site {siteHost}", static (_, create) => create(true), createGate);
        return _siteGates.GetOrAdd(siteHost, gate);
    }

    /// <summary>What spaces out the requests to each of the site's hosts now (example.com and www.example.com are one).</summary>
    public IEnumerable<(string Host, HostGateState State)> ReadSiteStates() => _siteGates.Select(site => (site.Key, site.Value.ReadState()));

    /// <summary>Whether the URL's exact origin (scheme, host and port) answers.</summary>
    public OriginReachability OriginOf(Uri uri) =>
        _origins.GetOrAdd($"{uri.Scheme}://{UrlNormalizer.GetHost(uri)}:{uri.Port}", static _ => new OriginReachability());

    /// <summary>An origin of the site is down from <paramref name="now"/>. Returns true when it wasn't already.</summary>
    public bool ReportDown(OriginReachability origin, Uri uri, DateTimeOffset now)
    {
        // Listed first: an answer that arrives once it is marked down then finds it listed, and takes it off again.
        if (_down.TryAdd(origin, uri.GetLeftPart(UriPartial.Authority)))
        {
            Interlocked.Increment(ref _downCount);
        }

        return origin.MarkDown(now);
    }

    /// <summary>
    /// A response from an origin: it is up again if it was down. And when the site answers, an origin of it that was down
    /// only because nothing answered, and has never answered itself, is gone after all.
    /// </summary>
    public void ReportAnswer(OriginReachability origin, bool isInternal)
    {
        if (origin.ReportAnswer())
        {
            Forget(origin);
        }

        if (!isInternal || Volatile.Read(ref _downCount) == 0)
        {
            return;
        }

        foreach (var down in _down.Keys.Where(down => down.WriteOffIfNeverAnswered()))
        {
            Forget(down);
        }
    }

    /// <summary>The site's origin that has been down longest, paused time aside, or null while none is.</summary>
    public (string Origin, TimeSpan Duration)? LongestOutage(DateTimeOffset now)
    {
        if (Volatile.Read(ref _downCount) == 0)
        {
            return null;
        }

        (string Origin, TimeSpan Duration)? longest = null;
        foreach (var (origin, name) in _down)
        {
            var duration = origin.DownFor(now);
            if (duration > (longest?.Duration ?? TimeSpan.Zero))
            {
                longest = (name, duration);
            }
        }

        return longest;
    }

    /// <summary>The crawl was paused from <paramref name="from"/> to <paramref name="to"/>: the outages don't count that time.</summary>
    public void ExcludePause(DateTimeOffset from, DateTimeOffset to)
    {
        foreach (var origin in _down.Keys)
        {
            origin.ExcludePause(from, to);
        }
    }

    private void Forget(OriginReachability origin)
    {
        if (_down.TryRemove(origin, out _))
        {
            Interlocked.Decrement(ref _downCount);
        }
    }
}
