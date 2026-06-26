namespace ClientPatcher;

/// <summary>
/// Parsed CLI options (design §1). Defaults applied by <see cref="ArgumentParser"/>.
/// </summary>
public sealed class PatchOptions
{
    /// <summary>--client (required): directory holding the client files.</summary>
    public string? ClientDir;

    /// <summary>--find (required, ASCII): operator-supplied search string.</summary>
    public string? Find;

    /// <summary>--ip (default 127.0.0.1, AC-1.2/FR-4): replacement host.</summary>
    public string Ip = "127.0.0.1";

    /// <summary>--port (default 9958, AC-1.2/FR-4): auth port.</summary>
    public int Port = 9958;

    /// <summary>--out (default &lt;ClientDir&gt;/patched): output directory.</summary>
    public string? OutDir;

    /// <summary>--help/-h: print usage and exit 0.</summary>
    public bool ShowHelp;
}
