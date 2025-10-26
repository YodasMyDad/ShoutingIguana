using ShoutingIguana.Core.Configuration;

namespace ShoutingIguana.Core.Services;

/// <summary>
/// Service for testing proxy connections.
/// </summary>
public interface IProxyTestService
{
    /// <summary>
    /// Tests a proxy connection by attempting to fetch a known URL.
    /// </summary>
    /// <param name="proxySettings">Proxy settings to test.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Test result with success status and message.</returns>
    Task<ProxyTestResult> TestConnectionAsync(ProxySettings proxySettings, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a proxy connection test.
/// </summary>
public class ProxyTestResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int? StatusCode { get; set; }
    public TimeSpan? ResponseTime { get; set; }
    public string? ErrorDetails { get; set; }
}

