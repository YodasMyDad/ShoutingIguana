namespace ShoutingIguana.Engine.Export.Xlsx.Models;

/// <summary>One cell of a row: text, a number (a date is one, shown as a date by its style), or nothing.</summary>
internal readonly record struct XlsxCell(XlsxCellKind Kind, string? TextValue, double NumberValue, XlsxStyle Style)
{
    public static XlsxCell Empty => default;

    /// <summary>
    /// Text, which a workbook never runs as a formula: a text cell is text whatever it holds. Text that looks like a
    /// formula is also marked so that editing the cell leaves it text (<see cref="XlsxStyle.QuotedText"/>).
    /// </summary>
    public static XlsxCell Text(string? text, XlsxStyle style = XlsxStyle.None) => string.IsNullOrEmpty(text)
        ? Empty
        : new XlsxCell(XlsxCellKind.Text, text, 0, style == XlsxStyle.None && SpreadsheetText.LooksLikeFormula(text) ? XlsxStyle.QuotedText : style);

    public static XlsxCell Number(double number, XlsxStyle style = XlsxStyle.None) =>
        double.IsFinite(number) ? new XlsxCell(XlsxCellKind.Number, null, number, style) : Empty;

    /// <summary>A date and time, as the clock of whoever reads it would show it: a workbook's dates have no time zone.</summary>
    public static XlsxCell DateTime(System.DateTime local) => new(XlsxCellKind.Number, null, local.ToOADate(), XlsxStyle.DateTime);
}
