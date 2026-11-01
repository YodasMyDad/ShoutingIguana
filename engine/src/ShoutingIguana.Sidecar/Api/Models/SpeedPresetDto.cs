namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>A crawl speed the UI offers: the two options it sets, under their option names.</summary>
/// <param name="MaxRequestsPerSecond">0 is no cap, with the engine's automatic backoff.</param>
internal sealed record SpeedPresetDto(string Id, int Concurrency, double MaxRequestsPerSecond);
