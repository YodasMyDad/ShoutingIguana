using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Parsing.Models;

namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>Document basics: viewport, language, encoding, and Open Graph tags on indexable pages.</summary>
internal sealed class HtmlRules : IPageRule
{
    private static readonly string[] OPEN_GRAPH = ["og:title", "og:description", "og:image"];

    public IEnumerable<IssueHit> Check(AnalyzedPage page)
    {
        var facts = page.Facts;
        if (!HasMeta(facts.MetaTags, "viewport"))
        {
            yield return new IssueHit(IssueCatalog.VIEWPORT_MISSING, page.UrlId);
        }

        if (string.IsNullOrWhiteSpace(facts.Language))
        {
            yield return new IssueHit(IssueCatalog.LANG_MISSING, page.UrlId);
        }

        if (facts.DeclaredCharset is null && page.Header("content-type")?.Contains("charset=", StringComparison.OrdinalIgnoreCase) != true)
        {
            yield return new IssueHit(IssueCatalog.CHARSET_MISSING, page.UrlId);
        }

        if (page.IsIndexable && OPEN_GRAPH.Where(name => !HasMeta(facts.MetaTags, name)).ToList() is { Count: > 0 } missing)
        {
            yield return new IssueHit(IssueCatalog.OPEN_GRAPH_MISSING, page.UrlId, $"Missing {string.Join(", ", missing)}");
        }
    }

    private static bool HasMeta(IReadOnlyList<MetaTagFact> tags, string name) =>
        tags.Any(tag => tag.Name == name && !string.IsNullOrWhiteSpace(tag.Content));
}
