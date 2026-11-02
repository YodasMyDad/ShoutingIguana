using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Sidecar.Errors.Models;

namespace ShoutingIguana.Sidecar.Errors;

/// <summary>Every problem the sidecar answers with. The titles are plain sentences the UI shows as they are.</summary>
internal static class Problems
{
    public static ProblemException InvalidOptions(IReadOnlyDictionary<string, IReadOnlyList<string>> errors) =>
        new(new ProblemDto("invalid-options", "Some of the crawl settings need changing.", 422) { Errors = errors });

    public static ProblemException NeedsSecrets(IReadOnlyList<string> missingHeaders, bool proxyCredentials)
    {
        var needed = (missingHeaders.Count > 0, proxyCredentials) switch
        {
            (true, true) => "its header values and its proxy's user name and password",
            (true, false) => missingHeaders.Count == 1 ? "the value of its header " + missingHeaders[0] : "the values of its headers",
            _ => "its proxy's user name and password"
        };

        return new(new ProblemDto("needs-secrets", $"Continuing this crawl needs {needed} again: crawl files don't store them.", 422)
        {
            MissingHeaders = missingHeaders,
            ProxyCredentials = proxyCredentials
        });
    }

    public static ProblemException NotACrawl(string title, string? detail = null) =>
        new(new ProblemDto("not-a-crawl", title, 400) { Detail = detail });

    public static ProblemException CrawlNotFound() => new(new ProblemDto("crawl-not-found", "No crawl has this id.", 404));

    public static ProblemException CrawlBusy(string title) => new(new ProblemDto("crawl-busy", title, 409));

    public static ProblemException NotAnalysable(string title) => new(new ProblemDto("not-analysable", title, 409));

    public static ProblemException NotAnalyzed() => new(new ProblemDto("not-analyzed", "This crawl hasn't been analysed yet.", 404));

    public static ProblemException CheckNotFound(string checkId) =>
        new(new ProblemDto("check-not-found", $"There's no check called '{checkId}'.", 404));

    public static ProblemException FileError(string title, string? detail = null) =>
        new(new ProblemDto("file-error", title, 400) { Detail = detail });

    /// <summary>A grid query the engine can't run, with what's wrong by the field's path (<c>filters[1].value</c>).</summary>
    public static ProblemException InvalidQuery(IReadOnlyDictionary<string, IReadOnlyList<string>> errors) =>
        new(new ProblemDto("invalid-query", "This view's query can't be run.", 422) { Errors = errors, Detail = errors.Values.SelectMany(messages => messages).FirstOrDefault() });

    /// <summary>A grid filters or sorts on what the URL summary holds, and the crawl has none that describes it.</summary>
    public static ProblemException NeedsAnalysis(SummaryUnavailableReason reason, IReadOnlyList<string> columns)
    {
        var why = reason switch
        {
            SummaryUnavailableReason.Crawling => "which can't run while the crawl does",
            SummaryUnavailableReason.NotAnalyzed => "and this crawl hasn't been analysed yet",
            SummaryUnavailableReason.EngineChanged => "and this crawl was analysed by another version of Shouting Iguana, so it needs analysing again",
            _ => "and this crawl was continued after its last analysis, so it needs analysing again"
        };

        var name = reason.ToString();
        return new(new ProblemDto("needs-analysis", $"Filtering or sorting by these columns needs an analysis, {why}.", 409)
        {
            Reason = char.ToLowerInvariant(name[0]) + name[1..],
            Columns = columns
        });
    }

    public static ProblemException UrlNotFound() => new(new ProblemDto("url-not-found", "This crawl didn't find that URL.", 404));

    /// <summary>A request the UI shouldn't send: a missing or malformed body, a parameter out of range.</summary>
    public static ProblemException BadRequest(string title, string? detail = null) =>
        new(new ProblemDto("bad-request", title, 400) { Detail = detail });

    /// <summary>A bug. The title is generic; the detail is the exception's message, never its stack trace.</summary>
    public static ProblemDto Internal(Exception exception) =>
        new("internal", "Something went wrong in the engine.", 500) { Detail = exception.Message };

    public static ProblemDto BadHost() =>
        new("bad-host", "Requests must be addressed to 127.0.0.1 or localhost, on the engine's port.", StatusCodes.Status421MisdirectedRequest);

    public static ProblemDto OriginNotAllowed() => new("origin-not-allowed", "This origin isn't allowed to call the engine.", 403);

    public static ProblemDto Unauthorized() => new("unauthorized", "The engine's token is missing or wrong.", 401);

    public static ProblemDto BodyTooLarge() => new("body-too-large", "The request is larger than 1 MB.", 413);

    public static ProblemDto NotFound() => new("not-found", "There's nothing at this address.", 404);
}
