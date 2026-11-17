namespace ShoutingIguana.TestSite.Models;

/// <summary>
/// A rate limit on the product pages, like the one a real shop's firewall put in front of its products: more than
/// <see cref="MaxRequests"/> product requests within <see cref="Window"/> trips it, and then every product request is
/// answered 429 Too Many Requests with a Retry-After of <see cref="Penalty"/>, a penalty each of those requests renews.
/// The rest of the site answers as ever. <see cref="NeverRelents"/> answers every product request with 429.
/// </summary>
public sealed record SyntheticRateLimit(int MaxRequests, TimeSpan Window, TimeSpan Penalty, bool NeverRelents = false);
