using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>The URLs one page's issue names: how many, and the first of them, each with what it is.</summary>
internal sealed record IssueTargetsOfUrlDto(int UrlId, long Total, IReadOnlyList<IssueTargetDto> Targets)
{
    public static IssueTargetsOfUrlDto From(IssueTargetsOfUrl page) => new(page.UrlId, page.Total, [.. page.Targets.Select(IssueTargetDto.From)]);
}
