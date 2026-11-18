namespace ShoutingIguana.Engine.Fetching;

/// <summary>
/// The two ways a URL is stored as not checked because its site kept asking the crawler to slow down, told apart by its
/// error: refused (asked, and answered 429 Too Many Requests at its last try) or never asked (held in a section the site
/// limits apart, whose turn didn't come before the crawl gave up on the section, <see cref="RetryingFetcher.NEVER_ASKED_ERROR"/>).
/// </summary>
internal static class RateLimitedErrors
{
    /// <summary>Not checked because the site kept asking the crawler to slow down: refused, or never asked.</summary>
    public static bool IsRateLimited(string? error) => error?.StartsWith(RetryingFetcher.RATE_LIMITED_ERROR, StringComparison.Ordinal) == true;

    /// <summary>Asked, and answered 429 Too Many Requests at its last try.</summary>
    public static bool WasRefused(string? error) => IsRateLimited(error) && !WasNeverAsked(error);

    /// <summary>Held in a section the site limits apart, and the crawl gave up on the section before its turn came.</summary>
    public static bool WasNeverAsked(string? error) => error?.StartsWith(RetryingFetcher.NEVER_ASKED_ERROR, StringComparison.Ordinal) == true;
}
