namespace ShoutingIguana.Core.Services.NuGet;

/// <summary>
/// Result from a NuGet package search.
/// </summary>
public class PackageSearchResult
{
    public required string Id { get; init; }
    public required string Version { get; init; }
    public string? Description { get; init; }
    public string? Authors { get; init; }
    public long DownloadCount { get; init; }
    public string? IconUrl { get; init; }
    public string? ProjectUrl { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
}

/// <summary>
/// Validation result for a NuGet package.
/// </summary>
public enum PackageValidationResult
{
    Valid,
    NoPlugin,
    IncompatibleSdk,
    InvalidPackage
}

/// <summary>
/// Result of package validation.
/// </summary>
public class ValidationResult
{
    public required PackageValidationResult Result { get; init; }
    public string? ErrorMessage { get; init; }
    public string? PluginId { get; init; }
    public string? PluginName { get; init; }
    public string? PluginVersion { get; init; }
    public string? MinSdkVersion { get; init; }
}

