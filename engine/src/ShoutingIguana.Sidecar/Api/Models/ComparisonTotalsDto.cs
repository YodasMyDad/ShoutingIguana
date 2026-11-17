using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>A comparison's totals: the checks by state, and their rows added up (null when it is counts only).</summary>
internal sealed record ComparisonTotalsDto(CheckStateCountsDto Checks, RowSplit? Issues);
