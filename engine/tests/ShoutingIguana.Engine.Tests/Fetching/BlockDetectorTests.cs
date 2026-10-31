using ShoutingIguana.Engine.Fetching;
using ShoutingIguana.Engine.Fetching.Models;

namespace ShoutingIguana.Engine.Tests.Fetching;

public sealed class BlockDetectorTests
{
    [Fact]
    public void CloudflareChallengeHeader() =>
        Assert.Equal("Cloudflare challenge", BlockDetector.Detect(403, [new ResponseHeader("cf-mitigated", "challenge")], null));

    [Fact]
    public void RateLimited() => Assert.Equal("Rate limited (HTTP 429)", BlockDetector.Detect(429, [], null));

    [Fact]
    public void CloudflareChallengePage() =>
        Assert.Equal("Cloudflare", BlockDetector.Detect(503, [new ResponseHeader("server", "cloudflare")], "<title>Just a moment...</title>"));

    [Fact]
    public void PlainForbiddenIsNotABlock() =>
        Assert.Null(BlockDetector.Detect(403, [new ResponseHeader("server", "nginx")], "<h1>Forbidden</h1>"));

    [Fact]
    public void CaptchaOnANormalPageIsNotABlock() =>
        Assert.Null(BlockDetector.Detect(200, [], "<form><div class=\"g-recaptcha\"></div></form>"));
}
