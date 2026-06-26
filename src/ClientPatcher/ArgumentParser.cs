namespace ClientPatcher;

/// <summary>Thrown on malformed/unknown flags (→ <see cref="ExitCode.Validation"/>).</summary>
public sealed class ArgParseException : Exception
{
    public ArgParseException(string message) : base(message)
    {
    }
}

/// <summary>
/// Hand-rolled CLI parser (design §1, FR-3/FR-4). Zero NuGet. Recognizes
/// <c>--client --find --ip --port --out --help/-h</c>; unknown flag throws
/// <see cref="ArgParseException"/>. POC: minimal validation (hardened in Phase 2).
/// </summary>
public static class ArgumentParser
{
    /// <summary>
    /// Parse <paramref name="args"/> into <see cref="PatchOptions"/>, applying
    /// defaults (<c>--ip 127.0.0.1</c>, <c>--port 9958</c>). Throws
    /// <see cref="ArgParseException"/> on unknown flags, missing values, or a
    /// non-integer <c>--port</c>.
    /// </summary>
    public static PatchOptions Parse(string[] args)
    {
        var o = new PatchOptions();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--help":
                case "-h":
                    o.ShowHelp = true;
                    break;
                case "--client":
                    o.ClientDir = TakeValue(args, ref i, "--client");
                    break;
                case "--find":
                    o.Find = TakeValue(args, ref i, "--find");
                    break;
                case "--ip":
                    o.Ip = TakeValue(args, ref i, "--ip");
                    break;
                case "--out":
                    o.OutDir = TakeValue(args, ref i, "--out");
                    break;
                case "--port":
                    var raw = TakeValue(args, ref i, "--port");
                    if (!int.TryParse(raw, out var port))
                    {
                        throw new ArgParseException($"--port '{raw}' is not an integer");
                    }

                    o.Port = port;
                    break;
                default:
                    throw new ArgParseException($"unknown flag '{arg}'");
            }
        }

        return o;
    }

    /// <summary>Usage text listing every flag (printed for <c>--help</c>).</summary>
    public static string UsageText()
    {
        return string.Join(
            Environment.NewLine,
            "ClientPatcher — repoint a CO 5065 client's auth endpoint (auth-only byte rewriter).",
            string.Empty,
            "Usage:",
            "  ClientPatcher --client <dir> --find <string> [--ip <host>] [--port <n>] [--out <dir>]",
            string.Empty,
            "Flags:",
            "  --client <dir>     Directory holding the client files (Conquer.exe / server.dat). Required.",
            "  --find <string>    ASCII search string to replace (the current auth host). Required.",
            "  --ip <host>        Replacement host. Default: 127.0.0.1.",
            "  --port <n>         Auth port (1..65535). Default: 9958.",
            "  --out <dir>        Output directory. Default: <client>/patched.",
            "  --help, -h         Print this help and exit.");
    }

    private static string TakeValue(string[] args, ref int i, string flag)
    {
        if (i + 1 >= args.Length)
        {
            throw new ArgParseException($"{flag} requires a value");
        }

        return args[++i];
    }
}
