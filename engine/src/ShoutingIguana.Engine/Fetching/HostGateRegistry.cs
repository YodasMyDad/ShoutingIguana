using System.Collections.Concurrent;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Fetching;

/// <summary>One <see cref="HostGate"/> per host and port, created on first contact.</summary>
internal sealed class HostGateRegistry(Func<Uri, HostGate> createGate)
{
    private readonly ConcurrentDictionary<string, HostGate> _gates = new(StringComparer.Ordinal);

    public HostGate For(Uri uri) =>
        _gates.GetOrAdd($"{UrlNormalizer.GetHost(uri)}:{uri.Port}", static (_, state) => state.CreateGate(state.Uri), (CreateGate: createGate, Uri: uri));
}
