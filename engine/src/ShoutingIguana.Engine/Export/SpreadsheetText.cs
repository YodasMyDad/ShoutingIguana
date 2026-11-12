namespace ShoutingIguana.Engine.Export;

/// <summary>
/// Text from a crawl on its way into a spreadsheet. A crawl's text is the site's (a title, an anchor, a URL), and a
/// spreadsheet runs a cell that starts with =, +, - or @ as a formula, which can read other cells, fetch a URL or start
/// a program. So text that starts with one of those, or with a tab or a carriage return (which some spreadsheets skip
/// before deciding), is written so that it stays text.
/// </summary>
internal static class SpreadsheetText
{
    /// <summary>Whether a spreadsheet would take text typed like this for a formula.</summary>
    public static bool LooksLikeFormula(ReadOnlySpan<char> text) => text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r';
}
