using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
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

    /// <summary>
    /// Applies what may change when this stored crawl is resumed. The changes can't touch anything else: a resumed crawl
    /// keeps the scope and rules it started with. Also says which secrets weren't given again, since crawl files don't
    /// store them (see <see cref="WithoutSecrets"/>).
    /// </summary>
    public ResumePlan PlanResume(ResumeChanges changes)
    {
        // Header names match whatever their case, as HTTP header names do.
        var given = new HashSet<string>(changes.ExtraHeaders.Keys, StringComparer.OrdinalIgnoreCase);
        var options = this with
        {
            Concurrency = changes.Concurrency ?? Concurrency,
            MaxRequestsPerSecond = changes.MaxRequestsPerSecond ?? MaxRequestsPerSecond,
            RequestTimeoutSeconds = changes.RequestTimeoutSeconds ?? RequestTimeoutSeconds,
            ExtraHeaders = changes.ExtraHeaders,
            ProxyUrl = changes.ProxyUrl ?? ProxyUrl
        };

        return new ResumePlan(
            options,
            [.. ExtraHeaders.Keys.Where(name => !given.Contains(name))],
            changes.ProxyUrl is null && HasRedactedProxyCredentials);
    }

    /// <summary>Every problem with these options, one sentence each, in the order <see cref="ValidateFields"/> gives them.</summary>
    public IReadOnlyList<string> Validate() => [.. Problems().Select(problem => problem.Message)];

    /// <summary>
    /// Every problem with these options, keyed by the JSON name of the property it is about (<c>startUrl</c>,
    /// <c>extraHeaders</c>...), so a form can show each one beside its field. Valid options give an empty dictionary.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> ValidateFields() =>
        Problems()
            .GroupBy(problem => problem.Field, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, IReadOnlyList<string> (group) => [.. group.Select(problem => problem.Message)], StringComparer.Ordinal);

    private IEnumerable<(string Field, string Message)> Problems()
    {
        if (!UrlNormalizer.TryNormalize(StartUrl, out _))
        {
            yield return (Field(nameof(StartUrl)), "Start URL must be an absolute http or https URL.");
        }

        if (MaxUrls < 0)
        {
            yield return (Field(nameof(MaxUrls)), "Max URLs cannot be negative.");
        }

        if (MaxDepth < 0)
        {
            yield return (Field(nameof(MaxDepth)), "Max depth cannot be negative.");
        }

        if (Concurrency is < 1 or > 100)
        {
            yield return (Field(nameof(Concurrency)), "Concurrency must be between 1 and 100.");
        }

        if (ExternalConcurrency is < 1 or > 50)
        {
            yield return (Field(nameof(ExternalConcurrency)), "External concurrency must be between 1 and 50.");
        }

        if (MaxRequestsPerSecond < 0)
        {
            yield return (Field(nameof(MaxRequestsPerSecond)), "Max requests per second cannot be negative.");
        }

        if (UserAgent == UserAgentPreset.Custom && string.IsNullOrWhiteSpace(CustomUserAgent))
        {
            yield return (Field(nameof(CustomUserAgent)), "A custom user agent needs a value.");
        }
        else if (UserAgent == UserAgentPreset.Custom && !IsFieldValue(CustomUserAgent))
        {
            // Sent as it is with every request, as the headers are, so it can't break or add to them either.
            yield return (Field(nameof(CustomUserAgent)), "A custom user agent must be printable ASCII on one line.");
        }

        if (!IsFieldValue(AcceptLanguage))
        {
            yield return (Field(nameof(AcceptLanguage)), "Accept-Language must be printable ASCII on one line.");
        }

        if (RequestTimeoutSeconds is < 1 or > 300)
        {
            yield return (Field(nameof(RequestTimeoutSeconds)), "Request timeout must be between 1 and 300 seconds.");
        }

        if (MaxRetries is < 0 or > 10)
        {
            yield return (Field(nameof(MaxRetries)), "Max retries must be between 0 and 10.");
        }

        if (MaxHtmlBytes is < 64 * 1024 or > 100 * 1024 * 1024)
        {
            yield return (Field(nameof(MaxHtmlBytes)), "Max HTML size must be between 64 KB and 100 MB.");
        }

        if (ProxyUrl is not null && !Uri.TryCreate(ProxyUrl, UriKind.Absolute, out _))
        {
            yield return (Field(nameof(ProxyUrl)), "Proxy must be an absolute URL.");
        }

        foreach (var (name, value) in ExtraHeaders)
        {
            if (!IsToken(name) || !IsFieldValue(value))
            {
                yield return (Field(nameof(ExtraHeaders)), $"Header '{name}' is not valid.");
            }
            else if (CLIENT_HEADERS.Contains(name) || !IsRequestHeader(name, value))
            {
                yield return (Field(nameof(ExtraHeaders)), $"Header '{name}' is set by the crawler's HTTP client and can't be overridden.");
            }
        }

        foreach (var sitemap in ExtraSitemaps.Where(sitemap => !UrlNormalizer.TryNormalize(sitemap, out _)))
        {
            yield return (Field(nameof(ExtraSitemaps)), $"Sitemap '{sitemap}' is not an absolute http or https URL.");
        }
    }

    /// <summary>A property's name as JSON spells it, which is how hosts name the fields of these options.</summary>
    private static string Field(string propertyName) => JsonNamingPolicy.CamelCase.ConvertName(propertyName);

    /// <summary>An RFC 9110 token. Anything else (a space, a colon, non-ASCII) makes HttpClient throw on every request.</summary>
    private static bool IsToken(string name) =>
        name.Length > 0 && name.All(character => char.IsAsciiLetterOrDigit(character) || TOKEN_PUNCTUATION.Contains(character));

    /// <summary>Printable ASCII, spaces and tabs: line breaks would inject headers, and HttpClient refuses non-ASCII values.</summary>
    private static bool IsFieldValue(string? value) => value is not null && value.All(character => character is '\t' or (>= ' ' and <= '~'));

    /// <summary>
    /// False for the headers HttpClient only takes on a request's content (Content-Type, Allow, Expires, Last-Modified...).
    /// Set on the request itself, one makes every request throw.
    /// </summary>
    private static bool IsRequestHeader(string name, string value)
    {
        using var request = new HttpRequestMessage();
        return request.Headers.TryAddWithoutValidation(name, value);
    }
}
