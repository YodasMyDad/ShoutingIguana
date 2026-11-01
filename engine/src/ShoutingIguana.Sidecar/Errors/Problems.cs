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
