using System.Text.Json.Serialization;
using ShoutingIguana.Engine.Configuration.Models;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>
/// Every public <see cref="CrawlOptions"/> property, under its JSON name, which is also the field name validation keys its
/// messages by. In a request, a property left out keeps the engine's default.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record CrawlOptionsDto
{
    public string? StartUrl { get; init; }
    public int? MaxUrls { get; init; }
    public int? MaxDepth { get; init; }
    public int? Concurrency { get; init; }
    public int? ExternalConcurrency { get; init; }
    public double? MaxRequestsPerSecond { get; init; }
    public UserAgentPreset? UserAgent { get; init; }
    public string? CustomUserAgent { get; init; }
    public string? AcceptLanguage { get; init; }

    /// <summary>Header name to value. Stored crawls and saved defaults keep the names with empty values.</summary>
    public IReadOnlyDictionary<string, string>? ExtraHeaders { get; init; }

    /// <summary>
    /// Shopify crawler signatures, by the host each was made for. Taken in requests only: a response never gives one back,
    /// and stored crawls and saved defaults keep none.
    /// </summary>
    public IReadOnlyDictionary<string, CrawlSignatureDto>? Signatures { get; init; }

    public bool? RespectRobotsTxt { get; init; }
    public bool? RespectCrawlDelay { get; init; }
    public bool? CheckResources { get; init; }
    public bool? CheckExternalLinks { get; init; }
    public bool? IncludeSubdomains { get; init; }
    public bool? StayInStartFolder { get; init; }
    public bool? FollowNofollow { get; init; }
    public bool? CrawlCanonicals { get; init; }
    public bool? CrawlHreflang { get; init; }
    public bool? CrawlPagination { get; init; }
    public bool? DiscoverSitemaps { get; init; }
    public IReadOnlyList<string>? ExtraSitemaps { get; init; }
    public bool? StoreHtml { get; init; }
    public bool? UseCookies { get; init; }

    /// <summary>When a page is rendered in a browser. Left out, a new crawl renders a client-rendered shell.</summary>
    [JsonPropertyName("javaScriptRendering")]
    public JavaScriptRendering? Rendering { get; init; }

    /// <summary>Regular expressions matched against the whole URL. Used when <see cref="JavaScriptRendering"/> is Matching.</summary>
    public IReadOnlyList<string>? RenderPatterns { get; init; }

    /// <summary>When any are set, a URL is fetched only if one matches.</summary>
    public IReadOnlyList<string>? IncludePatterns { get; init; }

    /// <summary>A URL that matches any of these is recorded and not fetched.</summary>
    public IReadOnlyList<string>? ExcludePatterns { get; init; }

    /// <summary>Custom extraction rules, applied while each HTML page is parsed. Empty extracts nothing.</summary>
    public IReadOnlyList<ExtractionRuleDto>? ExtractionRules { get; init; }
    public string? ProxyUrl { get; init; }
    public int? RequestTimeoutSeconds { get; init; }
    public int? MaxRetries { get; init; }
    public int? MaxHtmlBytes { get; init; }

    /// <summary>A list a crawl file from before the property existed reads back as null.</summary>
    private static IReadOnlyList<string> List(IReadOnlyList<string>? values) => values ?? [];

    /// <summary>Given in responses: the proxy's user name and password were removed, so they must be given again. Ignored in requests.</summary>
    public bool? HasRedactedProxyCredentials { get; init; }

    public static CrawlOptionsDto From(CrawlOptions options) => new()
    {
        StartUrl = options.StartUrl,
        MaxUrls = options.MaxUrls,
        MaxDepth = options.MaxDepth,
        Concurrency = options.Concurrency,
        ExternalConcurrency = options.ExternalConcurrency,
        MaxRequestsPerSecond = options.MaxRequestsPerSecond,
        UserAgent = options.UserAgent,
        CustomUserAgent = options.CustomUserAgent,
        AcceptLanguage = options.AcceptLanguage,
        ExtraHeaders = new Dictionary<string, string>(options.ExtraHeaders, StringComparer.OrdinalIgnoreCase),
        // Never a signature's values in a response: an empty object, as the engine's default reads.
        Signatures = new Dictionary<string, CrawlSignatureDto>(),
        RespectRobotsTxt = options.RespectRobotsTxt,
        RespectCrawlDelay = options.RespectCrawlDelay,
        CheckResources = options.CheckResources,
        CheckExternalLinks = options.CheckExternalLinks,
        IncludeSubdomains = options.IncludeSubdomains,
        StayInStartFolder = options.StayInStartFolder,
        FollowNofollow = options.FollowNofollow,
        CrawlCanonicals = options.CrawlCanonicals,
        CrawlHreflang = options.CrawlHreflang,
        CrawlPagination = options.CrawlPagination,
        DiscoverSitemaps = options.DiscoverSitemaps,
        ExtraSitemaps = [.. options.ExtraSitemaps],
        StoreHtml = options.StoreHtml,
        UseCookies = options.UseCookies,
        Rendering = options.Rendering,
        RenderPatterns = List(options.RenderPatterns),
        IncludePatterns = List(options.IncludePatterns),
        ExcludePatterns = List(options.ExcludePatterns),
        ExtractionRules = (options.ExtractionRules ?? []).Select(ExtractionRuleDto.From).ToList(),
        ProxyUrl = options.ProxyUrl,
        RequestTimeoutSeconds = options.RequestTimeoutSeconds,
        MaxRetries = options.MaxRetries,
        MaxHtmlBytes = options.MaxHtmlBytes,
        HasRedactedProxyCredentials = options.HasRedactedProxyCredentials
    };

    /// <summary>
    /// The engine's options, with the engine's defaults for whatever is left out. Validate them before use: this only
    /// adds the problems the engine can't see once they are built, keyed as validation keys them.
    /// </summary>
    public CrawlOptions ToCrawlOptions(out IReadOnlyDictionary<string, IReadOnlyList<string>> errors)
    {
        var headers = Headers(ExtraHeaders, out errors);
        var defaults = new CrawlOptions { StartUrl = StartUrl ?? string.Empty };
        return defaults with
        {
            MaxUrls = MaxUrls ?? defaults.MaxUrls,
            MaxDepth = MaxDepth ?? defaults.MaxDepth,
            Concurrency = Concurrency ?? defaults.Concurrency,
            ExternalConcurrency = ExternalConcurrency ?? defaults.ExternalConcurrency,
            MaxRequestsPerSecond = MaxRequestsPerSecond ?? defaults.MaxRequestsPerSecond,
            UserAgent = UserAgent ?? defaults.UserAgent,
            CustomUserAgent = CustomUserAgent ?? defaults.CustomUserAgent,
            AcceptLanguage = AcceptLanguage ?? defaults.AcceptLanguage,
            ExtraHeaders = headers,
            Signatures = CrawlSignatureDto.ToEngine(Signatures),
            RespectRobotsTxt = RespectRobotsTxt ?? defaults.RespectRobotsTxt,
            RespectCrawlDelay = RespectCrawlDelay ?? defaults.RespectCrawlDelay,
            CheckResources = CheckResources ?? defaults.CheckResources,
            CheckExternalLinks = CheckExternalLinks ?? defaults.CheckExternalLinks,
            IncludeSubdomains = IncludeSubdomains ?? defaults.IncludeSubdomains,
            StayInStartFolder = StayInStartFolder ?? defaults.StayInStartFolder,
            FollowNofollow = FollowNofollow ?? defaults.FollowNofollow,
            CrawlCanonicals = CrawlCanonicals ?? defaults.CrawlCanonicals,
            CrawlHreflang = CrawlHreflang ?? defaults.CrawlHreflang,
            CrawlPagination = CrawlPagination ?? defaults.CrawlPagination,
            DiscoverSitemaps = DiscoverSitemaps ?? defaults.DiscoverSitemaps,
            ExtraSitemaps = ExtraSitemaps is null ? defaults.ExtraSitemaps : [.. ExtraSitemaps],
            StoreHtml = StoreHtml ?? defaults.StoreHtml,
            UseCookies = UseCookies ?? defaults.UseCookies,
            Rendering = this.Rendering ?? defaults.Rendering,
            RenderPatterns = RenderPatterns is null ? defaults.RenderPatterns : [.. RenderPatterns],
            IncludePatterns = IncludePatterns is null ? defaults.IncludePatterns : [.. IncludePatterns],
            ExcludePatterns = ExcludePatterns is null ? defaults.ExcludePatterns : [.. ExcludePatterns],
            ExtractionRules = ExtractionRules is null ? defaults.ExtractionRules : ExtractionRules.Select(rule => rule.ToRule()).ToList(),
            ProxyUrl = ProxyUrl ?? defaults.ProxyUrl,
            RequestTimeoutSeconds = RequestTimeoutSeconds ?? defaults.RequestTimeoutSeconds,
            MaxRetries = MaxRetries ?? defaults.MaxRetries,
            MaxHtmlBytes = MaxHtmlBytes ?? defaults.MaxHtmlBytes
        };
    }

    /// <summary>
    /// Header names compared as HTTP compares them, whatever their case. Two that differ only in case would both be sent,
    /// so that is refused under the same field as the engine's header messages.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Headers(IReadOnlyDictionary<string, string>? given,
        out IReadOnlyDictionary<string, IReadOnlyList<string>> errors)
    {
        Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
        List<string> twice = [];
        foreach (var (name, value) in given ?? new Dictionary<string, string>())
        {
            if (!headers.TryAdd(name, value))
            {
                twice.Add($"Header '{name}' is given more than once.");
            }
        }

        errors = twice.Count == 0
            ? new Dictionary<string, IReadOnlyList<string>>()
            : new Dictionary<string, IReadOnlyList<string>> { [OptionFields.EXTRA_HEADERS] = twice };
        return headers;
    }
}
