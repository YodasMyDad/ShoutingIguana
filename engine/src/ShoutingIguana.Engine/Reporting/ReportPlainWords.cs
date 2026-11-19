using System.Globalization;
using System.Text.RegularExpressions;
using ShoutingIguana.Engine.Analysis.Rules;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Reporting;

/// <summary>
/// What a problem found, in Simple's plain words, for the report as Simple shows it (<see cref="Models.ReportWording.Plain"/>):
/// how an address answered ("is missing (404 Not Found)"), where on a page a link is ("in the main content"), a page's
/// evidence ("Main heading: “Sofas”", "2 clicks from the home page"), an image's file and the size it's shown at. Each is
/// the app's own sentence, mirrored from <c>app/src/simple/plainEvidence.ts</c> and <c>plainAnswers.ts</c>: the report a
/// site owner reads says what the screens they came from say. The two are held together by the cases in
/// <c>engine/tests/ShoutingIguana.Engine.Tests/Reporting/PlainRows/plain-rows.json</c>, which a test on each side reads.
/// </summary>
internal static partial class ReportPlainWords
{
    private static readonly CultureInfo NUMBERS = CultureInfo.GetCultureInfo("en-GB");

    private static string Count(long value) => value.ToString("N0", NUMBERS);

    /// <summary>Text quoted on one line, cut to <paramref name="most"/> characters with "…": “Buy Now”.</summary>
    public static string Quote(string text, int most = 70)
    {
        var trimmed = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return $"“{(trimmed.Length > most ? trimmed[..(most - 1)].TrimEnd() + "…" : trimmed)}”";
    }

