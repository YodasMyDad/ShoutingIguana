using System.Net;
using ShoutingIguana.TestSite;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.Engine.Tests.TestSite;

/// <summary>
/// The synthetic Shopify store itself, asked directly: Shopify's headers, the unsigned allowance on its product pages and
/// then 429 with Retry-After: 60, and its one signature taken only while it lasts and only for the host it was made for.
/// </summary>
public sealed class SyntheticShopifyTests : IAsyncLifetime
{
    private SyntheticSiteServer _server = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _server = await SyntheticSiteServer.StartAsync(new SyntheticSiteOptions { PageCount = 4, Https = true, Shopify = new SyntheticShopify(UnsignedProductRequests: 2) });
        var trusted = _server.Certificate!.GetCertHashString();
        _client = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false,
            ServerCertificateCustomValidationCallback = (_, certificate, _, _) => certificate?.GetCertHashString() == trusted
        });
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _server.DisposeAsync();
    }

    [Fact]
    public async Task EveryPageSaysItIsShopifys()
    {
        using var home = await GetAsync("/");

        Assert.Equal("Shopify", home.Headers.GetValues("powered-by").Single());
        Assert.Contains("Shopify.theme = ", await home.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnsignedProductRequestsAreRefusedPastTheAllowance()
    {
        using var first = await GetAsync("/p/1");
        using var second = await GetAsync("/p/2");
        using var third = await GetAsync("/p/3");
        using var home = await GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(60), third.Headers.RetryAfter?.Delta);
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
    }

    [Fact]
    public async Task ItsSignatureIsAlwaysLetThrough()
    {
        for (var i = 0; i < 5; i++)
        {
            using var product = await GetAsync("/p/1", SyntheticShopify.SIGNATURE_INPUT);
            Assert.Equal(HttpStatusCode.OK, product.StatusCode);
        }

        Assert.Equal(0, _server.ShopifyRefused);
        Assert.Equal(5, _server.ShopifySigned);
    }

    [Fact]
    public async Task AnExpiredSignatureIsRefused()
    {
        await UseUpTheAllowanceAsync();

        using var expired = await GetAsync("/p/1", SyntheticShopify.EXPIRED_SIGNATURE_INPUT);

        Assert.Equal(HttpStatusCode.TooManyRequests, expired.StatusCode);
    }

    [Fact]
    public async Task ASignatureForAnotherHostIsRefused()
    {
        await using var server = await SyntheticSiteServer.StartAsync(new SyntheticSiteOptions
        {
            PageCount = 4,
            Https = true,
            Shopify = new SyntheticShopify(UnsignedProductRequests: 0, SignatureFor: "www.shop.example")
        });
        var trusted = server.Certificate!.GetCertHashString();
        using var client = new HttpClient(new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, certificate, _, _) => certificate?.GetCertHashString() == trusted });
        using var request = Signed(new Uri(server.BaseUri, "/p/1"), SyntheticShopify.SIGNATURE_INPUT);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    [Fact]
    public async Task ASignatureToAnotherHostIsCounted()
    {
        using var request = Signed(new Uri($"https://localhost:{_server.BaseUri.Port}/"), SyntheticShopify.SIGNATURE_INPUT);

        using var response = await _client.SendAsync(request);

        Assert.Equal(1, _server.ShopifySignedElsewhere);
    }

    [Fact]
    public async Task TheAwayPathLeavesForAnotherHost()
    {
        using var away = await GetAsync(SyntheticShopify.AWAY_PATH);

        Assert.Equal(HttpStatusCode.MovedPermanently, away.StatusCode);
        Assert.Equal($"https://localhost:{_server.BaseUri.Port}/p/2", away.Headers.Location?.AbsoluteUri);
    }

    private async Task UseUpTheAllowanceAsync()
    {
        for (var i = 0; i < 3; i++)
        {
            using var response = await GetAsync("/p/2");
        }
    }

    private Task<HttpResponseMessage> GetAsync(string path, string? signatureInput = null) =>
        _client.SendAsync(signatureInput is null ? new HttpRequestMessage(HttpMethod.Get, new Uri(_server.BaseUri, path)) : Signed(new Uri(_server.BaseUri, path), signatureInput));

    private static HttpRequestMessage Signed(Uri uri, string signatureInput)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("Signature-Input", signatureInput);
        request.Headers.TryAddWithoutValidation("Signature", SyntheticShopify.SIGNATURE);
        request.Headers.TryAddWithoutValidation("Signature-Agent", SyntheticShopify.AGENT);
        return request;
    }
}
