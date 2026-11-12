using System.Globalization;
using System.Text;

namespace ShoutingIguana.Engine.Export.Xlsx;

/// <summary>
/// A workbook's shared strings, written as they come. A workbook keeps the text of its cells in one table, each cell
/// naming its text by index; it's what Excel itself writes, and what every reader of the format reads (some read the
/// other form, text inline in each cell, badly or not at all). The table is a part of its own, after the sheets, so its
/// items go to a file of their own as the sheets are written, and are copied in at the end: nothing is held but
/// the texts seen lately, by which a repeat names the item it already has (a page's links all name the page; a status's
/// words repeat down a column). The same text may be in the table twice, which the format allows.
/// </summary>
internal sealed class SharedStrings : IDisposable
{
    /// <summary>The texts remembered, to name a repeat by the item it has: when there are this many, they're forgotten and remembered afresh.</summary>
    private const int REMEMBERED = 65_536;

    /// <summary>And the characters they may hold between them, so a column of long texts can't hold much either.</summary>
    private const int REMEMBERED_CHARS = 4 * 1_024 * 1_024;

    /// <summary>A text longer than this is never remembered: a long one is seldom repeated.</summary>
    private const int LONGEST_REMEMBERED = 512;

    private const int BUFFER_BYTES = 1 << 16;

    private readonly Dictionary<string, int> _recent = new(StringComparer.Ordinal);
    private readonly Stream _file;
    private readonly StreamWriter _items;
    private long _recentChars;
    private bool _disposed;

    /// <param name="file">
    /// A file of its own to write the items to and read them back from, on the volume the workbook is written to; closed
    /// with this.
    /// </param>
    public SharedStrings(Stream file)
    {
        _file = file;
        _items = new StreamWriter(file, new UTF8Encoding(false), BUFFER_BYTES, leaveOpen: true);
    }

    /// <summary>The items in the table.</summary>
    public int Count { get; private set; }

    /// <summary>The cells that name one.</summary>
    public long References { get; private set; }

    /// <summary>The index of the text's item, written now unless one seen lately holds the same text.</summary>
    public int Add(ReadOnlySpan<char> text)
    {
        References++;
        var remember = text.Length <= LONGEST_REMEMBERED;
        if (remember && _recent.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(text, out var known))
        {
            return known;
        }

        _items.Write("<si><t xml:space=\"preserve\">");
        XlsxText.Write(_items, text);
        _items.Write("</t></si>");
        var index = Count++;
        if (remember)
        {
            if (_recent.Count >= REMEMBERED || _recentChars + text.Length > REMEMBERED_CHARS)
            {
                _recent.Clear();
                _recentChars = 0;
            }

            _recent[text.ToString()] = index;
            _recentChars += text.Length;
        }

        return index;
    }

    /// <summary>Writes the whole table as a part: its opening with the counts, the items, its close.</summary>
    public void CopyTo(Stream part)
    {
        _items.Flush();
        var opening = Encoding.UTF8.GetBytes(
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n" +
            $"<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" count=\"{References.ToString(CultureInfo.InvariantCulture)}\" " +
            $"uniqueCount=\"{Count.ToString(CultureInfo.InvariantCulture)}\">");
        part.Write(opening);
        _file.Position = 0;
        _file.CopyTo(part, BUFFER_BYTES);
        part.Write("</sst>"u8);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _items.Dispose();
        }
        finally
        {
            _file.Dispose();
        }
    }
}
