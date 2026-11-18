using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Analysis.Rules;
using ShoutingIguana.Engine.Analysis.Vocabulary;
using ShoutingIguana.Engine.Parsing;
using ShoutingIguana.Engine.Parsing.Models;

namespace ShoutingIguana.Engine.Tests.Analysis;

/// <summary>
/// Step 3b's structured data checks (batch 5 of docs/v2-new-checks.md): what isn't schema.org's, what Google recommends
/// that's missing, rich result types on a noindex page, and Microdata read as JSON-LD (change C5).
/// </summary>
public sealed class StructuredDataCheckTests
{
    private const string NOINDEX = "<meta name=\"robots\" content=\"noindex\">";

    [Fact]
    public void TheBundledVocabularyKnowsSchemaOrgsTypesPropertiesDatesAndCurrencies()
    {
        var vocabulary = SchemaVocabulary.Instance;
        Assert.True(vocabulary.IsType("Product") && vocabulary.IsType("BlogPosting") && vocabulary.IsType("Restaurant"));
        Assert.False(vocabulary.IsType("Organisation"));
        Assert.True(vocabulary.IsPropertyOf(["Product"], "offers"));
        Assert.True(vocabulary.IsPropertyOf(["BlogPosting"], "datePublished"));
        Assert.True(vocabulary.IsPropertyOf(["Restaurant"], "name"));
        Assert.False(vocabulary.IsPropertyOf(["Product"], "author"));
        Assert.True(vocabulary.IsPropertyOf(["Product", "Book"], "author"));
        Assert.True(vocabulary.TakesOnlyDates("datePublished") && vocabulary.TakesOnlyDates("priceValidUntil"));
        Assert.False(vocabulary.TakesOnlyDates("temporalCoverage"));
        Assert.True(vocabulary.IsCurrency("GBP") && vocabulary.IsCurrency("eur"));
        Assert.False(vocabulary.IsCurrency("Dollars") || vocabulary.IsCurrency("£"));
    }

    [Theory]
    [InlineData("""{"@context":"https://schema.org","@type":"Organisation","name":"Shop"}""", "Organisation isn't a schema.org type")]
    [InlineData("""{"@type":"Organization","name":"Shop"}""", "No @context")]
    [InlineData("""{"@context":"http://example.org","@type":"Organization"}""", "@context \"http://example.org\" isn't schema.org")]
    [InlineData("""{"@context":"https://schema.org","@type":"Product","name":"Sofa","colour":"red"}""", "Product: \"colour\" isn't a schema.org property")]
    [InlineData("""{"@context":"https://schema.org","@type":"Product","name":"Sofa","author":"Ann"}""", "Product: \"author\" isn't a property of Product")]
    [InlineData("""{"@context":"https://schema.org","@type":"Product","name":"Sofa","offers":{"@type":"Offer","price":"5","priceCurrency":"Dollars"}}""",
        "Offer: priceCurrency \"Dollars\" isn't ISO 4217")]
    [InlineData("""{"@context":"https://schema.org","@type":"Article","headline":"A","datePublished":"last week"}""", "Article: datePublished \"last week\" isn't an ISO 8601 date")]
    [InlineData("""[{"@context":"https://schema.org","@type":"Thing","nme":"a","colour":"b"},{"@context":"https://schema.org","@type":"Organisation"},{"@type":"Thing"}]""",
        "Thing: \"nme\" isn't a schema.org property; Thing: \"colour\" isn't a schema.org property; and 2 more")]
    public void WhatIsntSchemaOrgsIsFlaggedWithTheFirstThreeFaults(string block, string detail)
    {
        Assert.Equal(detail, Detail(IssueCatalog.STRUCTURED_DATA_SCHEMA_ERRORS, Page(block)));
    }

