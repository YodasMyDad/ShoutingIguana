namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>A library item's rechecks (the fix loop): how many, and the latest in brief.</summary>
internal sealed record RechecksBriefDto(int Count, LastRecheckDto Last);
