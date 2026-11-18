using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>
/// <c>GET /api/crawls/{id}/shopify</c>: a Shopify store's crawl, as far as its crawler access goes. <c>host</c> is the
/// store's (a signature is made for it), <c>signal</c> what told it was a store (null when only a refused signature
/// did), <c>notCheckedUnsigned</c> the site's URLs Shopify refused for want of a signature, <c>notCheckedSigned</c> those
/// it refused though the crawl sent one, and <c>urlIds</c> both kinds' ids, most important first, at most a recheck's
/// 1,000: what "Recheck these pages" asks for. The answer is <c>null</c> for any other site.
/// </summary>
internal sealed record ShopifyAccessDto(string Host, string? Signal, int NotCheckedUnsigned, int NotCheckedSigned, IReadOnlyList<int> UrlIds)
{
    public static ShopifyAccessDto From(ShopifyAccess access) =>
        new(access.Host, access.Signal, access.NotCheckedUnsigned, access.NotCheckedSigned, access.NotCheckedUrlIds);
}
