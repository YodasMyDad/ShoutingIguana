using System.Net;
using System.Text;
using ShoutingIguana.Sidecar.Tests.Support;

namespace ShoutingIguana.Sidecar.Tests;

/// <summary>The token, the Host header (DNS rebinding), the Origin allowlist and CORS, and the body limit.</summary>
public sealed class SecurityTests : IAsyncLifetime
{
    private TestSidecar _sidecar = null!;

    public async Task InitializeAsync() => _sidecar = await TestSidecar.StartAsync();

    public async Task DisposeAsync() => await _sidecar.DisposeAsync();

    [Theory]
    [InlineData(null)]
    [InlineData("Bearer wrong-token")]
    [InlineData("Bearer " + TestSidecar.TOKEN + "x")]
    [InlineData("Bearer test-token-0123456789abcde")]
    [InlineData("Basic " + TestSidecar.TOKEN)]
    [InlineData(TestSidecar.TOKEN)]
    public async Task EveryRequestNeedsTheToken(string? authorization)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/health");
        request.Headers.Authorization = null;
        if (authorization is not null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
        }

        using var response = await Send(request, withToken: false);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.Single().Scheme);
        Assert.Contains("\"type\":\"unauthorized\"", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task TheTokenOpensEveryEndpointIncludingUnknownOnes()
    {
        using (var health = await Send(new HttpRequestMessage(HttpMethod.Get, "api/health")))
        {
            Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        }

        // An unknown path is refused without the token like any other, and is a problem with it.
        using (var anonymous = await Send(new HttpRequestMessage(HttpMethod.Get, "api/nothing-here"), withToken: false))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }

        TestSidecar.AssertProblem(await _sidecar.SendAsync(HttpMethod.Get, "api/nothing-here"), HttpStatusCode.NotFound, "not-found");
    }

    [Theory]
    [InlineData("evil.example")]
    [InlineData("evil.example:{port}")]
    [InlineData("127.0.0.1")]
    [InlineData("127.0.0.1:1")]
    [InlineData("localhost.evil.example:{port}")]
    [InlineData("[::1]:{port}")]
    public async Task TheHostMustBeTheLoopbackAddressOnThisPort(string host)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/health");
        request.Headers.Host = host.Replace("{port}", _sidecar.Host.Port.ToString(System.Globalization.CultureInfo.InvariantCulture));

        using var response = await Send(request);

        Assert.Equal(HttpStatusCode.MisdirectedRequest, response.StatusCode);
        Assert.Contains("\"type\":\"bad-host\"", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("127.0.0.1:{port}")]
    [InlineData("localhost:{port}")]
    [InlineData("LOCALHOST:{port}")]
    public async Task LoopbackNamesAreAccepted(string host)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/health");
        request.Headers.Host = host.Replace("{port}", _sidecar.Host.Port.ToString(System.Globalization.CultureInfo.InvariantCulture));

        using var response = await Send(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("http://evil.example")]
    [InlineData("null")]
    [InlineData("http://tauri.localhost.evil.example")]
    [InlineData("https://tauri.localhost")]
    public async Task AnOriginMustBeOnTheAllowlist(string origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/health");
        request.Headers.Add("Origin", origin);

        using var response = await Send(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.Contains("\"type\":\"origin-not-allowed\"", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AnAllowedOriginGetsCorsHeadersWithoutCredentials()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/health");
        request.Headers.Add("Origin", TestSidecar.ORIGIN);

        using var response = await Send(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(TestSidecar.ORIGIN, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Equal("Origin", response.Headers.GetValues("Vary").Single());
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));

        // A refusal carries them too, or the UI couldn't read why it was refused.
        using var refused = new HttpRequestMessage(HttpMethod.Get, "api/health");
        refused.Headers.Add("Origin", TestSidecar.ORIGIN);
        using var unauthorized = await Send(refused, withToken: false);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Equal(TestSidecar.ORIGIN, unauthorized.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Equal("Origin", unauthorized.Headers.GetValues("Vary").Single());
        Assert.True(unauthorized.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task NoAnswerIsKept()
    {
        using var health = await Send(new HttpRequestMessage(HttpMethod.Get, "api/health"));
        using var problem = await Send(new HttpRequestMessage(HttpMethod.Get, "api/crawls/0123456789abcdef"));

        Assert.Equal(HttpStatusCode.NotFound, problem.StatusCode);
        Assert.All(new[] { health, problem }, response => Assert.Equal("no-store", response.Headers.CacheControl?.ToString()));
    }

    [Fact]
    public async Task PreflightsAreAnsweredForAllowedOriginsOnly()
    {
        using var allowed = Preflight(TestSidecar.ORIGIN);
        using (var response = await Send(allowed, withToken: false))
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Equal(TestSidecar.ORIGIN, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
            Assert.Equal("GET, POST, PUT, DELETE, OPTIONS", response.Headers.GetValues("Access-Control-Allow-Methods").Single());
            Assert.Equal("authorization, content-type, last-event-id", response.Headers.GetValues("Access-Control-Allow-Headers").Single());
            Assert.Equal("7200", response.Headers.GetValues("Access-Control-Max-Age").Single());
            Assert.Equal("Origin", response.Headers.GetValues("Vary").Single());
            Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
            Assert.False(response.Headers.Contains("Access-Control-Allow-Private-Network"));
        }

        // Chromium's Private Network Access asks first whether a page may call a loopback address.
        using var privateNetwork = Preflight(TestSidecar.ORIGIN);
        privateNetwork.Headers.Add("Access-Control-Request-Private-Network", "true");
        using (var response = await Send(privateNetwork, withToken: false))
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Equal("true", response.Headers.GetValues("Access-Control-Allow-Private-Network").Single());
        }

        using var denied = Preflight("http://evil.example");
        using (var response = await Send(denied, withToken: false))
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
            Assert.False(response.Headers.Contains("Access-Control-Allow-Methods"));
        }

        using var withoutOrigin = new HttpRequestMessage(HttpMethod.Options, "api/crawls");
        using (var response = await Send(withoutOrigin, withToken: false))
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Fact]
    public async Task ABodyOverOneMegabyteIsRefused()
    {
        var path = new string('a', 1024 * 1024);
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/crawls/open")
        {
            Content = new StringContent($"{{\"path\":\"{path}\"}}", Encoding.UTF8, "application/json")
        };

        using var response = await Send(request);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Contains("\"type\":\"body-too-large\"", await response.Content.ReadAsStringAsync());
    }

    private static HttpRequestMessage Preflight(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "api/crawls");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "authorization, content-type");
        return request;
    }

    private async Task<HttpResponseMessage> Send(HttpRequestMessage request, bool withToken = true)
    {
        using (request)
        {
            using var client = new HttpClient { BaseAddress = _sidecar.Client.BaseAddress };
            if (withToken && request.Headers.Authorization is null && !request.Headers.Contains("Authorization"))
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TestSidecar.TOKEN);
            }

            return await client.SendAsync(request);
        }
    }
}
