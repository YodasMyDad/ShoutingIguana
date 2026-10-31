using ShoutingIguana.Engine.Fetching;
using ShoutingIguana.Engine.Fetching.Models;

namespace ShoutingIguana.Engine.Tests.Fetching;

public sealed class BlockDetectorTests
{
    private const string FORBIDDEN_PAGE = "<html><head><title>403 Forbidden</title></head><body><h1>Forbidden</h1></body></html>";

    /// <summary>The JavaScript-detections snippet Cloudflare adds to every HTML page of a site that enables it.</summary>
    private const string CLOUDFLARE_INJECTED_SCRIPT = """
        <script>(function(){function c(){var b=a.contentDocument||a.contentWindow.document;if(b){var d=b.createElement('script');
        d.innerHTML="window.__CF$cv$params={r:'8f1c2a',t:'MTcz'};var a=document.createElement('script');a.nonce='';
        a.src='/cdn-cgi/challenge-platform/scripts/jsd/main.js';document.getElementsByTagName('head')[0].appendChild(a);";
        b.getElementsByTagName('head')[0].appendChild(d)}}})();</script>
        """;

    [Fact]
    public void CloudflareChallengeHeader() =>
        Assert.Equal("Cloudflare challenge", BlockDetector.Detect(403, [new ResponseHeader("cf-mitigated", "challenge")], null));

    [Fact]
    public void RateLimited() => Assert.Equal("Rate limited (HTTP 429)", BlockDetector.Detect(429, [], null));

    [Fact]
    public void CloudflareChallengePage() =>
        Assert.Equal("Cloudflare", BlockDetector.Detect(503, [new ResponseHeader("server", "cloudflare")], "<title>Just a moment...</title>"));

    [Theory]
    [InlineData("<script>window._cf_chl_opt={cvId: '3', cType: 'managed'};</script>")]
    [InlineData("<title>Attention Required! | Cloudflare</title>")]
    [InlineData("<h1 data-translate=\"block_headline\">Sorry, you have been blocked</h1>")]
    public void CloudflareChallengeAndBlockPages(string body) =>
        Assert.Equal("Cloudflare", BlockDetector.Detect(403, [new ResponseHeader("server", "cloudflare")], body));

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(503)]
    public void CloudflareScriptsOnARealErrorPageAreNotABlock(int status) =>
        Assert.Null(BlockDetector.Detect(status, [new ResponseHeader("server", "cloudflare"), new ResponseHeader("cf-ray", "8f1c2a-LHR")],
            FORBIDDEN_PAGE.Replace("</body>", CLOUDFLARE_INJECTED_SCRIPT + "<div id=\"cf-chl-widget-a1b2c\"></div></body>", StringComparison.Ordinal)));

    [Fact]
    public void DataDomeBlockHeader() =>
        Assert.Equal("DataDome", BlockDetector.Detect(403, [new ResponseHeader("x-datadome", "protected"), new ResponseHeader("x-dd-b", "1")], null));

    [Fact]
    public void DataDomeCaptchaPage() =>
        Assert.Equal("DataDome", BlockDetector.Detect(403, [new ResponseHeader("x-datadome", "protected")],
            "<script>var dd={'rt':'c','cid':'AHrlqAAA','host':'geo.captcha-delivery.com'}</script><script src=\"https://ct.captcha-delivery.com/c.js\"></script>"));

    [Fact]
    public void DataDomeProtectedSiteWithARealErrorIsNotABlock() =>
        Assert.Null(BlockDetector.Detect(403,
            [new ResponseHeader("x-datadome", "protected"), new ResponseHeader("set-cookie", "datadome=AHrlqAAA; Path=/; Secure")], FORBIDDEN_PAGE));

    [Theory]
    [InlineData("Request unsuccessful. Incapsula incident ID: 876000310076211839-49573386040131780")]
    [InlineData("<iframe id=\"main-iframe\" src=\"/_Incapsula_Resource?CWUDNSAI=24&xinfo=8-1234&incident_id=876\"></iframe>")]
    public void ImpervaBlockPage(string body) =>
        Assert.Equal("Imperva", BlockDetector.Detect(403, [new ResponseHeader("x-iinfo", "8-1234-0 NNNN CT(0 0 0) RT(1 0) q(0 0 0 -1) r(0 0) U5")], body));

    [Fact]
    public void ImpervaProtectedSiteWithARealErrorIsNotABlock() =>
        Assert.Null(BlockDetector.Detect(403, [new ResponseHeader("x-iinfo", "8-1234-0 NNNN CT(0 0 0) RT(1 0) q(0 0 0 -1) r(0 0) U5"),
            new ResponseHeader("set-cookie", "visid_incap_1234=abc; path=/")], FORBIDDEN_PAGE));

    [Fact]
    public void SucuriBlockHeader() =>
        Assert.Equal("Sucuri", BlockDetector.Detect(403, [new ResponseHeader("x-sucuri-id", "18015"), new ResponseHeader("x-sucuri-block", "GEO02")], null));

    [Theory]
    [InlineData("<title>Sucuri WebSite Firewall - Access Denied</title>")]
    [InlineData("<h1>Access Denied - Sucuri Website Firewall</h1>")]
    public void SucuriBlockPage(string body) =>
        Assert.Equal("Sucuri", BlockDetector.Detect(403, [new ResponseHeader("x-sucuri-id", "18015")], body));

    [Fact]
    public void SucuriProtectedSiteWithARealErrorIsNotABlock() =>
        Assert.Null(BlockDetector.Detect(403, [new ResponseHeader("x-sucuri-id", "18015"), new ResponseHeader("server", "Sucuri/Cloudproxy")], FORBIDDEN_PAGE));

    [Fact]
    public void AkamaiBlockPage() =>
        Assert.Equal("Akamai", BlockDetector.Detect(403, [new ResponseHeader("server", "AkamaiGHost")],
            "<HTML><HEAD><TITLE>Access Denied</TITLE></HEAD><BODY><H1>Access Denied</H1>You don't have permission to access this server.<P>Reference #18.4f2e1b2c</BODY></HTML>"));

    [Fact]
    public void AccessDeniedFromAServerOtherThanAkamaiIsNotABlock() =>
        Assert.Null(BlockDetector.Detect(403, [new ResponseHeader("server", "nginx")], "<title>Access Denied</title>"));

    [Theory]
    [InlineData("<div class=\"g-recaptcha\" data-sitekey=\"6Lc\"></div>")]
    [InlineData("<div class=\"h-captcha\" data-sitekey=\"10000000\"></div>")]
    [InlineData("<div class=\"cf-turnstile\" data-sitekey=\"0x4AAA\"></div>")]
    public void ACaptchaOnAnErrorPageIsABlock(string widget) =>
        Assert.Equal("CAPTCHA challenge", BlockDetector.Detect(403, [new ResponseHeader("server", "nginx")], $"<form>{widget}</form>"));

    [Fact]
    public void PlainForbiddenIsNotABlock() =>
        Assert.Null(BlockDetector.Detect(403, [new ResponseHeader("server", "nginx")], "<h1>Forbidden</h1>"));

    [Fact]
    public void CaptchaOnANormalPageIsNotABlock() =>
        Assert.Null(BlockDetector.Detect(200, [], "<form><div class=\"g-recaptcha\"></div></form>"));
}
