namespace ShoutingIguana.Engine.Export.Models;

/// <summary>The file an export writes.</summary>
public enum ExportFormat
{
    /// <summary>RFC 4180 text: UTF-8 with a byte-order mark, CRLF rows.</summary>
    Csv,

    /// <summary>An Excel workbook.</summary>
    Xlsx,

    /// <summary>The report's document.</summary>
    Html
}
