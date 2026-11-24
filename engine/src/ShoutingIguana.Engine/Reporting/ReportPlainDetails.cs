using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using static ShoutingIguana.Engine.Reporting.ReportPlainWords;

namespace ShoutingIguana.Engine.Reporting;

/// <summary>
/// A check's detail in Simple's plain words, for the report as Simple shows it. The engine writes a detail as a fact with
/// its unit, for Advanced: "H1 → H3", "1 broken URL", "Empty og:title". Simple says the same fact as a site owner would:
/// "It goes from the main heading to a level-3 heading, skipping level 2", "1 broken link", "For sharing, its title is
/// empty". Each check here reads the forms its rule writes, as the app's <c>app/src/simple/plainDetails.ts</c> does, and
/// says them in the app's own words (the cases in <c>Reporting/PlainRows/plain-rows.json</c> hold the two together). A
/// check that isn't here, or a detail in a form its entry doesn't know, gives null: the engine's own text is said then,
/// as the app says it.
/// </summary>
internal static class ReportPlainDetails
{
    private static readonly ConcurrentDictionary<string, Regex> PATTERNS = new(StringComparer.Ordinal);

    private static Match Read(string text, string pattern) =>
        PATTERNS.GetOrAdd(pattern, static source => new Regex(source, RegexOptions.CultureInvariant | RegexOptions.Compiled)).Match(text);

    /// <summary>A group's text, or null where it didn't take part in the match.</summary>
    private static string? Part(Match match, int group) => match.Groups[group].Success ? match.Groups[group].Value : null;

    /// <summary>A count as the engine writes it ("1,036"), as a number.</summary>
    private static long NumberOf(string written) => long.Parse(written.Replace(",", string.Empty, StringComparison.Ordinal), CultureInfo.InvariantCulture);

    /// <summary>"1 image", "3 images", from a count as written.</summary>
    private static string Counted(string written, string one, string many) => $"{written} {(NumberOf(written) == 1 ? one : many)}";

