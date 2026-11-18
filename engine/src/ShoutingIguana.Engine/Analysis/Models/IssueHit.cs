namespace ShoutingIguana.Engine.Analysis.Models;

/// <summary>One URL flagged with one issue.</summary>
/// <param name="Detail">A short explanation for this URL, e.g. "78 characters".</param>
/// <param name="RelatedUrlId">The other URL involved, when there is one: the broken target, the duplicate, the canonical.</param>
internal readonly record struct IssueHit(IssueDefinition Issue, int UrlId, string? Detail = null, int? RelatedUrlId = null)
{
    /// <summary>
    /// In <see cref="Targets"/>, and in <c>issue_targets</c>, no other URL: the group of a check grouped by the other URL whose
    /// issues name none, or are about the page itself as well (a link to a section missing on the page itself). URL ids
    /// start at 1, and a key without rowid can't hold a null.
    /// </summary>
    public const int NO_OTHER_URL = 0;

    /// <summary>
    /// Every URL the issue is about, when that isn't just <see cref="RelatedUrlId"/>: each of a page's broken (redirecting,
    /// nofollow, large...) targets, the first of which is the related URL, and <see cref="NO_OTHER_URL"/> when the page
    /// itself is one too; or, for a duplicate, the first page of its cluster, which that page names itself, so a cluster
    /// is one group. Null for the related URL alone.
    /// A link without text to the page itself (<c>empty-anchor-text</c>: a home page's logo) names the page by its id,
    /// as a duplicate's first page does: it groups with the other pages whose logo links there, and its targets read the
    /// link's place and flags. Only a missing section of the page itself (<c>broken-fragment-links</c>) is
    /// <see cref="NO_OTHER_URL"/>, as there's no other page to go to.
    /// </summary>
    public IReadOnlyList<int>? Targets { get; init; }

    /// <summary>
    /// For a check of the site's files (<see cref="IssueDefinition.About"/>): every robots.txt or sitemap it found at fault, as
    /// full URLs in the order its detail names them, stored in <c>issue_files</c> so a view can name the file instead of the
    /// home page the issue is reported on. Null for any other check.
    /// </summary>
    public IReadOnlyList<string>? Files { get; init; }

    /// <summary>The URLs the issue is about, as <see cref="Targets"/> says: those, or the related URL, or none.</summary>
    public IReadOnlyList<int> AllTargets => Targets ?? (RelatedUrlId is { } related ? [related] : []);

    public bool Equals(IssueHit other) =>
        Issue == other.Issue && UrlId == other.UrlId && Detail == other.Detail && RelatedUrlId == other.RelatedUrlId && AllTargets.SequenceEqual(other.AllTargets);

    public override int GetHashCode() => HashCode.Combine(Issue, UrlId, Detail, RelatedUrlId);
}
