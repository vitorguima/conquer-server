using System.Text;

namespace ClientPatcher;

/// <summary>
/// Renders a human-readable plain-text report to stdout (design §7, FR-9,
/// AC-2.4, NFR-7). No JSON. Per file: hex offset, old bytes, new bytes, match
/// count; then totals, backups, warnings, and the port-applied/unchanged line.
/// </summary>
public static class ReportWriter
{
    /// <summary>
    /// Write the report for <paramref name="results"/> to <paramref name="o"/>,
    /// echoing <paramref name="warnings"/> and reflecting the
    /// <paramref name="endpoint"/> port plan.
    /// </summary>
    public static void Write(
        TextWriter o,
        IReadOnlyList<PatchResult> results,
        IReadOnlyList<string> warnings,
        EndpointPlan endpoint)
    {
        o.WriteLine("ClientPatcher — auth endpoint repoint (auth-only; game IP never touched)");
        o.WriteLine();

        foreach (var warning in warnings)
        {
            o.WriteLine("WARNING: " + warning);
            o.WriteLine();
        }

        var totalOffsets = 0;
        var backups = new List<string>();

        foreach (var result in results)
        {
            o.WriteLine("File: " + result.FileName);
            foreach (var edit in result.Edits)
            {
                o.WriteLine(
                    $"  offset 0x{edit.Offset:X8}  old \"{Render(edit.OldBytes)}\"  ->  new \"{Render(edit.NewBytes)}\"  (host)");
            }

            o.WriteLine($"  matches: {result.MatchCount}");
            totalOffsets += result.MatchCount;
            backups.Add($"patched/{result.FileName}.bak");
        }

        if (endpoint.PortApplied)
        {
            o.WriteLine($"Auth port: applied ({endpoint.Port}) — co-located in matched string for this build.");
        }
        else
        {
            o.WriteLine("Auth port: left unchanged (not co-located in matched string for this build).");
        }

        o.WriteLine();
        o.WriteLine($"Total files patched: {results.Count}   Total offsets changed: {totalOffsets}");
        if (backups.Count > 0)
        {
            o.WriteLine("Backups: " + string.Join(", ", backups));
        }
    }

    /// <summary>
    /// Render bytes for display: printable ASCII verbatim, NUL as <c>\0</c>, any
    /// other byte as <c>\xNN</c>.
    /// </summary>
    private static string Render(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length);
        foreach (var b in bytes)
        {
            if (b == 0x00)
            {
                sb.Append("\\0");
            }
            else if (b >= 0x20 && b <= 0x7E)
            {
                sb.Append((char)b);
            }
            else
            {
                sb.Append("\\x").Append(b.ToString("X2"));
            }
        }

        return sb.ToString();
    }
}
