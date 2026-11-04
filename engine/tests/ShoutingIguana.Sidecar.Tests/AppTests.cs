using System.Text.Json;
using ShoutingIguana.Engine;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Sidecar.Tests.Support;

namespace ShoutingIguana.Sidecar.Tests;

/// <summary>Health and the check catalog.</summary>
public sealed class AppTests : IAsyncLifetime
{
    private TestSidecar _sidecar = null!;

    public async Task InitializeAsync() => _sidecar = await TestSidecar.StartAsync();

    public async Task DisposeAsync() => await _sidecar.DisposeAsync();

    [Fact]
    public async Task HealthNamesTheVersionsFoldersAndProcess()
    {
        var health = await _sidecar.GetJsonAsync("api/health");

        Assert.Equal(EngineInfo.Version, (string?)health["engineVersion"]);
        Assert.Equal(EngineInfo.Version, (string?)health["version"]);
        Assert.Equal(_sidecar.DataDirectory, (string?)health["dataDir"]);
        Assert.Equal(_sidecar.Options.StateDirectory, (string?)health["stateDir"]);
        Assert.Equal(Environment.ProcessId, (int?)health["pid"]);
    }

    [Fact]
    public async Task ChecksComeFromTheCatalogWithATitleForEveryCategory()
    {
        var checks = await _sidecar.GetJsonAsync("api/checks");

        var categories = checks["categories"]!.AsArray();
        Assert.Equal(Enum.GetValues<IssueCategory>().Length, categories.Count);
        Assert.All(categories, category => Assert.False(string.IsNullOrWhiteSpace((string?)category!["title"])));
        Assert.Contains(categories, category => (string?)category!["id"] == "metaDescriptions" && (string?)category["title"] == "Meta descriptions");

        // In catalog order, each in a listed category, with the texts the UI shows.
        var listed = checks["checks"]!.AsArray();
        Assert.Equal(IssueCatalog.All.Select(check => check.Id), listed.Select(check => (string?)check!["id"]));
        var categoryIds = categories.Select(category => (string?)category!["id"]).ToHashSet();
        Assert.All(listed, check => Assert.Contains((string?)check!["category"], categoryIds));
        var tooLong = listed.Single(check => (string?)check!["id"] == IssueCatalog.TITLE_TOO_LONG.Id)!;
        Assert.Equal(JsonNamingPolicy.CamelCase.ConvertName(IssueCatalog.TITLE_TOO_LONG.Severity.ToString()), (string?)tooLong["severity"]);
        Assert.Equal(IssueCatalog.TITLE_TOO_LONG.Title, (string?)tooLong["title"]);
        Assert.Equal(IssueCatalog.TITLE_TOO_LONG.Description, (string?)tooLong["description"]);
        Assert.Equal(IssueCatalog.TITLE_TOO_LONG.Why, (string?)tooLong["why"]);
        Assert.Equal(IssueCatalog.TITLE_TOO_LONG.Fix, (string?)tooLong["fix"]);
        Assert.Equal(IssueCatalog.TITLE_TOO_LONG.LearnMore, (string?)tooLong["learnMore"]);
        Assert.Equal("titles", (string?)tooLong["category"]);
        Assert.Null((string?)listed.Single(check => (string?)check!["id"] == IssueCatalog.IMAGE_BROKEN.Id)!["learnMore"]);
    }
}
