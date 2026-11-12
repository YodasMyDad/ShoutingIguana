namespace ShoutingIguana.Engine.Export;

/// <summary>
/// Kept note of the files an export is part-way through writing. An export writes beside its target under another name
/// and moves the file into place once whole, so one that is cancelled or fails leaves nothing. A process that is killed
/// has no chance to clean up: whoever keeps this note can delete, when it next starts, the part files that were left,
/// knowing each by its full path, with no need to look through a folder of the user's for files that look like one.
/// <para>
/// Both calls come from the export's own thread, and must not throw: a note that can't be kept costs a part file that
/// stays after a crash, never the export.
/// </para>
/// </summary>
public interface IExportJournal
{
    /// <summary>A part file is about to be created at this full path: called before the file exists.</summary>
    void Opening(string partFile);

    /// <summary>The part file is no longer there: it was moved into place as the export's file, or deleted.</summary>
    void Closed(string partFile);
}
