using ShoutingIguana.Engine.Fetching.Models;

namespace ShoutingIguana.Engine.Fetching;

/// <summary>Parses RFC 8288 Link headers, which carry canonicals and hreflang for non-HTML files.</summary>
internal static class LinkHeaderParser
{
    public static IReadOnlyList<LinkHeaderEntry> Parse(IEnumerable<string> headerValues)
    {
        List<LinkHeaderEntry> entries = [];

        foreach (var value in headerValues)
        {
            var position = 0;
            while (position < value.Length)
            {
                var urlStart = value.IndexOf('<', position);
                if (urlStart < 0)
                {
                    break;
                }

                var urlEnd = value.IndexOf('>', urlStart + 1);
                if (urlEnd < 0)
                {
                    break;
                }

                var entryEnd = FindEntryEnd(value, urlEnd + 1);
                var url = value[(urlStart + 1)..urlEnd].Trim();
                string? rel = null;
                string? hreflang = null;

                foreach (var parameter in value[(urlEnd + 1)..entryEnd].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var equals = parameter.IndexOf('=');
                    if (equals < 0)
                    {
                        continue;
                    }

                    var key = parameter[..equals].Trim();
                    var parameterValue = parameter[(equals + 1)..].Trim().Trim('"');
                    if (key.Equals("rel", StringComparison.OrdinalIgnoreCase))
                    {
                        rel = parameterValue.ToLowerInvariant();
                    }
                    else if (key.Equals("hreflang", StringComparison.OrdinalIgnoreCase))
                    {
                        hreflang = parameterValue;
                    }
                }

                if (rel is not null && url.Length > 0)
                {
                    entries.Add(new LinkHeaderEntry(url, rel, hreflang));
                }

                position = entryEnd + 1;
            }
        }

        return entries;
    }

    private static int FindEntryEnd(string value, int from)
    {
        var inQuotes = false;
        for (var index = from; index < value.Length; index++)
        {
            if (value[index] == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (value[index] == ',' && !inQuotes)
            {
                return index;
            }
        }

        return value.Length;
    }
}
