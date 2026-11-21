using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.TestSite;

/// <summary>
/// What the crawler sent the site itself (not the external port), for tests of its options: the values of the
/// request headers a test watches, and the most requests it had open at once.
/// </summary>
public sealed class SyntheticRequestLog
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<string>> _watched = new(StringComparer.OrdinalIgnoreCase);
    private ConcurrentQueue<SyntheticHit>? _hits;
    private int _open;
    private int _mostOpen;

    /// <summary>The most requests the site was answering at once since the start or the last <see cref="ResetMostOpen"/>.</summary>
    public int MostOpen => Volatile.Read(ref _mostOpen);

    /// <summary>From now on, the value of <paramref name="header"/> in every request that sends it.</summary>
    public ConcurrentQueue<string> Watch(string header) => _watched.GetOrAdd(header, _ => new ConcurrentQueue<string>());

    /// <summary>From now on, each request's path and whether it came from a browser. Nothing is kept until a test asks.</summary>
    public ConcurrentQueue<SyntheticHit> WatchHits() => _hits ??= new ConcurrentQueue<SyntheticHit>();

    public void ResetMostOpen() => Interlocked.Exchange(ref _mostOpen, 0);

    internal void Started(HttpRequest request)
    {
        var open = Interlocked.Increment(ref _open);
        for (var most = Volatile.Read(ref _mostOpen); open > most; most = Volatile.Read(ref _mostOpen))
        {
            if (Interlocked.CompareExchange(ref _mostOpen, open, most) == most)
            {
                break;
            }
        }

        foreach (var (header, values) in _watched)
        {
            if (request.Headers.TryGetValue(header, out var value))
            {
                values.Enqueue(value.ToString());
            }
        }

        _hits?.Enqueue(new SyntheticHit(request.Path.Value ?? "/",
            request.Headers.ContainsKey("Sec-Fetch-Mode") || request.Headers.ContainsKey("sec-ch-ua")));
    }

    internal void Finished() => Interlocked.Decrement(ref _open);
}
