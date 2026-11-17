using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Sidecar.Api.Models;

namespace ShoutingIguana.Sidecar.Options;

/// <summary>The crawl speeds the UI offers. Provisional until the IA sign-off.</summary>
internal static class SpeedPresets
{
    private static readonly CrawlOptions ENGINE = new() { StartUrl = string.Empty };

    public static IReadOnlyList<SpeedPresetDto> ALL { get; } =
    [
        new("polite", 2, 2),

        // The engine's defaults: no cap. Each host still starts slowly and slows down when it asks.
        new("standard", ENGINE.Concurrency, ENGINE.MaxRequestsPerSecond),
        new("fast", 16, 0)
    ];
}
