namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>Everything the writer persists for one fetched URL, in one transaction.</summary>
/// <param name="PageNoFollow">The page's robots directives say nofollow, so none of its links are followed.</param>
/// <param name="Details">Brotli-compressed JSON of the headers and page facts.</param>
/// <param name="Html">Brotli-compressed HTML, only when storing HTML is switched on.</param>
internal sealed record PageRecord(
    FrontierItem Item,
    ResponseRow Response,
    IReadOnlyList<OutLink> Links,
    bool PageNoFollow,
    byte[]? Details,
    byte[]? Html);
