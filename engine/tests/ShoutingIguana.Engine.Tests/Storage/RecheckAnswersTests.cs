using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Fetching;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Tests.Storage;

/// <summary>
/// How a recheck reads the site's answer for a URL from what its fetch stored: read; refused again, and why; or never
/// asked. A page a limited section gave up on before its turn was never asked, though it is stored as rate-limited too.
/// </summary>
public sealed class RecheckAnswersTests
{
    [Theory]
    [InlineData(FetchOutcome.Success, null, RecheckAnswer.Read)]
    [InlineData(FetchOutcome.Redirect, null, RecheckAnswer.Read)]
    [InlineData(FetchOutcome.ClientError, null, RecheckAnswer.Read)]
    [InlineData(FetchOutcome.ServerError, null, RecheckAnswer.Read)]
    [InlineData(FetchOutcome.Blocked, null, RecheckAnswer.Blocked)]
    [InlineData(FetchOutcome.RobotsDisallowed, null, RecheckAnswer.RobotsTxt)]
    [InlineData(FetchOutcome.Timeout, "The request timed out", RecheckAnswer.NoAnswer)]
    [InlineData(FetchOutcome.NetworkError, "Connection refused", RecheckAnswer.NoAnswer)]
    [InlineData(FetchOutcome.NetworkError, RetryingFetcher.RATE_LIMITED_ERROR + " (429 Too Many Requests)", RecheckAnswer.RateLimited)]
    [InlineData(FetchOutcome.NetworkError, RetryingFetcher.NEVER_ASKED_ERROR, RecheckAnswer.NotAsked)]
    [InlineData(FetchOutcome.NetworkError, RetryingFetcher.NOT_CHECKED_ERROR + ": https://example.com stopped answering", RecheckAnswer.NotAsked)]
    public void TheAnswerIsReadFromWhatTheFetchStored(FetchOutcome outcome, string? error, RecheckAnswer answer)
    {
        var item = new FrontierItem(1, "https://example.com/p/1", 1, FetchMode.Crawl, IsInternal: true, DiscoverySource.Link);

        Assert.Equal(answer, RecheckAnswers.Of(PageProcessor.Failed(item, 0, outcome, error).Response));
    }
}
