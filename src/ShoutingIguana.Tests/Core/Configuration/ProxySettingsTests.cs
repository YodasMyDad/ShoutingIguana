using ShoutingIguana.Core.Configuration;
using Xunit;

namespace ShoutingIguana.Tests.Core.Configuration;

public class ProxySettingsTests
{
    [Fact]
    public void GetRedactedProxyUrlReturnsEmptyWhenDisabled()
    {
        var settings = new ProxySettings { Enabled = false, Server = "host", Port = 8080 };

        Assert.Equal(string.Empty, settings.GetRedactedProxyUrl());
    }

    [Fact]
    public void GetRedactedProxyUrlReturnsEmptyWhenServerMissing()
    {
        var settings = new ProxySettings { Enabled = true, Server = string.Empty, Port = 8080 };

        Assert.Equal(string.Empty, settings.GetRedactedProxyUrl());
    }

    [Fact]
    public void GetRedactedProxyUrlUnchangedWhenNoUserInfo()
    {
        var settings = new ProxySettings
        {
            Enabled = true,
            Type = ProxyType.Http,
            Server = "proxy.example.com",
            Port = 8080
        };

        Assert.Equal("http://proxy.example.com:8080", settings.GetRedactedProxyUrl());
    }

    [Fact]
    public void GetRedactedProxyUrlMasksEmbeddedCredentials()
    {
        // Credentials would get baked into Server by some callers; ensure masking is applied
        var settings = new ProxySettings
        {
            Enabled = true,
            Type = ProxyType.Http,
            Server = "user:secret@proxy.example.com",
            Port = 8080
        };

        var redacted = settings.GetRedactedProxyUrl();

        Assert.DoesNotContain("user", redacted);
        Assert.DoesNotContain("secret", redacted);
        Assert.Contains("***", redacted);
        Assert.Contains("proxy.example.com", redacted);
    }

    [Theory]
    [InlineData(ProxyType.Http, "http")]
    [InlineData(ProxyType.Https, "https")]
    [InlineData(ProxyType.Socks5, "socks5")]
    public void GetProxyUrlUsesCorrectScheme(ProxyType type, string expectedScheme)
    {
        var settings = new ProxySettings
        {
            Enabled = true,
            Type = type,
            Server = "proxy.example.com",
            Port = 1080
        };

        Assert.StartsWith(expectedScheme + "://", settings.GetProxyUrl());
    }
}
