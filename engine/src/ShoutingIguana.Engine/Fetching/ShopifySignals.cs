using ShoutingIguana.Engine.Fetching.Models;

namespace ShoutingIguana.Engine.Fetching;

/// <summary>
/// Tells a Shopify store by its responses. Every storefront response of the Jitsunami crawl (pages, the cart, policies,
/// a redirect from www) carried <c>powered-by: Shopify</c> and <c>shopify-complexity-score</c>, and none of Cloudflare's
/// own redirects did; Shopify has also sent <c>x-shopid</c>, <c>x-shopify-stage</c> and <c>x-sorting-hat-shopid</c>. A
/// page's markup says so too (<c>Parsing.Models.PageFacts.ShopifyMarkup</c>), read in the one parse.
/// </summary>
public static class ShopifySignals
{
    public const string PLATFORM = "shopify";

    /// <summary>
    /// Ends the error of a URL a Shopify store refused with 429 when the crawl sent no signature for its host: "Not
    /// checked: the site asked the crawler to slow down (429 Too Many Requests): " and this. Shopify limits unsigned
    /// crawlers on purpose, so it is no fault of the page's, and a signature is the way through.
    /// </summary>
    public const string UNSIGNED_REFUSAL = "Shopify limits crawlers without a signature";

    /// <summary>Ends the error of a URL refused with 429 though its request carried the crawl's signature.</summary>
    public const string SIGNED_REFUSAL = "Shopify refused the crawler's signature, which may be invalid, expired or for another domain (a new one can take up to an hour to start working)";

    /// <summary>Headers only a Shopify storefront sends, whatever their value.</summary>
    private static readonly string[] HEADERS = ["x-shopid", "x-shopify-stage", "x-sorting-hat-shopid", "shopify-complexity-score"];

    /// <summary>The first header that says the response is a Shopify store's, named, or null for none.</summary>
    public static string? FromHeaders(IReadOnlyList<ResponseHeader> headers)
    {
        foreach (var header in headers)
        {
            if (header.Name == "powered-by" && header.Value.Contains("shopify", StringComparison.OrdinalIgnoreCase))
            {
                return "powered-by: Shopify";
            }

            if (HEADERS.Contains(header.Name, StringComparer.Ordinal))
            {
                return header.Name;
            }
        }

        return null;
    }
}
