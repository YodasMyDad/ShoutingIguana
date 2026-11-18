using System.Collections.Concurrent;
using ShoutingIguana.Engine.Fetching;
using ShoutingIguana.Engine.Fetching.Models;

namespace ShoutingIguana.Engine.Crawling;

/// <summary>
/// The site's hosts found to be a Shopify store's this run, each with what told it (<see cref="ShopifySignals"/>), and
/// whether the crawl can sign its requests to one. <see cref="PageProcessor"/> fills it as it reads responses. It is the
/// place the crawl's pacing asks whether a host is a store that limits an unsigned crawler
/// (<see cref="IsUnsignedShopify"/>), so it can stop waiting on one rather than sit out its limit.
/// </summary>
internal sealed class ShopifyWatch(SignatureBook signatures)
{
    private readonly ConcurrentDictionary<string, string> _hosts = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<int, bool> _refused = new();

    /// <summary>Any of the site's hosts is a Shopify store's.</summary>
    public bool IsDetected => !_hosts.IsEmpty;

    /// <summary>Notes what a response or page said of its host. True only the first time a host is told to be a store's.</summary>
    public bool Observe(Uri uri, string? signal) => signal is not null && _hosts.TryAdd(WebBotSignature.AuthorityOf(uri), signal);

    /// <summary>The URL's host was told to be a Shopify store's this run.</summary>
    public bool IsShopify(Uri uri) => _hosts.ContainsKey(WebBotSignature.AuthorityOf(uri));

    /// <summary>The URL's host is a Shopify store's, and the crawl has no signature it can send there now (none given, or expired).</summary>
    public bool IsUnsignedShopify(Uri uri) => IsShopify(uri) && signatures.For(uri) is null;

    /// <summary>What the crawl's signatures had to do with a request to <paramref name="uri"/> now (<see cref="SignatureBook.Explain"/>).</summary>
    public SignatureSituation Explain(Uri uri) => signatures.Explain(uri);

    /// <summary>The site's URLs a store refused this run without a signature, each counted once however often it was asked.</summary>
    public int Refused => _refused.Count;

    /// <summary>Counts a URL a store refused without a signature; the count so far.</summary>
    public int NoteRefused(int urlId)
    {
        _refused.TryAdd(urlId, true);
        return _refused.Count;
    }
}
