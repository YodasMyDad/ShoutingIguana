namespace ShoutingIguana.Engine.Parsing.Models;

public enum LinkKind : byte
{
    Hyperlink,
    Image,
    Script,
    Stylesheet,
    Iframe,
    Media,
    Icon,
    Canonical,
    Hreflang,
    Pagination,
    MetaRefresh,
    Redirect,

    /// <summary>An og:image or twitter:image given as a full URL: the image a network shows with a share (batch 9).</summary>
    SocialImage
}
