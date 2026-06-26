using System.Text;

namespace ClientPatcher.Tests.Fixtures;

/// <summary>
/// Builds deterministic, synthetic in-memory fixtures for the test suite
/// (NFR-4/NFR-5, AC-6.2). No real TQ/Conquer assets are used anywhere — every
/// byte array here is hand-constructed filler with a known placeholder host
/// token (<c>192.168.0.10\0</c>) embedded among it.
/// </summary>
public static class FixtureFactory
{
    /// <summary>The synthetic placeholder host string the fixtures embed.</summary>
    public const string PlaceholderHost = "192.168.0.10";

    /// <summary>NUL terminator that follows the host token in the fixtures.</summary>
    public const byte Terminator = 0x00;

    /// <summary>
    /// Build a stub <c>Conquer.exe</c> byte array: deterministic filler with the
    /// placeholder host token <c>192.168.0.10\0</c> embedded twice (two distinct
    /// offsets) to exercise multi-match patching. Filler bytes are chosen to never
    /// collide with the host token.
    /// </summary>
    public static byte[] BuildConquerExe()
    {
        var host = HostToken(PlaceholderHost);

        // Deterministic, host-free filler: a repeating 0xA5/0x5A pattern that
        // cannot contain the ASCII host token (no '1'/'9'/'2'/'.' bytes).
        var lead = Filler(64, 0);
        var mid = Filler(48, 1);
        var tail = Filler(32, 2);

        return Concat(lead, host, mid, host, tail);
    }

    /// <summary>
    /// Build a stub <c>server.dat</c> byte array: deterministic filler with a
    /// single placeholder host token <c>192.168.0.10\0</c>.
    /// </summary>
    public static byte[] BuildServerDat()
    {
        var host = HostToken(PlaceholderHost);
        var lead = Filler(40, 3);
        var tail = Filler(24, 4);

        return Concat(lead, host, tail);
    }

    /// <summary>
    /// Write the synthetic <c>Conquer.exe</c> and <c>server.dat</c> stubs into a
    /// freshly created unique temp directory (under the system temp path — this
    /// runs inside the dockerized SDK container where the system temp is fine).
    /// Returns the absolute path to that directory; the caller is responsible for
    /// deleting it.
    /// </summary>
    public static string WriteClientDir()
    {
        var dir = Path.Combine(
            Path.GetTempPath(),
            "client-patcher-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        File.WriteAllBytes(Path.Combine(dir, "Conquer.exe"), BuildConquerExe());
        File.WriteAllBytes(Path.Combine(dir, "server.dat"), BuildServerDat());

        return dir;
    }

    /// <summary>ASCII bytes of <paramref name="host"/> followed by a NUL terminator.</summary>
    public static byte[] HostToken(string host)
    {
        var ascii = Encoding.ASCII.GetBytes(host);
        var token = new byte[ascii.Length + 1];
        Array.Copy(ascii, token, ascii.Length);
        token[ascii.Length] = Terminator;
        return token;
    }

    /// <summary>
    /// Deterministic host-token-free filler of <paramref name="length"/> bytes.
    /// Uses an alternating 0xA5/0x5A pattern keyed by <paramref name="seed"/> so
    /// each filler block differs but never contains printable digit/dot bytes.
    /// </summary>
    private static byte[] Filler(int length, int seed)
    {
        var buffer = new byte[length];
        for (var i = 0; i < length; i++)
        {
            buffer[i] = (byte)(((i + seed) & 1) == 0 ? 0xA5 : 0x5A);
        }

        return buffer;
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var total = 0;
        foreach (var part in parts)
        {
            total += part.Length;
        }

        var result = new byte[total];
        var offset = 0;
        foreach (var part in parts)
        {
            Array.Copy(part, 0, result, offset, part.Length);
            offset += part.Length;
        }

        return result;
    }
}
