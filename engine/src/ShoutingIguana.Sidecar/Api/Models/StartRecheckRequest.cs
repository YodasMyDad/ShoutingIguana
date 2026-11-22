using System.Text.Json.Serialization;
using ShoutingIguana.Engine.Configuration.Models;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary><c>POST …/rechecks</c>: what to recheck, and the secrets a crawl file doesn't keep, as Continue takes them.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record StartRecheckRequest : RecheckAskRequest
{
    /// <summary>The crawl's headers with their values: crawl files keep only the names.</summary>
    public IReadOnlyDictionary<string, string>? ExtraHeaders { get; init; }

    /// <summary>The Shopify crawler signatures to send, by host: crawl files keep none, so a recheck sends only those given here.</summary>
    public IReadOnlyDictionary<string, CrawlSignatureDto>? Signatures { get; init; }

    /// <summary>Needed again when the stored proxy had a user name and password.</summary>
    public string? ProxyUrl { get; init; }

    /// <summary>The HTTP logins to send. A crawl file keeps them without passwords.</summary>
    public IReadOnlyList<SiteLogin>? HttpLogins { get; init; }

    /// <summary>The cookies to send. A crawl file keeps them without values.</summary>
    public IReadOnlyList<SentCookie>? Cookies { get; init; }

    /// <summary>The form sign-in to run. A crawl file keeps it without its password.</summary>
    public FormLogin? FormLogin { get; init; }

    /// <summary>Recheck without the stored headers that weren't given again, instead of asking for them.</summary>
    public bool SkipMissingHeaders { get; init; }
}
