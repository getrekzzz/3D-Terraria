namespace Strata.Core.Persistence;

/// <summary>Crash-safe file writes: write to a temp file, keep a backup of the previous version, rename.</summary>
public static class AtomicFile
{
    public static void WriteAllBytes(string path, ReadOnlySpan<byte> bytes, bool keepBackup = true)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            fs.Write(bytes);
            fs.Flush(flushToDisk: true);
        }
        if (keepBackup && File.Exists(path)) File.Copy(path, path + ".bak", overwrite: true);
        File.Move(tmp, path, overwrite: true);
    }

    public static void WriteAllText(string path, string text, bool keepBackup = true) =>
        WriteAllBytes(path, System.Text.Encoding.UTF8.GetBytes(text), keepBackup);

    /// <summary>Reads a file, falling back to its backup if the primary is missing or unreadable.</summary>
    public static byte[]? ReadWithFallback(string path, Func<byte[], bool>? isValid = null)
    {
        foreach (var candidate in new[] { path, path + ".bak" })
        {
            if (!File.Exists(candidate)) continue;
            var bytes = File.ReadAllBytes(candidate);
            if (isValid == null || isValid(bytes)) return bytes;
        }
        return null;
    }
}
