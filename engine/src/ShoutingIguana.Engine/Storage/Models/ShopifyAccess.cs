namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// A Shopify store's crawl, as far as its crawler access goes (<see cref="ShopifyAccessReader"/>): the store's host and
/// what told it, the site's URLs Shopify refused with 429 without a signature and with one, those URLs' ids, most
/// important first and at most a recheck's worth, for "Recheck these pages", and what was wrong with the signature the
/// crawl had, where it could tell (<see cref="Reasons"/>: "the signature for shop.example expired on 1 March 2027").
/// </summary>
public sealed record ShopifyAccess(
    string Host,
    string? Signal,
    int NotCheckedUnsigned,
    int NotCheckedSigned,
    IReadOnlyList<int> NotCheckedUrlIds,
    IReadOnlyList<string> Reasons);
