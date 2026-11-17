namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>How many checks of a comparison are in each state.</summary>
internal sealed record CheckStateCountsDto(int Fixed, int New, int Fewer, int More, int Same, int NewCheck, int NotCheckedNow);
