using ShoutingIguana.Engine.Configuration.Models;

namespace ShoutingIguana.Engine.Fetching;

/// <summary>
/// Which request carries which crawler signature: the one made for the request's exact authority (host, and port when
/// it isn't the default), over https only, and only until it expires. Never another host's, a subdomain's or the www
/// twin's without one of its own, so a redirect to another host, a link to another site and a resource on a CDN all go
/// unsigned. A signature not in the form Shopify gives is never sent: the crawl's options refuse it before it starts.
/// </summary>
internal sealed class SignatureBook
{
    private readonly Dictionary<string, (CrawlSignature Signature, DateTimeOffset? Expires)> _byAuthority = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;

    public SignatureBook(IReadOnlyDictionary<string, CrawlSignature> signatures, TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        foreach (var (host, signature) in signatures)
        {
            var reading = WebBotSignature.Read(signature?.SignatureInput, signature?.Signature);
            if (signature is not null && reading.IsValid && WebBotSignature.TryNormalizeHost(host, out var authority))
            {
                _byAuthority.TryAdd(authority, (signature, reading.Expires));
            }
        }
    }

    /// <summary>The signature to send with a request to <paramref name="uri"/> now, or null to send none.</summary>
    public CrawlSignature? For(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps &&
        _byAuthority.TryGetValue(WebBotSignature.AuthorityOf(uri), out var entry) &&
        !(entry.Expires <= _timeProvider.GetUtcNow())
            ? entry.Signature
            : null;
}
