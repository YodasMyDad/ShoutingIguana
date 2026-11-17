using ShoutingIguana.Engine.Fetching;
using ShoutingIguana.Engine.Fetching.Models;

namespace ShoutingIguana.Engine.Tests.Fetching;

/// <summary>What each answer tells a host's pace: only a request to slow down, or a server failing, slows it.</summary>
public sealed class HostSignalTests
{
    [Theory]
    [InlineData(FetchOutcome.Success, 200, false, "Success")]
    [InlineData(FetchOutcome.ClientError, 404, false, "Success")]
    [InlineData(FetchOutcome.ServerError, 500, false, "Success")]
    [InlineData(FetchOutcome.Blocked, 429, false, "Throttled")]
    [InlineData(FetchOutcome.Blocked, 429, true, "Throttled")]
    [InlineData(FetchOutcome.ServerError, 503, true, "Throttled")]
    [InlineData(FetchOutcome.ServerError, 503, false, "Failed")]
    [InlineData(FetchOutcome.ServerError, 502, false, "Failed")]
    [InlineData(FetchOutcome.ServerError, 504, false, "Failed")]
    [InlineData(FetchOutcome.Blocked, 403, false, "Refused")]
    [InlineData(FetchOutcome.Blocked, 503, false, "Refused")]
    [InlineData(FetchOutcome.Timeout, null, false, "NoAnswer")]
    [InlineData(FetchOutcome.NetworkError, null, false, "NoAnswer")]
    public void EachAnswerSaysWhatItSays(FetchOutcome outcome, int? status, bool retryAfter, string signal) =>
        Assert.Equal(Enum.Parse<HostSignal>(signal), RetryingFetcher.SignalFor(new FetchResponse
        {
            RequestUri = new Uri("https://example.com/"),
            Outcome = outcome,
            StatusCode = status,
            RetryAfter = retryAfter ? TimeSpan.FromSeconds(60) : null
        }));
}
