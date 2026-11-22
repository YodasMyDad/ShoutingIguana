using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Sidecar.Errors.Models;

namespace ShoutingIguana.Sidecar.Errors;

/// <summary>Every problem the sidecar answers with. The titles are plain sentences the UI shows as they are.</summary>
internal static class Problems
{
    public static ProblemException InvalidOptions(IReadOnlyDictionary<string, IReadOnlyList<string>> errors) =>
        new(new ProblemDto("invalid-options", "Some of the crawl settings need changing.", 422) { Errors = errors });

    /// <summary>The analysis's limits can't be saved as sent, with what's wrong by limit.</summary>
    public static ProblemException InvalidSettings(IReadOnlyDictionary<string, IReadOnlyList<string>> errors) =>
        new(new ProblemDto("invalid-settings", "Some of the analysis's limits need changing.", 422) { Errors = errors });

    /// <param name="doing">What needs them: continuing the crawl, or rechecking it (the fix loop).</param>
    public static ProblemException NeedsSecrets(IReadOnlyList<string> missingHeaders, bool proxyCredentials, bool auth = false, string doing = "Continuing")
    {
        List<string> parts = [];
        if (missingHeaders.Count > 0)
        {
            parts.Add(missingHeaders.Count == 1 ? "the value of its header " + missingHeaders[0] : "the values of its headers");
        }

        if (proxyCredentials)
        {
            parts.Add("its proxy's user name and password");
        }

        if (auth)
        {
            parts.Add("its sign-in");
        }

        var needed = parts.Count switch
        {
            0 => "its sign-in",
            1 => parts[0],
            2 => parts[0] + " and " + parts[1],
            _ => string.Join(", ", parts[..^1]) + " and " + parts[^1]
        };

        return new(new ProblemDto("needs-secrets", $"{doing} this crawl needs {needed} again: crawl files don't store them.", 422)
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

    public static ProblemException NotAnalyzed(string title = "This crawl hasn't been analysed yet.") => new(new ProblemDto("not-analyzed", title, 404));

    /// <summary>A recheck while another run of the same site is live here, or a crawl while one of the site's is rechecked (the fix loop).</summary>
    public static ProblemException SiteBusy(string title) => new(new ProblemDto("site-busy", title, 409));

    /// <summary>A recheck of a crawl that isn't its site's latest: it would rewrite what the newer one compares with (the fix loop).</summary>
    public static ProblemException RecheckNotLatest() => new(new ProblemDto("recheck-not-latest", "Recheck works on the site’s latest check.", 409));

    /// <summary>The crawl file has no recheck with this number (the fix loop).</summary>
    public static ProblemException RecheckNotFound() => new(new ProblemDto("recheck-not-found", "This crawl has no recheck with that number.", 404));

    /// <summary>A recheck of a check a recheck can't settle, or one the analysis skipped (the fix loop).</summary>
    public static ProblemException RecheckUnsupported(string title) => new(new ProblemDto("recheck-unsupported", title, 422));

    /// <summary>A comparison with a crawl that can't be compared (the fix loop): another site's, never analysed, live, interrupted, missing.</summary>
    public static ProblemException CrawlNotComparable(string title) => new(new ProblemDto("crawl-not-comparable", title, 409));

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

    public static ProblemException SitemapNotFound() => new(new ProblemDto("sitemap-not-found", "This crawl has no sitemap with that id.", 404));

    public static ProblemException OriginNotFound() => new(new ProblemDto("origin-not-found", "This crawl has no robots.txt for that origin.", 404));

    /// <summary>An export that can't be made as asked, with what's wrong by the request's field.</summary>
    public static ProblemException InvalidExport(IReadOnlyDictionary<string, IReadOnlyList<string>> errors) =>
        new(new ProblemDto("invalid-export", "This export can’t be made as asked.", 422) { Errors = errors, Detail = errors.Values.SelectMany(messages => messages).FirstOrDefault() });

    public static ProblemException InvalidExport(string field, string message) =>
        InvalidExport(new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) { [field] = [message] });

    public static ProblemException ExportNotFound() => new(new ProblemDto("export-not-found", "There’s no export with this id.", 404));

    /// <summary>An export can't start now: too many are running, one is writing the same file, or the sidecar is shutting down.</summary>
    public static ProblemException ExportBusy(string title) => new(new ProblemDto("export-busy", title, 409));

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
