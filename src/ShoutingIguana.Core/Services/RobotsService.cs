using System.Linq;
using Microsoft.Extensions.Logging;

namespace ShoutingIguana.Core.Services;

public class RobotsService(ILogger<RobotsService> logger, IHttpClientFactory httpClientFactory) : IRobotsService
{
    private readonly ILogger<RobotsService> _logger = logger;
    private readonly HttpClient _httpClient = CreateHttpClient(httpClientFactory);
    private readonly Dictionary<string, RobotsTxtFile> _cache = [];

    private static HttpClient CreateHttpClient(IHttpClientFactory factory)
    {
        var client = factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(10);
        return client;
    }

    public async Task<bool> IsAllowedAsync(string url, string userAgent)
    {
        try
        {
            var uri = new Uri(url);
            var host = $"{uri.Scheme}://{uri.Host}";
            
            var robotsTxt = await GetRobotsTxtAsync(host);
            if (robotsTxt == null)
            {
                // If no robots.txt, allow all
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

    public async Task<double?> GetCrawlDelayAsync(string host, string userAgent)
    {
        try
        {
            var robotsTxt = await GetRobotsTxtAsync(host);
            return robotsTxt?.GetCrawlDelay(userAgent);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting crawl delay for {Host}", host);
            return null;
        }
    }

    private async Task<RobotsTxtFile?> GetRobotsTxtAsync(string host)
    {
        if (_cache.TryGetValue(host, out var cached))
        {
            return cached;
        }

        try
        {
            var robotsUrl = $"{host}/robots.txt";
            var response = await _httpClient.GetAsync(robotsUrl);
            
            if (!response.IsSuccessStatusCode)
            {
                // No robots.txt found, cache null result
                _cache[host] = null!;
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            var robotsTxt = new RobotsTxtFile(content);
            _cache[host] = robotsTxt;
            
            // Limit cache size to prevent unbounded growth
            if (_cache.Count > 10000)
            {
                // Remove half of the oldest entries (simple LRU-like behavior)
                var toRemove = _cache.Keys.Take(_cache.Count / 2).ToList();
                foreach (var key in toRemove)
                {
                    _cache.Remove(key);
                }
            }
            
            _logger.LogInformation("Fetched robots.txt for {Host}", host);
            return robotsTxt;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch robots.txt for {Host}", host);
            _cache[host] = null!;
            return null;
        }
    }

    private class RobotsTxtFile
    {
        private readonly List<RobotRule> _rules = new();
        private readonly Dictionary<string, double> _crawlDelays = new();

        public RobotsTxtFile(string content)
        {
            ParseRobotsTxt(content);
        }

        private void ParseRobotsTxt(string content)
        {
            var lines = content.Split('\n');
            string? currentUserAgent = null;

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                    continue;

                var parts = trimmed.Split(':', 2);
                if (parts.Length != 2)
                    continue;

                var field = parts[0].Trim().ToLowerInvariant();
                var value = parts[1].Trim();

                switch (field)
                {
                    case "user-agent":
                        currentUserAgent = value;
                        break;
                    case "disallow":
                        if (!string.IsNullOrEmpty(currentUserAgent) && !string.IsNullOrEmpty(value))
                        {
                            _rules.Add(new RobotRule
                            {
                                UserAgent = currentUserAgent,
                                Path = value,
                                Allow = false
                            });
                        }
                        break;
                    case "allow":
                        if (!string.IsNullOrEmpty(currentUserAgent) && !string.IsNullOrEmpty(value))
                        {
                            _rules.Add(new RobotRule
                            {
                                UserAgent = currentUserAgent,
                                Path = value,
                                Allow = true
                            });
                        }
                        break;
                    case "crawl-delay":
                        if (!string.IsNullOrEmpty(currentUserAgent) && double.TryParse(value, out var delay))
                        {
                            _crawlDelays[currentUserAgent] = delay;
                        }
                        break;
                }
            }
        }

        public bool IsAllowed(string path, string userAgent)
        {
            var applicableRules = _rules
                .Where(r => MatchesUserAgent(r.UserAgent, userAgent) && path.StartsWith(r.Path))
                .OrderByDescending(r => r.Path.Length)
                .ToList();

            if (!applicableRules.Any())
                return true;

            var mostSpecificRule = applicableRules.First();
            return mostSpecificRule.Allow;
        }

        public double? GetCrawlDelay(string userAgent)
        {
            if (_crawlDelays.TryGetValue(userAgent, out var delay))
                return delay;
            
            if (_crawlDelays.TryGetValue("*", out var wildcardDelay))
                return wildcardDelay;

            return null;
        }

        private static bool MatchesUserAgent(string pattern, string userAgent)
        {
            return pattern == "*" || userAgent.Contains(pattern, StringComparison.OrdinalIgnoreCase);
        }

        private class RobotRule
        {
            public string UserAgent { get; set; } = string.Empty;
            public string Path { get; set; } = string.Empty;
            public bool Allow { get; set; }
        }
    }
}

