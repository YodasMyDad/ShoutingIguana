using System.Text;

namespace ShoutingIguana.Engine.Urls;

/// <summary>
/// Canonical string form for every URL the crawler stores. Lowercases scheme and host, drops default
/// ports, empty queries and fragments, and normalises percent-encoding. Path case and trailing slashes
/// are kept exactly as published, because servers treat them as different URLs.
/// </summary>
public static class UrlNormalizer
{
    private const string UNRESERVED_PUNCTUATION = "-._~";

    public static bool IsHttp(Uri uri) =>
        uri.IsAbsoluteUri && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    public static bool TryNormalize(string? url, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ||
            !IsHttp(uri))
        {
            return false;
        }

        normalized = Normalize(uri);
        return true;
    }

    public static string Normalize(Uri uri)
    {
        var builder = new StringBuilder(uri.OriginalString.Length + 8);
        builder.Append(uri.Scheme).Append("://").Append(GetHost(uri));

        if (!uri.IsDefaultPort)
        {
            builder.Append(':').Append(uri.Port);
        }

        var path = uri.AbsolutePath;
        AppendPercentNormalized(builder, path.Length == 0 ? "/" : path);

        if (uri.Query.Length > 1)
        {
            AppendPercentNormalized(builder, uri.Query);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Resolves an href the way a browser does (HTML whitespace stripped, relative to the base URL) and
    /// accepts only http/https results. mailto:, tel:, javascript: and data: links are rejected.
    /// </summary>
    public static bool TryResolve(Uri baseUri, string? href, out Uri resolved)
    {
        resolved = baseUri;
        if (href is null)
        {
            return false;
        }

        var cleaned = StripHtmlWhitespace(href);
        if (cleaned.Length == 0 || !Uri.TryCreate(baseUri, cleaned, out var candidate) || !IsHttp(candidate))
        {
            return false;
        }

        resolved = candidate;
        return true;
    }

    /// <summary>
    /// The fragment without its '#'; empty for a bare '#', so a link written "#" (a script's handle, or a jump to the top)
    /// can be told from one to the page's own address; null with none.
    /// </summary>
    public static string? GetFragment(Uri uri) => uri.Fragment.Length > 0 ? uri.Fragment[1..] : null;

    /// <summary>Lowercase ASCII host; IPv6 literals keep their brackets.</summary>
    public static string GetHost(Uri uri) =>
        (uri.HostNameType == UriHostNameType.IPv6 ? uri.Host : uri.IdnHost).ToLowerInvariant();

    private static void AppendPercentNormalized(StringBuilder builder, string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            var current = value[i];
            if (current == '%' && i + 2 < value.Length && Uri.IsHexDigit(value[i + 1]) && Uri.IsHexDigit(value[i + 2]))
            {
                var decoded = (char)Convert.ToByte(value.Substring(i + 1, 2), 16);
                if (IsUnreserved(decoded))
                {
                    builder.Append(decoded);
                }
                else
                {
                    builder.Append('%').Append(char.ToUpperInvariant(value[i + 1])).Append(char.ToUpperInvariant(value[i + 2]));
                }

                i += 2;
                continue;
            }

            builder.Append(current);
        }
    }

    private static bool IsUnreserved(char value) =>
        char.IsAsciiLetterOrDigit(value) || UNRESERVED_PUNCTUATION.Contains(value);

    private static string StripHtmlWhitespace(string href)
    {
        var trimmed = href.AsSpan().Trim(" \t\n\r\f\v\0");
        if (trimmed.IndexOfAny('\t', '\n', '\r') < 0)
        {
            return trimmed.ToString();
        }

        var builder = new StringBuilder(trimmed.Length);
        foreach (var character in trimmed)
        {
            if (character is not ('\t' or '\n' or '\r'))
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }
}
