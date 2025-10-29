namespace ShoutingIguana.Core.Services.NuGet;

/// <summary>
/// Service for validating package security.
/// </summary>
public interface IPackageSecurityService
{
    /// <summary>
    /// Validates a package and its dependencies against security policies.
    /// </summary>
    /// <param name="packageId">The package ID to validate.</param>
    /// <param name="version">The package version.</param>
    /// <param name="dependencies">List of resolved dependencies.</param>
    /// <param name="feedUrl">The feed URL the package comes from.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Security validation result.</returns>
    Task<SecurityValidationResult> ValidatePackageSecurityAsync(
        string packageId,
        string version,
        IReadOnlyList<DependencyInfo> dependencies,
        string feedUrl,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates package size against limits.
    /// </summary>
    Task<bool> ValidatePackageSizeAsync(string packagePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current security settings.
    /// </summary>
    PackageSecuritySettings GetSettings();
}

/// <summary>
/// Result of security validation.
/// </summary>
public class SecurityValidationResult
{
    public required bool IsValid { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<string> Errors { get; init; } = [];
    public bool IsBlocked { get; init; }
    public string? BlockReason { get; init; }
}