    /// <summary>Text the engine quoted ("Sofas"), quoted as Simple quotes the site's words (“Sofas”), a quote cut short closed.</summary>
    private static string Curly(string text)
    {
        var said = Regex.Replace(text, "\"([^\"]*)\"", "“$1”");
        var open = said.IndexOf('"', StringComparison.Ordinal);
        return open < 0 ? said : $"{said[..open]}“{said[(open + 1)..].Replace("\"", string.Empty, StringComparison.Ordinal)}”";
    }

    /// <summary>"a, b and c".</summary>
    private static string Listed(IReadOnlyList<string> items, string last = "and") =>
        items.Count > 1 ? $"{string.Join(", ", items.Take(items.Count - 1))} {last} {items[^1]}" : items.Count == 1 ? items[0] : string.Empty;

    private static string Steps(string written) => Counted(written, "step", "steps");

    // ---- How an address answered, plainly.

    private static readonly Dictionary<string, string> STATUS_WORDS = new(StringComparer.Ordinal)
    {
        ["redirects in a loop"] = "forwards round in a loop",
        ["redirects with an instant meta refresh"] = "forwards straight away",
        ["redirects back to this page"] = "forwards back to this page",
        ["timed out"] = "didn’t answer in time",
        ["couldn't be reached"] = "couldn’t be reached",
        ["is blocked by a firewall"] = "turned the check away",
        ["is blocked by robots.txt"] = "is kept out by the rules file",
        ["is noindex"] = "asks not to appear in search results",
        ["is canonicalised to another URL"] = "names another address as its main address",
        ["wasn't checked"] = "wasn’t checked"
    };

    /// <summary>Where a canonical's redirects end, after "forwards (301 Moved Permanently)": " to /b", " and, after 2 steps, ends at /b".</summary>
    private static string? ForwardTail(string tail)
    {
        if (tail.Length == 0)
        {
            return string.Empty;
        }

        static string? Which(string? rest) => rest is null ? string.Empty : StatusPhrase(rest) is { } answer ? $", which {answer}" : null;
        if (Read(tail, @"^ to (\S+?)(?:, which (.+))?$") is { Success: true } to)
        {
            return Which(Part(to, 2)) is { } end ? $" to {to.Groups[1].Value}{end}" : null;
        }

        if (Read(tail, @"^ and, after (\d[\d,]*) redirects?, (ends at|leads to) (\S+?)(?:, which (.+))?$") is { Success: true } after)
        {
            return Which(Part(after, 4)) is { } end ? $" and, after {Steps(after.Groups[1].Value)}, {after.Groups[2].Value} {after.Groups[3].Value}{end}" : null;
        }

        return null;
    }

    /// <summary>
    /// What the crawl found at an address, as the engine says it after "the URL" (<c>RuleText.Status</c>), as a clause after
    /// "It": "is missing (404 Not Found)", "forwards to an address that is missing (404 Not Found)". Null for a form it doesn't know.
    /// </summary>
    private static string? StatusPhrase(string text)
    {
        var said = text.Trim();
        if (Read(said, @"^returns ((\d{3}) .+|HTTP (\d{3}))$") is { Success: true } returns)
        {
            return CodeClause(int.Parse(Part(returns, 2) ?? returns.Groups[3].Value, CultureInfo.InvariantCulture), returns.Groups[1].Value);
        }

        if (Read(said, "^redirects to a URL that (.+)$") is { Success: true } through)
        {
            return StatusPhrase(through.Groups[1].Value) is { } end ? $"forwards to an address that {end}" : null;
        }

        if (Read(said, @"^is still redirecting after (\d[\d,]*) hops?, more than browsers follow$") is { Success: true } still)
        {
            return $"is still forwarding after {Steps(still.Groups[1].Value)}, more than browsers follow";
        }

        if (Read(said, "^redirects with (.+) but no usable Location header$") is { Success: true } nowhere)
        {
            return $"says it forwards ({nowhere.Groups[1].Value}) but not where to";
        }

        if (Read(said, @"^redirects \(([^)]+)\)(.*)$") is { Success: true } forwards)
        {
            return ForwardTail(forwards.Groups[2].Value) is { } tail ? $"forwards ({forwards.Groups[1].Value}){tail}" : null;
        }

        return STATUS_WORDS.GetValueOrDefault(said);
    }

    /// <summary>A status as the engine names it ("404 Not Found", "HTTP 599"), as a clause after "It": "is missing (404 Not Found)".</summary>
    private static string? CodeClauseOf(string detail) => Read(detail, @"^(?:(\d{3}) \S.*|HTTP (\d{3}))$") is { Success: true } code
        ? CodeClause(int.Parse(Part(code, 1) ?? code.Groups[2].Value, CultureInfo.InvariantCulture), detail)
        : null;

    /// <summary>"(it|the first) redirects to a URL that …" after a count of broken addresses: "; the first forwards to …".</summary>
    private static string? BrokenTail(string? which, string? phrase) =>
        which is null || phrase is null ? string.Empty : StatusPhrase(phrase) is { } said ? $"; {which} {said}" : null;

    // ---- The page's parts.

    /// <summary>A heading by its level: "the main heading", "a section heading", "a level-4 heading".</summary>
    private static string HeadingName(int level) => level switch
    {
        1 => "the main heading",
        2 => "a section heading",
        _ => string.Create(CultureInfo.InvariantCulture, $"a level-{level} heading")
    };

    /// <summary>Levels a jump leaves out: "level 2", "levels 2 and 3".</summary>
    private static string SkippedLevels(int from, int to)
    {
        List<string> levels = [.. Enumerable.Range(from + 1, to - from - 1).Select(level => level.ToString(CultureInfo.InvariantCulture))];
        return levels.Count == 1 ? $"level {levels[0]}" : $"levels {Listed(levels)}";
    }

    /// <summary>Open Graph and X card tags by what they set: og:title, the share title.</summary>
    private static readonly Dictionary<string, string> SHARE_TAGS = new(StringComparer.Ordinal)
    {
        ["og:title"] = "share title",
        ["og:description"] = "share description",
        ["og:image"] = "share image",
        ["og:url"] = "share address",
        ["og:type"] = "share type",
        ["twitter:card"] = "X card type",
        ["twitter:title"] = "X card title",
        ["twitter:description"] = "X card description",
        ["twitter:image"] = "X card image"
    };

    /// <summary>The security headers by what each one does.</summary>
    private static readonly Dictionary<string, string> SECURITY_SETTINGS = new(StringComparer.Ordinal)
    {
        ["Strict-Transport-Security"] = "keep browsers on the secure connection",
        ["X-Content-Type-Options"] = "stop browsers guessing file types",
        ["Content-Security-Policy"] = "limit what the pages may load",
        ["X-Frame-Options"] = "stop other sites showing your pages inside theirs",
        ["Referrer-Policy"] = "limit what other sites learn of where visitors came from"
    };

    /// <summary>A list the engine joins with "; ", each part said its own way; a part cut short ends it with "and more".</summary>
    private static string? EachPart(string detail, Func<string, string?> sayPart)
    {
        var parts = detail.Split("; ");
        List<string> said = [];
        for (var index = 0; index < parts.Length; index++)
        {
            if (Read(parts[index], @"^and (\d[\d,]*) more$") is { Success: true } more)
            {
                said.Add($"and {more.Groups[1].Value} more");
                continue;
            }

            if (sayPart(parts[index]) is { } plain)
            {
                said.Add(plain);
                continue;
            }

            // Cut to the detail's length: the last part, part-said, is left for "and more".
            if (index == parts.Length - 1 && index > 0 && parts[index].EndsWith('…'))
            {
                said.Add("and more");
                break;
            }

            return null;
        }

        return string.Join("; ", said);
    }

    private static string? WeakSetting(string part)
    {
        if (Read(part, @"^HSTS max-age (\d+) \(.+?\), under a year$") is { Success: true } hsts)
        {
            return $"it tells browsers to use the secure connection for {Length(long.Parse(hsts.Groups[1].Value, CultureInfo.InvariantCulture))}; a year is advised";
        }

        if (part == "HSTS without max-age")
        {
            return "it asks browsers to keep to the secure connection, but doesn’t say for how long";
        }

        if (Read(part, "^HSTS max-age \"(.*)\" unreadable$") is { Success: true } unreadable)
        {
            return $"it asks browsers to keep to the secure connection for a time that can’t be read (“{unreadable.Groups[1].Value}”)";
        }

        return Read(part, "^X-Content-Type-Options \"(.*)\"$") is { Success: true } options
            ? $"the setting that stops browsers guessing file types is set wrongly (“{options.Groups[1].Value}”)"
            : null;
    }

    private static string? InsecureCookies(string detail)
    {
        List<string> unsecured = [], readable = [], refused = [];
        var cut = false;
        var parts = detail.Split("; ");
        for (var index = 0; index < parts.Length; index++)
        {
            var cookie = Read(parts[index], "^(.+?): (.+)$");
            var faults = cookie.Success ? cookie.Groups[2].Value.Split(", ") : [];
            if (!cookie.Success || faults.Length == 0 || !faults.All(fault => fault is "no Secure" or "no HttpOnly" or "SameSite=None without Secure"))
            {
                if (index == parts.Length - 1 && index > 0 && parts[index].EndsWith('…'))
                {
                    cut = true;
                    break;
                }

                return null;
            }

            var name = $"“{cookie.Groups[1].Value}”";
            if (faults.Contains("no Secure"))
            {
                unsecured.Add(name);
            }

            if (faults.Contains("no HttpOnly"))
            {
                readable.Add(name);
            }

            if (faults.Contains("SameSite=None without Secure"))
            {
                refused.Add(name);
            }
        }

        static string Cookies(List<string> names) => $"{(names.Count == 1 ? "the cookie" : "the cookies")} {Listed(names)}";
        List<string> said = [];
        if (unsecured.Count > 0)
        {
            said.Add($"{Cookies(unsecured)} can be sent without the secure connection");
        }

        if (readable.Count > 0)
        {
            said.Add($"the page’s scripts can read {Cookies(readable)}, which {(readable.Count == 1 ? "looks" : "look")} like a login");
        }

        if (refused.Count > 0)
        {
            said.Add($"browsers will refuse {Cookies(refused)}: shared with other sites, but not kept to the secure connection");
        }

        return said.Count > 0 ? $"{string.Join("; ", said)}{(cut ? "; and more" : string.Empty)}" : null;
    }

    // ---- Each check.

    /// <summary>"1 other page has", "3 other pages have": a duplicate's other pages.</summary>
    private static string? Others(string detail, string what) => Read(detail, @"^(\d[\d,]*) other pages?$") is { Success: true } match
        ? NumberOf(match.Groups[1].Value) == 1 ? $"another page has the same {what}" : $"{match.Groups[1].Value} other pages have the same {what}"
        : null;

    /// <summary>"3 images" and what they lack: "3 images with no alt text".</summary>
    private static string? Images(string detail, string what) =>
        Read(detail, @"^(\d[\d,]*) images?$") is { Success: true } match ? $"{Counted(match.Groups[1].Value, "image", "images")} {what}" : null;

    /// <summary>"2 titles: "A" · "B"" → "The page has 2 titles: “A” · “B”".</summary>
    private static string? Several(string detail, string written, string said) =>
        Read(detail, $@"^(\d[\d,]*) {written}(?:: (.*))?$") is { Success: true } match
            ? $"the page has {match.Groups[1].Value} {said}{(Part(match, 2) is { Length: > 0 } texts ? $": {Curly(texts)}" : string.Empty)}"
            : null;

    private static string? TooWide(string detail) =>
        Read(detail, @"^(?:[\d,]+ px, over [\d,]+ \()?(\d[\d,]*) characters?(?: \([^)]*\))?\)?(?:, over [\d,]+)?$") is { Success: true } match
            ? $"too long to show in full in search results ({Counted(match.Groups[1].Value, "character", "characters")})"
            : null;

    private static string? TooShort(string detail) =>
        Read(detail, @"^(?:[\d,]+ px, under [\d,]+ \()?(\d[\d,]*) characters?\)?(?:, under [\d,]+)?$") is { Success: true } match
            ? $"only {Counted(match.Groups[1].Value, "character", "characters")} long"
            : null;

    /// <summary>A canonical's target, said after "the canonical URL": "The main address it gives is missing (404 Not Found)".</summary>
    private static string? CanonicalTarget(string detail) =>
        Read(detail, "^The canonical URL (.+)$") is { Success: true } match && StatusPhrase(match.Groups[1].Value) is { } said ? $"the main address it gives {said}" : null;

    private static string? BrokenLinks(string detail)
    {
        if (Read(detail, @"^(\d[\d,]*) broken URLs?(?: \((it|the first) (.+)\))?$") is not { Success: true } match)
        {
            return null;
        }

        return BrokenTail(Part(match, 2), Part(match, 3)) is { } tail ? $"{Counted(match.Groups[1].Value, "broken link", "broken links")}{tail}" : null;
    }

    /// <summary>A page or file past what Google reads: "2.1 MB, past the 2 MB Google reads".</summary>
    private static string? PastGoogle(string detail, string what) =>
        Read(detail, @"^([\d.]+ [KMG]B)(?:, past the ([\d.]+ [KMG]B) Google reads)?$") is { Success: true } match
            ? $"the {what} is {match.Groups[1].Value}; Google reads only the first {Part(match, 2) ?? "part"}"
            : null;

    /// <summary>A series' next or previous page and how it answered: <c>rel="next" returns 404 Not Found</c>.</summary>
    private static string? PagedTarget(string detail) =>
        Read(detail, "^rel=\"(next|prev)\" (.+)$") is { Success: true } match && StatusPhrase(match.Groups[2].Value) is { } said
            ? $"its {(match.Groups[1].Value == "next" ? "next" : "previous")} page {said}"
            : null;

    private static string? ItStatus(string detail) => CodeClauseOf(detail) is { } clause ? $"it {clause}" : null;

    private static string? AddressStatus(string detail) =>
        Read(detail, "^The URL (.+)$") is { Success: true } match && StatusPhrase(match.Groups[1].Value) is { } said ? $"it {said}" : null;

    private static string Quoted(string detail) => Listed([.. detail.Split(", ").Select(name => $"“{name}”")]);

    private static readonly Dictionary<string, Func<string, string?>> DETAIL_WORDS = new(StringComparer.Ordinal)
    {
        // Response codes
        ["internal-client-error"] = ItStatus,
        ["internal-server-error"] = ItStatus,
        ["internal-unreachable"] = detail => detail switch
        {
            "Timed out" => "it didn’t answer in time",
            "Connection failed" => "it couldn’t be reached",
            _ => $"it {FailureClause(detail) ?? "couldn’t be reached"}"
        },
        ["internal-blocked"] = detail => $"the site’s protection turned the check away: {detail}",
        ["content-type-mismatch"] = detail =>
        {
            if (Read(detail, "^Used as (.+?), sent (with no Content-Type|as (.+))$") is not { Success: true } match)
            {
                return null;
            }

            var uses = Regex.Replace(match.Groups[1].Value, @"\bstylesheet\b", "style sheet");
            return $"pages use it as {uses}, but the server {(Part(match, 3) is { } type ? $"says it’s {FileKind(type)}" : "doesn’t say what kind of file it is")}";
        },
        // Redirects
        ["redirect-chain"] = detail =>
        {
            if (Read(detail, @"^(\d[\d,]*) redirects(?:, more than the (\d+) Googlebot follows)?$") is { Success: true } chain)
            {
                return $"it forwards {chain.Groups[1].Value} times in a row{(Part(chain, 2) is { } google ? $", more than the {google} Google follows" : string.Empty)}";
            }

            return Read(detail, @"^More than (\d+) redirects, which browsers don't follow$") is { Success: true } endless
                ? $"it forwards more than {endless.Groups[1].Value} times in a row, more than browsers follow"
                : null;
        },
        ["redirect-broken"] = detail =>
        {
            if (Read(detail, "^Ends at a URL that (.+)$") is { Success: true } ends)
            {
                return StatusPhrase(ends.Groups[1].Value) is { } said ? $"it forwards to an address that {said}" : null;
            }

            return Read(detail, "^(.+) without a usable Location header$") is { Success: true } nowhere ? $"it says it forwards ({nowhere.Groups[1].Value}) but not where to" : null;
        },
        ["redirect-temporary"] = detail => CodeClauseOf(detail) is not null ? $"it says the move is only for now ({detail})" : null,
        ["redirect-temporary-cached"] = detail => Read(detail, "^(.+), cached for (.+)$") is { Success: true } match
            ? $"it says the move is only for now ({match.Groups[1].Value}), but tells browsers to remember it for {match.Groups[2].Value}"
            : null,
        // Links
        ["broken-internal-links"] = BrokenLinks,
        ["broken-external-links"] = BrokenLinks,
        ["links-to-redirects"] = detail => Read(detail, @"^(\d[\d,]*) redirecting URLs?$") is { Success: true } match
            ? $"links to {Counted(match.Groups[1].Value, "old address", "old addresses")} that {(NumberOf(match.Groups[1].Value) == 1 ? "forwards" : "forward")} elsewhere"
            : null,
        ["internal-nofollow-links"] = detail => Read(detail, @"^Links to (\d[\d,]*) URLs?$") is { Success: true } match
            ? $"links to {Counted(match.Groups[1].Value, "address", "addresses")}, telling search engines not to follow {(NumberOf(match.Groups[1].Value) == 1 ? "it" : "them")}"
            : null,
        ["empty-anchor-text"] = detail => Read(detail, @"^Links to (\d[\d,]*) URLs?$") is { Success: true } match
            ? $"links to {Counted(match.Groups[1].Value, "address", "addresses")} with no words to click"
            : null,
        ["broken-fragment-links"] = detail =>
        {
            if (Read(detail, @"^(\d[\d,]*) links?(?: to missing sections: (.*))?$") is not { Success: true } match)
            {
                return null;
            }

            var one = NumberOf(match.Groups[1].Value) == 1;
            return $"{Counted(match.Groups[1].Value, "link", "links")} to {(one ? "part of a page that isn’t" : "parts of pages that aren’t")} there{(Part(match, 2) is { Length: > 0 } sections ? $": {sections}" : string.Empty)}";
        },
        ["orphan-page"] = detail => detail switch
        {
            "Only found in a sitemap" => "no page links here: it’s only in your sitemap",
            "Only nofollow links lead here" => "the only links here tell search engines not to follow them",
            _ => null
        },
        ["broken-resources"] = detail =>
        {
            if (Read(detail, @"^(\d[\d,]*) broken files?(?: \((it|the first) (.+)\))?$") is not { Success: true } match || BrokenTail(Part(match, 2), Part(match, 3)) is not { } tail)
            {
                return null;
            }

            return $"{Counted(match.Groups[1].Value, "file", "files")} the page loads {(NumberOf(match.Groups[1].Value) == 1 ? "is" : "are")} broken{tail}";
        },
        ["few-internal-outlinks"] = detail =>
        {
            if (detail == "No links to other pages")
            {
                return "it links to none of your other pages";
            }

            return Read(detail, @"^(\d[\d,]*) links? to (?:another page|other pages), under (\d[\d,]*)$") is { Success: true } match
                ? $"it links to only {Counted(match.Groups[1].Value, "other page", "other pages")}; at least {match.Groups[2].Value} {(NumberOf(match.Groups[2].Value) == 1 ? "is" : "are")} advised"
                : null;
        },
        ["generic-anchor-text"] = detail =>
        {
            if (Read(detail, @"^(\d[\d,]*) links?: (.*)$") is not { Success: true } match)
            {
                return null;
            }

            var texts = Regex.Replace(Curly(match.Groups[2].Value), @" ×(\d+)", " ($1 times)");
            return $"{Counted(match.Groups[1].Value, "link", "links")} whose words don’t say where {(NumberOf(match.Groups[1].Value) == 1 ? "it goes" : "they go")}: {texts}";
        },
        ["non-indexable-inlinks-only"] = detail =>
        {
            if (detail == "Linked from 1 page, which isn't indexable")
            {
                return "only 1 page links here, and it can’t appear in search results";
            }

            return Read(detail, @"^Linked from (\d[\d,]*) pages, none of them indexable$") is { Success: true } match
                ? $"only {match.Groups[1].Value} pages link here, and none of them can appear in search results"
                : null;
        },
        ["deep-page"] = detail => Read(detail, @"^(\d[\d,]*) clicks? from the start, over (\d[\d,]*)$") is { Success: true } match
            ? $"{Counted(match.Groups[1].Value, "click", "clicks")} from the home page; at most {match.Groups[2].Value} is advised"
            : null,
        ["too-many-links"] = detail => Read(detail, @"^(\d[\d,]*) internal links, over (\d[\d,]*)$") is { Success: true } match
            ? $"{match.Groups[1].Value} links to your own pages, more than {match.Groups[2].Value}"
            : null,
        ["slow-external-links"] = detail => Read(detail, @"^(\d[\d,]*) slow links?, the slowest ([\d.]+) s$") is { Success: true } match
            ? $"{Counted(match.Groups[1].Value, "link", "links")} to {(NumberOf(match.Groups[1].Value) == 1 ? "a slow page" : "slow pages")} on other sites; the slowest took {Seconds(double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) * 1000)}"
            : null,
        ["pagination-broken"] = PagedTarget,
        ["pagination-to-non-indexable"] = PagedTarget,
        // Indexability
        ["blocked-by-robots"] = detail =>
        {
            if (Read(detail, @"^robots\.txt unreachable: (.+)$") is { Success: true } unreachable)
            {
                return $"the rules file couldn’t be read ({FailureWords(unreachable.Groups[1].Value)}), so search engines stay out";
            }

            return Read(detail, @"(?i)^((?:Disallow|Allow):.*), line (\d[\d,]*)$") is { Success: true } rule
                ? $"the rules file says “{rule.Groups[1].Value}”, on line {rule.Groups[2].Value}"
                : null;
        },
        ["snippet-blocked"] = detail => $"the page tells search engines not to show a text preview of it (“{detail}”)",
        ["site-hidden-from-search"] = detail => Read(detail, @"^(\d[\d,]*) of (\d[\d,]*) pages? (?:is|are) noindex$") is { Success: true } match
            ? $"{match.Groups[1].Value} of {Counted(match.Groups[2].Value, "page", "pages")} {(NumberOf(match.Groups[1].Value) == 1 ? "asks" : "ask")} not to appear in search results"
            : null,
        ["robots-directives-conflict"] = detail => Read(detail, "^The HTML says \"(.*?)\"; the X-Robots-Tag header says \"(.*?)\"?$") is { Success: true } match
            ? $"the page says “{match.Groups[1].Value}”, but the server says “{match.Groups[2].Value}”"
            : null,
        ["noindex-near-home"] = detail => Read(detail, @"^(\d[\d,]*) clicks? from the start$") is { Success: true } match
            ? $"{Counted(match.Groups[1].Value, "click", "clicks")} from the home page"
            : null,
        // Canonicals
        ["canonical-broken"] = CanonicalTarget,
        ["canonical-to-redirect"] = CanonicalTarget,
        ["canonical-to-non-indexable"] = CanonicalTarget,
        ["canonical-to-first-page"] = detail => Read(detail, @"^Page (\d[\d,]*) names page 1 as its canonical$") is { Success: true } page
            ? $"page {page.Groups[1].Value} names page 1 as its main address"
            : detail == "A later page names page 1 as its canonical" ? "a later page names page 1 as its main address" : null,
        ["canonical-other-domain"] = detail => Read(detail, "^On another host: (.+)$") is { Success: true } match ? $"the main address it gives is on another website: {match.Groups[1].Value}" : null,
        ["canonical-on-redirect"] = detail => Read(detail, @"^Redirects to (\S+); its canonical header names (\S+)$") is { Success: true } match
            ? $"it forwards to {match.Groups[1].Value}, but the server names {match.Groups[2].Value} as its main address"
            : null,
        ["canonical-fragment"] = detail => $"the main address it gives points to part of a page: {detail}",
        ["canonical-unlinked"] = detail => detail == "Only canonicals lead here" ? "other pages name it as their main address, but none links to it" : null,
        // Sitemaps
        ["sitemap-non-indexable"] = AddressStatus,
        ["sitemap-url-other-host"] = detail => Read(detail, @"^The URL is (\w+)://, the sitemap (\w+)://$") is { Success: true } schemes
            ? $"its address starts {schemes.Groups[1].Value}://, but the sitemap’s starts {schemes.Groups[2].Value}://"
            // The fault is where the address is, not how it answers: "It forwards" alone read as the problem.
            : AddressStatus(detail) is { } status ? $"it isn’t on the sitemap’s own website, and {status}" : null,
        ["robots-txt-no-sitemap"] = detail => Read(detail, @"^(\S+) has no Sitemap: line; the sitemap was found at (\S+)$") is { Success: true } match
            ? $"the rules file {match.Groups[1].Value} doesn’t say where your sitemap is; it was found at {match.Groups[2].Value}"
            : null,
        ["sitemap-empty"] = detail => Read(detail, @"^(\S+) and (\d[\d,]*) more$") is { Success: true } more
            ? $"the sitemaps {more.Groups[1].Value} and {more.Groups[2].Value} more list no pages"
            : Read(detail, @"^\S+$").Success ? $"the sitemap {detail} lists no pages" : null,
        ["sitemap-url-repeated"] = detail => Read(detail, @"^In (\d[\d,]*) sitemaps$") is { Success: true } match ? $"listed in {match.Groups[1].Value} of your sitemaps" : null,
        // Titles, descriptions, headings
        ["title-multiple"] = detail => Several(detail, "titles", "titles"),
        ["title-too-long"] = TooWide,
        ["title-too-short"] = TooShort,
        ["title-duplicate"] = detail => Others(detail, "title"),
        ["description-multiple"] = detail => Several(detail, "meta descriptions", "search descriptions"),
        ["description-too-long"] = TooWide,
        ["description-too-short"] = TooShort,
        ["description-duplicate"] = detail => Others(detail, "search description"),
        ["h1-missing"] = detail => detail == "The H1 has no text" ? "the main heading has no words" : null,
        ["h1-multiple"] = detail => Several(detail, "H1s", "main headings"),
        ["h1-duplicate"] = detail => Others(detail, "main heading"),
        ["heading-levels-skipped"] = detail =>
        {
            if (Read(detail, "^An H([2-6]) before the H1$") is { Success: true } before)
            {
                return $"{HeadingName(int.Parse(before.Groups[1].Value, CultureInfo.InvariantCulture))} comes before the main heading";
            }

            if (Read(detail, "^H([1-6]) → H([1-6])(?:: (.*))?$") is not { Success: true } match)
            {
                return null;
            }

            var (from, to) = (int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture));
            return to <= from + 1 ? null
                : $"it goes from {HeadingName(from)} to {HeadingName(to)}{(Part(match, 3) is { Length: > 0 } quoted ? $", {Curly(quoted)}" : string.Empty)}, skipping {SkippedLevels(from, to)}";
        },
        ["h1-image-only"] = detail => Read(detail, "^The H1 is an image: (.*)$") is { Success: true } match ? $"the main heading is a picture: {Curly(match.Groups[1].Value)}" : null,
        ["heading-too-long"] = detail => Read(detail, @"^H([1-6]) of (\d[\d,]*) characters, over (\d[\d,]*)(?:: (.*))?$") is { Success: true } match
            ? $"{HeadingName(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))} is {match.Groups[2].Value} characters long, more than {match.Groups[3].Value}{(Part(match, 4) is { Length: > 0 } quoted ? $": {Curly(quoted)}" : string.Empty)}"
            : null,
        ["h2-missing"] = detail => Read(detail, @"^(\d[\d,]*) words, no H2$") is { Success: true } match ? $"{match.Groups[1].Value} words with no subheadings" : null,
        // Content
        ["low-word-count"] = detail => Read(detail, @"^(\d[\d,]*) words?(?:, under (\d[\d,]*))?$") is { Success: true } match
            ? $"only {Counted(match.Groups[1].Value, "word", "words")}{(Part(match, 2) is { } limit ? $", fewer than {limit}" : string.Empty)}"
            : null,
        ["placeholder-text"] = detail => detail.StartsWith('"') ? $"placeholder text: {Curly(detail)}" : null,
        ["readability-low"] = detail => Read(detail, @"^Reading ease (-?\d[\d,]*), under (\d+) \(very difficult\)$") is { Success: true } match
            ? $"very hard to read: it scores {match.Groups[1].Value} for reading ease, where under {match.Groups[2].Value} is very hard"
            : null,
        ["boilerplate-heavy"] = detail => Read(detail, @"^(\d[\d,]*) of (\d[\d,]*) words are the page's own \((\d+)%\)$") is { Success: true } match
            ? $"only {match.Groups[1].Value} of its {match.Groups[2].Value} words are its own ({match.Groups[3].Value}%); the rest is shared with other pages"
            : null,
        ["duplicate-content"] = detail => Others(detail, "text"),
        ["near-duplicate-content"] = detail => Read(detail, @"^(\d[\d,]*) similar pages?$") is { Success: true } match
            ? NumberOf(match.Groups[1].Value) == 1 ? "another page has almost the same text" : $"{match.Groups[1].Value} other pages have almost the same text"
            : null,
        // Images
        ["image-missing-alt"] = detail => Images(detail, "with no alt text"),
        ["image-alt-too-long"] = detail => Images(detail, "with very long alt text"),
        ["image-missing-dimensions"] = detail => Images(detail, "with no size set"),
        ["image-broken"] = detail =>
            Read(detail, @"^(\d[\d,]*) images?(?: \((it|the first) (.+)\))?$") is { Success: true } match && BrokenTail(Part(match, 2), Part(match, 3)) is { } tail
                ? $"{Counted(match.Groups[1].Value, "broken image", "broken images")}{tail}"
                : null,
        ["image-too-large"] = detail =>
        {
            if (Read(detail, @"^(\d[\d,]*) images? over ([\d.]+ [KMG]B)(?:: ([\d.]+ [KMG]B)|, the largest ([\d.]+ [KMG]B))?$") is not { Success: true } match)
            {
                return null;
            }

            var size = Part(match, 3) is { } only ? $": {only}" : Part(match, 4) is { } largest ? $", the largest {largest}" : string.Empty;
            return $"{Counted(match.Groups[1].Value, "image", "images")} bigger than {match.Groups[2].Value}{size}";
        },
        ["image-no-srcset"] = detail => Read(detail, @"^1 image, (\d[\d,]*) px wide$") is { Success: true } one
            ? $"1 image with one size for every screen, {one.Groups[1].Value} pixels wide"
            : Read(detail, @"^(\d[\d,]*) images, the widest (\d[\d,]*) px$") is { Success: true } many
                ? $"{many.Groups[1].Value} images with one size for every screen, the widest {many.Groups[2].Value} pixels"
                : null,
        ["image-legacy-format"] = detail => Read(detail, "^1 image: (.+)$") is { Success: true } one
            ? $"1 image in an older, heavier file type: {one.Groups[1].Value}"
            : Read(detail, @"^(\d[\d,]*) images, the largest (.+)$") is { Success: true } many
                ? $"{many.Groups[1].Value} images in older, heavier file types, the largest {many.Groups[2].Value}"
                : null,
        ["image-alt-unhelpful"] = detail => Read(detail, @"^(\d[\d,]*) images?$") is { Success: true } match
            ? $"{Counted(match.Groups[1].Value, "image", "images")} whose alt text doesn’t describe {(NumberOf(match.Groups[1].Value) == 1 ? "it" : "them")}"
            : null,
        ["image-hotlinked"] = detail => Read(detail, @"^(\d[\d,]*) images? from (\d[\d,]*) sites?$") is { Success: true } match
            ? $"{Counted(match.Groups[1].Value, "image", "images")} shown straight from {(NumberOf(match.Groups[2].Value) == 1 ? "another website" : $"{match.Groups[2].Value} other websites")}"
            : null,
        // Hreflang
        ["hreflang-invalid-code"] = detail => $"it uses a language code that doesn’t exist: “{detail}”",
        ["hreflang-missing-return"] = detail => Read(detail, @"^(\d[\d,]*) alternates? (?:doesn't|don't) link back$") is { Success: true } match
            ? $"{Counted(match.Groups[1].Value, "language version", "language versions")} {(NumberOf(match.Groups[1].Value) == 1 ? "doesn’t" : "don’t")} name this page back"
            : null,
        ["hreflang-broken-target"] = detail => Read(detail, @"^(\d[\d,]*) URLs?$") is { Success: true } match
            ? $"{Counted(match.Groups[1].Value, "language version", "language versions")} {(NumberOf(match.Groups[1].Value) == 1 ? "is" : "are")} broken"
            : null,
        ["hreflang-to-non-indexable"] = detail => Read(detail, @"^(\d[\d,]*) URLs?$") is { Success: true } match
            ? $"{Counted(match.Groups[1].Value, "language version", "language versions")} can’t appear in search results"
            : null,
        ["hreflang-unlinked"] = detail => detail == "No link or sitemap entry leads here" ? "other pages name it as a language version, but no link or sitemap leads here" : null,
        ["hreflang-lang-mismatch"] = detail => Read(detail, @"^hreflang (\S+), lang (\S+)$") is { Success: true } match
            ? $"listed as “{match.Groups[1].Value}”, but the page says its language is “{match.Groups[2].Value}”"
            : null,
        // Structured data
        ["structured-data-on-noindex"] = detail => $"it describes {Quoted(detail)} for search results, but asks not to appear in them",
        // Security
        ["mixed-content"] = detail => Read(detail, @"^(\d[\d,]*) HTTP resources?$") is { Success: true } match ? $"{Counted(match.Groups[1].Value, "file", "files")} loaded without https" : null,
        ["security-headers-missing"] = detail =>
        {
            if (Read(detail, "^Missing (.+)$") is not { Success: true } match)
            {
                return null;
            }

            List<string?> settings = [.. match.Groups[1].Value.Split(", ").Select(name => SECURITY_SETTINGS.GetValueOrDefault(name.Trim()))];
            return settings.Contains(null) ? null : $"your site doesn’t send the security settings that {Listed([.. settings.OfType<string>()])}";
        },
        ["security-headers-weak"] = detail => EachPart(detail, WeakSetting),
        ["cookies-insecure"] = InsecureCookies,
        ["form-not-secure"] = detail => Read(detail, @"^Sends to (\S+)$") is { Success: true } sends
            ? $"a form sends what’s typed to {sends.Groups[1].Value}, without the secure connection"
            : detail == "A password form on an http:// page" ? "a password form on a page without the secure connection" : null,
        ["protocol-relative-links"] = detail => Read(detail, @"^(\d[\d,]*) URLs?, such as (\S+)$") is { Success: true } match
            ? $"{Counted(match.Groups[1].Value, "address", "addresses")} that {(NumberOf(match.Groups[1].Value) == 1 ? "leaves" : "leave")} out “https:”, such as {match.Groups[2].Value}"
            : null,
        // URLs
        ["url-too-long"] = detail => Read(detail, @"^(\d[\d,]*) characters(?:, over (\d[\d,]*))?$") is { Success: true } match
            ? $"the address is {match.Groups[1].Value} characters long{(Part(match, 2) is { } limit ? $", more than {limit}" : string.Empty)}"
            : null,
        ["url-tracking-parameters"] = detail => $"the address carries {(detail.Split(", ").Length == 1 ? "a tracking code" : "tracking codes")}: {Quoted(detail)}",
        ["url-parameters"] = detail => $"the address has extra settings after a “?”: {Quoted(detail)}",
        ["url-internal-search"] = detail => $"it’s a page of results from your site’s own search ({Quoted(detail)})",
        // Performance
        ["slow-response"] = detail =>
        {
            if (Read(detail, @"^(\d[\d,]*) ms(?:, over (\d[\d,]*) ms)?(?: \(first byte after (\d[\d,]*) ms\))?$") is not { Success: true } match)
            {
                return null;
            }

            var took = NumberOf(match.Groups[1].Value);
            var began = Part(match, 3) is { } first && Seconds(NumberOf(first)) != Seconds(took) ? $"; it began to answer after {Seconds(NumberOf(first))}" : string.Empty;
            return $"it took {Seconds(took)} to open{(Part(match, 2) is { } limit ? $", more than {Seconds(NumberOf(limit))}" : string.Empty)}{began}";
        },
        ["large-page"] = detail => Read(detail, @"^([\d.]+ [KMG]B)(?:, over ([\d.]+ [KMG]B))?$") is { Success: true } match
            ? $"the page is {match.Groups[1].Value}{(Part(match, 2) is { } limit ? $", more than {limit}" : string.Empty)}"
            : null,
        ["html-past-crawl-limit"] = detail => PastGoogle(detail, "page"),
        ["resource-past-crawl-limit"] = detail => PastGoogle(detail, "file"),
        // HTML
        ["charset-missing"] = detail => detail.StartsWith("A meta tag declares it, but ends at byte", StringComparison.Ordinal)
            ? "the page names its text encoding too far down its code, past where browsers look"
            : null,
        ["open-graph-missing"] = detail =>
        {
            if (Read(detail, "^(?:Missing ([^;]+?))?(?:(?:; )?[Ee]mpty (.+))?$") is not { Success: true } match || (Part(match, 1) is null && Part(match, 2) is null))
            {
                return null;
            }

            static List<string?> Names(string? tags) =>
                tags is null ? [] : [.. tags.Split(", ").Select(tag => SHARE_TAGS.GetValueOrDefault(tag.Trim()) is { } name ? Regex.Replace(name, "^share ", string.Empty) : null)];
            var (missing, empty) = (Names(Part(match, 1)), Names(Part(match, 2)));
            if (missing.Contains(null) || empty.Contains(null))
            {
                return null;
            }

            List<string> parts = [];
            if (missing.Count > 0)
            {
                parts.Add($"it has no {Listed([.. missing.OfType<string>()], "or")}");
            }

            if (empty.Count > 0)
            {
                parts.Add($"its {Listed([.. empty.OfType<string>()])} {(empty.Count == 1 ? "is" : "are")} empty");
            }

            return $"for sharing, {string.Join(", and ", parts)}";
        },
        ["og-url-not-canonical"] = detail => Read(detail, "^og:url is (.+)$") is { Success: true } match ? $"the address it gives for sharing is {match.Groups[1].Value}, not its main address" : null,
        ["og-title-duplicate"] = detail => Others(detail, "share title"),
        ["og-description-duplicate"] = detail => Others(detail, "share description"),
        ["lang-invalid"] = detail => $"it gives its language as “{detail}”, which isn’t a language code such as “en”",
        ["mobile-alternate"] = detail => Read(detail, "^Mobile version: (.+)$") is { Success: true } match ? $"it has a separate mobile version: {match.Groups[1].Value}" : null
    };

    /// <summary>The checks whose details are said in plain words here; any other check's detail is the engine's own.</summary>
    public static IReadOnlyCollection<string> Mirrored => DETAIL_WORDS.Keys;

    /// <summary>
    /// A check's detail in plain words: "It goes from the main heading to a level-3 heading, skipping level 2". Null for a
    /// check that isn't mirrored here, or a form of its detail this doesn't know: the engine's own text is said then.
    /// </summary>
    public static string? Say(string checkId, string detail) =>
        DETAIL_WORDS.TryGetValue(checkId, out var say) && say(detail.Trim()) is { Length: > 0 } said ? Sentence(said) : null;
}
