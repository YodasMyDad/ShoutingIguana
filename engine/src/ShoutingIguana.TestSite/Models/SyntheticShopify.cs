namespace ShoutingIguana.TestSite.Models;

/// <summary>
/// The site dressed as a Shopify store (<see cref="SyntheticSiteOptions.Shopify"/>), for the crawler's Shopify support.
/// Over https only, as a store is: every page answers with Shopify's headers, and the home page has the theme object
/// and two ways off the site over https (a link, and a redirect at <see cref="AWAY_PATH"/>) to the same server under
/// the name <c>localhost</c>, another host. The product pages (/p/) take <see cref="UnsignedProductRequests"/> requests
/// without a signature a minute and answer every other with 429 and <c>Retry-After: 60</c>, a penalty each renews, as a
/// store does an unsigned crawler. A request signed with <see cref="SIGNATURE_INPUT"/> and <see cref="SIGNATURE"/> (and
/// <c>Signature-Agent: "https://shopify.com"</c>) is always answered, unless the signature has expired or the request
/// is to another host than the one it was made for.
/// </summary>
/// <param name="SignatureFor">
/// The host the store's signature was made for, as <c>host:port</c>: null for the site's own, or another to play a
/// signature made for another of the store's domains.
/// </param>
public sealed record SyntheticShopify(int UnsignedProductRequests = 2, string? SignatureFor = null)
{
    /// <summary>The key id of the store's one signature. Made up: no real store has it.</summary>
    public const string KEY_ID = "synthetic-store-fake-key";

    /// <summary>1 Jan 2026, 00:00 UTC.</summary>
    public const long CREATED = 1_767_225_600;

    /// <summary>1 Jan 2100, 00:00 UTC.</summary>
    public const long EXPIRES = 4_102_444_800;

    /// <summary>The store's signature's <c>Signature-Input</c>, as its admin shows it.</summary>
    public const string SIGNATURE_INPUT = $"sig1=(\"@authority\" \"signature-agent\");created=1767225600;expires=4102444800;keyid=\"{KEY_ID}\";alg=\"ed25519\";tag=\"web-bot-auth\"";

    /// <summary>The same signature, expired an hour after it was made: the store refuses it.</summary>
    public const string EXPIRED_SIGNATURE_INPUT = $"sig1=(\"@authority\" \"signature-agent\");created=1767225600;expires=1767229200;keyid=\"{KEY_ID}\";alg=\"ed25519\";tag=\"web-bot-auth\"";

    /// <summary>The store's signature's <c>Signature</c>: made-up bytes in base64 that say so.</summary>
    public const string SIGNATURE = "sig1=:U3ludGhldGljU3RvcmVGYWtlU2lnbmF0dXJlTm90UmVhbFN5bnRoZXRpY1N0b3JlRmFrZVNpZ25hdHVyZU5vdFI=:";

    /// <summary>The <c>Signature-Agent</c> the store asks for, the quotes part of it.</summary>
    public const string AGENT = "\"https://shopify.com\"";

    /// <summary>A path on the store that redirects off it, to the same server under another host's name.</summary>
    public const string AWAY_PATH = "/shopify/away";

    /// <summary>The <c>Retry-After</c> the product pages answer an unsigned crawler with, in seconds.</summary>
    public const int RETRY_AFTER_SECONDS = 60;
}
