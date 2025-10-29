using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ShoutingIguana.Core.Services.NuGet;

/// <summary>
/// Implementation of IPackageSecurityService that validates packages against security policies.
/// </summary>
public class PackageSecurityService : IPackageSecurityService
{
    private readonly ILogger<PackageSecurityService> _logger;
    private readonly PackageSecuritySettings _settings;

    public PackageSecurityService(ILogger<PackageSecurityService> logger)
    {
        _logger = logger;
        _settings = LoadSecuritySettings();
    }

    public PackageSecuritySettings GetSettings() => _settings;

    public Task<SecurityValidationResult> ValidatePackageSecurityAsync(
        string packageId,
        string version,
        IReadOnlyList<DependencyInfo> dependencies,
        string feedUrl,
        CancellationToken cancellationToken = default)
    {
        var warnings = new List<string>();
        var errors = new List<string>();

        try
        {
            // Check if package is blocked
            if (IsPackageBlocked(packageId))
            {
                _logger.LogWarning("Package {PackageId} is on the blocklist", packageId);
                return Task.FromResult(new SecurityValidationResult
                {
                    IsValid = false,
                    IsBlocked = true,
                    BlockReason = $"Package '{packageId}' is on the blocklist",
                    Errors = [$"Package '{packageId}' is blocked by security policy"]
                });
            }

            // Check if allowlist is enforced
            if (_settings.Allowlist.Any() && !IsPackageAllowed(packageId))
            {
                _logger.LogWarning("Package {PackageId} is not on the allowlist", packageId);
                return Task.FromResult(new SecurityValidationResult
                {
                    IsValid = false,
                    IsBlocked = true,
                    BlockReason = $"Package '{packageId}' is not on the allowlist",
                    Errors = [$"Package '{packageId}' is not on the allowlist"]
                });
            }

            // Check feed trust
            if (!IsFeedTrusted(feedUrl) && !_settings.AllowUntrustedFeeds)
            {
                warnings.Add($"Package from untrusted feed: {feedUrl}");
            }

            // Validate dependencies
            foreach (var dep in dependencies)
            {
                // Check if dependency is blocked
                if (IsPackageBlocked(dep.PackageId))
                {
                    errors.Add($"Dependency '{dep.PackageId}' is blocked by security policy");
                    _logger.LogWarning("Dependency {PackageId} is on the blocklist", dep.PackageId);
                }

                // Check dependency depth
                if (dep.Depth > _settings.MaxDependencyDepth)
                {
                    errors.Add($"Dependency depth {dep.Depth} exceeds maximum allowed ({_settings.MaxDependencyDepth})");
                    _logger.LogWarning("Dependency {PackageId} exceeds max depth", dep.PackageId);
                }

                // Check for known vulnerable patterns
                if (HasSuspiciousPattern(dep.PackageId))
                {
                    warnings.Add($"Dependency '{dep.PackageId}' has a suspicious name pattern");
                }
            }

            // Calculate total size
            var totalSize = dependencies.Sum(d => d.PackageSize);
            if (totalSize > _settings.MaxTotalDownloadSize)
            {
                errors.Add($"Total download size ({FormatBytes(totalSize)}) exceeds limit ({FormatBytes(_settings.MaxTotalDownloadSize)})");
            }

            // Warn about large dependency counts
            if (dependencies.Count > 50)
            {
                warnings.Add($"Package has {dependencies.Count} dependencies, which is unusually high");
            }

            var isValid = errors.Count == 0;
            
            if (!isValid)
            {
                _logger.LogWarning("Security validation failed for {PackageId}: {Errors}",
                    packageId, string.Join(", ", errors));
            }
            else if (warnings.Any())
            {
                _logger.LogInformation("Security validation passed with warnings for {PackageId}: {Warnings}",
                    packageId, string.Join(", ", warnings));
            }

            return Task.FromResult(new SecurityValidationResult
            {
                IsValid = isValid,
                Warnings = warnings,
                Errors = errors,
                IsBlocked = !isValid && errors.Any(e => e.Contains("blocked"))
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during security validation for {PackageId}", packageId);
            return Task.FromResult(new SecurityValidationResult
            {
                IsValid = false,
                Errors = [$"Security validation error: {ex.Message}"]
            });
        }
    }

    public Task<bool> ValidatePackageSizeAsync(string packagePath, CancellationToken cancellationToken = default)
    {
        try
        {
            var fileInfo = new FileInfo(packagePath);
            if (!fileInfo.Exists)
            {
                _logger.LogWarning("Package file not found: {Path}", packagePath);
                return Task.FromResult(false);
            }

            if (fileInfo.Length > _settings.MaxPackageSize)
            {
                _logger.LogWarning("Package size {Size} exceeds limit {Limit}",
                    FormatBytes(fileInfo.Length), FormatBytes(_settings.MaxPackageSize));
                return Task.FromResult(false);
            }

            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating package size for {Path}", packagePath);
            return Task.FromResult(false);
        }
    }

    private bool IsPackageBlocked(string packageId)
    {
        foreach (var blockedPattern in _settings.Blocklist)
        {
            if (blockedPattern.Contains('*'))
            {
                // Simple wildcard matching
                var pattern = "^" + System.Text.RegularExpressions.Regex.Escape(blockedPattern)
                    .Replace("\\*", ".*") + "$";
                if (System.Text.RegularExpressions.Regex.IsMatch(packageId, pattern, 
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    return true;
                }
            }
            else if (packageId.Equals(blockedPattern, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsPackageAllowed(string packageId)
    {
        if (!_settings.Allowlist.Any())
        {
            return true; // Empty allowlist means everything is allowed
        }

        foreach (var allowedPattern in _settings.Allowlist)
        {
            if (allowedPattern.Contains('*'))
            {
                // Simple wildcard matching
                var pattern = "^" + System.Text.RegularExpressions.Regex.Escape(allowedPattern)
                    .Replace("\\*", ".*") + "$";
                if (System.Text.RegularExpressions.Regex.IsMatch(packageId, pattern,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    return true;
                }
            }
            else if (packageId.Equals(allowedPattern, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsFeedTrusted(string feedUrl)
    {
        return _settings.TrustedFeeds.Any(trusted =>
            feedUrl.Equals(trusted, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasSuspiciousPattern(string packageId)
    {
        // Basic checks for suspicious patterns
        var suspiciousPatterns = new[]
        {
            "malicious",
            "hack",
            "crack",
            "keygen",
            "exploit"
        };

        return suspiciousPatterns.Any(pattern =>
            packageId.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB"];
        double len = bytes;
        int order = 0;
        
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }

        return $"{len:0.##} {sizes[order]}";
    }

    private PackageSecuritySettings LoadSecuritySettings()
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var configPath = Path.Combine(appData, "ShoutingIguana", "security-settings.json");

            if (File.Exists(configPath))
            {
                var json = File.ReadAllText(configPath);
                var settings = JsonSerializer.Deserialize<PackageSecuritySettings>(json);
                
                if (settings != null)
                {
                    _logger.LogInformation("Loaded security settings from {Path}", configPath);
                    return settings;
                }
            }

            // Return default settings and save them
            var defaultSettings = PackageSecuritySettings.Default;
            SaveSecuritySettings(defaultSettings, configPath);
            
            _logger.LogInformation("Using default security settings");
            return defaultSettings;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load security settings, using defaults");
            return PackageSecuritySettings.Default;
        }
    }

    private void SaveSecuritySettings(PackageSecuritySettings settings, string configPath)
    {
        try
        {
            var directory = Path.GetDirectoryName(configPath);
            if (directory != null)
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(configPath, json);
            
            _logger.LogInformation("Saved security settings to {Path}", configPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save security settings");
        }
    }
}

