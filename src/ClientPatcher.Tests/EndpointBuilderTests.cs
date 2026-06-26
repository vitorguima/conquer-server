using System.Text;
using ClientPatcher;
using Xunit;

namespace ClientPatcher.Tests;

/// <summary>
/// Unit tests for <see cref="EndpointBuilder"/> (FR-1/FR-2/FR-6, AC-1.1/1.3).
/// Host-only by default; port applied only when --find carries a co-located
/// :port suffix. The replacement payload is always derived solely from
/// --ip/--port — never a game IP (AC-1.3).
/// </summary>
public sealed class EndpointBuilderTests
{
    private static string Ascii(byte[] bytes) => Encoding.ASCII.GetString(bytes);

    [Fact]
    public void HostOnly_PortNotApplied_ReplacementIsHostBytes()
    {
        var o = new PatchOptions
        {
            Find = "192.168.0.10", // no :port suffix
            Ip = "127.0.0.1",
            Port = 9958,
        };

        var plan = EndpointBuilder.Build(o);

        Assert.False(plan.PortApplied);
        Assert.Null(plan.Port);
        Assert.Equal("127.0.0.1", Ascii(plan.HostBytes));
        Assert.Equal(Encoding.ASCII.GetBytes("127.0.0.1"), plan.HostBytes);
    }

    [Fact]
    public void ColocatedPort_PortApplied_ReplacementIsIpColonPort()
    {
        var o = new PatchOptions
        {
            Find = "192.168.0.10:9000", // co-located :port suffix
            Ip = "127.0.0.1",
            Port = 9958,
        };

        var plan = EndpointBuilder.Build(o);

        Assert.True(plan.PortApplied);
        Assert.Equal(9958, plan.Port);
        Assert.Equal("127.0.0.1:9958", Ascii(plan.HostBytes));
    }

    /// <summary>
    /// AC-1.3: the replacement payload contains ONLY bytes derived from --ip/--port.
    /// It never contains a game IP (here, the placeholder host from --find). Proven
    /// by asserting the payload equals exactly the expected ip(:port) ASCII bytes.
    /// </summary>
    [Theory]
    [InlineData("192.168.0.10", "127.0.0.1", 9958, "127.0.0.1")]
    [InlineData("192.168.0.10:1234", "10.0.0.5", 7777, "10.0.0.5:7777")]
    public void Payload_ContainsOnlyIpPortDerivedBytes_NeverGameIp(
        string find, string ip, int port, string expectedPayload)
    {
        var o = new PatchOptions { Find = find, Ip = ip, Port = port };

        var plan = EndpointBuilder.Build(o);

        // Exactly the --ip/--port-derived payload, nothing else.
        Assert.Equal(Encoding.ASCII.GetBytes(expectedPayload), plan.HostBytes);

        // The game-IP host portion of --find must NOT appear in the payload.
        Assert.DoesNotContain("192.168.0.10", Ascii(plan.HostBytes));
    }
}
