using System.Text;

namespace ClientPatcher;

internal static class Program
{
    private static int Main(string[] args)
    {
        // Temporary POC harness (removed in task 1.26): proves PatchEngine on an
        // in-memory fixture without touching disk.
        if (args.Length == 1 && args[0] == "--selftest")
        {
            return SelfTest();
        }

        Console.WriteLine("ClientPatcher — auth endpoint repoint (auth-only; game IP never touched)");
        return 0;
    }

    private static int SelfTest()
    {
        const string findStr = "192.168.0.10";
        const string replStr = "127.0.0.1";

        // Build a buffer with the placeholder host (null-terminated) among filler.
        var prefix = Encoding.ASCII.GetBytes("FILLER_HEAD\0");
        var find = Encoding.ASCII.GetBytes(findStr);
        var terminatorAndTail = Encoding.ASCII.GetBytes("\0FILLER_TAIL");
        var source = new byte[prefix.Length + find.Length + terminatorAndTail.Length];
        Array.Copy(prefix, 0, source, 0, prefix.Length);
        Array.Copy(find, 0, source, prefix.Length, find.Length);
        Array.Copy(terminatorAndTail, 0, source, prefix.Length + find.Length, terminatorAndTail.Length);

        var replacement = Encoding.ASCII.GetBytes(replStr);
        var (output, result, error) = PatchEngine.Apply(source, find, replacement);

        if (error != PatchError.None)
        {
            Console.Error.WriteLine($"SELFTEST FAIL: unexpected error {error}");
            return 1;
        }

        // Length must be preserved.
        if (output.Length != source.Length)
        {
            Console.Error.WriteLine(
                $"SELFTEST FAIL: length {output.Length} != source length {source.Length}");
            return 1;
        }

        if (result.MatchCount != 1)
        {
            Console.Error.WriteLine($"SELFTEST FAIL: expected 1 match, got {result.MatchCount}");
            return 1;
        }

        // The matched slot must be replacement bytes followed by null padding.
        var offset = result.Edits[0].Offset;
        var expectedSlot = new byte[find.Length];
        Array.Copy(replacement, 0, expectedSlot, 0, replacement.Length);
        // remaining bytes already 0x00

        var actualSlot = new byte[find.Length];
        Array.Copy(output, offset, actualSlot, 0, find.Length);

        if (!actualSlot.AsSpan().SequenceEqual(expectedSlot))
        {
            Console.Error.WriteLine("SELFTEST FAIL: matched region not <ip> + null padding");
            return 1;
        }

        Console.WriteLine(
            $"SELFTEST PASS: offset 0x{offset:X8}, '{findStr}' -> '{replStr}' + null pad, " +
            $"length preserved ({output.Length} bytes)");
        return 0;
    }
}
