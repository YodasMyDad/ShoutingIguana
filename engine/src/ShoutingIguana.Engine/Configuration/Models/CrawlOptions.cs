using System.Globalization;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using ShoutingIguana.Engine.Configuration;
using ShoutingIguana.Engine.Fetching;
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

    /// <summary>Crawl files keep URLs as they are given, and people share them, so credentials go in a header, whose value isn't kept.</summary>
    private const string CREDENTIALS_ADVICE = "They would be stored in the crawl file: use a sign-in (--auth, or a sign-in page), or send credentials in an Authorization header.";

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

    /// <summary>
    /// Parallel requests to the site being crawled, at most: the ceiling of the pace each host is asked at, which starts
    /// at one and grows while the host answers well.
    /// </summary>
    public int Concurrency { get; init; } = 5;

    /// <summary>Parallel status checks against other sites.</summary>
    public int ExternalConcurrency { get; init; } = 4;

    /// <summary>
    /// Request cap per host: the ceiling of the pace it is asked at, which starts at one a second, grows while the host
    /// answers well and slows when it asks. 0 means no cap: as fast as concurrency and the host's answers allow.
    /// </summary>
    public double MaxRequestsPerSecond { get; init; }

    public UserAgentPreset UserAgent { get; init; } = UserAgentPreset.ShoutingIguana;
    public string? CustomUserAgent { get; init; }
    public string AcceptLanguage { get; init; } = "en-US,en;q=0.9";

    /// <summary>Sent with every request to the site being crawled (never to other sites), e.g. a secret header its firewall allowlists.</summary>
    public IReadOnlyDictionary<string, string> ExtraHeaders { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// Crawler signatures (Shopify's crawler access), each keyed by the host it was made for: <c>shop.example</c>, or
    /// <c>host:port</c> off the default port. Sent to that host alone, over https, never to another host, a subdomain or
    /// the www twin without one of its own (<see cref="SignatureBook"/>). A crawl file never keeps them.
    /// </summary>
    public IReadOnlyDictionary<string, CrawlSignature> Signatures { get; init; } = new Dictionary<string, CrawlSignature>();

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

    /// <summary>
    /// HTTP Basic and Digest logins, sent only to the host each one names. Empty signs in no host. A crawl file keeps
    /// the user names and not the passwords. A crawl file from before this reads as none: the property is absent.
    /// </summary>
    public IReadOnlyList<SiteLogin> HttpLogins { get; init; } = [];

    /// <summary>
    /// Cookies sent from the first request, for a session the crawl did not start. Empty sends only what the site sets
    /// while crawling, when <see cref="UseCookies"/> is on. A crawl file keeps the names and not the values.
    /// </summary>
    public IReadOnlyList<SentCookie> Cookies { get; init; } = [];

    /// <summary>
    /// Signs in through a page in the browser before the first fetch, and sends the cookies that page sets. Null does
    /// not. A crawl file keeps the address and not the password. A crawl file from before this reads as null.
    /// </summary>
    public FormLogin? FormLogin { get; init; }

    /// <summary>
    /// When a page is rendered in a browser and that HTML is parsed. A new crawl renders a client-rendered shell.
    /// A crawl file from before this was stored reads as <see cref="JavaScriptRendering.Off"/>: the property is absent.
    /// </summary>
    [JsonPropertyName("javaScriptRendering")]
    public JavaScriptRendering Rendering { get; init; } = JavaScriptRendering.Automatic;

    /// <summary>Regular expressions matched against the whole URL. Used when <see cref="Rendering"/> is <see cref="JavaScriptRendering.Matching"/>.</summary>
    public IReadOnlyList<string> RenderPatterns { get; init; } = [];

    /// <summary>When any are set, a URL is fetched only if one matches. Empty fetches every URL the rest of the crawl would.</summary>
    public IReadOnlyList<string> IncludePatterns { get; init; } = [];

    /// <summary>A URL that matches any of these is recorded and not fetched, so a rendered crawl can stay out of a filter trap.</summary>
    public IReadOnlyList<string> ExcludePatterns { get; init; } = [];

    /// <summary>
    /// Custom extraction rules, applied while each HTML page is parsed. Empty extracts nothing. A crawl file from
    /// before this was stored reads as none: the property is absent.
    /// </summary>
    public IReadOnlyList<ExtractionRule> ExtractionRules { get; init; } = [];

    /// <summary>
    /// Addresses to crawl instead of following links. Empty follows links from <see cref="StartUrl"/>, as a crawl always
    /// has. When any are set, those addresses and the start URL are fetched, and a link found on them is recorded and
    /// not fetched, except a redirect or a meta refresh. A crawl file from before this reads as none: the property is absent.
    /// </summary>
    public IReadOnlyList<string> ListUrls { get; init; } = [];

    /// <summary>
    /// When a host blocks the plain HTTP fetch, read that host's pages in a headless browser instead and parse that
    /// HTML once. Off, the default, leaves a block as a block. A crawl file from before this reads as off: the
    /// property is absent. This is not <see cref="Rendering"/>.
    /// </summary>
    public bool BrowserFallback { get; init; }

    /// <summary>How many addresses a list may name. Past this, the crawl is refused rather than started half-read.</summary>
    public const int MAX_LIST_URLS = 100_000;

    /// <summary>True when this crawl fetches a list and does not follow links. A missing list, from an older crawl file, is not.</summary>
    internal bool IsListMode => ListUrls?.Any(static url => !string.IsNullOrWhiteSpace(url)) == true;

    /// <summary>http://, https:// or socks5:// proxy. Credentials may be given as user:password@host.</summary>
    public string? ProxyUrl { get; init; }

    public int RequestTimeoutSeconds { get; init; } = 20;
    public int MaxRetries { get; init; } = 2;
    public int MaxHtmlBytes { get; init; } = 8 * 1024 * 1024;

    /// <summary>Tests only: also accept this self-signed certificate, which the HTTPS synthetic site serves.</summary>
    internal X509Certificate2? TrustedCertificate { get; init; }

    /// <summary>
    /// What a crawl file stores, since people share them: header names without their values, the proxy without its
    /// credentials (<see cref="REDACTED"/> takes their place), and no crawler signature at all, not even the host it was
    /// for. Whoever resumes the crawl supplies them again.
    /// </summary>
    public CrawlOptions WithoutSecrets() => this with
    {
        ExtraHeaders = ExtraHeaders.ToDictionary(header => header.Key, _ => string.Empty, StringComparer.OrdinalIgnoreCase),
        Signatures = new Dictionary<string, CrawlSignature>(),
        HttpLogins = HttpLogins.Select(login => login with { Password = "" }).ToList(),
        Cookies = Cookies.Select(cookie => cookie with { Value = "" }).ToList(),
        FormLogin = FormLogin is null ? null : FormLogin with { Password = "" },
        ProxyUrl = ProxyUrl is not null && Uri.TryCreate(ProxyUrl, UriKind.Absolute, out var proxy) && proxy.UserInfo.Length > 0
            ? new UriBuilder(proxy) { UserName = REDACTED, Password = string.Empty }.Uri.AbsoluteUri
            : ProxyUrl
    };

    /// <summary>True when the proxy's credentials were removed by <see cref="WithoutSecrets"/>.</summary>
    public bool HasRedactedProxyCredentials => ProxyUrl is not null && IsRedactedProxy(ProxyUrl);

    /// <summary>True when a sign-in, a cookie or a form login was stored without the secret a crawl file doesn't keep.</summary>
    public bool HasRedactedAuth =>
        HttpLogins.Any(login => login.Username.Length > 0 && login.Password.Length == 0) ||
        Cookies.Any(cookie => cookie.Name.Length > 0 && cookie.Value.Length == 0) ||
        FormLogin is { Url.Length: > 0, Password: "" };

    /// <summary>
    /// Applies what may change when this stored crawl is resumed. The changes can't touch anything else: a resumed crawl
    /// keeps the scope and rules it started with. Also says which secrets weren't given again, since crawl files don't
    /// store them (see <see cref="WithoutSecrets"/>). What a crawl file keeps in their place, given back (a form filled
    /// in from the stored options would), doesn't count as given: a header without a value, which isn't sent at all, and
    /// a proxy whose user name is <see cref="REDACTED"/>.
    /// </summary>
    public ResumePlan PlanResume(ResumeChanges changes)
    {
        // Header names match whatever their case, as HTTP header names do.
        Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in changes.ExtraHeaders.Where(header => !string.IsNullOrWhiteSpace(header.Value)))
        {
            headers[name] = value;
        }

        var proxyUrl = changes.ProxyUrl is { } given && !IsRedactedProxy(given) ? given : null;
        var options = this with
        {
            Concurrency = changes.Concurrency ?? Concurrency,
            MaxRequestsPerSecond = changes.MaxRequestsPerSecond ?? MaxRequestsPerSecond,
            RequestTimeoutSeconds = changes.RequestTimeoutSeconds ?? RequestTimeoutSeconds,
            ExtraHeaders = headers,
            Signatures = changes.Signatures.Count > 0 ? changes.Signatures : Signatures,
            ProxyUrl = proxyUrl ?? ProxyUrl,
            HttpLogins = changes.HttpLogins.Count > 0 ? changes.HttpLogins : HttpLogins,
            Cookies = changes.Cookies.Count > 0 ? changes.Cookies : Cookies,
            FormLogin = changes.FormLogin is { Url.Length: > 0 } ? changes.FormLogin : FormLogin
        };

        return new ResumePlan(
            options,
            [.. ExtraHeaders.Keys.Where(name => !headers.ContainsKey(name))],
            proxyUrl is null && HasRedactedProxyCredentials,
            HasRedactedAuth && options.HasRedactedAuth);
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
        else if (HasUserInfo(StartUrl))
        {
            // The crawler never sends them, and the start URL is stored, listed and logged as it is.
            yield return (Field(nameof(StartUrl)), $"Start URL can't include a user name or password. {CREDENTIALS_ADVICE}");
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
        else if (ProxyUrl is not null && !ProxyFactory.IsValid(ProxyUrl))
        {
            yield return (Field(nameof(ProxyUrl)), "Proxy must be an http, https or socks5 address with a host.");
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

        foreach (var problem in SignatureProblems())
        {
            yield return (Field(nameof(Signatures)), problem);
        }

        foreach (var problem in AuthProblems())
        {
            yield return problem;
        }

        foreach (var sitemap in ExtraSitemaps)
        {
            if (!UrlNormalizer.TryNormalize(sitemap, out _))
            {
                yield return (Field(nameof(ExtraSitemaps)), $"Sitemap '{sitemap}' is not an absolute http or https URL.");
            }
            else if (HasUserInfo(sitemap))
            {
                yield return (Field(nameof(ExtraSitemaps)), $"Sitemap '{WithoutUserInfo(sitemap)}' can't include a user name or password. {CREDENTIALS_ADVICE}");
            }
        }

        foreach (var (property, patterns) in new[]
        {
            (nameof(RenderPatterns), RenderPatterns),
            (nameof(IncludePatterns), IncludePatterns),
            (nameof(ExcludePatterns), ExcludePatterns)
        })
        {
            if (!UrlPatterns.TryCompile(patterns, out _, out var error))
            {
                yield return (Field(property), error ?? "The pattern can't be used.");
            }
        }

        if (Rendering == JavaScriptRendering.Matching && NoPatterns(RenderPatterns))
        {
            yield return (Field(nameof(RenderPatterns)), "Say which URLs to render, one regular expression a line.");
        }

        foreach (var problem in ExtractionProblems())
        {
            yield return (Field(nameof(ExtractionRules)), problem);
        }

        foreach (var problem in ListProblems())
        {
            yield return (Field(nameof(ListUrls)), problem);
        }

        if (UrlNormalizer.TryNormalize(StartUrl, out var start) &&
            UrlPatterns.TryCompile(IncludePatterns, out var include, out _) &&
            UrlPatterns.TryCompile(ExcludePatterns, out var exclude, out _) &&
            (exclude.Matches(start, whenUnsure: true) || (include.Any && !include.Matches(start, whenUnsure: false))))
        {
            var byExclude = exclude.Matches(start, whenUnsure: true);
            yield return (
                Field(byExclude ? nameof(ExcludePatterns) : nameof(IncludePatterns)),
                "The start URL is left out by these patterns, so the crawl would have nowhere to start.");
        }
    }

    /// <summary>A list a crawl file from before the property existed deserialises as null.</summary>
    private static bool NoPatterns(IReadOnlyList<string>? patterns) => patterns is null || patterns.Count == 0;

    /// <summary>The list, as stored URL keys, so a link can be told from an address the list named.</summary>
    internal HashSet<UInt128> ListKeys()
    {
        HashSet<UInt128> keys = [];
        foreach (var raw in ListUrls ?? [])
        {
            if (!string.IsNullOrWhiteSpace(raw) && UrlNormalizer.TryNormalize(raw, out var url))
            {
                keys.Add(UrlKey.Compute(url));
            }
        }

        return keys;
    }

    /// <summary>
    /// Each address can be fetched, and there are not too many of them. A blank line is nothing. A missing list is none.
    /// At most twenty bad addresses are named, so a file of them doesn't come back as a book.
    /// </summary>
    private IEnumerable<string> ListProblems()
    {
        var listed = 0;
        var rejected = 0;
        foreach (var raw in ListUrls ?? [])
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            listed++;
            var url = raw.Trim();
            string? problem = null;
            if (!UrlNormalizer.TryNormalize(url, out _))
            {
                problem = $"'{url}' is not an absolute http or https URL.";
            }
            else if (HasUserInfo(url))
            {
                problem = $"'{WithoutUserInfo(url)}' can't include a user name or password. {CREDENTIALS_ADVICE}";
            }

            if (problem is not null)
            {
                rejected++;
                if (rejected <= 20)
                {
                    yield return problem;
                }
            }
        }

        if (listed > MAX_LIST_URLS)
        {
            yield return $"A list can hold at most {MAX_LIST_URLS} addresses. This one has {listed}.";
        }

        if (rejected > 20)
        {
            yield return $"{rejected - 20} more addresses in the list can't be used.";
        }
    }

    /// <summary>Each rule can be applied, and no two share a name. A missing list is none.</summary>
    private IEnumerable<string> ExtractionProblems()
    {
        var rules = ExtractionRules ?? [];
        if (rules.Count > ExtractionRule.MAX_RULES)
        {
            yield return $"At most {ExtractionRule.MAX_RULES} extraction rules. This crawl has {rules.Count}.";
        }

        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in rules)
        {
            if (rule is null)
            {
                yield return "An extraction rule was empty.";
                continue;
            }

            var name = rule.Name.Trim();
            if (name.Length > 0 && !names.Add(name))
            {
                yield return $"Two extraction rules are named '{name}': keep one.";
            }

            var problem = ExtractionSyntax.Problem(rule);
            if (problem is not null)
            {
                yield return problem;
            }
        }
    }

    /// <summary>
    /// Each crawler signature must be for a host, one per host, in the form Shopify gives, and not expired: an expired one
    /// is refused rather than sent, since Shopify would only refuse the crawler. No problem repeats a value.
    /// </summary>
    private IEnumerable<(string Field, string Message)> AuthProblems()
    {
        if (HttpLogins.Count > 20)
        {
            yield return (Field(nameof(HttpLogins)), "A crawl can sign in to at most 20 hosts.");
        }

        if (Cookies.Count > 50)
        {
            yield return (Field(nameof(Cookies)), "A crawl can be given at most 50 cookies.");
        }

        var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var login in HttpLogins)
        {
            if (login.Username.Length == 0 || login.Password.Length == 0)
            {
                yield return (Field(nameof(HttpLogins)), "A sign-in needs a user name and a password.");
            }
            else if (!IsFieldValue(login.Username) || !IsFieldValue(login.Password))
            {
                yield return (Field(nameof(HttpLogins)), "A sign-in's user name and password must be printable ASCII on one line.");
            }

            if (login.Host.Length > 0 && (login.Host.Contains('/') || login.Host.Contains(':') || !IsFieldValue(login.Host)))
            {
                yield return (Field(nameof(HttpLogins)), $"'{login.Host}' is not a host. Leave it empty to sign in to the site being crawled.");
            }
            else if (!hosts.Add(login.Host.Length == 0 ? "" : login.Host))
            {
                yield return (Field(nameof(HttpLogins)), "Two sign-ins are for the same host: keep one.");
            }
        }

        if (Cookies.Count > 0 && !UseCookies)
        {
            yield return (Field(nameof(Cookies)), "Cookies to send need Keep cookies on, or they are never sent.");
        }

        foreach (var cookie in Cookies)
        {
            if (cookie.Name.Length == 0 || cookie.Value.Length == 0)
            {
                yield return (Field(nameof(Cookies)), "A cookie needs a name and a value.");
            }
            else if (!IsToken(cookie.Name) || !IsFieldValue(cookie.Value))
            {
                yield return (Field(nameof(Cookies)), $"Cookie '{cookie.Name}' is not valid.");
            }

            if (cookie.Domain.Length > 0 && (cookie.Domain.Contains('/') || !IsFieldValue(cookie.Domain)))
            {
                yield return (Field(nameof(Cookies)), $"'{cookie.Domain}' is not a cookie domain.");
            }
        }

        if (FormLogin is { } form)
        {
            if (!UrlNormalizer.TryNormalize(form.Url, out _))
            {
                yield return (Field(nameof(FormLogin)), "The sign-in page must be an absolute http or https URL.");
            }
            else if (HasUserInfo(form.Url))
            {
                yield return (Field(nameof(FormLogin)), $"The sign-in page can't include a user name or password. {CREDENTIALS_ADVICE}");
            }

            if (form.Username.Length == 0 || form.Password.Length == 0)
            {
                yield return (Field(nameof(FormLogin)), "The sign-in page needs a user name and a password.");
            }
            else if (!IsFieldValue(form.Username) || !IsFieldValue(form.Password))
            {
                yield return (Field(nameof(FormLogin)), "The sign-in page's user name and password must be printable ASCII on one line.");
            }

            foreach (var selector in new[] { form.UsernameField, form.PasswordField, form.Submit })
            {
                if (selector.Length > 0 && (selector.Length > 500 || !IsFieldValue(selector)))
                {
                    yield return (Field(nameof(FormLogin)), "A sign-in field must be a short CSS selector on one line.");
                }
            }
        }
    }

    private IEnumerable<string> SignatureProblems()
    {
        HashSet<string> hosts = new(StringComparer.Ordinal);
        foreach (var (host, signature) in Signatures)
        {
            if (!WebBotSignature.TryNormalizeHost(host, out var authority))
            {
                yield return $"A Shopify signature is for '{host}', which isn't a domain.";
                continue;
            }

            if (!hosts.Add(authority))
            {
                yield return $"There are two Shopify signatures for {authority}: keep one.";
                continue;
            }

            var reading = WebBotSignature.Read(signature?.SignatureInput, signature?.Signature);
            if (reading.Problem is { } problem)
            {
                yield return $"The Shopify signature for {authority}: {problem}";
            }
            else if (reading is { Expires: { } expires } && reading.HasExpired(DateTimeOffset.UtcNow))
            {
                yield return $"The Shopify signature for {authority} expired on {expires.UtcDateTime.ToString("d MMMM yyyy", CultureInfo.InvariantCulture)}. " +
                             "Create a new one in Shopify admin: Online Store › Preferences › Crawler access.";
            }
        }
    }

    /// <summary>A user name or password in a URL (<c>https://user:password@host/</c>), which the crawler never uses.</summary>
    private static bool HasUserInfo(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.UserInfo.Length > 0;

    /// <summary>The URL to show in a message, so a password given by mistake isn't repeated back.</summary>
    private static string WithoutUserInfo(string url) =>
        new UriBuilder(new Uri(url, UriKind.Absolute)) { UserName = string.Empty, Password = string.Empty }.Uri.AbsoluteUri;

    /// <summary>The stand-in <see cref="WithoutSecrets"/> leaves for a proxy's credentials, whatever its password part holds.</summary>
    private static bool IsRedactedProxy(string proxyUrl) =>
        Uri.TryCreate(proxyUrl, UriKind.Absolute, out var proxy) &&
        Uri.UnescapeDataString(proxy.UserInfo.Split(':')[0]) == REDACTED;

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
