namespace ShoutingIguana.Engine.Fetching.Models;

/// <summary>One entry of an HTTP Link header, e.g. <c>&lt;https://example.com/&gt;; rel="canonical"</c>.</summary>
/// <param name="Rel">Lowercase rel value; may hold several space-separated tokens.</param>
internal sealed record LinkHeaderEntry(string Url, string Rel, string? Hreflang);
