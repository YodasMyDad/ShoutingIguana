namespace ShoutingIguana.Engine.Fetching.Models;

/// <summary>
/// What rendering a page gave. <paramref name="Html"/> is the page once its scripts have run, or null to keep the
/// HTTP response. <paramref name="LeftFor"/> is where the page's script sent the browser, when it loaded another
/// page: the crawl follows that as it follows a redirect, and the page itself is kept as it was sent.
/// </summary>
internal sealed record RenderResult(string? Html, Uri? LeftFor = null)
{
    /// <summary>The HTTP response is kept, and nothing more is known.</summary>
    public static RenderResult None { get; } = new((string?)null);
}
