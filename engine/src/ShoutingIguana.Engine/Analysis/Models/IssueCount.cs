namespace ShoutingIguana.Engine.Analysis.Models;

/// <param name="Count">URLs flagged with the issue (0 = checked and clean).</param>
public sealed record IssueCount(IssueDefinition Issue, long Count)
{
    /// <summary>
    /// The platform that set everything the check found, which the site's owner can't change (a Shopify store's security
    /// headers), or null when it's the site's own. Such a check is no part of the score or Fix first.
    /// </summary>
    public PlatformOwner? Platform { get; init; }
}
