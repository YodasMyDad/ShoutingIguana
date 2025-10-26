using System;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace ShoutingIguana.ViewModels;

public partial class AboutViewModel : ObservableObject
{
    private readonly ILogger<AboutViewModel> _logger;
    private readonly Window _dialog;

    [ObservableProperty]
    private string _buildDate = DateTime.Now.ToString("MMMM dd, yyyy");

    public AboutViewModel(ILogger<AboutViewModel> logger, Window dialog)
    {
        _logger = logger;
        _dialog = dialog;
    }

    [RelayCommand]
    private void OpenDocumentation()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://github.com/yourusername/ShoutingIguana/wiki",
                UseShellExecute = true
            });
        }
        catch
        {
            MessageBox.Show(
                "Documentation: https://github.com/yourusername/ShoutingIguana/wiki",
                "Documentation",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    [RelayCommand]
    private void OpenGitHub()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://github.com/yourusername/ShoutingIguana",
                UseShellExecute = true
            });
        }
        catch
        {
            MessageBox.Show(
                "GitHub: https://github.com/yourusername/ShoutingIguana",
                "GitHub Repository",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        try
        {
            _logger.LogInformation("Checking for updates from About dialog...");

            // Check GitHub API for latest release
            const string currentVersion = "1.0.0";
            const string githubApiUrl = "https://api.github.com/repos/yourusername/ShoutingIguana/releases/latest";
            
            using var httpClient = new System.Net.Http.HttpClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "ShoutingIguana");
            httpClient.Timeout = TimeSpan.FromSeconds(10);

            var response = await httpClient.GetAsync(githubApiUrl);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("GitHub API returned {StatusCode}", response.StatusCode);
                MessageBox.Show(
                    "Unable to check for updates. Please visit the GitHub releases page manually.",
                    "Update Check",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Safely extract properties with fallbacks
            var latestVersionTag = currentVersion;
            var releaseName = "Latest Release";
            var releaseUrl = string.Empty;
            
            if (root.TryGetProperty("tag_name", out var tagElement))
            {
                latestVersionTag = tagElement.GetString()?.TrimStart('v') ?? currentVersion;
            }
            
            if (root.TryGetProperty("name", out var nameElement))
            {
                releaseName = nameElement.GetString() ?? "Latest Release";
            }
            
            if (root.TryGetProperty("html_url", out var urlElement))
            {
                releaseUrl = urlElement.GetString() ?? string.Empty;
            }
            
            _logger.LogInformation("Latest version: {LatestVersion}, Current: {CurrentVersion}", latestVersionTag, currentVersion);

            // Simple version comparison (assumes semantic versioning)
            if (IsNewerVersion(currentVersion, latestVersionTag))
            {
                var result = MessageBox.Show(
                    $"A new version is available!\n\n" +
                    $"Current version: {currentVersion}\n" +
                    $"Latest version: {latestVersionTag}\n" +
                    $"Release: {releaseName}\n\n" +
                    $"Would you like to download the update?",
                    "Update Available",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (result == MessageBoxResult.Yes && !string.IsNullOrEmpty(releaseUrl))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = releaseUrl,
                        UseShellExecute = true
                    });
                }
            }
            else
            {
                MessageBox.Show(
                    $"You are running the latest version of Shouting Iguana (v{currentVersion}).",
                    "No Updates Available",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            _logger.LogInformation("Update check complete");
        }
        catch (System.Net.Http.HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Network error checking for updates");
            MessageBox.Show(
                "Unable to check for updates. Please check your internet connection.",
                "Network Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check for updates");
            MessageBox.Show(
                $"Failed to check for updates: {ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Compares two semantic version strings.
    /// </summary>
    /// <returns>True if newVersion is newer than currentVersion.</returns>
    private static bool IsNewerVersion(string currentVersion, string newVersion)
    {
        try
        {
            var current = Version.Parse(currentVersion);
            var latest = Version.Parse(newVersion);
            return latest > current;
        }
        catch
        {
            // If parsing fails, assume no update
            return false;
        }
    }

    [RelayCommand]
    private void Close()
    {
        _dialog.Close();
    }
}

