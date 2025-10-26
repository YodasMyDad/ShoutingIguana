namespace ShoutingIguana.Core.Services.NuGet;

/// <summary>
/// Represents a NuGet feed configuration.
/// </summary>
public class NuGetFeed
{
    public required string Name { get; init; }
    public required string Url { get; init; }
    public string? Username { get; init; }
    public string? Password { get; init; }
    public bool Enabled { get; init; } = true;
}

