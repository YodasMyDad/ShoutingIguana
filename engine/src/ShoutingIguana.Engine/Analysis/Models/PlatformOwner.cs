namespace ShoutingIguana.Engine.Analysis.Models;

/// <summary>
/// The platform a site runs on, when it sets what a check found and the site's owner can't change it: a Shopify store's
/// security headers, and the cookies Shopify sets (<see cref="Fetching.ShopifySignals.OWNER"/>). Such a check is still
/// reported, marked with this, and is no part of the score or Fix first.
/// </summary>
/// <param name="Name">The platform as people know it: "Shopify".</param>
/// <param name="Reason">Why it isn't the site's to fix, one sentence: "Set by Shopify. You can't change this on a Shopify store."</param>
public sealed record PlatformOwner(string Name, string Reason);
