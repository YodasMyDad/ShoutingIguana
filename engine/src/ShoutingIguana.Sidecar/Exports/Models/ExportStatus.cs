namespace ShoutingIguana.Sidecar.Exports.Models;

/// <summary>Where an export has got to.</summary>
internal enum ExportStatus
{
    Running,

    /// <summary>The file is whole, and at its path.</summary>
    Done,

    /// <summary>Cancelled: nothing was written.</summary>
    Cancelled,

    /// <summary>Failed: nothing was written, and the export's error says why.</summary>
    Failed
}
