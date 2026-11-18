namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// A Shopify store's crawl, as far as its crawler access goes (<see cref="ShopifyAccessReader"/>): the store's host and
/// what told it, the site's URLs Shopify refused with 429 without a signature and with one, and those URLs' ids, most
/// important first and at most a recheck's worth, for "Recheck these pages".
/// </summary>
public sealed record ShopifyAccess(string Host, string? Signal, int NotCheckedUnsigned, int NotCheckedSigned, IReadOnlyList<int> NotCheckedUrlIds);
