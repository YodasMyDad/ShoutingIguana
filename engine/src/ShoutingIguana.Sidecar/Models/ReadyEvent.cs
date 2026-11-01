using System.Text.Json.Serialization;

namespace ShoutingIguana.Sidecar.Models;

/// <summary>The one line the sidecar writes to stdout once it serves requests.</summary>
/// <param name="Token">Only when the sidecar made the token up, for development.</param>
internal sealed record ReadyEvent(
    string Event,
    int Port,
    string Version,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Token);
