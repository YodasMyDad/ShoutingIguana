using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>"Indexable, and why": the verdict and each test.</summary>
internal sealed record IndexabilityDto(bool? Indexable, IndexabilityReason? Reason, IndexabilityJudge JudgedBy, IReadOnlyList<IndexabilityTestDto> Tests);
