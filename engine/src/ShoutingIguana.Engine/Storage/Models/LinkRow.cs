using ShoutingIguana.Engine.Parsing.Models;

namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// A link to or from the URL inspected. <see cref="Id"/>, <see cref="Url"/> and the status are the other end's: the page
/// linking for an inlink, the target for an outlink.
/// </summary>
/// <param name="Text">The anchor text of a hyperlink, the alt text of an image, or the language of an hreflang link.</param>
/// <param name="Followed">For a hyperlink, whether search engines follow it: no nofollow on it or on its page.</param>
/// <param name="Fetched">The other end was fetched.</param>
public sealed record LinkRow(
    int Id,
    string Url,
    LinkKind Kind,
    string? Text,
    LinkPosition Position,
    LinkFlags Flags,
    bool? Followed,
    string? Fragment,
    int? StatusCode,
    bool? Indexable,
    bool Fetched);
