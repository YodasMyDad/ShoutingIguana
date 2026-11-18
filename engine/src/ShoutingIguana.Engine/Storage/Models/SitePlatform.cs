namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// What the site runs on, as the crawl found it (<see cref="SitePlatformReader"/>): <see cref="Name"/> is
/// <c>shopify</c>, the only platform told so far; <see cref="Host"/> the host whose response first said so, and
/// <see cref="Signal"/> what said so (<c>powered-by: Shopify</c>, <c>Shopify.theme</c>...).
/// </summary>
public sealed record SitePlatform(string Name, string Host, string Signal, int? UrlId);
