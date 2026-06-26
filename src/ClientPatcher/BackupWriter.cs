namespace ClientPatcher;

/// <summary>
/// Writes a <c>&lt;output&gt;.bak</c> of the original-copy bytes BEFORE patched
/// bytes land (design §6, FR-8, AC-2.1/2.2). The source under <c>--client</c> is
/// never opened for write — the backup lives beside the patched output in the
/// output directory. A prior <c>.bak</c> is overwritten (deterministic).
/// </summary>
public static class BackupWriter
{
    /// <summary>
    /// Write <c>&lt;outputPath&gt;.bak</c> containing
    /// <paramref name="originalCopyBytes"/> (the unmodified source-copy bytes).
    /// Creates the output directory if needed. Returns the backup path.
    /// </summary>
    public static string WriteBackup(string outputPath, byte[] originalCopyBytes)
    {
        var backupPath = outputPath + ".bak";

        var dir = Path.GetDirectoryName(backupPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // Overwrite any prior .bak (it is the original-source copy; deterministic).
        File.WriteAllBytes(backupPath, originalCopyBytes);
        return backupPath;
    }
}