    [Theory]
    // A Yoast graph: the schema.org URL with its slash, WebPage dates, a SearchAction's query-input annotation.
    [InlineData("""{"@context":"https://schema.org/","@graph":[{"@type":"WebSite","@id":"https://example.com/#website","url":"https://example.com/","potentialAction":[{"@type":"SearchAction","target":{"@type":"EntryPoint","urlTemplate":"https://example.com/?s={search_term_string}"},"query-input":{"@type":"PropertyValueSpecification","valueRequired":true,"valueName":"search_term_string"}}]},{"@type":"WebPage","datePublished":"2026-01-02T10:00:00+00:00","dateModified":"2026-01-03 09:00:00","isPartOf":{"@id":"https://example.com/#website"}}]}""")]
    [InlineData("""{"@context":{"@vocab":"http://schema.org/"},"@type":"schema:Product","name":"Sofa","offers":{"@type":"Offer","priceCurrency":"gbp","price":"5","priceValidUntil":"2026-12"}}""")]
    [InlineData("""{"@context":["https://schema.org",{"ex":"https://example.com/ns#"}],"@type":["Product","ex:Sofa"],"ex:legs":4,"name":"Sofa"}""")]
    [InlineData("""{"@context":"https://schema.org","@type":"Event","name":"Gig","startDate":{"@type":"Date","@value":"2026-12-01"},"location":{"name":"Hall"}}""")]
    // gardenfurnituresets.co.uk's home page, in the real-site audit: www.schema.org redirects to schema.org, and validators read it so.
    [InlineData("""{"@context":"http://www.schema.org","@type":"Organization","name":"Shop"}""")]
    public void SchemaOrgWrittenAsSitesWriteItIsntFlagged(string block)
    {
        Assert.Null(Detail(IssueCatalog.STRUCTURED_DATA_SCHEMA_ERRORS, Page(block)));
    }

    [Fact]
    public void WhatGoogleRecommendsIsAskedOfIndexablePagesOnly()
    {
        Assert.Equal("Article: lacks image, datePublished, author",
            Detail(IssueCatalog.STRUCTURED_DATA_MISSING_RECOMMENDED, Page("""{"@context":"https://schema.org","@type":"Article","headline":"A thin article"}""")));
        Assert.Equal("Product: lacks image, offers.availability; Organization: lacks logo", Detail(IssueCatalog.STRUCTURED_DATA_MISSING_RECOMMENDED, Page(
            """[{"@context":"https://schema.org","@type":"Product","name":"Sofa","offers":[{"@type":"Offer","priceCurrency":"GBP","price":"5"},{"@type":"AggregateOffer","lowPrice":"1"}]},{"@context":"https://schema.org","@type":"Organization","url":"https://example.com/"}]""")));
        Assert.Null(Detail(IssueCatalog.STRUCTURED_DATA_MISSING_RECOMMENDED, Page(
            """{"@context":"https://schema.org","@type":"BlogPosting","headline":"A","image":"https://example.com/a.jpg","datePublished":"2026-01-01","author":{"@type":"Person","name":"Ann"}}""")));
        Assert.Null(Detail(IssueCatalog.STRUCTURED_DATA_MISSING_RECOMMENDED,
            Page("""{"@context":"https://schema.org","@type":"Article","headline":"A"}""", NOINDEX, IndexabilityReason.Noindex)));
    }

    [Fact]
    public void RichResultTypesOnANoindexPageAreNamed()
    {
        const string BLOCK = """[{"@context":"https://schema.org","@type":"Product","name":"Sofa"},{"@context":"https://schema.org","@type":"WebPage"},{"@context":"https://schema.org","@type":"BreadcrumbList","itemListElement":[]},{"@context":"https://schema.org","@type":"Restaurant","name":"Bistro"}]""";
        Assert.Equal("Product, BreadcrumbList, Restaurant", Detail(IssueCatalog.STRUCTURED_DATA_ON_NOINDEX, Page(BLOCK, NOINDEX, IndexabilityReason.Noindex)));
        Assert.Null(Detail(IssueCatalog.STRUCTURED_DATA_ON_NOINDEX, Page(BLOCK)));
        Assert.Null(Detail(IssueCatalog.STRUCTURED_DATA_ON_NOINDEX, Page("""{"@context":"https://schema.org","@type":"WebPage"}""", NOINDEX, IndexabilityReason.Noindex)));
    }

