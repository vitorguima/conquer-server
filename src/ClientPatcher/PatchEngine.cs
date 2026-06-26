namespace ClientPatcher;

/// <summary>One replaced occurrence: where, the original bytes, the written bytes.</summary>
public sealed record MatchEdit(int Offset, byte[] OldBytes, byte[] NewBytes);

/// <summary>Per-file patch outcome (design §5).</summary>
public sealed class PatchResult
{
    public string FileName = string.Empty;
    public IReadOnlyList<MatchEdit> Edits = Array.Empty<MatchEdit>();
    public int MatchCount;
}

/// <summary>Patch failure modes (design §5). <see cref="None"/> = success.</summary>
public enum PatchError
{
    None,
    FindNotFound,
    NewLongerThanOld,
}

/// <summary>
/// Core length-preserving, null-padded ASCII search/replace (design §5,
/// FR-1/FR-6/FR-11, NFR-2/3/6). Pure: same bytes in → same bytes out.
/// </summary>
public static class PatchEngine
{
    /// <summary>
    /// Replace every occurrence of <paramref name="find"/> in a copy of
    /// <paramref name="source"/> with <paramref name="replacement"/>, null-padding
    /// the remainder of each matched slot. Output length always equals
    /// <paramref name="source"/> length — bytes are mutated in place, never
    /// inserted or removed.
    /// </summary>
    public static (byte[] output, PatchResult result, PatchError error)
        Apply(byte[] source, byte[] find, byte[] replacement)
    {
        var result = new PatchResult();

        // Rule 1: replacement longer than the slot → reject, no output (AC-3.3).
        if (replacement.Length > find.Length)
        {
            return (Array.Empty<byte>(), result, PatchError.NewLongerThanOld);
        }

        // Rule 2: collect ALL match offsets via linear ASCII byte search (AC-3.1).
        var offsets = FindAll(source, find);

        // Rule 3: zero matches → not found, no output (AC-3.2).
        if (offsets.Count == 0)
        {
            return (Array.Empty<byte>(), result, PatchError.FindNotFound);
        }

        // Mutate an in-memory copy; length preserved (NFR-3, AC-2.3).
        var output = (byte[])source.Clone();
        var edits = new List<MatchEdit>(offsets.Count);

        foreach (var offset in offsets)
        {
            var oldBytes = new byte[find.Length];
            Array.Copy(source, offset, oldBytes, 0, find.Length);

            // Write replacement bytes...
            Array.Copy(replacement, 0, output, offset, replacement.Length);
            // ...then null-pad the rest of the slot (0x00) up to find.Length (AC-3.4).
            for (var i = replacement.Length; i < find.Length; i++)
            {
                output[offset + i] = 0x00;
            }

            var newBytes = new byte[find.Length];
            Array.Copy(output, offset, newBytes, 0, find.Length);
            edits.Add(new MatchEdit(offset, oldBytes, newBytes));
        }

        result.Edits = edits;
        result.MatchCount = edits.Count;
        return (output, result, PatchError.None);
    }

    /// <summary>
    /// All start offsets of <paramref name="find"/> within <paramref name="source"/>,
    /// bounded to [0, len-len(find)] so no read/write straddles end-of-file.
    /// Overlapping matches are reported; the scan advances one byte at a time.
    /// </summary>
    private static List<int> FindAll(byte[] source, byte[] find)
    {
        var offsets = new List<int>();
        if (find.Length == 0 || find.Length > source.Length)
        {
            return offsets;
        }

        var last = source.Length - find.Length;
        for (var i = 0; i <= last; i++)
        {
            var match = true;
            for (var j = 0; j < find.Length; j++)
            {
                if (source[i + j] != find[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                offsets.Add(i);
            }
        }

        return offsets;
    }
}
