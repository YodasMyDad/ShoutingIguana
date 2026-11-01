namespace ShoutingIguana.Sidecar.Api.Models;

/// <param name="Options">The user's saved defaults, or the engine's. Never a start URL, header value or proxy password.</param>
internal sealed record DefaultsDto(CrawlOptionsDto Options, IReadOnlyList<SpeedPresetDto> SpeedPresets);
