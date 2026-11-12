using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Fetching;
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

        // A byte-order mark declares the encoding too, and beats the other two. A header with an empty charset declares none.
        if (facts.DeclaredCharset is null && facts.ByteOrderMark is null && ContentTypeHeader.CharsetOf(page.Header("content-type")) is null)
        {
            yield return new IssueHit(IssueCatalog.CHARSET_MISSING, page.UrlId);
        }

        if (page.IsIndexable && OPEN_GRAPH.Where(name => !HasMeta(facts.MetaTags, name)).ToList() is { Count: > 0 } lacking)
        {
            // A tag that's there but empty does no more than a missing one, but the fix is different, so say which.
            var empty = lacking.Where(name => facts.MetaTags.Any(tag => tag.Name == name)).ToList();
            var missing = lacking.Except(empty).ToList();
            var detail = (missing.Count > 0, empty.Count > 0) switch
            {
                (true, true) => $"Missing {string.Join(", ", missing)}; empty {string.Join(", ", empty)}",
                (true, false) => $"Missing {string.Join(", ", missing)}",
                _ => $"Empty {string.Join(", ", empty)}"
            };

            yield return new IssueHit(IssueCatalog.OPEN_GRAPH_MISSING, page.UrlId, detail);
        }
    }

    private static bool HasMeta(IReadOnlyList<MetaTagFact> tags, string name) =>
        tags.Any(tag => tag.Name == name && !string.IsNullOrWhiteSpace(tag.Content));
}
