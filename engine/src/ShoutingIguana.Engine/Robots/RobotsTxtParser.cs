using System.Globalization;
using System.Text;
using ShoutingIguana.Engine.Robots.Models;

namespace ShoutingIguana.Engine.Robots;

/// <summary>
/// Parses robots.txt per RFC 9309. Rules from every group naming our product token are combined; if no
/// group names us, the <c>*</c> groups apply; if neither exists, nothing is disallowed.
/// </summary>
internal static class RobotsTxtParser
{
    private const string UNRESERVED_PUNCTUATION = "-._~";

    /// <summary>Characters <see cref="Uri"/> percent-encodes in a path or query, so URLs never contain them raw.</summary>
    private const string ESCAPED_BY_URI = " \"<>^`{|}";

    /// <summary>A longer Crawl-delay would stall the crawl (and a huge one overflows TimeSpan), so it is capped.</summary>
    internal const double MAX_CRAWL_DELAY_SECONDS = 60;

    /// <param name="lines">
    /// When given, every line of the file is added to it as it's read: what it is, whether it's part of what applies to the
    /// crawler, and why the parser made nothing of it when it didn't. The rules are the same either way.
    /// </param>
    public static RobotsTxt Parse(string content, string productToken, List<RobotsLine>? lines = null)
    {
        var token = productToken.ToLowerInvariant();
        List<RobotsGroupBuilder> groups = [];
        List<string> sitemaps = [];
        RobotsGroupBuilder? current = null;

        foreach (var (number, rawLine) in Lines(content.TrimStart('\uFEFF')))
        {
            var line = StripComment(rawLine).Trim();
            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                lines?.Add(line.Length > 0
                    ? new RobotsLine(number, rawLine, RobotsLineKind.Invalid) { Problem = RobotsLineProblem.NotADirective }
                    : new RobotsLine(number, rawLine, rawLine.Trim().Length > 0 ? RobotsLineKind.Comment : RobotsLineKind.Blank));
                continue;
            }

            var key = line[..colon].Trim().ToLowerInvariant();
            var value = line[(colon + 1)..].Trim();

            // What the line is and, where the parser makes nothing of it, why; a group's lines are kept with the group.
            void Read(RobotsLineKind kind, RobotsLineProblem? problem = null, bool? applies = null)
            {
                if (lines is null)
                {
                    return;
                }

                var read = new RobotsLine(number, rawLine, kind) { Problem = problem, Applies = applies };
                lines.Add(read);
                if (problem != RobotsLineProblem.NoUserAgent && kind is RobotsLineKind.UserAgent or RobotsLineKind.Allow or RobotsLineKind.Disallow or RobotsLineKind.CrawlDelay)
                {
                    current?.Lines.Add(read);
                }
            }

            switch (key)
            {
                case "user-agent" or "useragent":
                    if (current is null || current.HasDirectives)
                    {
                        current = new RobotsGroupBuilder();
                        groups.Add(current);
                    }

                    current.Agents.Add(ProductTokenOf(value));
                    Read(RobotsLineKind.UserAgent);
                    break;
                case "allow" or "disallow" when current is not null:
                    // An empty rule allows everything, but like any rule it ends the run of user-agent lines, so a
                    // following user-agent starts a new group (RFC 9309 grammar, and Google's parser).
                    if (value.Length > 0)
                    {
                        current.Rules.Add(CreateRule(value, key == "allow") with { Line = number });
                    }

                    current.HasDirectives = true;
                    Read(key == "allow" ? RobotsLineKind.Allow : RobotsLineKind.Disallow);
                    break;
                case "crawl-delay" when current is not null:
                    var valid = double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var delay) && delay >= 0;
                    if (valid)
                    {
                        current.CrawlDelaySeconds = Math.Min(delay, MAX_CRAWL_DELAY_SECONDS);
                    }

                    current.HasDirectives = true;
                    Read(RobotsLineKind.CrawlDelay, valid ? null : RobotsLineProblem.InvalidValue);
                    break;
                case "sitemap" when value.Length > 0:
                    sitemaps.Add(value);
                    Read(RobotsLineKind.Sitemap, applies: true);
                    break;
                case "allow" or "disallow" or "crawl-delay":
                    // Before any user-agent line: it belongs to no group, so it's no rule of anyone's.
                    Read(key switch { "allow" => RobotsLineKind.Allow, "disallow" => RobotsLineKind.Disallow, _ => RobotsLineKind.CrawlDelay },
                        RobotsLineProblem.NoUserAgent, applies: false);
                    break;
                case "sitemap":
                    Read(RobotsLineKind.Sitemap, RobotsLineProblem.InvalidValue, applies: false);
                    break;
                default:
                    Read(RobotsLineKind.Other, RobotsLineProblem.UnknownDirective);
                    break;
            }
        }

        var applicable = groups.Where(group => group.Agents.Contains(token)).ToList();
        if (applicable.Count == 0)
        {
            applicable = groups.Where(group => group.Agents.Contains("*")).ToList();
        }

        foreach (var group in groups)
        {
            group.Lines.ForEach(line => line.Applies = applicable.Contains(group));
        }

        var rules = applicable.SelectMany(group => group.Rules).ToList();
        var crawlDelay = applicable.Select(group => group.CrawlDelaySeconds).FirstOrDefault(seconds => seconds is not null);
        return new RobotsTxt(rules, sitemaps, crawlDelay);
    }

    /// <summary>
    /// Normalises a path pattern the way <see cref="Uri"/> and <c>UrlNormalizer</c> normalise the URLs it is matched
    /// against, so they compare byte for byte: characters a URL can't hold raw (non-ASCII, controls, space,
    /// <c>" &lt; &gt; ^ ` { | }</c> and a <c>%</c> that starts no escape) are percent-encoded, and a backslash in the
    /// path becomes a slash.
    /// </summary>
    internal static string NormalizePattern(string pattern)
    {
        var builder = new StringBuilder(pattern.Length);
        Span<byte> utf8 = stackalloc byte[4];
        var inQuery = false;

        for (var i = 0; i < pattern.Length; i++)
        {
            var character = pattern[i];
            if (character == '%' && i + 2 < pattern.Length && Uri.IsHexDigit(pattern[i + 1]) && Uri.IsHexDigit(pattern[i + 2]))
            {
                var decoded = (char)Convert.ToByte(pattern.Substring(i + 1, 2), 16);
                if (char.IsAsciiLetterOrDigit(decoded) || UNRESERVED_PUNCTUATION.Contains(decoded))
                {
                    builder.Append(decoded);
                }
                else
                {
                    builder.Append('%').Append(char.ToUpperInvariant(pattern[i + 1])).Append(char.ToUpperInvariant(pattern[i + 2]));
                }

                i += 2;
            }
            else if (character == '\\' && !inQuery)
            {
                builder.Append('/');
            }
            else if (character > 0x7F || char.IsControl(character) || character is '%' or '\\' || ESCAPED_BY_URI.Contains(character))
            {
                var isPair = char.IsHighSurrogate(character) && i + 1 < pattern.Length;
                var written = Encoding.UTF8.GetBytes(pattern.AsSpan(i, isPair ? 2 : 1), utf8);
                for (var b = 0; b < written; b++)
                {
                    builder.Append('%').Append(utf8[b].ToString("X2", CultureInfo.InvariantCulture));
                }

                if (isPair)
                {
                    i++;
                }
            }
            else
            {
                inQuery |= character == '?';
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// The text's lines, numbered from 1: a line ends at <c>\n</c>, <c>\r</c> or <c>\r\n</c>, and a line break that ends the
    /// text starts no line after it.
    /// </summary>
    private static IEnumerable<(int Number, string Line)> Lines(string text)
    {
        var (number, start) = (0, 0);
        for (var index = 0; index <= text.Length; index++)
        {
            if (index < text.Length && text[index] is not ('\r' or '\n'))
            {
                continue;
            }

            if (index == text.Length && start == text.Length)
            {
                yield break;
            }

            yield return (++number, text[start..index]);
            if (index + 1 < text.Length && text[index] == '\r' && text[index + 1] == '\n')
            {
                index++;
            }

            start = index + 1;
        }
    }

    private static RobotsRule CreateRule(string value, bool allow)
    {
        var anchored = value.EndsWith('$');
        var pattern = NormalizePattern(anchored ? value[..^1] : value);

        // Measured once normalised, as the URLs are, so /%61bc and /abc are equally specific and the allow wins the tie.
        return new RobotsRule(pattern, allow, anchored, pattern.Contains('*'), pattern.Length + (anchored ? 1 : 0));
    }

    private static string ProductTokenOf(string userAgent)
    {
        var end = userAgent.IndexOfAny(['/', ' ', '\t']);
        return (end < 0 ? userAgent : userAgent[..end]).ToLowerInvariant();
    }

    private static string StripComment(string line)
    {
        var hash = line.IndexOf('#');
        return hash < 0 ? line : line[..hash];
    }
}
