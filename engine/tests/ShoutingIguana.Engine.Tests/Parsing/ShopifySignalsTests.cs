using ShoutingIguana.Engine.Fetching;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing;
using ShoutingIguana.Engine.Serialization;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Tests.Parsing;

/// <summary>
/// A Shopify store, told by what its responses and pages carry. The signals are the ones every page of the Jitsunami
/// crawl had: <c>powered-by: Shopify</c> and <c>shopify-complexity-score</c> on each storefront response (a Cloudflare
/// redirect has neither), and in each page an inline <c>Shopify.theme = {…}</c> and scripts from cdn.shopify.com.
/// </summary>
public sealed class ShopifySignalsTests
{
    private static readonly Uri PAGE = new("https://shop.example/");

    [Theory]
    [InlineData("powered-by", "Shopify", "powered-by: Shopify")]
    [InlineData("powered-by", "shopify", "powered-by: Shopify")]
    [InlineData("x-shopid", "78530740512", "x-shopid")]
    [InlineData("x-shopify-stage", "production", "x-shopify-stage")]
    [InlineData("x-sorting-hat-shopid", "1", "x-sorting-hat-shopid")]
    [InlineData("shopify-complexity-score", "1700", "shopify-complexity-score")]
    public void AShopifyHeaderSaysSo(string name, string value, string signal) =>
        Assert.Equal(signal, ShopifySignals.FromHeaders([new ResponseHeader("server", "cloudflare"), new ResponseHeader(name, value)]));

    [Theory]
    [InlineData("powered-by", "WordPress")]
    [InlineData("server", "cloudflare")]
    [InlineData("x-powered-by", "PHP/8.3")]
    public void OtherHeadersDoNot(string name, string value) =>
        Assert.Null(ShopifySignals.FromHeaders([new ResponseHeader(name, value)]));

    [Fact]
    public void ThePagesThemeObjectSaysSo()
    {
        var facts = Parse("""<html><head><script>var Shopify = Shopify || {}; Shopify.theme = {"name":"Dawn","id":1};</script></head><body>Hi</body></html>""");

        Assert.Equal("Shopify.theme", facts.ShopifyMarkup);
    }

    [Theory]
    [InlineData("""<script src="https://cdn.shopify.com/shopifycloud/shop-js/loader.js"></script>""")]
    [InlineData("""<script src="//cdn.shopify.com/s/files/1/theme.js" defer></script>""")]
    [InlineData("""<link rel="stylesheet" href="https://cdn.shopify.com/extensions/app.css">""")]
    public void TheStorefrontsOwnScriptsSaySo(string tag)
    {
        var facts = Parse($"<html><head>{tag}</head><body>Hi</body></html>");

        Assert.Equal("cdn.shopify.com", facts.ShopifyMarkup);
    }

    [Theory]
    [InlineData("""<p>We moved from Shopify.theme builders last year.</p>""")]
    [InlineData("""<a href="https://cdn.shopify.com/s/files/1/brochure.pdf">Brochure</a>""")]
    [InlineData("""<img src="https://cdn.shopify.com/s/files/1/logo.png" alt="Logo">""")]
    [InlineData("""<script src="https://sdks.shopifycdn.com/buy-button/latest/buy-button-storefront.min.js"></script>""")]
    [InlineData("""<script type="application/ld+json">{"name":"Shopify.theme"}</script>""")]
    public void TextLinksAndABuyButtonDoNot(string tag)
    {
        var facts = Parse($"<html><head></head><body>{tag}</body></html>");

        Assert.Null(facts.ShopifyMarkup);
    }

    [Fact]
    public void TheSignalIsNoPartOfTheStoredFacts()
    {
        var facts = Parse("""<html><head><script>Shopify.theme = {};</script></head><body>Hi</body></html>""");

        var stored = BlobCodec.DecodeDetails(BlobCodec.EncodeDetails(new ResponseDetails([], facts)));

        Assert.Null(stored.Page!.ShopifyMarkup);
    }

    private static Engine.Parsing.Models.PageFacts Parse(string html) => HtmlPageParser.Parse(html, PAGE, [], "shoutingiguana").Facts;
}
