using System.Text.Json.Serialization;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <param name="Limits">The limits to put back to the engine's values; all of them when left out or empty.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record ResetAnalysisSettingsRequest(IReadOnlyList<string?>? Limits);
