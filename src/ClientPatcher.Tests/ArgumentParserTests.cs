using ClientPatcher;
using Xunit;

namespace ClientPatcher.Tests;

/// <summary>
/// Unit tests for <see cref="ArgumentParser"/> (FR-3/FR-4, AC-1.2). Defaults,
/// full-flag parsing, help, and unknown-flag rejection.
/// </summary>
public sealed class ArgumentParserTests
{
    /// <summary>AC-1.2: omitted --ip/--port default to 127.0.0.1 / 9958.</summary>
    [Fact]
    public void Defaults_WhenIpAndPortOmitted()
    {
        var o = ArgumentParser.Parse(new[] { "--client", "/some/dir", "--find", "192.168.0.10" });

        Assert.Equal("127.0.0.1", o.Ip);
        Assert.Equal(9958, o.Port);
        Assert.Equal("/some/dir", o.ClientDir);
        Assert.Equal("192.168.0.10", o.Find);
        Assert.False(o.ShowHelp);
    }

    [Fact]
    public void AllFlags_Parsed()
    {
        var o = ArgumentParser.Parse(new[]
        {
            "--client", "/client",
            "--find", "10.0.0.1",
            "--ip", "203.0.113.9",
            "--port", "5000",
            "--out", "/out",
        });

        Assert.Equal("/client", o.ClientDir);
        Assert.Equal("10.0.0.1", o.Find);
        Assert.Equal("203.0.113.9", o.Ip);
        Assert.Equal(5000, o.Port);
        Assert.Equal("/out", o.OutDir);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public void Help_SetsShowHelp(string flag)
    {
        var o = ArgumentParser.Parse(new[] { flag });

        Assert.True(o.ShowHelp);
    }

    [Fact]
    public void UnknownFlag_Throws()
    {
        Assert.Throws<ArgParseException>(() => ArgumentParser.Parse(new[] { "--bogus" }));
    }

    [Fact]
    public void NonIntegerPort_Throws()
    {
        Assert.Throws<ArgParseException>(() =>
            ArgumentParser.Parse(new[] { "--port", "not-a-number" }));
    }

    [Fact]
    public void FlagWithoutValue_Throws()
    {
        Assert.Throws<ArgParseException>(() => ArgumentParser.Parse(new[] { "--client" }));
    }

    [Fact]
    public void UsageText_ListsAllFlags()
    {
        var usage = ArgumentParser.UsageText();

        Assert.Contains("--client", usage);
        Assert.Contains("--find", usage);
        Assert.Contains("--ip", usage);
        Assert.Contains("--port", usage);
        Assert.Contains("--out", usage);
        Assert.Contains("--help", usage);
    }
}
