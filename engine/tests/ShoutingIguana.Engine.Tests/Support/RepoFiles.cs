namespace ShoutingIguana.Engine.Tests.Support;

/// <summary>Files of the repository that a test reads as they stand: a data file, a document, the design's reference code.</summary>
internal static class RepoFiles
{
    /// <summary>The path of a file in the repository, from its root: <c>Find("docs", "v2-ui-simple.md")</c>.</summary>
    public static string Find(params string[] parts) => Path.Combine([Folder(), .. parts]);

    /// <summary>The file's text, with the line endings of whichever checkout this is read as "\n".</summary>
    public static string Read(params string[] parts) => File.ReadAllText(Find(parts)).ReplaceLineEndings("\n");

    /// <summary>The repository's root: the folder above the one that holds the engine's solution.</summary>
    private static string Folder()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "ShoutingIguana.Engine.slnx")))
            {
                return folder.Parent!.FullName;
            }
        }

        throw new InvalidOperationException($"No engine folder above {AppContext.BaseDirectory}.");
    }
}
