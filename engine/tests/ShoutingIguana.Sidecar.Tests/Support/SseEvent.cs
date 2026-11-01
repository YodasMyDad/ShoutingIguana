using System.Text.Json.Nodes;

namespace ShoutingIguana.Sidecar.Tests.Support;

/// <summary>One server-sent event: its name and its JSON data.</summary>
internal sealed record SseEvent(string Name, JsonNode Data);
