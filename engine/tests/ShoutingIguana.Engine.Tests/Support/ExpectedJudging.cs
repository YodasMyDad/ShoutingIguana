using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.Engine.Tests.Support;

/// <summary>
/// What each check needs of a URL to judge it, for the synthetic site's expected comparison
/// (<see cref="TestSite.SyntheticSite.GetChanges"/>), from the catalog, written apart from the engine's own rule so the
/// two prove each other: the response-code checks judge any answer, a check of pages only a page, and the rest any status.
/// </summary>
internal static class ExpectedJudging
{
    private static readonly HashSet<string> RESPONSES =
    [
        IssueCatalog.INTERNAL_CLIENT_ERROR.Id, IssueCatalog.INTERNAL_SERVER_ERROR.Id, IssueCatalog.INTERNAL_UNREACHABLE.Id, IssueCatalog.INTERNAL_BLOCKED.Id
    ];

    public static SyntheticJudging Of(string checkId) =>
        RESPONSES.Contains(checkId) ? SyntheticJudging.Responses
        : IssueCatalog.Find(checkId)?.Subject == IssueSubject.Page ? SyntheticJudging.Pages
        : SyntheticJudging.Urls;
}
