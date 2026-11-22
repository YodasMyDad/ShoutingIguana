using System.Text.Json.Serialization;
using ShoutingIguana.Engine.Configuration.Models;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>What may change when a crawl continues, as with the CLI's --resume. A null keeps the stored value.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record ContinueCrawlRequest
{
    public int? Concurrency { get; init; }
    public double? MaxRequestsPerSecond { get; init; }
    public int? RequestTimeoutSeconds { get; init; }

    /// <summary>The headers to send from now on, with their values: crawl files keep only the names.</summary>
    public IReadOnlyDictionary<string, string>? ExtraHeaders { get; init; }

    /// <summary>The Shopify crawler signatures to send from now on, by host: crawl files keep none.</summary>
    public IReadOnlyDictionary<string, CrawlSignatureDto>? Signatures { get; init; }

    /// <summary>Needed again when the stored proxy had a user name and password.</summary>
    public string? ProxyUrl { get; init; }

    /// <summary>The HTTP logins to send from now on. A crawl file keeps them without passwords.</summary>
    public IReadOnlyList<SiteLogin>? HttpLogins { get; init; }

    /// <summary>The cookies to send from now on. A crawl file keeps them without values.</summary>
    public IReadOnlyList<SentCookie>? Cookies { get; init; }

    /// <summary>The form sign-in to run from now on. A crawl file keeps it without its password.</summary>
    public FormLogin? FormLogin { get; init; }

    /// <summary>Continue without the stored headers that weren't given again, instead of asking for them.</summary>
    public bool SkipMissingHeaders { get; init; }
}
