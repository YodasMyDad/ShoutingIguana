namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// How a condition on the crawl's own tables (a title's text, a search, a check's URLs) is read into the URL ids it
/// matches, once per filter set (<see cref="UrlIdSet"/>): the scans that give them, the grid row's column the ids are
/// tested against, and whether the condition matches the rows those ids don't.
/// </summary>
/// <param name="Subject">The row's id tested against the set: its URL's (<c>s.url_id</c>), or another URL's (<c>s.final_url_id</c>).</param>
/// <param name="Negated">The condition matches what the ids don't (a negative operator, or "is empty"), unknown values included.</param>
internal sealed record IdSource(IReadOnlyList<IdScan> Scans, string Subject, bool Negated)
{
    /// <summary>The ids are the grid row's own URL's, so the set's count is the condition's.</summary>
    public bool OnRowId => Subject == "s.url_id";
}
