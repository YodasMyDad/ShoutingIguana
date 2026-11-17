using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>One check compared between two crawls (<c>/changes</c>): its state, its counts then and now, and how its rows moved (null when counts only).</summary>
internal sealed record CheckChangeDto(
    string CheckId,
    IssueSeverity? Severity,
    IssueSeverity? SeverityThen,
    CheckChangeState State,
    long? Then,
    long? Now,
    long? Fixed,
    long? Gone,
    long? NotChecked,
    long? Still,
    long? New,
    long? NewUrl,
    long? NotComparedThen)
{
    public static CheckChangeDto From(CheckChange check) => new(check.CheckId, check.Severity, check.SeverityThen, check.State, check.Then, check.Now,
        check.Rows?.Fixed, check.Rows?.Gone, check.Rows?.NotChecked, check.Rows?.Still, check.Rows?.New, check.Rows?.NewUrl, check.Rows?.NotComparedThen);
}
