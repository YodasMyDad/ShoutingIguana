using System.Text;

namespace ShoutingIguana.Sidecar.Serialization;

/// <summary>Writes the sidecar's own files in its state folder (the library, the defaults, the summary cache).</summary>
internal static class StateFile
{
    /// <summary>
    /// Written beside the file under a name of its own, flushed to disk, then moved over it: a crash or a power cut leaves
    /// the old file or the new one, never half of one, and a temporary file something else still holds is never reused.
    /// </summary>
    /// <exception cref="IOException">The file couldn't be written or replaced; it is as it was.</exception>
    /// <exception cref="UnauthorizedAccessException">This user may not write it.</exception>
    public static void Write(string path, string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 4096))
            {
                file.Write(Encoding.UTF8.GetBytes(contents));
                file.Flush(flushToDisk: true);
            }

            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Left behind, under a name nothing else uses: the next write doesn't need it gone.
        }
    }
}
