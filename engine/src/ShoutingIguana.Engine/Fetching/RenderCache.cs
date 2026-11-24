using ShoutingIguana.Engine.Fetching.Models;

namespace ShoutingIguana.Engine.Fetching;

/// <summary>
/// The scripts and stylesheets a crawl's rendered pages have asked for, by URL, so the second page that asks for one
/// is given it without another request to the site. One for a crawl, gone with it. Each page is still rendered in a
/// browser context of its own, so no cookie or stored value passes from one page to the next: only these files do,
/// which are the same for every visitor.
/// <para>
/// Bounded: an answer larger than <see cref="MAX_ENTRY_BYTES"/> is not kept, and once the bodies pass the budget the
/// ones asked for longest ago are dropped. Thread-safe.
/// </para>
/// </summary>
internal sealed class RenderCache(long budgetBytes = RenderCache.DEFAULT_BUDGET_BYTES)
{
    /// <summary>The most memory the bodies may take: enough for a large site's bundles, small beside the browser.</summary>
    public const long DEFAULT_BUDGET_BYTES = 64 * 1024 * 1024;

    /// <summary>A body larger than this is not kept.</summary>
    public const int MAX_ENTRY_BYTES = 4 * 1024 * 1024;

    /// <summary>Headers that describe how a body was sent. A kept body is sent again as it is, so they'd be wrong.</summary>
    private static readonly HashSet<string> TRANSPORT_HEADERS = new(StringComparer.OrdinalIgnoreCase)
    {
        "content-encoding", "content-length", "transfer-encoding", "connection", "keep-alive"
    };

    private readonly Lock _sync = new();
    private readonly Dictionary<string, LinkedListNode<(string Url, CachedResponse Response)>> _byUrl = new(StringComparer.Ordinal);
    private readonly LinkedList<(string Url, CachedResponse Response)> _recent = new();
    private long _bytes;

    /// <summary>The bytes of every body kept now. Never above the budget.</summary>
    public long Bytes
    {
        get
        {
            lock (_sync)
            {
                return _bytes;
            }
        }
    }

    /// <summary>
    /// Whether a request is one whose answer may be kept and given again: a GET for a script or a stylesheet. A
    /// document, a data request and anything a page posts are always asked of the site.
    /// </summary>
    public static bool Serves(string method, string resourceType) =>
        method == "GET" && resourceType is "script" or "stylesheet";

    public bool TryGet(string url, out CachedResponse response)
    {
        lock (_sync)
        {
            if (_byUrl.TryGetValue(url, out var node))
            {
                _recent.Remove(node);
                _recent.AddFirst(node);
                response = node.Value.Response;
                return true;
            }
        }

        response = null!;
        return false;
    }

    /// <summary>
    /// Keeps a 200 the site lets be kept: not one marked <c>no-store</c>, not one that sets a cookie, and not one
    /// that differs by cookie. Returns whether it was kept.
    /// </summary>
    public bool Offer(string url, int status, IReadOnlyDictionary<string, string> headers, byte[] body)
    {
        if (status != 200 || body.Length > MAX_ENTRY_BYTES || body.Length > budgetBytes || !MayKeep(headers))
        {
            return false;
        }

        var kept = new CachedResponse(status, [.. headers.Where(header => !TRANSPORT_HEADERS.Contains(header.Key))], body);
        lock (_sync)
        {
            if (_byUrl.Remove(url, out var old))
            {
                _recent.Remove(old);
                _bytes -= old.Value.Response.Body.Length;
            }

            _byUrl[url] = _recent.AddFirst((url, kept));
            _bytes += body.Length;
            while (_bytes > budgetBytes && _recent.Last is { } oldest)
            {
                _recent.RemoveLast();
                _byUrl.Remove(oldest.Value.Url);
                _bytes -= oldest.Value.Response.Body.Length;
            }
        }

        return true;
    }

    private static bool MayKeep(IReadOnlyDictionary<string, string> headers)
    {
        foreach (var (name, value) in headers)
        {
            if (name.Equals("set-cookie", StringComparison.OrdinalIgnoreCase) ||
                (name.Equals("cache-control", StringComparison.OrdinalIgnoreCase) && value.Contains("no-store", StringComparison.OrdinalIgnoreCase)) ||
                (name.Equals("vary", StringComparison.OrdinalIgnoreCase) &&
                 (value.Contains("cookie", StringComparison.OrdinalIgnoreCase) || value.Contains('*'))))
            {
                return false;
            }
        }

        return true;
    }
}
