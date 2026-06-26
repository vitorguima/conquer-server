using System.Text;
using ClientPatcher;
using ClientPatcher.Tests.Fixtures;
using Xunit;

namespace ClientPatcher.Tests;

/// <summary>
/// Unit tests for <see cref="PatchEngine"/> (FR-1/FR-6, AC-2.3/3.1/3.2/3.3/3.4,
/// NFR-3/6). All fixtures are synthetic in-memory byte arrays — no real assets.
/// </summary>
public sealed class PatchEngineTests
{
    private static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);

    /// <summary>
    /// AC-1.1/3.1: replace placeholder host → matched bytes == new host + null-pad;
    /// then reverse-replace the patched bytes restores the original source.
    /// </summary>
    [Fact]
    public void RoundTrip_ReplaceThenReverse_RestoresOriginal()
    {
        var source = FixtureFactory.BuildServerDat();
        var find = Ascii(FixtureFactory.PlaceholderHost);     // "192.168.0.10" (12 bytes)
        var replacement = Ascii("127.0.0.1");                  // 9 bytes (shorter)

        var (output, result, error) = PatchEngine.Apply(source, find, replacement);

        Assert.Equal(PatchError.None, error);
        Assert.Equal(1, result.MatchCount);

        var offset = result.Edits[0].Offset;
        // Matched slot == replacement bytes followed by null padding to find.Length.
        for (var i = 0; i < replacement.Length; i++)
        {
            Assert.Equal(replacement[i], output[offset + i]);
        }

        for (var i = replacement.Length; i < find.Length; i++)
        {
            Assert.Equal((byte)0x00, output[offset + i]);
        }

        // Reverse-restore: the patched slot now holds "127.0.0.1\0\0\0". Replace
        // that exact 12-byte slot back with the original host token to restore.
        var patchedSlot = new byte[find.Length];
        Array.Copy(output, offset, patchedSlot, 0, find.Length);

        var (restored, _, restoreError) = PatchEngine.Apply(output, patchedSlot, find);
        Assert.Equal(PatchError.None, restoreError);
        Assert.Equal(source, restored);
    }

    /// <summary>AC-3.3: replacement longer than find → NewLongerThanOld, no output.</summary>
    [Fact]
    public void ReplacementLongerThanFind_RejectedWithNoOutput()
    {
        var source = FixtureFactory.BuildServerDat();
        var find = Ascii("127.0.0.1");           // 9 bytes
        var replacement = Ascii("192.168.0.10"); // 12 bytes (longer)

        var (output, result, error) = PatchEngine.Apply(source, find, replacement);

        Assert.Equal(PatchError.NewLongerThanOld, error);
        Assert.Empty(output);
        Assert.Equal(0, result.MatchCount);
    }

    /// <summary>
    /// AC-3.4: shorter replacement → tail of the slot is null-padded, and the byte
    /// at offset+len(find) (the terminator just past the slot) is unchanged.
    /// </summary>
    [Fact]
    public void ShorterReplacement_NullPadsTail_TerminatorUnchanged()
    {
        var source = FixtureFactory.BuildServerDat();
        var find = Ascii(FixtureFactory.PlaceholderHost); // 12 bytes
        var replacement = Ascii("127.0.0.1");              // 9 bytes

        var (output, result, _) = PatchEngine.Apply(source, find, replacement);

        var offset = result.Edits[0].Offset;

        // Tail of the slot (bytes [replacement.Length, find.Length)) is 0x00.
        for (var i = replacement.Length; i < find.Length; i++)
        {
            Assert.Equal((byte)0x00, output[offset + i]);
        }

        // The terminator at offset+find.Length is the fixture's own NUL terminator
        // and must be unchanged vs source.
        var terminatorIndex = offset + find.Length;
        Assert.Equal(source[terminatorIndex], output[terminatorIndex]);
    }

    /// <summary>AC-3.2: find absent → FindNotFound, no output.</summary>
    [Fact]
    public void FindAbsent_ReturnsFindNotFound_NoOutput()
    {
        var source = FixtureFactory.BuildServerDat();
        var find = Ascii("203.0.113.7"); // not present in the fixture

        var (output, result, error) = PatchEngine.Apply(source, find, Ascii("10.0.0.1"));

        Assert.Equal(PatchError.FindNotFound, error);
        Assert.Empty(output);
        Assert.Equal(0, result.MatchCount);
    }

    /// <summary>NFR-3/AC-2.3: output length == source length for every success case.</summary>
    [Theory]
    [InlineData("127.0.0.1")]   // shorter than find
    [InlineData("99.99.99.999")] // exactly find.Length (12 bytes)
    public void OutputLength_AlwaysEqualsSourceLength(string replacement)
    {
        var source = FixtureFactory.BuildServerDat();
        var find = Ascii(FixtureFactory.PlaceholderHost); // 12 bytes

        var (output, _, error) = PatchEngine.Apply(source, find, Ascii(replacement));

        Assert.Equal(PatchError.None, error);
        Assert.Equal(source.Length, output.Length);
    }

    /// <summary>NFR-6: same inputs twice → byte-identical output (determinism).</summary>
    [Fact]
    public void Determinism_SameInputsTwice_ByteIdentical()
    {
        var source = FixtureFactory.BuildConquerExe();
        var find = Ascii(FixtureFactory.PlaceholderHost);
        var replacement = Ascii("127.0.0.1");

        var (first, _, _) = PatchEngine.Apply(source, find, replacement);
        var (second, _, _) = PatchEngine.Apply(source, find, replacement);

        Assert.Equal(first, second);
    }

    /// <summary>
    /// Assumption d (multi-match): two embedded placeholders → two distinct offsets
    /// reported, both replaced.
    /// </summary>
    [Fact]
    public void TwoPlaceholders_TwoOffsets_BothReplaced()
    {
        var source = FixtureFactory.BuildConquerExe(); // embeds host token twice
        var find = Ascii(FixtureFactory.PlaceholderHost);
        var replacement = Ascii("127.0.0.1");

        var (output, result, error) = PatchEngine.Apply(source, find, replacement);

        Assert.Equal(PatchError.None, error);
        Assert.Equal(2, result.MatchCount);
        Assert.NotEqual(result.Edits[0].Offset, result.Edits[1].Offset);

        foreach (var edit in result.Edits)
        {
            for (var i = 0; i < replacement.Length; i++)
            {
                Assert.Equal(replacement[i], output[edit.Offset + i]);
            }

            for (var i = replacement.Length; i < find.Length; i++)
            {
                Assert.Equal((byte)0x00, output[edit.Offset + i]);
            }
        }
    }
}
