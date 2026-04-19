using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace ShoutingIguana.Core.Services;

public class RobotsService : IRobotsService, IDisposable
{
    private const string HttpClientName = "robots";
    private const int CacheSizeLimit = 10_000;
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(24);

    private readonly ILogger<RobotsService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly MemoryCache _cache;

    public RobotsService(ILogger<RobotsService> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = CacheSizeLimit });
    }

    public async Task<bool> IsAllowedAsync(string url, string userAgent, HttpClient? httpClient = null)
    {
        try
        {
            var uri = new Uri(url);
            var host = $"{uri.Scheme}://{uri.Host}";

            var robotsTxt = await GetRobotsTxtAsync(host, httpClient).ConfigureAwait(false);
            if (robotsTxt == null)
            {
                return true;
            }

            return robotsTxt.IsAllowed(uri.PathAndQuery, userAgent);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking robots.txt for {Url}, allowing by default", url);
            return true;
        }
    }

    public async Task<double?> GetCrawlDelayAsync(string host, string userAgent, HttpClient? httpClient = null)
    {
        try
        {
            var robotsTxt = await GetRobotsTxtAsync(host, httpClient).ConfigureAwait(false);
            return robotsTxt?.GetCrawlDelay(userAgent);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting crawl delay for {Host}", host);
            return null;
        }
    }

    public async Task<List<string>> GetSitemapUrlsFromRobotsTxtAsync(string host, HttpClient? httpClient = null)
    {
        try
        {
            var robotsTxt = await GetRobotsTxtAsync(host, httpClient).ConfigureAwait(false);
            return robotsTxt?.GetSitemapUrls() ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting sitemap URLs from robots.txt for {Host}", host);
            return [];
        }
    }

    private async Task<RobotsTxtFile?> GetRobotsTxtAsync(string host, HttpClient? httpClient = null)
    {
        if (_cache.TryGetValue<RobotsTxtFile?>(host, out var cached))
        {
            return cached;
        }

        RobotsTxtFile? robotsTxt = null;
        try
        {
            var robotsUrl = $"{host}/robots.txt";
            var client = httpClient ?? _httpClientFactory.CreateClient(HttpClientName);
            var response = await client.GetAsync(robotsUrl).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                CacheEntry(host, null);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            robotsTxt = new RobotsTxtFile(content);
            CacheEntry(host, robotsTxt);
            _logger.LogInformation("Fetched robots.txt for {Host}", host);
            return robotsTxt;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogDebug("Cannot fetch robots.txt for {Host}: {Message}", host, ex.Message);
            CacheEntry(host, null);
            return null;
        }
        catch (TaskCanceledException)
        {
            _logger.LogDebug("Timeout fetching robots.txt for {Host}", host);
            CacheEntry(host, null);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Unexpected error fetching robots.txt for {Host}: {Message}", host, ex.Message);
            CacheEntry(host, null);
            return null;
        }
    }

    private void CacheEntry(string host, RobotsTxtFile? value)
    {
        _cache.Set(host, value, new MemoryCacheEntryOptions
        {
            Size = 1,
            AbsoluteExpirationRelativeToNow = CacheLifetime,
        });
    }

    public void Dispose() => _cache.Dispose();

    private class RobotsTxtFile
    {
        // Rules grouped by user-agent (case-insensitive). RFC 9309 says a specific
        // UA group supersedes the '*' group entirely — we never mix rules across
        // groups, so the grouping happens at parse time.
        private readonly Dictionary<string, List<RobotRule>> _rulesByUserAgent = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, double> _crawlDelays = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _sitemapUrls = new();

        public RobotsTxtFile(string content)
        {
            ParseRobotsTxt(content);
        }

        private void ParseRobotsTxt(string content)
        {
            var lines = content.Split('\n');
            var pendingAgents = new List<string>();
            var expectingRules = false;

            foreach (var line in lines)
            {
                var trimmed = StripComment(line).Trim();
                if (string.IsNullOrEmpty(trimmed))
                {
                    continue;
                }

                var colon = trimmed.IndexOf(':');
                if (colon < 0)
                {
                    continue;
                }

                var field = trimmed[..colon].Trim().ToLowerInvariant();
                var value = trimmed[(colon + 1)..].Trim();

                switch (field)
                {
                    case "user-agent":
                        if (expectingRules)
                        {
                            // A new User-agent after any rule starts a fresh group.
                            pendingAgents.Clear();
                            expectingRules = false;
                        }
                        if (!string.IsNullOrEmpty(value))
                        {
                            pendingAgents.Add(value);
                            if (!_rulesByUserAgent.ContainsKey(value))
                            {
                                _rulesByUserAgent[value] = new List<RobotRule>();
                            }
                        }
                        break;
                    case "disallow":
                    case "allow":
                        if (pendingAgents.Count == 0)
                        {
                            // Rule before any User-agent — ignore per RFC.
                            break;
                        }
                        expectingRules = true;
                        var allow = field == "allow";
                        foreach (var agent in pendingAgents)
                        {
                            _rulesByUserAgent[agent].Add(new RobotRule(value, allow));
                        }
                        break;
                    case "crawl-delay":
                        if (pendingAgents.Count > 0 && double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var delay))
                        {
                            expectingRules = true;
                            foreach (var agent in pendingAgents)
                            {
                                _crawlDelays[agent] = delay;
                            }
                        }
                        break;
                    case "sitemap":
                        if (!string.IsNullOrEmpty(value))
                        {
                            _sitemapUrls.Add(value);
                        }
                        break;
                }
            }
        }

        public bool IsAllowed(string path, string userAgent)
        {
            var rules = ResolveRules(userAgent);
            if (rules == null || rules.Count == 0)
            {
                return true;
            }

            // RFC 9309 match precedence: longest matching pattern wins; if two
            // rules tie on match length, Allow beats Disallow.
            RobotRule? winner = null;
            int winnerMatchLength = -1;

            foreach (var rule in rules)
            {
                var matchLength = PatternMatchLength(rule.Path, path);
                if (matchLength < 0)
                {
                    continue;
                }

                if (matchLength > winnerMatchLength
                    || (matchLength == winnerMatchLength && rule.Allow && winner is { Allow: false }))
                {
                    winner = rule;
                    winnerMatchLength = matchLength;
                }
            }

            return winner == null || winner.Allow;
        }

        public double? GetCrawlDelay(string userAgent)
        {
            foreach (var (pattern, delay) in _crawlDelays)
            {
                if (!string.Equals(pattern, "*", StringComparison.Ordinal)
                    && UserAgentMatches(pattern, userAgent))
                {
                    return delay;
                }
            }

            return _crawlDelays.TryGetValue("*", out var wildcardDelay) ? wildcardDelay : null;
        }

        public List<string> GetSitemapUrls()
        {
            return new List<string>(_sitemapUrls);
        }

        private List<RobotRule>? ResolveRules(string userAgent)
        {
            // Prefer the most-specific matching agent group. Fall back to '*' only
            // when no specific group matches (RFC 9309 §2.2.1).
            List<RobotRule>? bestMatch = null;
            int bestTokenLength = -1;

            foreach (var (pattern, rules) in _rulesByUserAgent)
            {
                if (string.Equals(pattern, "*", StringComparison.Ordinal))
                {
                    continue;
                }

                if (UserAgentMatches(pattern, userAgent) && pattern.Length > bestTokenLength)
                {
                    bestMatch = rules;
                    bestTokenLength = pattern.Length;
                }
            }

            if (bestMatch != null)
            {
                return bestMatch;
            }

            return _rulesByUserAgent.TryGetValue("*", out var wildcard) ? wildcard : null;
        }

        private static bool UserAgentMatches(string pattern, string userAgent)
        {
            // robots.txt user-agent tokens are matched case-insensitively on
            // substring (a pattern "Googlebot" matches "Googlebot-Image/1.0").
            return !string.IsNullOrEmpty(pattern)
                && userAgent.Contains(pattern, StringComparison.OrdinalIgnoreCase);
        }

        private static int PatternMatchLength(string pattern, string path)
        {
            // Empty pattern matches nothing (per spec an empty Disallow allows all).
            if (string.IsNullOrEmpty(pattern))
            {
                return -1;
            }

            // Anchor at end-of-URL when the pattern ends in '$'.
            var endAnchor = pattern.EndsWith('$');
            var trimmed = endAnchor ? pattern[..^1] : pattern;

            // Split into literal segments separated by '*' wildcards. Every segment
            // must match in order; the first segment is anchored to the start of
            // the path.
            var segments = trimmed.Split('*');
            int cursor = 0;

            for (int i = 0; i < segments.Length; i++)
            {
                var segment = segments[i];
                if (segment.Length == 0)
                {
                    continue;
                }

                if (i == 0)
                {
                    if (!path.AsSpan(cursor).StartsWith(segment))
                    {
                        return -1;
                    }
                    cursor += segment.Length;
                }
                else
                {
                    var found = path.IndexOf(segment, cursor, StringComparison.Ordinal);
                    if (found < 0)
                    {
                        return -1;
                    }
                    cursor = found + segment.Length;
                }
            }

            if (endAnchor && cursor != path.Length)
            {
                return -1;
            }

            // Match length is the pattern length (minus the trailing '$' if present).
            return trimmed.Length;
        }

        private static string StripComment(string line)
        {
            var hash = line.IndexOf('#');
            return hash < 0 ? line : line[..hash];
        }

        private sealed record RobotRule(string Path, bool Allow);
    }
}