    [Fact]
    public void MicrodataIsReadAsJsonLdWithItsSchemaOrgNames()
    {
        var parsed = HtmlPageParser.Parse(RuleHarness.Html(body: """
            <div itemscope itemtype="https://schema.org/Product">
              <h2 itemprop="name"> Garden   sofa </h2>
              <img itemprop="image" src="/img/sofa.jpg" alt="Sofa">
              <div itemprop="offers" itemscope itemtype="http://schema.org/Offer">
                <meta itemprop="priceCurrency" content="GBP"><span itemprop="price">499</span>
                <link itemprop="availability" href="https://schema.org/InStock">
              </div>
              <span itemprop="https://schema.org/sku">S-1</span><span itemprop="ex:legs">4</span>
              <time itemprop="releaseDate" datetime="2026-01-02">2 January</time>
            </div>
            <div itemscope><span itemprop="name">Untyped</span></div>
            <div itemscope itemtype="http://data-vocabulary.org/Breadcrumb"><span itemprop="title">Home</span></div>
            """), new Uri("https://example.com/shop/"), [], "ShoutingIguana");

        Assert.Equal(
        [
            """{"@context":"https://schema.org","@type":"Product","name":"Garden sofa","image":"https://example.com/img/sofa.jpg","offers":{"@type":"Offer","priceCurrency":"GBP","price":"499","availability":"https://schema.org/InStock"},"sku":"S-1","releaseDate":"2026-01-02"}""",
            """{"@context":"http://data-vocabulary.org","@type":"http://data-vocabulary.org/Breadcrumb","title":"Home"}"""
        ], parsed.Facts.Microdata);
    }

    /// <summary>
    /// A <c>content</c> attribute is the value on any element, as schema.org's own examples write a price on a span, and it
    /// wins over the element's own attribute or text, as RDFa's <c>@content</c> does; an item is an item all the same. The
    /// price is a real site's: the text holds the old price and the new.
    /// </summary>
    [Fact]
    public void MicrodataTakesAContentAttributeOnAnyElementAsTheValue()
    {
        var parsed = HtmlPageParser.Parse(RuleHarness.Html(body: """
            <div itemscope itemtype="https://schema.org/Product">
              <p itemprop="name">Bench</p>
              <div itemprop="offers" itemscope itemtype="https://schema.org/Offer" content="not a value">
                <span itemprop="priceCurrency" content="GBP">£</span><div itemprop="price" content="9.1">£15.15 £9.10</div>
                <time itemprop="priceValidUntil" datetime="2026-12-31" content="2027-01-31">31 December</time>
                <a itemprop="url" href="/bench" content="https://example.com/offers/bench">Buy</a>
                <data itemprop="sku" value="B-1" content="B-2">B</data>
                <meta itemprop="availability" content="https://schema.org/InStock">
              </div>
            </div>
            """), new Uri("https://example.com/shop/"), [], "ShoutingIguana");

        Assert.Equal(
        [
            """{"@context":"https://schema.org","@type":"Product","name":"Bench","offers":{"@type":"Offer","priceCurrency":"GBP","price":"9.1","priceValidUntil":"2027-01-31","url":"https://example.com/offers/bench","sku":"B-2","availability":"https://schema.org/InStock"}}"""
        ], parsed.Facts.Microdata);
    }

    [Fact]
    public void MicrodataIsHeldToWhatJsonLdIs()
    {
        var page = Page(block: null, body: """<div itemscope itemtype="https://schema.org/Product"><span itemprop="name">Sofa</span><meta itemprop="image" content="https://example.com/a.jpg"></div><div itemscope itemtype="http://data-vocabulary.org/Breadcrumb"><span itemprop="title">Home</span></div>""");
        Assert.Equal("Product: needs offers or review or aggregateRating", Detail(IssueCatalog.STRUCTURED_DATA_MISSING_REQUIRED, page));
        Assert.Equal("@context \"http://data-vocabulary.org\" isn't schema.org", Detail(IssueCatalog.STRUCTURED_DATA_SCHEMA_ERRORS, page));
        Assert.Null(Detail(IssueCatalog.STRUCTURED_DATA_INVALID, page));
    }

    private static AnalyzedPage Page(string? block, string head = "", IndexabilityReason reason = IndexabilityReason.None, string body = "<p>Text</p>")
    {
        var site = new RuleHarness();
        var script = block is null ? string.Empty : $"<script type=\"application/ld+json\">{block}</script>";
        return site.Page($"{site.Site}/page", RuleHarness.Html(head + script, body: body), reason);
    }

    private static string? Detail(IssueDefinition check, AnalyzedPage page) =>
        new StructuredDataRules().Check(page).Where(hit => hit.Issue == check).Select(hit => hit.Detail).SingleOrDefault();
}
