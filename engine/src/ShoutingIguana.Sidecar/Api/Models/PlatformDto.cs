namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>
/// The platform that set everything a check found, which the site's owner can't change: on a Shopify store, its security
/// headers and the cookies Shopify sets.
/// </summary>
/// <param name="Name">The platform as people know it: "Shopify".</param>
/// <param name="Reason">Why it isn't the site's to fix, one sentence: "Set by Shopify. You can't change this on a Shopify store."</param>
internal sealed record PlatformDto(string Name, string Reason);
