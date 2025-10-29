namespace ShoutingIguana.Core.Services.NuGet;

/// <summary>
/// Security settings for package installation and validation.
/// </summary>
public class PackageSecuritySettings
{
    /// <summary>
    /// List of approved package IDs. If empty, all packages are allowed (subject to blocklist).
    /// </summary>
    public IReadOnlyList<string> Allowlist { get; init; } = [];

    /// <summary>
    /// List of banned package IDs or package ID patterns (supports wildcards like "*.Malicious").
    /// </summary>
    public IReadOnlyList<string> Blocklist { get; init; } = [];

    /// <summary>
    /// Maximum depth of dependency chain to prevent dependency chain attacks.
    /// Default: 10
    /// </summary>
    public int MaxDependencyDepth { get; init; } = 10;

    /// <summary>
    /// Maximum package size in bytes to prevent resource exhaustion.
    /// Default: 100MB
    /// </summary>
    public long MaxPackageSize { get; init; } = 100 * 1024 * 1024;

    /// <summary>
    /// Whether to require package signatures. Note: nuget.org doesn't always sign packages.
    /// Default: false
    /// </summary>
    public bool RequireSignedPackages { get; init; } = false;

    /// <summary>
    /// List of trusted feed URLs that bypass some security checks.
    /// </summary>
    public IReadOnlyList<string> TrustedFeeds { get; init; } = 
    [
        "https://api.nuget.org/v3/index.json"
    ];

    /// <summary>
    /// Maximum total download size for a package and all its dependencies.
    /// Default: 500MB
    /// </summary>
    public long MaxTotalDownloadSize { get; init; } = 500 * 1024 * 1024;

    /// <summary>
    /// Whether to allow packages from untrusted feeds.
    /// Default: true
    /// </summary>
    public bool AllowUntrustedFeeds { get; init; } = true;

    /// <summary>
    /// Loads default security settings.
    /// </summary>
    public static PackageSecuritySettings Default => new();
}

