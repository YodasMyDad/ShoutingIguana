using ShoutingIguana.Engine.Parsing.Models;

namespace ShoutingIguana.Engine.Parsing;

public static class LinkKindExtensions
{
    /// <summary>Kinds that point at documents to crawl, as opposed to resources that only get a status check.</summary>
    public static bool IsPage(this LinkKind kind) =>
        kind is LinkKind.Hyperlink or LinkKind.Canonical or LinkKind.Hreflang or LinkKind.Pagination
            or LinkKind.MetaRefresh or LinkKind.Redirect;

    /// <summary>Kinds whose target keeps the source's depth; a redirect is not an extra click.</summary>
    public static bool KeepsDepth(this LinkKind kind) => kind is LinkKind.Redirect or LinkKind.MetaRefresh;
}
