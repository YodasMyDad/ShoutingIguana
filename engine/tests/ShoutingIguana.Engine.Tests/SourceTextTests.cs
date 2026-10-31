namespace ShoutingIguana.Engine.Tests;

/// <summary>
/// Source files carry no invisible characters (zero-width joiners, soft hyphens, byte order marks mid-file) or
/// bidirectional controls, which hide what code says or make it read differently. Tests that need them spell them
/// as escapes, like \u200C.
/// </summary>
public sealed class SourceTextTests
{
    [Fact]
    public void SourcesSpellInvisibleCharactersAsEscapes()
    {
        var root = FindEngineFolder();
        List<string> found = [];
        foreach (var path in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                     .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj")))
        {
            var lines = File.ReadAllLines(path);
            for (var line = 0; line < lines.Length; line++)
            {
                // ReadAllLines drops a byte order mark that starts the file, which is just an encoding marker.
                var character = lines[line].FirstOrDefault(IsInvisible);
                if (character != default)
                {
                    found.Add($"{Path.GetRelativePath(root, path)}:{line + 1} has U+{(int)character:X4}");
                }
            }
        }

        Assert.True(found.Count == 0, string.Join(Environment.NewLine, found));
    }

    private static bool IsInvisible(char character) =>
        (int)character is 0x00AD or 0x061C or 0xFEFF or (>= 0x200B and <= 0x200F) or (>= 0x202A and <= 0x202E) or (>= 0x2060 and <= 0x2069);

    private static string FindEngineFolder()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "ShoutingIguana.Engine.slnx")))
            {
                return folder.FullName;
            }
        }

        throw new InvalidOperationException($"No engine folder above {AppContext.BaseDirectory}.");
    }
}
