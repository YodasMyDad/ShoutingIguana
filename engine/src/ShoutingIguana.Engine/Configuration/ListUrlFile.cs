using System.Globalization;
using System.Text;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Configuration;

/// <summary>
/// Reads the old list-mode file: a text file is one URL a line, and a CSV file has a <c>Url</c> column and an optional
/// <c>Priority</c> column. A higher priority is crawled first. Blank lines are skipped. The addresses themselves are
/// checked later, with the crawl's other options.
/// </summary>
public static class ListUrlFile
{
    /// <summary>What a row with no priority of its own is crawled at, as the old importer did.</summary>
    public const int DEFAULT_PRIORITY = 1000;

    /// <summary>The addresses in these files, highest priority first. A problem is one sentence.</summary>
    public static bool TryRead(IReadOnlyList<string> paths, out IReadOnlyList<string> urls, out string error)
    {
        List<(string Url, int Priority, int Order)> rows = [];
        var order = 0;
        foreach (var path in paths)
        {
            if (!File.Exists(path))
            {
                urls = [];
                error = $"List file '{path}' was not found.";
                return false;
            }

            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                urls = [];
                error = $"List file '{path}' couldn't be read: {exception.Message}";
                return false;
            }

            if (!TryTake(text, $"'{path}'", ref order, rows, out error))
            {
                urls = [];
                return false;
            }
        }

        urls = InCrawlOrder(rows);
        error = string.Empty;
        return true;
    }

    /// <summary>The addresses in this text, as <see cref="TryRead"/> reads a file. A problem is one sentence.</summary>
    public static bool TryParse(string text, out IReadOnlyList<string> urls, out string error)
    {
        List<(string Url, int Priority, int Order)> rows = [];
        var order = 0;
        if (!TryTake(text, "the list", ref order, rows, out error))
        {
            urls = [];
            return false;
        }

        urls = InCrawlOrder(rows);
        error = string.Empty;
        return true;
    }

    /// <summary>Adds one file's rows. An empty file, or a priority that isn't a whole number, is a problem.</summary>
    private static bool TryTake(string text, string where, ref int order, List<(string Url, int Priority, int Order)> rows, out string error)
    {
        var lines = text.TrimStart('\uFEFF').Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0)
        {
            error = Empty(where);
            return false;
        }

        var header = Cells(lines[0]);
        var urlColumn = Column(header, "url");
        var priorityColumn = urlColumn >= 0 ? Column(header, "priority") : -1;
        var start = urlColumn >= 0 ? 1 : 0;
        var added = 0;
        for (var index = start; index < lines.Length; index++)
        {
            var cells = urlColumn >= 0 ? Cells(lines[index]) : [lines[index]];
            var url = urlColumn >= 0 ? Cell(cells, urlColumn) : cells[0];
            if (url.Length == 0)
            {
                continue;
            }

            var priorityText = priorityColumn >= 0 ? Cell(cells, priorityColumn) : string.Empty;
            if (priorityText.Length > 0 && !int.TryParse(priorityText, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                error = $"Priority '{priorityText}' in {where} isn't a whole number.";
                return false;
            }

            var priority = priorityText.Length == 0
                ? DEFAULT_PRIORITY
                : int.Parse(priorityText, NumberStyles.Integer, CultureInfo.InvariantCulture);
            rows.Add((url, priority, order++));
            added++;
        }

        if (added == 0)
        {
            error = Empty(where);
            return false;
        }

        error = string.Empty;
        return true;
    }

    /// <summary>Highest priority first, and the earlier row when they tie. The same address once, keeping that row.</summary>
    private static List<string> InCrawlOrder(List<(string Url, int Priority, int Order)> rows)
    {
        rows.Sort(static (left, right) =>
        {
            var byPriority = right.Priority.CompareTo(left.Priority);
            return byPriority != 0 ? byPriority : left.Order.CompareTo(right.Order);
        });

        HashSet<string> seen = new(StringComparer.Ordinal);
        List<string> urls = [];
        foreach (var (url, _, _) in rows)
        {
            if (seen.Add(Identity(url)))
            {
                urls.Add(url);
            }
        }

        return urls;
    }

    /// <summary>
    /// The address as the crawl will store it, so two spellings of one address collapse. One with a user name stays as
    /// written: the crawl refuses it, and collapsing it would hide that.
    /// </summary>
    private static string Identity(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.UserInfo.Length == 0 && UrlNormalizer.TryNormalize(url, out var normalized)
            ? normalized
            : url;

    private static string Empty(string where) => where.StartsWith('\'') ? $"The list in {where} has no URLs." : "The list has no URLs.";

    private static int Column(List<string> header, string name)
    {
        for (var index = 0; index < header.Count; index++)
        {
            if (header[index].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private static string Cell(List<string> cells, int index) => index < cells.Count ? cells[index] : string.Empty;

    /// <summary>CSV cells, with quotes. A text file never comes here: a line there is one address, commas and all.</summary>
    private static List<string> Cells(string line)
    {
        List<string> cells = [];
        var current = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (quoted)
            {
                if (character == '"')
                {
                    if (index + 1 < line.Length && line[index + 1] == '"')
                    {
                        current.Append('"');
                        index++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    current.Append(character);
                }
            }
            else if (character == '"')
            {
                quoted = true;
            }
            else if (character == ',')
            {
                cells.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(character);
            }
        }

        cells.Add(current.ToString().Trim());
        return cells;
    }
}
