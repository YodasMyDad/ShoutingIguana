using ShoutingIguana.Engine.Parsing.Models;

namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>
/// The title and description a page gives networks to show with a share: Open Graph's, else X's (batch 9's duplicate
/// checks, and the responses columns a recheck compares them by).
/// </summary>
internal static class SocialText
{
    public static string? ShareTitle(PageFacts facts) => First(facts.MetaTags, "og:title") ?? First(facts.MetaTags, "twitter:title");

    public static string? ShareDescription(PageFacts facts) => First(facts.MetaTags, "og:description") ?? First(facts.MetaTags, "twitter:description");

    private static string? First(IReadOnlyList<MetaTagFact> tags, string name) =>
        tags.FirstOrDefault(tag => tag.Name == name && !string.IsNullOrWhiteSpace(tag.Content))?.Content;
}
