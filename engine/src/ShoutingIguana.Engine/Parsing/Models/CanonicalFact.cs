namespace ShoutingIguana.Engine.Parsing.Models;

/// <param name="Href">The value as written.</param>
/// <param name="Url">Resolved and normalised target, or null when the href is not a valid http(s) URL.</param>
public sealed record CanonicalFact(string Href, string? Url, bool InHead, bool FromHttpHeader)
{
    /// <summary>
    /// Which of <c>hreflang</c>, <c>lang</c>, <c>media</c> and <c>type</c> the canonical's <c>&lt;link&gt;</c> also has, in
    /// that order and comma-separated (<c>media</c>, <c>hreflang, type</c>): attributes that make it an alternate's, not the
    /// page's own canonical. Null with none, for a Link header, and for a page crawled before the crawler noted them
    /// (<see cref="PageFacts.CollectedVersion"/>).
    /// </summary>
    public string? Attributes { get; init; }

    /// <summary>
    /// Whether search engines take it as the page's canonical: in the head or an HTTP header, as Google requires, and
    /// without the attributes of an alternate ("rel="canonical" annotations with hreflang, lang, media, and type attributes
    /// are not used for canonicalization").
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsHonoured => (InHead || FromHttpHeader) && Attributes is null;
}
