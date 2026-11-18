using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Fetching.Models;

namespace ShoutingIguana.Engine.Fetching;

/// <summary>
/// Which request carries which crawler signature: the one made for the request's exact authority (host, and port when
/// it isn't the default), over https only, and only until it expires. Never another host's, a subdomain's or the www
/// twin's without one of its own, so a redirect to another host, a link to another site and a resource on a CDN all go
/// unsigned. A signature not in the form Shopify gives is never sent: the crawl's options refuse it before it starts.
/// </summary>
internal sealed class SignatureBook
{
    private readonly Dictionary<string, (CrawlSignature Signature, DateTimeOffset? Created, DateTimeOffset? Expires)> _byAuthority = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;

    public SignatureBook(IReadOnlyDictionary<string, CrawlSignature> signatures, TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        foreach (var (host, signature) in signatures)
        {
            var reading = WebBotSignature.Read(signature?.SignatureInput, signature?.Signature);
            if (signature is not null && reading.IsValid && WebBotSignature.TryNormalizeHost(host, out var authority))
            {
                _byAuthority.TryAdd(authority, (signature, reading.Created, reading.Expires));
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

    /// <summary>
    /// What the signatures had to do with a request to <paramref name="uri"/> now, for a refusal to say which problem it
    /// is: it was signed (and when the signature was made), its host's signature had expired, or the signature given is
    /// its www twin's or its bare domain's. Over http nothing is signed, which is no signature's fault: none.
    /// </summary>
    public SignatureSituation Explain(Uri uri)
    {
        var authority = WebBotSignature.AuthorityOf(uri);
        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            return SignatureSituation.None(authority);
        }

        if (_byAuthority.TryGetValue(authority, out var entry))
        {
            return entry.Expires <= _timeProvider.GetUtcNow()
                ? new SignatureSituation(SignatureSituationKind.Expired, authority, authority, entry.Expires)
                : new SignatureSituation(SignatureSituationKind.Signed, authority, authority, entry.Created);
        }

        var twin = authority.StartsWith("www.", StringComparison.Ordinal) ? authority["www.".Length..] : $"www.{authority}";
        return _byAuthority.ContainsKey(twin)
            ? new SignatureSituation(SignatureSituationKind.OtherHost, authority, twin)
            : SignatureSituation.None(authority);
    }
}
