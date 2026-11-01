namespace ShoutingIguana.Sidecar.Api.Models;

internal sealed record HealthDto(string Version, string EngineVersion, string DataDir, string StateDir, int Pid);
