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

    public static RobotsTxt Parse(string content, string productToken)
    {
        var token = productToken.ToLowerInvariant();
        List<RobotsGroupBuilder> groups = [];
        List<string> sitemaps = [];
        RobotsGroupBuilder? current = null;

        foreach (var rawLine in content.TrimStart('﻿').Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = StripComment(rawLine).Trim();
            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var key = line[..colon].Trim().ToLowerInvariant();
            var value = line[(colon + 1)..].Trim();

            switch (key)
            {
                case "user-agent" or "useragent":
                    if (current is null || current.HasDirectives)
                    {
                        current = new RobotsGroupBuilder();
                        groups.Add(current);
                    }

                    current.Agents.Add(ProductTokenOf(value));
                    break;
                case "allow" or "disallow" when current is not null:
                    // An empty rule allows everything, but like any rule it ends the run of user-agent lines, so a
                    // following user-agent starts a new group (RFC 9309 grammar, and Google's parser).
                    if (value.Length > 0)
                    {
                        current.Rules.Add(CreateRule(value, key == "allow"));
                    }

                    current.HasDirectives = true;
                    break;
                case "crawl-delay" when current is not null:
                    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var delay) && delay >= 0)
                    {
                        current.CrawlDelaySeconds = Math.Min(delay, MAX_CRAWL_DELAY_SECONDS);
                    }

                    current.HasDirectives = true;
                    break;
                case "sitemap" when value.Length > 0:
                    sitemaps.Add(value);
                    break;
            }
        }

        var applicable = groups.Where(group => group.Agents.Contains(token)).ToList();
        if (applicable.Count == 0)
        {
            applicable = groups.Where(group => group.Agents.Contains("*")).ToList();
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

    private static RobotsRule CreateRule(string value, bool allow)
    {
        var anchored = value.EndsWith('$');
        var pattern = NormalizePattern(anchored ? value[..^1] : value);
        return new RobotsRule(pattern, allow, anchored, pattern.Contains('*'), value.Length);
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
