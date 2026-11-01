using ShoutingIguana.Engine.Configuration.Models;

namespace ShoutingIguana.Engine.Tests.Configuration;

/// <summary>What resuming a stored crawl may change, and the secrets it has to ask for again.</summary>
public sealed class ResumePlanTests
{
    /// <summary>As a crawl file stores the options: header values and proxy credentials removed.</summary>
    private static readonly CrawlOptions STORED = new CrawlOptions
    {
        StartUrl = "https://example.com/shop/",
        MaxUrls = 500,
        MaxDepth = 3,
        Concurrency = 2,
        MaxRequestsPerSecond = 2,
        RequestTimeoutSeconds = 30,
        UserAgent = UserAgentPreset.GooglebotSmartphone,
        RespectRobotsTxt = false,
        StoreHtml = true,
        ExtraHeaders = new Dictionary<string, string> { ["X-Crawler-Key"] = "s3cret", ["X-Team"] = "seo" },
        ProxyUrl = "http://crawler:pa55word@proxy.example:8080/"
    }.WithoutSecrets();

    [Fact]
    public void OnlyHowTheCrawlTalksToTheSiteChanges()
    {
        var changes = new ResumeChanges
        {
            Concurrency = 8,
            MaxRequestsPerSecond = 0,
            RequestTimeoutSeconds = 15,
            ExtraHeaders = new Dictionary<string, string> { ["x-crawler-key"] = "again", ["X-New"] = "1" },
            ProxyUrl = "http://crawler:pa55word@proxy.example:8080/"
        };

        var plan = STORED.PlanResume(changes);

        Assert.Equal(STORED with
        {
            Concurrency = 8,
            MaxRequestsPerSecond = 0,
            RequestTimeoutSeconds = 15,
            ExtraHeaders = changes.ExtraHeaders,
            ProxyUrl = changes.ProxyUrl
        }, plan.Options);

        // A header given again matches its stored name whatever the case; a new one may be added.
        Assert.Equal(["X-Team"], plan.MissingHeaders);
        Assert.False(plan.MissingProxyCredentials);
    }

    [Fact]
    public void NothingGivenKeepsTheStoredSettingsAndNamesTheSecretsToAskFor()
    {
        var plan = STORED.PlanResume(new ResumeChanges());

        // Headers that aren't given again aren't sent at all, not even empty.
        Assert.Empty(plan.Options.ExtraHeaders);
        Assert.Equal(STORED with { ExtraHeaders = plan.Options.ExtraHeaders }, plan.Options);
        Assert.Equal(["X-Crawler-Key", "X-Team"], plan.MissingHeaders);
        Assert.True(plan.MissingProxyCredentials);
    }

    [Fact]
    public void AProxyWithoutCredentialsIsKeptAsItWas()
    {
        var stored = STORED with { ProxyUrl = "http://proxy.example:8080/", ExtraHeaders = new Dictionary<string, string>() };

        var plan = stored.PlanResume(new ResumeChanges { Concurrency = 1 });

        Assert.Equal(stored with { Concurrency = 1, ExtraHeaders = plan.Options.ExtraHeaders }, plan.Options);
        Assert.Empty(plan.MissingHeaders);
        Assert.False(plan.MissingProxyCredentials);
    }
}
