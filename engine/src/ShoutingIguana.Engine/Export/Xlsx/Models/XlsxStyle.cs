namespace ShoutingIguana.Engine.Export.Xlsx.Models;

/// <summary>How a cell looks: the index of its format in the workbook's styles, which <see cref="XlsxWriter"/> writes in this order.</summary>
internal enum XlsxStyle
{
    None = 0,

    /// <summary>A header's cell, or a label: bold.</summary>
    Bold = 1,

    /// <summary>
    /// Text that would be a formula if it were typed (it starts with =, +, - or @): marked as Excel marks a cell typed with
    /// a leading apostrophe, so it stays text when someone edits the cell and presses Enter.
    /// </summary>
    QuotedText = 2,

    /// <summary>A date and time.</summary>
    DateTime = 3,

    /// <summary>A sheet's title: bold and larger.</summary>
    Title = 4,

    /// <summary>A share, as a percentage to two places.</summary>
    Percent = 5,

    /// <summary>A count, with its thousands apart.</summary>
    Count = 6
}
