using ClientPatcher;
using ClientPatcher.Tests.Fixtures;
using Xunit;

namespace ClientPatcher.Tests;

/// <summary>
/// Unit tests for <see cref="InputValidator"/> (FR-5/FR-10, AC-4.1/4.2/4.3/5.1).
/// The pinned AC-5.1 assertion checks the result's <c>Warnings</c> collection
/// directly (stream-independent). All fixtures are synthetic temp dirs.
/// </summary>
public sealed class InputValidatorTests : IDisposable
{
    private readonly string _clientDir;

    public InputValidatorTests()
    {
        // A valid client dir with synthetic Conquer.exe + server.dat so dir-level
        // checks pass and we can isolate the rule under test.
        _clientDir = FixtureFactory.WriteClientDir();
    }

    public void Dispose()
    {
        if (Directory.Exists(_clientDir))
        {
            Directory.Delete(_clientDir, recursive: true);
        }
    }

    private PatchOptions ValidOptions() => new()
    {
        ClientDir = _clientDir,
        Find = FixtureFactory.PlaceholderHost,
        Ip = "127.0.0.1",
        Port = 9958,
    };

    // ---- AC-4.1: IPv4 / hostname validity ----

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("192.168.0.10")]
    [InlineData("10.0.0.255")]
    [InlineData("0.0.0.0")]
    public void IsValidIpv4_AcceptsDottedQuads(string ip) => Assert.True(InputValidator.IsValidIpv4(ip));

    [Theory]
    [InlineData("999.1.1.1")]   // octet > 255
    [InlineData("1.2.3")]        // too few octets
    [InlineData("1.2.3.4.5")]    // too many octets
    [InlineData("01.2.3.4")]     // leading zero
    [InlineData("a.b.c.d")]      // non-numeric
    [InlineData("")]             // empty
    public void IsValidIpv4_RejectsMalformed(string ip) => Assert.False(InputValidator.IsValidIpv4(ip));

    [Theory]
    [InlineData("auth.example.com")]
    [InlineData("localhost")]
    [InlineData("my-host")]
    public void IsValidHostname_AcceptsRfc1123(string host) => Assert.True(InputValidator.IsValidHostname(host));

    [Theory]
    [InlineData("-bad.example.com")] // label starts with hyphen
    [InlineData("bad-.example.com")] // label ends with hyphen
    [InlineData("999.1.1.1")]        // all-numeric is IPv4 space, not a hostname
    [InlineData("")]                  // empty
    public void IsValidHostname_RejectsInvalid(string host) => Assert.False(InputValidator.IsValidHostname(host));

    [Fact]
    public void Validate_InvalidIp_ProducesError()
    {
        var o = ValidOptions();
        o.Ip = "999.1.1.1";

        var result = InputValidator.Validate(o);

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, e => e.Contains("--ip"));
    }

    [Fact]
    public void Validate_ValidHostnameIp_Ok()
    {
        var o = ValidOptions();
        o.Ip = "auth.example.com";

        var result = InputValidator.Validate(o);

        Assert.True(result.Ok);
    }

    // ---- AC-4.2: port boundaries 0, 1, 65535, 65536 ----

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(65535, true)]
    [InlineData(65536, false)]
    public void Validate_PortBoundaries(int port, bool expectOk)
    {
        var o = ValidOptions();
        o.Port = port;

        var result = InputValidator.Validate(o);

        Assert.Equal(expectOk, result.Ok);
        if (!expectOk)
        {
            Assert.Contains(result.Errors, e => e.Contains("--port"));
        }
    }

    // ---- AC-4.3: missing / empty client dir ----

    [Fact]
    public void Validate_MissingClientDir_ProducesError()
    {
        var o = ValidOptions();
        o.ClientDir = Path.Combine(Path.GetTempPath(), "client-patcher-does-not-exist-" + Guid.NewGuid().ToString("N"));

        var result = InputValidator.Validate(o);

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, e => e.Contains("--client"));
    }

    [Fact]
    public void Validate_EmptyClientDir_NoTargets_ProducesError()
    {
        var emptyDir = Path.Combine(Path.GetTempPath(), "client-patcher-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(emptyDir);
        try
        {
            var o = ValidOptions();
            o.ClientDir = emptyDir;

            var result = InputValidator.Validate(o);

            Assert.False(result.Ok);
            Assert.Contains(result.Errors, e => e.Contains("Conquer.exe") || e.Contains("server.dat"));
        }
        finally
        {
            Directory.Delete(emptyDir, recursive: true);
        }
    }

    // ---- non-ASCII --find rejection ----

    [Fact]
    public void Validate_NonAsciiFind_ProducesError()
    {
        var o = ValidOptions();
        o.Find = "café.example"; // 'é' is outside 0x20..0x7E

        var result = InputValidator.Validate(o);

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, e => e.Contains("--find") && e.Contains("ASCII"));
    }

    [Fact]
    public void Validate_EmptyFind_ProducesError()
    {
        var o = ValidOptions();
        o.Find = "";

        var result = InputValidator.Validate(o);

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, e => e.Contains("--find"));
    }

    // ---- AC-5.1 (pinned): LAN IP warning contains literal "Server.dat is damaged" ----

    [Fact]
    public void Validate_LanIp_WarningsContainServerDatIsDamagedSubstring()
    {
        var o = ValidOptions();
        o.Ip = "192.168.1.5"; // private/LAN range → warning, not error

        var result = InputValidator.Validate(o);

        // Pinned reviewer assertion: assert against the result's Warnings collection
        // (stream-independent) for the exact literal substring.
        Assert.Contains(result.Warnings, w => w.Contains("Server.dat is damaged"));
    }

    [Fact]
    public void Validate_LoopbackIp_NoLanWarning()
    {
        var o = ValidOptions();
        o.Ip = "127.0.0.1";

        var result = InputValidator.Validate(o);

        Assert.True(result.Ok);
        Assert.DoesNotContain(result.Warnings, w => w.Contains("Server.dat is damaged"));
        Assert.False(InputValidator.IsPrivateLanIpv4("127.0.0.1"));
    }
}
