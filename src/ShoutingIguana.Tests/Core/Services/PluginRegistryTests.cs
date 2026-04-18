using Microsoft.Extensions.Logging;
using Moq;
using ShoutingIguana.Core.Configuration;
using ShoutingIguana.Core.Services;
using Xunit;

namespace ShoutingIguana.Tests.Core.Services;

/// <summary>
/// Plugin trust allowlist enforcement tests. We avoid loading real plugin assemblies
/// by dropping dummy `.dll` files under <c>AppDomain.CurrentDomain.BaseDirectory/plugins</c>;
/// the allowlist check happens BEFORE <c>LoadFromAssemblyPath</c>, so a skipped file never
/// reaches the assembly loader.
/// </summary>
public sealed class PluginRegistryTests : IDisposable
{
    private readonly string _pluginsRoot;
    private readonly Mock<ILogger<PluginRegistry>> _logger = new();

    public PluginRegistryTests()
    {
        _pluginsRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins");
        if (Directory.Exists(_pluginsRoot))
        {
            // Isolate the test: remove any stray plugin directories left from earlier runs
            foreach (var dir in Directory.GetDirectories(_pluginsRoot))
            {
                try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
            }
        }
        else
        {
            Directory.CreateDirectory(_pluginsRoot);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_pluginsRoot))
        {
            foreach (var dir in Directory.GetDirectories(_pluginsRoot))
            {
                try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
            }
        }
    }

    [Fact]
    public async Task AssemblyNotOnAllowlistIsSkippedWithWarning()
    {
        var pluginDir = Path.Combine(_pluginsRoot, "TestPlugin_NotAllowed");
        Directory.CreateDirectory(pluginDir);
        var dllPath = Path.Combine(pluginDir, "NotOnAllowlist.dll");
        File.WriteAllBytes(dllPath, new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });

        var registry = BuildRegistry(enabled: true, allowlist: new List<string> { "ShoutingIguana.Plugins" });

        await registry.LoadPluginsAsync();

        Assert.Empty(registry.LoadedPlugins);
        VerifyWarning("Refusing to load plugin assembly");
    }

    [Fact]
    public async Task EmptyAllowlistSkipsEverything()
    {
        var pluginDir = Path.Combine(_pluginsRoot, "TestPlugin_Empty");
        Directory.CreateDirectory(pluginDir);
        File.WriteAllBytes(Path.Combine(pluginDir, "AnyPlugin.dll"), new byte[] { 0x00 });

        var registry = BuildRegistry(enabled: true, allowlist: new List<string>());

        await registry.LoadPluginsAsync();

        Assert.Empty(registry.LoadedPlugins);
        VerifyWarning("Refusing to load plugin assembly");
    }

    [Fact]
    public async Task AllowlistDisabledBypassesEnforcement()
    {
        // When trust is disabled, the allowlist is ignored and the loader attempts
        // to load the dummy DLL. It will fail (not a real assembly), but the
        // "Refusing to load" warning must NOT appear — that's the allowlist-enforcement check.
        var pluginDir = Path.Combine(_pluginsRoot, "TestPlugin_Disabled");
        Directory.CreateDirectory(pluginDir);
        File.WriteAllBytes(Path.Combine(pluginDir, "SomeDll.dll"), new byte[] { 0x00 });

        var registry = BuildRegistry(enabled: false, allowlist: new List<string> { "ShoutingIguana.Plugins" });

        await registry.LoadPluginsAsync();

        // No plugin actually loaded (dummy DLL isn't a real assembly), but no allowlist
        // warning should have been emitted either.
        Assert.Empty(registry.LoadedPlugins);
        VerifyNoWarning("Refusing to load plugin assembly");
    }

    private PluginRegistry BuildRegistry(bool enabled, List<string> allowlist)
    {
        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);

        var serviceProvider = new Mock<IServiceProvider>();
        var pluginConfig = new Mock<IPluginConfigurationService>();
        pluginConfig.Setup(p => p.GetAllPluginStatesAsync())
            .ReturnsAsync(new Dictionary<string, bool>());

        var appSettings = new Mock<IAppSettingsService>();
        appSettings.SetupGet(a => a.PluginTrust).Returns(new PluginTrustSettings
        {
            Enabled = enabled,
            Allowlist = allowlist
        });

        return new PluginRegistry(
            _logger.Object,
            loggerFactory.Object,
            serviceProvider.Object,
            pluginConfig.Object,
            appSettings.Object);
    }

    private void VerifyWarning(string messageFragment)
    {
        _logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains(messageFragment)),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    private void VerifyNoWarning(string messageFragment)
    {
        _logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains(messageFragment)),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }
}
