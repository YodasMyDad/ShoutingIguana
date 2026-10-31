using System.Security.Cryptography.X509Certificates;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Configuration.Models;

/// <summary>
/// Everything that shapes a crawl. Stored alongside the crawl data so a resumed run behaves the same.
/// </summary>
public sealed record CrawlOptions
{
    /// <summary>Stands in for proxy credentials that <see cref="WithoutSecrets"/> removed.</summary>
    public const string REDACTED = "***";

    /// <summary>The characters RFC 9110 allows in a header name besides letters and digits.</summary>
    private const string TOKEN_PUNCTUATION = "!#$%&'*+-.^_`|~";

    /// <summary>Request headers the HTTP client manages itself. Overriding one would break or reroute every request.</summary>
    private static readonly HashSet<string> CLIENT_HEADERS = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host", "Connection", "Keep-Alive", "Proxy-Connection", "Transfer-Encoding", "TE", "Trailer", "Upgrade", "Expect"
    };

    public required string StartUrl { get; init; }

    /// <summary>Maximum internal URLs to fetch (pages and resources). 0 means unlimited.</summary>
    public int MaxUrls { get; init; }

    /// <summary>Maximum link depth from the start URL. 0 means unlimited.</summary>
    public int MaxDepth { get; init; }

    /// <summary>Parallel requests to the site being crawled.</summary>
    public int Concurrency { get; init; } = 5;

    /// <summary>Parallel status checks against other sites.</summary>
    public int ExternalConcurrency { get; init; } = 4;

    /// <summary>Request cap per host. 0 means as fast as concurrency allows, with automatic backoff.</summary>
    public double MaxRequestsPerSecond { get; init; }

    public UserAgentPreset UserAgent { get; init; } = UserAgentPreset.ShoutingIguana;
    public string? CustomUserAgent { get; init; }
    public string AcceptLanguage { get; init; } = "en-US,en;q=0.9";

    /// <summary>Sent with every request to the site being crawled (never to other sites), e.g. a secret header its firewall allowlists.</summary>
    public IReadOnlyDictionary<string, string> ExtraHeaders { get; init; } = new Dictionary<string, string>();

    public bool RespectRobotsTxt { get; init; } = true;
    public bool RespectCrawlDelay { get; init; }
    public bool CheckResources { get; init; } = true;
    public bool CheckExternalLinks { get; init; } = true;
    public bool IncludeSubdomains { get; init; }
    public bool StayInStartFolder { get; init; } = true;
    public bool FollowNofollow { get; init; }
    public bool CrawlCanonicals { get; init; } = true;
    public bool CrawlHreflang { get; init; }
    public bool CrawlPagination { get; init; }
    public bool DiscoverSitemaps { get; init; } = true;
    public IReadOnlyList<string> ExtraSitemaps { get; init; } = [];
    public bool StoreHtml { get; init; }
    public bool UseCookies { get; init; } = true;

    /// <summary>http://, https:// or socks5:// proxy. Credentials may be given as user:password@host.</summary>
    public string? ProxyUrl { get; init; }

    public int RequestTimeoutSeconds { get; init; } = 20;
    public int MaxRetries { get; init; } = 2;
    public int MaxHtmlBytes { get; init; } = 8 * 1024 * 1024;

    /// <summary>Tests only: also accept this self-signed certificate, which the HTTPS synthetic site serves.</summary>
    internal X509Certificate2? TrustedCertificate { get; init; }

    /// <summary>
    /// What a crawl file stores, since people share them: header names without their values, and the proxy without
    /// its credentials (<see cref="REDACTED"/> takes their place). Whoever resumes the crawl supplies them again.
    /// </summary>
    public CrawlOptions WithoutSecrets() => this with
    {
        ExtraHeaders = ExtraHeaders.ToDictionary(header => header.Key, _ => string.Empty, StringComparer.OrdinalIgnoreCase),
        ProxyUrl = ProxyUrl is not null && Uri.TryCreate(ProxyUrl, UriKind.Absolute, out var proxy) && proxy.UserInfo.Length > 0
            ? new UriBuilder(proxy) { UserName = REDACTED, Password = string.Empty }.Uri.AbsoluteUri
            : ProxyUrl
    };

    /// <summary>True when the proxy's credentials were removed by <see cref="WithoutSecrets"/>.</summary>
    public bool HasRedactedProxyCredentials =>
        ProxyUrl is not null && Uri.TryCreate(ProxyUrl, UriKind.Absolute, out var proxy) && proxy.UserInfo == REDACTED;

    public IReadOnlyList<string> Validate()
    {
        List<string> errors = [];

        if (!UrlNormalizer.TryNormalize(StartUrl, out _))
        {
            errors.Add("Start URL must be an absolute http or https URL.");
        }

        if (MaxUrls < 0)
        {
            errors.Add("Max URLs cannot be negative.");
        }

        if (MaxDepth < 0)
        {
            errors.Add("Max depth cannot be negative.");
        }

        if (Concurrency is < 1 or > 100)
        {
            errors.Add("Concurrency must be between 1 and 100.");
        }

        if (ExternalConcurrency is < 1 or > 50)
        {
            errors.Add("External concurrency must be between 1 and 50.");
        }

        if (MaxRequestsPerSecond < 0)
        {
            errors.Add("Max requests per second cannot be negative.");
        }

        if (UserAgent == UserAgentPreset.Custom && string.IsNullOrWhiteSpace(CustomUserAgent))
        {
            errors.Add("A custom user agent needs a value.");
        }

        if (RequestTimeoutSeconds is < 1 or > 300)
        {
            errors.Add("Request timeout must be between 1 and 300 seconds.");
        }

        if (MaxRetries is < 0 or > 10)
        {
            errors.Add("Max retries must be between 0 and 10.");
        }

        if (MaxHtmlBytes is < 64 * 1024 or > 100 * 1024 * 1024)
        {
            errors.Add("Max HTML size must be between 64 KB and 100 MB.");
        }

        if (ProxyUrl is not null && !Uri.TryCreate(ProxyUrl, UriKind.Absolute, out _))
        {
            errors.Add("Proxy must be an absolute URL.");
        }

        foreach (var (name, value) in ExtraHeaders)
        {
            if (!IsToken(name) || !IsFieldValue(value))
            {
                errors.Add($"Header '{name}' is not valid.");
            }
            else if (CLIENT_HEADERS.Contains(name) || name.StartsWith("Content-", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Header '{name}' is set by the crawler's HTTP client and can't be overridden.");
            }
        }

        errors.AddRange(ExtraSitemaps
            .Where(sitemap => !UrlNormalizer.TryNormalize(sitemap, out _))
            .Select(sitemap => $"Sitemap '{sitemap}' is not an absolute http or https URL."));

        return errors;
    }

    /// <summary>An RFC 9110 token. Anything else (a space, a colon, non-ASCII) makes HttpClient throw on every request.</summary>
    private static bool IsToken(string name) =>
        name.Length > 0 && name.All(character => char.IsAsciiLetterOrDigit(character) || TOKEN_PUNCTUATION.Contains(character));

    /// <summary>Printable ASCII, spaces and tabs: line breaks would inject headers, and HttpClient refuses non-ASCII values.</summary>
    private static bool IsFieldValue(string value) => value.All(character => character is '\t' or (>= ' ' and <= '~'));
}
