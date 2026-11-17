using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Parsing.Models;

namespace ShoutingIguana.Engine.Analysis;

/// <summary>
/// The checks that need a page fact only later collections have (<see cref="PageFacts.CollectedVersion"/>), with the
/// collection that first had it. On a crawl with any page parsed before that, such a check is skipped, saying why: a fact
/// that wasn't collected isn't the page's lacking it. A check whose fact can only add flags where it's absent runs as
/// before and isn't listed (a rel="prev" a canonical check also reads from the URL; a Refresh header).
/// </summary>
internal static class CollectedFacts
{
    public static IReadOnlyDictionary<IssueDefinition, int> Needs { get; } = new Dictionary<IssueDefinition, int>
    {
        [IssueCatalog.CANONICAL_INVALID_ATTRIBUTE] = 1,
        [IssueCatalog.UNCRAWLABLE_LINKS] = 1,
        [IssueCatalog.PAGINATION_NOT_LINKED] = 1,
        [IssueCatalog.PAGINATION_BROKEN] = 1,
        [IssueCatalog.PAGINATION_TO_NON_INDEXABLE] = 1,
        [IssueCatalog.PAGINATION_SEQUENCE_ERROR] = 1,
        [IssueCatalog.HEAD_TAGS_IN_BODY] = 1,
        [IssueCatalog.HTML_STRUCTURE_ERRORS] = 1,
        [IssueCatalog.MOBILE_ALTERNATE] = 1,
        [IssueCatalog.IMAGE_MISSING_SRC] = 2,
        [IssueCatalog.IMAGE_NO_SRCSET] = 2,
        [IssueCatalog.IMAGE_INLINE_LARGE] = 2,
        [IssueCatalog.FORM_NOT_SECURE] = 3,
        [IssueCatalog.PROTOCOL_RELATIVE_LINKS] = 3
    };
}