    /// <summary>A line as a sentence: its first letter up.</summary>
    public static string Sentence(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    // ---- How an address answered.

    /// <summary>A status code as a clause after "It": "works", "is missing (404 Not Found)". <paramref name="said"/> is the code as the engine names it.</summary>
    public static string CodeClause(int statusCode, string said) => statusCode switch
    {
        < 300 => "works",
        < 400 => $"forwards ({said})",
        404 or 410 => $"is missing ({said})",
        < 500 => $"can’t be opened ({said})",
        _ => $"has a server error ({said})"
    };

    /// <summary>
    /// How an address answered, as a clause: "works", "is missing (404 Not Found)", "didn’t answer", "couldn’t be reached";
    /// where the crawl recorded why a request failed, that: "is on a site that couldn’t be found".
    /// </summary>
    public static string Status(int? statusCode, UrlOutcome? outcome, string? error = null)
    {
        if (statusCode is { } code)
        {
            return CodeClause(code, HttpStatusNames.Describe(code) is var named && named.StartsWith("HTTP ", StringComparison.Ordinal) ? code.ToString(CultureInfo.InvariantCulture) : named);
        }

        if (outcome is UrlOutcome.Timeout or UrlOutcome.NetworkError && FailureClause(error) is { } failed)
        {
            return failed;
        }

        return outcome switch
        {
            UrlOutcome.RobotsDisallowed => "is kept out by the rules file",
            UrlOutcome.Timeout => "didn’t answer",
            UrlOutcome.NetworkError => "couldn’t be reached",
            UrlOutcome.Blocked => "turned the check away",
            _ => "wasn’t checked"
        };
    }

    private enum FailureKind
    {
        Status,
        Dns,
        Tls,
        Refused,
        Timeout,
        Other
    }

    [GeneratedRegex("^HTTP (\\d{3})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BareStatus();

    [GeneratedRegex("^\\d{3} [A-Za-z][A-Za-z '-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex NamedStatus();

    [GeneratedRegex("DNS lookup failed|name resolution|no such host|name or service not known|nodename nor servname", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DnsFailure();

    [GeneratedRegex("\\bTLS\\b|\\bSSL\\b|certificate|secure channel|handshake", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TlsFailure();

    [GeneratedRegex("actively refused|connection refused|refused the connection", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RefusedFailure();

    [GeneratedRegex("timed out|time-?out|no response within|did not properly respond", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TimeoutFailure();

    /// <summary>What a failure the crawl recorded comes down to, and a status's words; null when it recorded nothing.</summary>
    private static (FailureKind Kind, string Words)? FailureOf(string? text)
    {
        var said = text?.Trim() ?? string.Empty;
        if (said.Length == 0)
        {
            return null;
        }

        if (BareStatus().Match(said) is { Success: true } bare)
        {
            return (FailureKind.Status, HttpStatusNames.Describe(int.Parse(bare.Groups[1].Value, CultureInfo.InvariantCulture)));
        }

        if (NamedStatus().IsMatch(said))
        {
            return (FailureKind.Status, said);
        }

        return (DnsFailure().IsMatch(said) ? FailureKind.Dns
            : TlsFailure().IsMatch(said) ? FailureKind.Tls
            : RefusedFailure().IsMatch(said) ? FailureKind.Refused
            : TimeoutFailure().IsMatch(said) ? FailureKind.Timeout
            : FailureKind.Other, string.Empty);
    }

    /// <summary>Why a request got no answer, as a clause after "It": "is on a site that couldn’t be found"; null when the text doesn't tell.</summary>
    public static string? FailureClause(string? error) => FailureOf(error)?.Kind switch
    {
        FailureKind.Dns => "is on a site that couldn’t be found",
        FailureKind.Refused => "refused the connection",
        FailureKind.Tls => "couldn’t be opened securely",
        FailureKind.Timeout => "didn’t answer",
        _ => null
    };

    /// <summary>Why a request failed, as words in brackets or after a colon: a status as it is, otherwise what it comes down to.</summary>
    public static string FailureWords(string error) => FailureOf(error) switch
    {
        (FailureKind.Status, var words) => words,
        (FailureKind.Dns, _) => "the site couldn’t be found",
        (FailureKind.Refused, _) => "the connection was refused",
        (FailureKind.Tls, _) => "a secure connection couldn’t be made",
        (FailureKind.Timeout, _) => "it didn’t answer in time",
        _ => "it couldn’t be reached"
    };

    /// <summary>What a page's request for an address recorded, as a sentence: "Its site couldn’t be found".</summary>
    public static string FetchError(string error) => FailureOf(error) switch
    {
        (FailureKind.Status, var words) => $"It answered {words}",
        (FailureKind.Dns, _) => "Its site couldn’t be found",
        (FailureKind.Refused, _) => "Its server refused the connection",
        (FailureKind.Tls, _) => "A secure connection to it couldn’t be made",
        (FailureKind.Timeout, _) => "It didn’t answer in time",
        _ => "It couldn’t be reached"
    };

    /// <summary>What kind of file a content type is, as a noun phrase: "a script", "plain text", "an image".</summary>
    public static string FileKind(string contentType)
    {
        var type = contentType.Split(';')[0].Trim().ToLowerInvariant();
        return type switch
        {
            "" => "a file of no stated kind",
            "text/html" or "application/xhtml+xml" => "a web page",
            "text/plain" => "plain text",
            "text/css" => "a style sheet",
            _ when type.Contains("javascript", StringComparison.Ordinal) || type.Contains("ecmascript", StringComparison.Ordinal) => "a script",
            _ when type.StartsWith("image/", StringComparison.Ordinal) => "an image",
            _ when type.Contains("font", StringComparison.Ordinal) => "a font",
            _ when type.StartsWith("audio/", StringComparison.Ordinal) || type.StartsWith("video/", StringComparison.Ordinal) => "a sound or video file",
            "application/pdf" => "a PDF",
            _ when type.Contains("json", StringComparison.Ordinal) || type.Contains("xml", StringComparison.Ordinal) => "a data file",
            "application/octet-stream" => "a file of no particular kind",
            _ => "another kind of file"
        };
    }

    /// <summary>A time in seconds, from milliseconds: "1.2 seconds", "1 second", "20 seconds".</summary>
    public static string Seconds(double ms)
    {
        var seconds = Math.Max(0.1, Math.Floor(ms / 100 + 0.5) / 10);
        return seconds == 1 ? "1 second" : $"{(seconds == Math.Floor(seconds) ? Count((long)seconds) : seconds.ToString("0.0", CultureInfo.InvariantCulture))} seconds";
    }

    /// <summary>How long a number of seconds is, in the unit a person would use: "3 months", "40 days", "6 hours".</summary>
    public static string Length(long seconds)
    {
        static string Counted(long value, string one) => $"{value.ToString("N0", NUMBERS)} {(value == 1 ? one : one + "s")}";
        var days = seconds / 86_400d;
        return days >= 365 ? Counted((long)Math.Floor(days / 365 + 0.5), "year")
            : days >= 60 ? Counted((long)Math.Floor(days / 30.44 + 0.5), "month")
            : days >= 1 ? Counted((long)Math.Floor(days), "day")
            : seconds >= 3_600 ? Counted(seconds / 3_600, "hour")
            : seconds >= 60 ? Counted(seconds / 60, "minute")
            : Counted(seconds, "second");
    }

    /// <summary>A file's size as the app says one: "1.3 MB", "214 KB", "812 bytes" (1 KB is 1,024 bytes).</summary>
    public static string Bytes(long bytes)
    {
        string[] units = ["bytes", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{Count(bytes)} bytes" : $"{value.ToString("#,##0.#", NUMBERS)} {units[unit]}";
    }

    // ---- Where on a page, and how a page refers to an address.

    /// <summary>Where on a page a link is: "in the footer", "in the menu", "in the main content"; null when it isn't known.</summary>
    public static string? Position(LinkPosition? position) => position switch
    {
        LinkPosition.Content => "in the main content",
        LinkPosition.Navigation => "in the menu",
        LinkPosition.Header => "in the header",
        LinkPosition.Footer => "in the footer",
        LinkPosition.Sidebar => "in the sidebar",
        LinkPosition.Head => "in the page’s code",
        _ => null
    };

    /// <summary>How a page refers to an address where it isn't by a link to click: "names it as its main address", "forwards here"; null for a link or an image.</summary>
    public static string? Reference(LinkKind? kind) => kind switch
    {
        LinkKind.Script or LinkKind.Stylesheet or LinkKind.Iframe or LinkKind.Media or LinkKind.Icon => "loads it",
        LinkKind.Canonical => "names it as its main address",
        LinkKind.Hreflang => "names it as a language version",
        LinkKind.Redirect or LinkKind.MetaRefresh => "forwards here",
        LinkKind.Pagination => "names it as the next or previous page",
        LinkKind.SocialImage => "names it as its share image",
        _ => null
    };

    /// <summary>
    /// The thing on a page to change, as the report names it before where it leads (the UX audit's section 4, "A broken
    /// link: what the card should lead with"): "The link that says “Buy Now”, in the main content", "A link with no words,
    /// in the footer", "The picture with the alt text “Sofa”, in the main content"; for a page that refers to an address
    /// another way, how ("Names it as its main address", "Forwards here"). Null for a link of which neither its words nor
    /// its place is known. The report's own words, as the app's "Where to fix it" card has them, not one of its sentences.
    /// </summary>
    public static string? LinkWords(LinkKind? kind, string? text, LinkPosition? position, LinkFlags flags)
    {
        var where = Position(position);
        var words = string.IsNullOrWhiteSpace(text) ? null : Quote(text, 50);
        var what = kind switch
        {
            null or LinkKind.Hyperlink when flags.HasFlag(LinkFlags.ImageLink) => words is null ? "A picture that is a link, with no alt text" : $"The picture that is a link, with the alt text {words}",
            null or LinkKind.Hyperlink => words is null ? where is null ? null : "A link with no words" : $"The link that says {words}",
            LinkKind.Image => words is null ? "A picture with no alt text" : $"The picture with the alt text {words}",
            _ => Reference(kind) is { } how ? Sentence(how) : null
        };

        // A page's code holds no place worth saying: "in the page's code" says nothing of a canonical or a redirect.
        return what is null ? null : where is not null && kind is null or LinkKind.Hyperlink or LinkKind.Image ? $"{what}, {where}" : what;
    }

    /// <summary>
    /// An image on a page, named by what a person editing the page can find it by: "The picture with the alt text “Sofa”,
    /// in the main content", "A picture with no alt text, in the footer".
    /// </summary>
    public static string PictureWords(IssueTarget target, LinkPosition? position, bool withLength = false)
    {
        var alt = target.Img is { } img ? img.HasAlt ? img.Alt?.Trim() ?? string.Empty : null : string.IsNullOrWhiteSpace(target.Text) ? null : target.Text.Trim();
        var what = alt switch
        {
            null when target.Img is null => "A picture",
            null => "A picture with no alt text",
            "" => "A picture with empty alt text",
            _ => $"The picture with the alt text {Quote(alt, 60)}{(withLength && target.Img?.AltLength is { } length ? $" ({Count(length)} characters)" : string.Empty)}"
        };
        return Position(position) is { } where ? $"{what}, {where}" : what;
    }

    /// <summary>What a file a page loads is, in brackets after its address: "a script", "a style sheet".</summary>
    public static string LoadedKind(LinkKind? kind) => kind switch
    {
        LinkKind.Script => "a script",
        LinkKind.Stylesheet => "a style sheet",
        LinkKind.Iframe => "a page shown inside this one",
        LinkKind.Media => "a sound or video file",
        LinkKind.Icon => "the site’s icon",
        LinkKind.Image => "a picture",
        _ => "a file"
    };

    /// <summary>Why an address can't appear in search results, as a sentence; null for one that can.</summary>
    public static string? NotInSearch(IndexabilityReason? reason) => reason switch
    {
        IndexabilityReason.Noindex => "It can’t appear in search results: it says not to show it (noindex)",
        IndexabilityReason.Canonicalised => "It can’t appear in search results: it names another address as its main address",
        IndexabilityReason.Redirected => "It can’t appear in search results: it forwards to another address",
        IndexabilityReason.ClientError => "It can’t appear in search results: it’s missing",
        IndexabilityReason.ServerError => "It can’t appear in search results: it has a server error",
        IndexabilityReason.BlockedByRobots => "It can’t appear in search results: the rules file keeps search engines out",
        IndexabilityReason.Blocked => "It can’t appear in search results: it turned the check away",
        IndexabilityReason.Unreachable => "It can’t appear in search results: it didn’t answer",
        _ => null
    };

    /// <summary>What a forwarding address changes: "It forwards from http to https, and adds “www”", "It forwards to another page".</summary>
    public static string RedirectKind(string kinds)
    {
        List<string> said =
        [
            .. kinds.Split(',').Select(kind => kind switch
            {
                RedirectKinds.HTTP_TO_HTTPS => "from http to https",
                RedirectKinds.HTTPS_TO_HTTP => "from https to http",
                RedirectKinds.ADDS_WWW => "adds “www”",
                RedirectKinds.REMOVES_WWW => "drops “www”",
                RedirectKinds.OTHER_HOST => "to another website",
                RedirectKinds.ADDS_SLASH => "adds a slash at the end",
                RedirectKinds.REMOVES_SLASH => "drops the slash at the end",
                RedirectKinds.CHANGES_CASE => "changes capital letters",
                RedirectKinds.OTHER => "to another page",
                _ => kind
            })
        ];
        return $"It forwards {(said.Count > 1 ? $"{string.Join(", ", said.Take(said.Count - 1))}, and {said[^1]}" : said[0])}";
    }

    // ---- An image.

    /// <summary>"The image file is 1.8 MB, 4000×3000 pixels": what's known of it; null when nothing is.</summary>
    public static string? ImageFile(long? sizeBytes, int? pixelWidth, int? pixelHeight)
    {
        List<string> parts = [];
        if (sizeBytes is { } size)
        {
            parts.Add(Bytes(size));
        }

        if (pixelWidth is { } width && pixelHeight is { } height)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{width}×{height} pixels"));
        }

        return parts.Count > 0 ? $"The image file is {string.Join(", ", parts)}" : null;
    }

    [GeneratedRegex("^\\d+$", RegexOptions.CultureInvariant)]
    private static partial Regex Digits();

    /// <summary>The size a page shows an image at: "Shown at 400×300 pixels"; what it lacks where it doesn't say.</summary>
    public static string ShownAt(string? width, string? height)
    {
        static string? Attribute(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        static string WithUnit(string value) => Digits().IsMatch(value) ? $"{value} pixels" : value;
        var (wide, high) = (Attribute(width), Attribute(height));
        return (wide, high) switch
        {
            ({ } w, { } h) when Digits().IsMatch(w) && Digits().IsMatch(h) => $"Shown at {w}×{h} pixels",
            ({ } w, { } h) => $"Shown at {WithUnit(w)} wide, {WithUnit(h)} high",
            ({ } w, null) => $"Shown {WithUnit(w)} wide, with no height set",
            (null, { } h) => $"Shown {WithUnit(h)} high, with no width set",
            _ => "No width or height set"
        };
    }

    /// <summary>An image's alt text, as a page has it: "No alt text", "Alt text: “Sofa” (140 characters)".</summary>
    public static string Alt(ImageOnPage img, bool withLength = false)
    {
        if (!img.HasAlt)
        {
            return "No alt text";
        }

        var alt = img.Alt?.Trim() ?? string.Empty;
        if (alt.Length == 0)
        {
            return "Empty alt text";
        }

        return $"Alt text: {Quote(alt, 60)}{(withLength && img.AltLength is { } length ? $" ({Count(length)} characters)" : string.Empty)}";
    }

    /// <summary>What a page's other targets are, by the check: "large image", "broken link".</summary>
    private static readonly Dictionary<string, (string One, string Many)> OTHER_NOUNS = new(StringComparer.Ordinal)
    {
        ["image-too-large"] = ("large image", "large images"),
        ["image-missing-alt"] = ("image without alt text", "images without alt text"),
        ["image-alt-too-long"] = ("image with long alt text", "images with long alt text"),
        ["image-missing-dimensions"] = ("image without a size", "images without a size"),
        ["image-broken"] = ("broken image", "broken images"),
        ["broken-internal-links"] = ("broken link", "broken links"),
        ["broken-external-links"] = ("broken link", "broken links"),
        ["links-to-redirects"] = ("link to an old address", "links to old addresses"),
        ["empty-anchor-text"] = ("link with no text", "links with no text"),
        ["internal-nofollow-links"] = ("link search engines are told not to follow", "links search engines are told not to follow"),
        ["broken-fragment-links"] = ("link to a missing section", "links to missing sections"),
        ["mixed-content"] = ("file loaded without https", "files loaded without https")
    };

    /// <summary>"and 1 other large image on this page", "and 3 other broken links on this page".</summary>
    public static string Others(string checkId, long count)
    {
        var (one, many) = OTHER_NOUNS.GetValueOrDefault(checkId, ("problem like it", "problems like it"));
        return $"and {Count(count)} other {(count == 1 ? one : many)} on this page";
    }

    // ---- A page's evidence.

    /// <summary>What kind of file an address is, by the URL grid's <c>type</c>: "It’s an image"; null for one of no particular kind.</summary>
    private static string? UrlType(string type) => type switch
    {
        "page" => "It’s a page",
        "image" => "It’s an image",
        "script" => "It’s a script, code the pages run",
        "stylesheet" => "It’s a style sheet, which sets how the pages look",
        "font" => "It’s a font",
        "media" => "It’s a sound or video file",
        "pdf" => "It’s a PDF",
        _ => null
    };

    private static readonly HashSet<string> SAID_ELSEWHERE = new(StringComparer.Ordinal)
    {
        "title", "titleLength", "descriptionLength", "titleWidth", "descriptionWidth", "titleInResults", "descriptionInResults"
    };

    /// <summary>An address as Simple names it: its path on the site's own origin, else in full.</summary>
    public static string Address(string url, string? origin) =>
        origin is { Length: > 0 } && url.StartsWith(origin, StringComparison.Ordinal) ? url[origin.Length..] is { Length: > 0 } path ? path : "/" : url;

    /// <summary>A redirect chain, plainly: "Forwards to /b/, then /new/, which works".</summary>
    private static string? Chain(RedirectChain chain, string? origin)
    {
        if (chain.Hops.Count == 0)
        {
            return null;
        }

        var steps = string.Join(", then ", chain.Hops.Select(hop => Address(hop.Url, origin)));
        var last = chain.Hops[^1];
        return chain.Loop ? $"Forwards to {steps}, and round again in a loop"
            : chain.TooLong ? $"Forwards to {steps}, and on past 20 steps"
            : $"Forwards to {steps}, which {Status(last.StatusCode, last.Outcome)}";
    }

    /// <summary>Why the rules file keeps search engines out: "The rules file says “Disallow: /cart/”, on line 12".</summary>
    private static string Rule(RobotsRefusal rule) => rule switch
    {
        { Reason: RobotsBlockReason.Unreachable } => "The rules file couldn’t be read, so search engines stay out",
        { Reason: RobotsBlockReason.Rule, Rule: { Length: > 0 } says } => $"The rules file says “{says}”{(rule.Line is { } line ? $", on line {Count(line)}" : string.Empty)}",
        { Reason: RobotsBlockReason.Unknown } => "The rules file kept the check out then, but doesn’t now. Check the site again to be sure",
        _ => "The rules file kept the check out"
    };

    /// <summary>
    /// One of a page's evidence columns in plain words: "Main heading: “Sofas”", "The page says “noindex”", "120 words", "2
    /// clicks from the home page". Null for no value, for what the row says another way (the title is the page's name; a
    /// length is its detail's), for a bare number that says nothing, and for the page's own address as where it forwards
    /// or its main address (<paramref name="url"/>, the row's).
    /// </summary>
    public static string? Evidence(string column, object? value, string? origin, string? url = null)
    {
        if (value is null or "" || SAID_ELSEWHERE.Contains(column))
        {
            return null;
        }

        static string Counting(long count, string none, string one, Func<string, string> many) => count == 0 ? none : count == 1 ? one : many(count.ToString("N0", NUMBERS));
        return (column, value) switch
        {
            (UrlComputedColumns.REDIRECT_CHAIN, RedirectChain chain) => Chain(chain, origin),
            (UrlComputedColumns.ROBOTS_RULE, RobotsRefusal rule) => Rule(rule),
            ("wordCount", long words) => words == 1 ? "1 word" : $"{Count(words)} words",
            ("inlinks", long links) => Counting(links, "No pages link here", "1 link to it", count => $"{count} links to it"),
            ("linkingPages", long pages) => Counting(pages, "No pages link here", "1 page links here", count => $"{count} pages link here"),
            ("outlinks", long links) => Counting(links, "No links on the page", "1 link on the page", count => $"{count} links on the page"),
            ("depth", long depth) => depth == 0 ? "It’s the page the check started from" : $"{(depth == 1 ? "1 click" : $"{Count(depth)} clicks")} from the home page",
            ("usedOn", long pages) => pages == 1 ? "Used on 1 page" : $"Used on {Count(pages)} pages",
            ("responseMs", long ms) => ms > 0 ? $"Checking it took {Seconds(ms)}" : null,
            ("sizeBytes", long bytes) => $"The file is {(bytes < 1024 ? "under 1 KB" : Bytes(bytes))}",
            ("h1", string text) => $"Main heading: {Quote(text)}",
            ("description", string text) => $"Search description: {Quote(text)}",
            ("metaRobots", string text) => $"The page says “{text}”",
            ("xRobotsTag", string text) => $"The server says “{text}”",
            ("language", string text) => $"The page gives its language as “{text}”",
            ("redirectKind", string kinds) => RedirectKind(kinds),
            ("canonicalUrl", string canonical) => canonical == url ? null : $"Main address: {Address(canonical, origin)}",
            ("finalUrl", string final) => final == url ? null : $"Forwards to {Address(final, origin)}",
            ("indexability", string reason) => reason == "none" ? "Can appear in search results"
                : NotInSearch(Enum.TryParse<IndexabilityReason>(reason, ignoreCase: true, out var parsed) ? parsed : null),
            ("fetchError", string error) => FetchError(error),
            ("type", string type) => UrlType(type),
            ("contentType", string type) => $"The server says it’s {FileKind(type)}",
            (_, string text) => text,
            _ => null
        };
    }
}
