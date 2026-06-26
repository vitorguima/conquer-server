using System.Text;
using ClientPatcher;
using ClientPatcher.Tests.Fixtures;
using Xunit;

namespace ClientPatcher.Tests;

/// <summary>
/// In-memory integration test of the full pipeline (FR-1/FR-8/FR-9,
/// AC-2.1/2.2/2.4) against synthetic fixtures written to a temp dir. Drives the
/// same orchestration path as <c>Program.Main</c> via <see cref="Patcher.Run"/>.
/// </summary>
public sealed class IntegrationTests : IDisposable
{
    private readonly string _clientDir;

    public IntegrationTests()
    {
        _clientDir = FixtureFactory.WriteClientDir();
    }

    public void Dispose()
    {
        if (Directory.Exists(_clientDir))
        {
            Directory.Delete(_clientDir, recursive: true);
        }
    }

    [Fact]
    public void FullPipeline_PatchesFixtures_BackupAndReport_SourceUntouched()
    {
        // Pristine copies of the source bytes to prove the source is never mutated.
        var conquerSrc = Path.Combine(_clientDir, "Conquer.exe");
        var serverSrc = Path.Combine(_clientDir, "server.dat");
        var conquerOriginal = File.ReadAllBytes(conquerSrc);
        var serverOriginal = File.ReadAllBytes(serverSrc);

        var options = new PatchOptions
        {
            ClientDir = _clientDir,
            Find = FixtureFactory.PlaceholderHost,
            Ip = "127.0.0.1",
            Port = 9958,
        };

        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exit = Patcher.Run(options, stdout, stderr);

        // Exit 0 (success).
        Assert.Equal(0, exit);

        var patchedDir = Path.Combine(_clientDir, "patched");

        // Backups written from the original-copy bytes (AC-2.1/2.2).
        var conquerBak = Path.Combine(patchedDir, "Conquer.exe.bak");
        var serverBak = Path.Combine(patchedDir, "server.dat.bak");
        Assert.True(File.Exists(conquerBak));
        Assert.True(File.Exists(serverBak));
        Assert.Equal(conquerOriginal, File.ReadAllBytes(conquerBak));
        Assert.Equal(serverOriginal, File.ReadAllBytes(serverBak));

        // Patched outputs exist with length == original (NFR-3, AC-2.3).
        var conquerPatched = Path.Combine(patchedDir, "Conquer.exe");
        var serverPatched = Path.Combine(patchedDir, "server.dat");
        var conquerPatchedBytes = File.ReadAllBytes(conquerPatched);
        var serverPatchedBytes = File.ReadAllBytes(serverPatched);
        Assert.Equal(conquerOriginal.Length, conquerPatchedBytes.Length);
        Assert.Equal(serverOriginal.Length, serverPatchedBytes.Length);

        // Patched bytes actually changed (host token was rewritten).
        Assert.NotEqual(conquerOriginal, conquerPatchedBytes);
        Assert.NotEqual(serverOriginal, serverPatchedBytes);

        // Source temp files are byte-unchanged (AC-2.1/2.2): the patcher never
        // opens the source for write.
        Assert.Equal(conquerOriginal, File.ReadAllBytes(conquerSrc));
        Assert.Equal(serverOriginal, File.ReadAllBytes(serverSrc));

        // Report substrings present (AC-2.4): "File:", an offset, "matches".
        var report = stdout.ToString();
        Assert.Contains("File:", report);
        Assert.Contains("offset", report);
        Assert.Contains("matches", report);

        // The new host appears in the patched output; the old placeholder host
        // token no longer appears intact.
        var patchedText = Encoding.ASCII.GetString(serverPatchedBytes);
        Assert.Contains("127.0.0.1", patchedText);
        Assert.DoesNotContain(FixtureFactory.PlaceholderHost, patchedText);
    }

    [Fact]
    public void FullPipeline_FindAbsent_ReturnsNotFound_NoOutput()
    {
        var options = new PatchOptions
        {
            ClientDir = _clientDir,
            Find = "203.0.113.77", // not present in fixtures
            Ip = "127.0.0.1",
            Port = 9958,
        };

        var exit = Patcher.Run(options, new StringWriter(), new StringWriter());

        // Not-found → exit 3, and nothing was written to the patched dir.
        Assert.Equal(3, exit);
        Assert.False(Directory.Exists(Path.Combine(_clientDir, "patched")));
    }
}
