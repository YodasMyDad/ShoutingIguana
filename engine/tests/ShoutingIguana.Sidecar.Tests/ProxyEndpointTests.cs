using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using ShoutingIguana.Sidecar.Tests.Support;
using ShoutingIguana.TestSite;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.Sidecar.Tests;

/// <summary>The proxy test: an answer for whatever the fetch found, and a bad request for what can't be fetched at all.</summary>
public sealed class ProxyEndpointTests : IAsyncLifetime
{
    private TestSidecar _sidecar = null!;

    public async Task InitializeAsync() => _sidecar = await TestSidecar.StartAsync();

    public async Task DisposeAsync() => await _sidecar.DisposeAsync();

    [Fact]
    public async Task ARefusedProxyIsAnAnswerInPlainWords()
    {
        int port;
        using (var listener = new TcpListener(IPAddress.Loopback, 0))
        {
            listener.Start();
            port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
        }

        var answer = await _sidecar.PostAsync("api/proxy/test", new JsonObject
        {
            ["proxyUrl"] = $"http://user:secret@127.0.0.1:{port}/",
            ["url"] = "http://127.0.0.1:9/page"
        });

        Assert.Equal(HttpStatusCode.OK, answer.Status);
        Assert.Equal("proxyRefused", (string?)answer.Body!["outcome"]);
        Assert.Null(answer.Body["status"]);
        Assert.True((int?)answer.Body["elapsedMs"] >= 0);
        Assert.Equal("Nothing answered at the proxy's address.", (string?)answer.Body["message"]);
    }

    /// <summary>A SOCKS5 proxy that asks for a login (RFC 1929): through it with the right one, and plain words for a wrong one.</summary>
    [Fact]
    public async Task ASocks5ProxysLoginIsTestedThroughIt()
    {
        await using var site = await SyntheticSiteServer.StartAsync(new SyntheticSiteOptions { PageCount = 2 });
        await using var socks = Socks5ProxyServer.Start("crawler", "pa55:w@rd");

        var through = await _sidecar.PostAsync("api/proxy/test", new JsonObject { ["proxyUrl"] = socks.Address("crawler", "pa55:w@rd"), ["url"] = site.BaseUri.AbsoluteUri });
        Assert.Equal(HttpStatusCode.OK, through.Status);
        Assert.Equal(("ok", 200), ((string?)through.Body!["outcome"], (int?)through.Body["status"]));
        Assert.Equal([("crawler", "pa55:w@rd", true)], socks.Logins);
        Assert.Contains($"127.0.0.1:{site.BaseUri.Port}", socks.Tunnels);

        var wrong = await _sidecar.PostAsync("api/proxy/test", new JsonObject { ["proxyUrl"] = socks.Address("crawler", "wrong"), ["url"] = site.BaseUri.AbsoluteUri });
        Assert.Equal(("proxyAuthRequired", null, "The SOCKS5 proxy didn't accept the user name and password."),
            ((string?)wrong.Body!["outcome"], (int?)wrong.Body["status"], (string?)wrong.Body["message"]));
        Assert.Single(socks.Tunnels);
    }

    [Theory]
    [InlineData("""{"url":"https://example.com/"}""")]
    [InlineData("""{"proxyUrl":"proxy.example.com:8080","url":"https://example.com/"}""")]
    [InlineData("""{"proxyUrl":"http://proxy.example.com:8080","url":"example.com"}""")]
    [InlineData("""{"proxyUrl":"http://proxy.example.com:8080","url":"ftp://example.com/"}""")]
    [InlineData("""{"proxyUrl":"http://proxy.example.com:8080","url":"https://me:pw@example.com/"}""")]
    [InlineData("""{"proxyUrl":"http://proxy.example.com:8080","url":"https://example.com/","timeout":5}""")]
    public async Task WhatCantBeFetchedIsABadRequest(string body)
    {
        var problem = TestSidecar.AssertProblem(
            await _sidecar.PostAsync("api/proxy/test", JsonNode.Parse(body)), HttpStatusCode.BadRequest, "bad-request");
        Assert.False(string.IsNullOrEmpty((string?)problem["title"]));
    }
}
