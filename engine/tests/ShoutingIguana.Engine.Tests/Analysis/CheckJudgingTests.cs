using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Tests.Support;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.Engine.Tests.Analysis;

public sealed class CheckJudgingTests
{
    /// <summary>The engine's line between fixed and not checked is the synthetic site's, written apart, for every check.</summary>
    [Fact]
    public void EveryCheckJudgesAsTheSyntheticSiteExpects() =>
        Assert.All(IssueCatalog.All, check => Assert.Equal((check.Id, ExpectedJudging.Of(check.Id).ToString()), (check.Id, CheckJudging.Of(check).ToString())));

    /// <summary>The site's fates are the engine's readings, by name, so its expected comparison speaks of the same things.</summary>
    [Fact]
    public void TheSyntheticSitesFatesAreTheEnginesReadings() =>
        Assert.Equal(Enum.GetNames<UrlReading>().Order(), Enum.GetNames<SyntheticUrlFate>().Order());

    [Theory]
    [InlineData(UrlJudging.Responses, UrlReading.Page, true)]
    [InlineData(UrlJudging.Responses, UrlReading.Answered, true)]
    [InlineData(UrlJudging.Responses, UrlReading.Failed, true)]
    [InlineData(UrlJudging.Responses, UrlReading.NotRead, false)]
    [InlineData(UrlJudging.Responses, UrlReading.NotFound, false)]
    [InlineData(UrlJudging.Urls, UrlReading.Page, true)]
    [InlineData(UrlJudging.Urls, UrlReading.Answered, true)]
    [InlineData(UrlJudging.Urls, UrlReading.Failed, false)]
    [InlineData(UrlJudging.Urls, UrlReading.NotRead, false)]
    [InlineData(UrlJudging.Pages, UrlReading.Page, true)]
    [InlineData(UrlJudging.Pages, UrlReading.Answered, false)]
    [InlineData(UrlJudging.Pages, UrlReading.Failed, false)]
    [InlineData(UrlJudging.Pages, UrlReading.NotRead, false)]
    public void AUrlIsJudgedOnlyByAReadingItsCheckCanUse(UrlJudging judging, UrlReading reading, bool judges) =>
        Assert.Equal(judges, CheckJudging.Judges(judging, reading));
}
