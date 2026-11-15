using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using ShoutingIguana.Engine.Fetching;
using ShoutingIguana.Engine.Fetching.Models;

namespace ShoutingIguana.Engine.Tests.Fetching;

/// <summary>
/// The proxy test against a proxy of the test's own: a loopback server that takes a request in absolute form, as a proxy
/// does, and passes it on to the site (another loopback server), asks for a password, or says it can't reach the site.
/// </summary>
public sealed class ProxyTesterTests : IAsyncLifetime
{
    private const string USER = "crawler";
    private const string PASSWORD = "pa55:word";

    private readonly HttpClient _forwarder = new(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false });
    private LoopbackServer _site = null!;
    private LoopbackServer _proxy = null!;
    private ProxyMode _mode = ProxyMode.Forward;
    private int _proxied;

    private enum ProxyMode
    {
        Forward,
        NeedsPassword,
        BadGateway
    }

    public async Task InitializeAsync()
    {
        _site = await LoopbackServer.StartAsync(context =>
        {
            context.Response.StatusCode = context.Request.Path == "/missing" ? 404 : 200;
            return context.Response.WriteAsync("<html></html>");
        });
        _proxy = await LoopbackServer.StartAsync(ProxyAsync);
    }

    public async Task DisposeAsync()
    {
        _forwarder.Dispose();
        await _proxy.DisposeAsync();
        await _site.DisposeAsync();
    }

    [Fact]
    public async Task ASitesAnswerThroughTheProxyIsOkWithItsStatus()
    {
        var result = await TestAsync(_proxy.BaseUri.AbsoluteUri, _site.Url("/page"));

        Assert.Equal(ProxyTestOutcome.Ok, result.Outcome);
        Assert.Equal(200, result.Status);
        Assert.Equal(1, _proxied);
        Assert.StartsWith("The proxy answered and the site replied 200 OK in ", result.Message);
        Assert.EndsWith(" ms.", result.Message);

        // Any status the site gives is still the proxy working.
        var missing = await TestAsync(_proxy.BaseUri.AbsoluteUri, _site.Url("/missing"));
        Assert.Equal(ProxyTestOutcome.Ok, missing.Outcome);
        Assert.Equal(404, missing.Status);
        Assert.Contains("404 Not Found", missing.Message);
    }

    [Fact]
    public async Task AProxyThatAsksForAPasswordSaysSo()
    {
        _mode = ProxyMode.NeedsPassword;

        var without = await TestAsync(_proxy.BaseUri.AbsoluteUri, _site.Url("/page"));
        Assert.Equal(ProxyTestOutcome.ProxyAuthRequired, without.Outcome);
        Assert.Equal(407, without.Status);
        Assert.Equal("The proxy asks for a user name and password (407).", without.Message);

        var wrong = await TestAsync(WithCredentials(USER, "wrong"), _site.Url("/page"));
        Assert.Equal(ProxyTestOutcome.ProxyAuthRequired, wrong.Outcome);
        Assert.Equal("The proxy didn't accept the user name and password (407).", wrong.Message);
    }

    [Fact]
    public async Task TheUserNameAndPasswordInTheProxysAddressAreSent()
    {
        _mode = ProxyMode.NeedsPassword;

        var result = await TestAsync(WithCredentials(USER, PASSWORD), _site.Url("/page"));

        Assert.Equal(ProxyTestOutcome.Ok, result.Outcome);
        Assert.Equal(200, result.Status);
    }

    [Fact]
    public async Task NothingAnsweringAtTheProxysAddressIsRefused()
    {
        var result = await TestAsync($"http://127.0.0.1:{ClosedPort()}/", _site.Url("/page"));

        Assert.Equal(ProxyTestOutcome.ProxyRefused, result.Outcome);
        Assert.Null(result.Status);
        Assert.Equal("Nothing answered at the proxy's address.", result.Message);
    }

    [Fact]
    public async Task AProxyThatCantReachTheSiteSaysSo()
    {
        _mode = ProxyMode.BadGateway;

        var result = await TestAsync(_proxy.BaseUri.AbsoluteUri, _site.Url("/page"));

        Assert.Equal(ProxyTestOutcome.SiteUnreachable, result.Outcome);
        Assert.Equal(502, result.Status);
        Assert.Equal("The proxy answered 502 Bad Gateway: it usually means the proxy couldn't reach the site.", result.Message);
    }

    [Theory]
    [InlineData("proxy.example.com:8080")]
    [InlineData("ftp://proxy.example.com/")]
    [InlineData("/relative")]
    public async Task AProxyAddressTheCrawlerCantUseIsRefusedBeforeAnyFetch(string proxyUrl)
    {
        Assert.False(ProxyTester.IsValidProxy(proxyUrl));
        await Assert.ThrowsAsync<ArgumentException>(() => TestAsync(proxyUrl, _site.Url("/page")));
        Assert.Equal(0, _proxied);
    }

    [Fact]
    public async Task OnlyAWebAddressIsFetched()
    {
        Assert.True(ProxyTester.IsValidProxy("socks5://127.0.0.1:1080"));
        await Assert.ThrowsAsync<ArgumentException>(() => TestAsync(_proxy.BaseUri.AbsoluteUri, new Uri("ftp://example.com/")));
    }

    private static Task<ProxyTestResult> TestAsync(string proxyUrl, Uri url) =>
        ProxyTester.TestAsync(proxyUrl, url, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), CancellationToken.None);

    private string WithCredentials(string user, string password) =>
        new UriBuilder(_proxy.BaseUri) { UserName = Uri.EscapeDataString(user), Password = Uri.EscapeDataString(password) }.Uri.AbsoluteUri;

    private static int ClosedPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private async Task ProxyAsync(HttpContext context)
    {
        Interlocked.Increment(ref _proxied);
        // A request to a proxy names the whole address it wants: "GET http://127.0.0.1:1234/page HTTP/1.1".
        var target = context.Features.Get<IHttpRequestFeature>()!.RawTarget;
        if (!target.StartsWith("http://", StringComparison.Ordinal))
        {
            context.Response.StatusCode = 400;
            return;
        }

        if (_mode == ProxyMode.BadGateway)
        {
            context.Response.StatusCode = 502;
            return;
        }

        var expected = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{USER}:{PASSWORD}"));
        if (_mode == ProxyMode.NeedsPassword && context.Request.Headers["Proxy-Authorization"] != expected)
        {
            context.Response.StatusCode = 407;
            context.Response.Headers["Proxy-Authenticate"] = "Basic realm=\"test\"";
            return;
        }

        using var response = await _forwarder.GetAsync(target, HttpCompletionOption.ResponseHeadersRead);
        context.Response.StatusCode = (int)response.StatusCode;
    }
}
