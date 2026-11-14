using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using ShoutingIguana.Sidecar.Tests.Support;

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
