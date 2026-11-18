using System.Globalization;
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
    /// Begins the Shopify part of the error of a URL a store refused with 429 when its request carried no signature:
    /// "Not checked: the site asked the crawler to slow down (429 Too Many Requests): " and this, then why none was sent
    /// when the crawl had one it couldn't send (<see cref="Explain"/>). Shopify limits unsigned crawlers on purpose, so it
    /// is no fault of the page's, and a signature is the way through.
    /// </summary>
    public const string UNSIGNED_REFUSAL = "Shopify limits crawlers without a signature";

    /// <summary>Begins the Shopify part of the error of a URL refused though its request carried the crawl's signature.</summary>
    public const string SIGNED_REFUSAL = "Shopify refused the crawler's signature";

    /// <summary>Where a store's owner makes a signature, in Shopify's own words.</summary>
    public const string WHERE_TO_SIGN = "Online Store › Preferences › Crawler access › Create signature";

    /// <summary>What to do about pages a store refused an unsigned crawler: the end of the crawl's notice and of the analysis's note.</summary>
    public const string HOW_TO_SIGN =
        $"To check them, create a signature in Shopify admin ({WHERE_TO_SIGN}), add it to the crawl as Shopify crawler access, and recheck them.";

    /// <summary>What to do about pages a store refused though the crawl sent its signature.</summary>
    public const string HOW_TO_RESIGN =
        "It may not be active yet (a new one can take up to an hour), may have expired, or may be for another domain. Check it in Shopify admin, or create a new one, then recheck them.";

    /// <summary>How long a new signature can take to start working, as Screaming Frog's users found.</summary>
    public static readonly TimeSpan ACTIVATION = TimeSpan.FromHours(1);

    /// <summary>
    /// The Shopify part of a refusal's error, naming which problem it is when the crawl can tell: a signature made less
    /// than an hour ago, which may not be active yet; one that had expired; one for the www twin or the bare domain.
    /// Null for a request that had nothing to do with a signature: the writer says Shopify's limit for it once it knows
    /// the site is a store.
    /// </summary>
    public static string? Explain(SignatureSituation situation, DateTimeOffset now) => situation.Kind switch
    {
        SignatureSituationKind.Signed when situation.At is { } created && now >= created && now - created < ACTIVATION =>
            $"{SIGNED_REFUSAL}: it was made {Minutes(now - created)} ago, and a new one can take up to an hour to start working",
        SignatureSituationKind.Signed =>
            $"{SIGNED_REFUSAL}: it may not be active yet (a new one can take up to an hour), may have expired, or may be for another domain",
        SignatureSituationKind.Expired when situation.At is { } expires =>
            $"{UNSIGNED_REFUSAL}: the signature for {situation.Host} expired on {expires.UtcDateTime.ToString("d MMMM yyyy", CultureInfo.InvariantCulture)}",
        SignatureSituationKind.OtherHost =>
            $"{UNSIGNED_REFUSAL}: the signature is for {situation.SignedHost}, not {situation.Host}, and each domain needs its own",
        _ => null
    };

    private static string Minutes(TimeSpan span) =>
        span.TotalMinutes < 1.5 ? "a minute" : $"{Math.Round(span.TotalMinutes).ToString(CultureInfo.InvariantCulture)} minutes";

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
