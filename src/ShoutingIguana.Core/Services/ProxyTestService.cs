using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Logging;
using ShoutingIguana.Core.Configuration;

namespace ShoutingIguana.Core.Services;

/// <summary>
/// Implementation of IProxyTestService for testing proxy connections.
/// </summary>
public class ProxyTestService(ILogger<ProxyTestService> logger) : IProxyTestService
{
    private readonly ILogger<ProxyTestService> _logger = logger;
    private const string TestUrl = "http://www.example.com";

    public async Task<ProxyTestResult> TestConnectionAsync(ProxySettings proxySettings, CancellationToken cancellationToken = default)
    {
        if (!proxySettings.Enabled || string.IsNullOrWhiteSpace(proxySettings.Server))
        {
            return new ProxyTestResult
            {
                Success = false,
                Message = "Proxy is not enabled or server is not configured"
            };
        }

        try
        {
            _logger.LogInformation("Testing proxy connection: {ProxyUrl}", proxySettings.GetProxyUrl());
            var stopwatch = Stopwatch.StartNew();

            using var handler = new HttpClientHandler
            {
                Proxy = CreateWebProxy(proxySettings),
                UseProxy = true,
                PreAuthenticate = proxySettings.RequiresAuthentication
            };

            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

            var response = await client.GetAsync(TestUrl, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            var result = new ProxyTestResult
            {
                Success = response.IsSuccessStatusCode,
                StatusCode = (int)response.StatusCode,
                ResponseTime = stopwatch.Elapsed,
                Message = response.IsSuccessStatusCode
                    ? $"✓ Connection successful (HTTP {response.StatusCode}) in {stopwatch.ElapsedMilliseconds}ms"
                    : $"✗ Connection failed with HTTP {response.StatusCode}"
            };

            _logger.LogInformation("Proxy test result: {Success}, Status: {StatusCode}, Time: {Time}ms",
                result.Success, result.StatusCode, stopwatch.ElapsedMilliseconds);

            return result;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Proxy test failed with HTTP request exception");
            return new ProxyTestResult
            {
                Success = false,
                Message = "✗ Connection failed: Unable to connect through proxy",
                ErrorDetails = ex.Message
            };
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Proxy test timed out");
            return new ProxyTestResult
            {
                Success = false,
                Message = "✗ Connection timed out",
                ErrorDetails = "The connection attempt timed out after 30 seconds"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Proxy test failed with unexpected error");
            return new ProxyTestResult
            {
                Success = false,
                Message = $"✗ Unexpected error: {ex.Message}",
                ErrorDetails = ex.ToString()
            };
        }
    }

    private static WebProxy CreateWebProxy(ProxySettings settings)
    {
        var proxy = new WebProxy(settings.GetProxyUrl());

        if (settings.RequiresAuthentication && !string.IsNullOrWhiteSpace(settings.Username))
        {
            var password = settings.GetPassword();
            proxy.Credentials = new NetworkCredential(settings.Username, password);
        }

        // Configure bypass list
        if (settings.BypassList.Count > 0)
        {
            proxy.BypassList = settings.BypassList.ToArray();
        }

        return proxy;
    }
}

