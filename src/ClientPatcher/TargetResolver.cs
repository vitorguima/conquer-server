namespace ClientPatcher;

/// <summary>A resolved target file: its canonical name, source path, and output path.</summary>
public sealed record TargetFile(string Name, string SourcePath, string OutputPath);

/// <summary>
/// Resolves which target files (<c>Conquer.exe</c> / <c>server.dat</c>) exist
/// under <c>--client</c> (design §3, FR-7). Matched case-insensitively (Windows
/// artifacts on a Linux/macOS host). Output path is <c>&lt;OutDir&gt;/&lt;name&gt;</c>,
/// where OutDir defaults to <c>&lt;ClientDir&gt;/patched</c>.
/// </summary>
public static class TargetResolver
{
    private static readonly string[] CanonicalNames = { "Conquer.exe", "server.dat" };

    /// <summary>
    /// Returns 1–2 entries for the canonical target files present on disk under
    /// <c>--client</c>. The on-disk file name is matched case-insensitively; the
    /// canonical name is used for the output file name.
    /// </summary>
    public static IReadOnlyList<TargetFile> Resolve(PatchOptions o)
    {
        var targets = new List<TargetFile>();

        var clientDir = o.ClientDir;
        if (string.IsNullOrEmpty(clientDir) || !Directory.Exists(clientDir))
        {
            return targets;
        }

        var outDir = string.IsNullOrEmpty(o.OutDir)
            ? Path.Combine(clientDir, "patched")
            : o.OutDir;

        var entries = Directory.GetFiles(clientDir);

        foreach (var canonical in CanonicalNames)
        {
            foreach (var entry in entries)
            {
                var fileName = Path.GetFileName(entry);
                if (string.Equals(fileName, canonical, StringComparison.OrdinalIgnoreCase))
                {
                    var outputPath = Path.Combine(outDir, canonical);
                    targets.Add(new TargetFile(canonical, entry, outputPath));
                    break;
                }
            }
        }

        return targets;
    }
}
