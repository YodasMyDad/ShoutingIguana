using Microsoft.Extensions.Time.Testing;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Fetching;
using ShoutingIguana.Engine.Tests.Support;

namespace ShoutingIguana.Engine.Tests.Fetching;

/// <summary>Shopify's crawler signature: read as Shopify gives it, refused when it isn't, and sent to its own host only.</summary>
public sealed class WebBotSignatureTests
{
    [Fact]
    public void ReadsTheNameAndWhenItStartsAndEnds()
    {
        var reading = WebBotSignature.Read(FakeSignatures.Input(), FakeSignatures.SIGNATURE);

        Assert.Null(reading.Problem);
        Assert.Equal("sig1", reading.Label);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(FakeSignatures.CREATED), reading.Created);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(FakeSignatures.LASTS_UNTIL), reading.Expires);
    }

    [Fact]
    public void ReadsAnyOrderOfParametersAndOptionalWhitespace()
    {
        var reading = WebBotSignature.Read(
            "web-bot=( \"@authority\"  \"signature-agent\" );tag=\"web-bot-auth\";expires=4102444800;keyid=\"k\";created=1767225600",
            "web-bot=:AAAA:");

        Assert.Null(reading.Problem);
        Assert.Equal("web-bot", reading.Label);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(FakeSignatures.LASTS_UNTIL), reading.Expires);
    }

    [Fact]
    public void AnInputWithoutAnEndHasNone()
    {
        var reading = WebBotSignature.Read("sig1=(\"@authority\");keyid=\"k\"", "sig1=:AAAA:");

        Assert.Null(reading.Problem);
        Assert.Null(reading.Expires);
        Assert.False(reading.HasExpired(DateTimeOffset.MaxValue));
    }

    [Theory]
    [InlineData("", "Signature-Input is empty.")]
    [InlineData("sig1", "Signature-Input isn't in the form Shopify gives")]
    [InlineData("sig1=:AAAA:", "Signature-Input isn't in the form Shopify gives")]
    [InlineData("sig1=(\"@authority\";created=1", "Signature-Input isn't in the form Shopify gives")]
    [InlineData("sig1=(\"@authority\");created=1\nX-Injected: 1", "Signature-Input isn't in the form Shopify gives")]
    [InlineData("Sig1=(\"@authority\")", "Signature-Input isn't in the form Shopify gives")]
    [InlineData("sig1=(\"@authority\");expires=soon", "Signature-Input's expires= isn't a time.")]
    [InlineData("sig1=(\"@authority\");created=\"yesterday\"", "Signature-Input's created= isn't a time.")]
    [InlineData("sig1=(\"@authority\");created=20;expires=10", "Signature-Input ends before it starts.")]
    [InlineData("sig1=(\"@authority\"), sig2=(\"@authority\")", "Signature-Input holds more than one signature")]
    public void RefusesAnInputNotInShopifysForm(string input, string problem)
    {
        var reading = WebBotSignature.Read(input, "sig1=:AAAA:");

        Assert.StartsWith(problem, reading.Problem);
    }

    [Theory]
    [InlineData("", "Signature is empty.")]
    [InlineData("AAAA", "Signature isn't in the form Shopify gives")]
    [InlineData("sig1=AAAA", "Signature isn't in the form Shopify gives")]
    [InlineData("sig1=:AA-A:", "Signature isn't in the form Shopify gives")]
    [InlineData("sig1=:AAAA", "Signature isn't in the form Shopify gives")]
    [InlineData("sig1=(\"x\")", "Signature isn't in the form Shopify gives")]
    [InlineData("sig1=:AAAA:, sig2=:AAAA:", "Signature holds more than one signature")]
    [InlineData("sig2=:AAAA:", "Signature-Input and Signature are for different signatures (sig1 and sig2)")]
    public void RefusesASignatureNotInShopifysForm(string signature, string problem)
    {
        var reading = WebBotSignature.Read(FakeSignatures.Input(), signature);

        Assert.StartsWith(problem, reading.Problem);
    }

    [Fact]
    public void NoProblemRepeatsTheValues()
    {
        string[] inputs = [$"{FakeSignatures.MARKER}", $"sig1=(\"{FakeSignatures.MARKER}\";", $"sig1=(\"@authority\");expires=\"{FakeSignatures.MARKER}\""];
        string[] signatures = [FakeSignatures.MARKER, $"sig1=:{FakeSignatures.MARKER}-:", $"sig9=:{FakeSignatures.MARKER}:"];

        foreach (var input in inputs.Append(FakeSignatures.Input()))
        {
            foreach (var signature in signatures)
            {
                var problem = WebBotSignature.Read(input, signature).Problem;
                Assert.NotNull(problem);
                Assert.DoesNotContain(FakeSignatures.MARKER, problem);
            }
        }
    }

    [Theory]
    [InlineData("Signature-Input: sig1=(\"@authority\")", "Signature-Input", "sig1=(\"@authority\")")]
    [InlineData("  signature-input:sig1=(\"@authority\")  ", "Signature-Input", "sig1=(\"@authority\")")]
    [InlineData("Signature: sig1=:AAAA:", "Signature", "sig1=:AAAA:")]
    [InlineData("sig1=:AAAA:\r\n", "Signature", "sig1=:AAAA:")]
    [InlineData("Signature-Input: x", "Signature", "Signature-Input: x")]
    public void CleansAValuePastedWithItsHeaderName(string pasted, string header, string value) =>
        Assert.Equal(value, WebBotSignature.Clean(pasted, header));

    [Theory]
    [InlineData("shop.example", "shop.example")]
    [InlineData("Shop.Example.", "shop.example")]
    [InlineData("https://shop.example/", "shop.example")]
    [InlineData("https://shop.example:443", "shop.example")]
    [InlineData("shop.example:8443", "shop.example:8443")]
    [InlineData("127.0.0.1:5001", "127.0.0.1:5001")]
    [InlineData("xn--bcher-kva.example", "xn--bcher-kva.example")]
    [InlineData("bücher.example", "xn--bcher-kva.example")]
    public void NamesTheHostAsItsAuthority(string given, string authority)
    {
        Assert.True(WebBotSignature.TryNormalizeHost(given, out var normalized));
        Assert.Equal(authority, normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("shop.example/products")]
    [InlineData("https://shop.example/?a=1")]
    [InlineData("user@shop.example")]
    [InlineData("ftp://shop.example")]
    [InlineData("shop example")]
    public void RefusesWhatIsNoHost(string given) => Assert.False(WebBotSignature.TryNormalizeHost(given, out _));

    [Theory]
    [InlineData("https://shop.example/products/a", true)]
    [InlineData("https://SHOP.example./cart", true)]
    [InlineData("https://shop.example:443/", true)]
    [InlineData("http://shop.example/products/a", false)]
    [InlineData("https://www.shop.example/", false)]
    [InlineData("https://cdn.shop.example/a.js", false)]
    [InlineData("https://shop.example:8443/", false)]
    [InlineData("https://other.example/", false)]
    [InlineData("https://shop.example.evil.example/", false)]
    [InlineData("https://cdn.shopify.com/s/files/a.png", false)]
    public void TheBookSignsItsOwnHostOverHttpsOnly(string url, bool signed)
    {
        var book = new SignatureBook(FakeSignatures.For("shop.example"), TimeProvider.System);

        Assert.Equal(signed, book.For(new Uri(url)) is not null);
    }

    [Fact]
    public void TheWwwTwinIsSignedOnlyWithASignatureOfItsOwn()
    {
        var both = new Dictionary<string, CrawlSignature>
        {
            ["shop.example"] = FakeSignatures.Valid(),
            ["www.shop.example"] = FakeSignatures.Valid() with { SignatureInput = FakeSignatures.Input(label: "sig2"), Signature = "sig2=:BBBB:" }
        };
        var book = new SignatureBook(both, TimeProvider.System);

        Assert.Equal(FakeSignatures.SIGNATURE, book.For(new Uri("https://shop.example/"))!.Signature);
        Assert.Equal("sig2=:BBBB:", book.For(new Uri("https://www.shop.example/"))!.Signature);
    }

    [Fact]
    public void AnExpiredSignatureIsNeverSent()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(FakeSignatures.CREATED));
        var expires = FakeSignatures.CREATED + 3_600;
        var book = new SignatureBook(FakeSignatures.For("shop.example", expires), clock);
        var page = new Uri("https://shop.example/");

        Assert.NotNull(book.For(page));
        clock.Advance(TimeSpan.FromSeconds(3_599));
        Assert.NotNull(book.For(page));
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Null(book.For(page));
    }

    [Fact]
    public void AMalformedSignatureIsNeverSent()
    {
        var book = new SignatureBook(new Dictionary<string, CrawlSignature>
        {
            ["shop.example"] = new() { SignatureInput = "nonsense", Signature = FakeSignatures.SIGNATURE }
        }, TimeProvider.System);

        Assert.Null(book.For(new Uri("https://shop.example/")));
    }

    [Fact]
    public void ARefusalNamesWhatWasWrongWithTheSignatureWhenItCanTell()
    {
        var made = DateTimeOffset.FromUnixTimeSeconds(FakeSignatures.CREATED);
        var clock = new FakeTimeProvider(made + TimeSpan.FromMinutes(12));
        var signatures = new Dictionary<string, CrawlSignature>
        {
            ["shop.example"] = FakeSignatures.Valid(),
            ["old.example"] = FakeSignatures.Valid(expires: FakeSignatures.CREATED + 60)
        };
        var book = new SignatureBook(signatures, clock);
        string? Explain(string url) => ShopifySignals.Explain(book.Explain(new Uri(url)), clock.GetUtcNow());

        Assert.Equal("Shopify refused the crawler's signature: it was made 12 minutes ago, and a new one can take up to an hour to start working",
            Explain("https://shop.example/products/a"));
        Assert.Equal("Shopify limits crawlers without a signature: the signature for old.example expired on 1 January 2026", Explain("https://old.example/"));
        Assert.Equal("Shopify limits crawlers without a signature: the signature is for shop.example, not www.shop.example, and each domain needs its own",
            Explain("https://www.shop.example/"));
        Assert.Null(Explain("https://other.example/"));
        Assert.Null(Explain("http://shop.example/"));

        clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal("Shopify refused the crawler's signature: it may not be active yet (a new one can take up to an hour), may have expired, or may be for another domain",
            Explain("https://shop.example/products/a"));
    }

    [Fact]
    public void ASignatureNeverPrintsItsValues() =>
        Assert.DoesNotContain(FakeSignatures.MARKER, FakeSignatures.Valid().ToString());
}
