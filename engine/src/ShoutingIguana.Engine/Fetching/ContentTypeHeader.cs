using System.Buffers;
using System.Net.Http.Headers;

namespace ShoutingIguana.Engine.Fetching;

/// <summary>
/// Reads Content-Type as leniently as browsers do. .NET's typed header is null for values servers really send, such
/// as <c>text/html;charset=UTF-8;</c>, <c>;;</c>, an empty or unclosed charset, or a missing semicolon, and a page
/// whose type can't be read would not be treated as HTML at all.
/// </summary>
internal static class ContentTypeHeader
{
    private const string CHARSET = "charset";

    private static readonly SearchValues<char> MEDIA_TYPE_END = SearchValues.Create(";, \t");

    /// <summary>The raw Content-Type value as sent, or null when there is none.</summary>
    public static string? Read(HttpContentHeaders headers) =>
        headers.NonValidated.TryGetValues("Content-Type", out var values)
            ? values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            : null;

    /// <summary>Lowercase media type without parameters (<c>text/html</c>): whatever comes before a <c>;</c>, <c>,</c> or space.</summary>
    public static string? MediaTypeOf(string? value)
    {
        var trimmed = value.AsSpan().Trim();
        var end = trimmed.IndexOfAny(MEDIA_TYPE_END);
        var mediaType = (end < 0 ? trimmed : trimmed[..end]).Trim();
        return mediaType.IsEmpty ? null : mediaType.ToString().ToLowerInvariant();
    }

    /// <summary>The charset parameter wherever it appears, with or without (even unbalanced) quotes; null when absent or empty.</summary>
    public static string? CharsetOf(string? value)
    {
        if (value is null)
        {
            return null;
        }

        for (var start = 0; start < value.Length;)
        {
            var index = value.IndexOf(CHARSET, start, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return null;
            }

            start = index + CHARSET.Length;
            if (index > 0 && value[index - 1] is not (';' or ',' or ' ' or '\t'))
            {
                // Part of another parameter's name, such as x-charset.
                continue;
            }

            var position = SkipWhitespace(value, start);
            if (position == value.Length || value[position] != '=')
            {
                continue;
            }

            position = SkipWhitespace(value, position + 1);
            var end = position;
            while (end < value.Length && value[end] is not (';' or ',' or ' ' or '\t'))
            {
                end++;
            }

            var charset = value[position..end].Trim('"', '\'');
            if (charset.Length > 0)
            {
                return charset;
            }
        }

        return null;
    }

    private static int SkipWhitespace(string value, int position)
    {
        while (position < value.Length && value[position] is ' ' or '\t')
        {
            position++;
        }

        return position;
    }
}
