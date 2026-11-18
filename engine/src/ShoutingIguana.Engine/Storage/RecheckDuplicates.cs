using ShoutingIguana.Engine.Analysis;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// The duplicate checks a recheck settles with care. A page is a duplicate of others, so whether it still is depends on
/// pages besides it, and a recheck reads only some: a page it didn't fetch may share the text now. So a recheck fetches
/// every stored page of the clusters it judges, and a page it judges is fixed only if its own text changed. One whose
/// text is as it was, and isn't flagged any more because the others changed, is not checked: a full crawl may find a page
/// that took the text meanwhile (revision 1 of the synthetic site plants one).
/// </summary>
internal static class RecheckDuplicates
{
    /// <summary>
    /// SQL over <c>responses r</c>: the text the check compares pages by, as text, or null for a check that isn't a
    /// duplicate check. The exact content's hash, and the near duplicates' whole fingerprint, stand for the content.
    /// </summary>
    public static string? TextOf(string? checkId) => checkId switch
    {
        _ when checkId == IssueCatalog.TITLE_DUPLICATE.Id => "r.title",
        _ when checkId == IssueCatalog.DESCRIPTION_DUPLICATE.Id => "r.meta_description",
        _ when checkId == IssueCatalog.H1_DUPLICATE.Id => "r.h1",
        _ when checkId == IssueCatalog.OG_TITLE_DUPLICATE.Id => "r.og_title",
        _ when checkId == IssueCatalog.OG_DESCRIPTION_DUPLICATE.Id => "r.og_description",
        _ when checkId == IssueCatalog.DUPLICATE_CONTENT.Id => "CAST(r.content_hash AS TEXT)",
        _ when checkId == IssueCatalog.NEAR_DUPLICATE_CONTENT.Id =>
            "r.simhash || ',' || COALESCE(r.simhash_1, '') || ',' || COALESCE(r.simhash_2, '') || ',' || COALESCE(r.simhash_3, '')",
        _ => null
    };
}
