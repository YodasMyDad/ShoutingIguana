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
    Redirect
}
