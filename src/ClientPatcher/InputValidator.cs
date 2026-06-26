namespace ClientPatcher;

/// <summary>Validation outcome (design §2): errors block the run; warnings do not.</summary>
public sealed class ValidationResult
{
    public bool Ok;
    public IReadOnlyList<string> Errors = Array.Empty<string>();
    public IReadOnlyList<string> Warnings = Array.Empty<string>();
}

/// <summary>
/// Validates <see cref="PatchOptions"/> BEFORE any file is touched (design §2,
/// FR-5/FR-10, AC-4.1/4.2/4.3/5.1). Collects all errors and returns a result;
/// never throws. A LAN/private IP yields a WARNING (not an error).
/// </summary>
public static class InputValidator
{
    private static readonly string[] CanonicalTargets = { "Conquer.exe", "server.dat" };

    /// <summary>
    /// Validate <paramref name="o"/>. Rules: <c>--ip</c> valid IPv4 or hostname;
    /// <c>--port</c> in 1..65535; <c>--client</c> dir exists and contains ≥1
    /// target file; <c>--find</c> non-empty pure ASCII (0x20..0x7E). A LAN IP
    /// adds a warning containing the literal substring <c>Server.dat is damaged</c>.
    /// </summary>
    public static ValidationResult Validate(PatchOptions o)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        // --ip: valid IPv4 or hostname.
        if (string.IsNullOrEmpty(o.Ip) || (!IsValidIpv4(o.Ip) && !IsValidHostname(o.Ip)))
        {
            errors.Add($"--ip '{o.Ip}' is not a valid IPv4 or hostname");
        }
        else if (IsPrivateLanIpv4(o.Ip))
        {
            warnings.Add(
                $"{o.Ip} is a LAN/private IP. The client may reject it with " +
                "\"Server.dat is damaged\" and may require an additional manual exe " +
                "hex patch (not applied by v1). Prefer loopback 127.0.0.1.");
        }

        // --port: 1..65535.
        if (o.Port < 1 || o.Port > 65535)
        {
            errors.Add($"--port {o.Port} must be 1..65535");
        }

        // --client: dir exists with at least one target file.
        if (string.IsNullOrEmpty(o.ClientDir) || !Directory.Exists(o.ClientDir))
        {
            errors.Add("--client dir is missing or does not exist");
        }
        else
        {
            if (!HasAnyTarget(o.ClientDir))
            {
                errors.Add("no Conquer.exe or server.dat found in --client dir");
            }

            // Source-safety (Layer-3 advisory): the resolved output dir must not be
            // the client/source dir itself, or patched output and backups would
            // overwrite the operator's original Conquer.exe/server.dat. The default
            // <client>/patched subdir is fine; only an --out pointing AT --client
            // (or a path that normalizes to it) is rejected.
            if (!string.IsNullOrEmpty(o.OutDir) && OutDirEqualsClientDir(o.ClientDir, o.OutDir))
            {
                errors.Add(
                    "--out must not be the --client dir itself; patched output and " +
                    "backups would overwrite the original source files. Use a separate " +
                    "output directory (default: <client>/patched).");
            }
        }

        // --find: non-empty pure ASCII (0x20..0x7E).
        if (string.IsNullOrEmpty(o.Find))
        {
            errors.Add("--find must not be empty");
        }
        else if (!IsPureAscii(o.Find))
        {
            errors.Add("--find must be ASCII (v1 limitation; wide/UTF-16 unsupported)");
        }

        return new ValidationResult
        {
            Ok = errors.Count == 0,
            Errors = errors,
            Warnings = warnings,
        };
    }

    /// <summary>True when <paramref name="s"/> is a dotted-quad IPv4 (octets 0..255).</summary>
    public static bool IsValidIpv4(string s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return false;
        }

        var parts = s.Split('.');
        if (parts.Length != 4)
        {
            return false;
        }

        foreach (var part in parts)
        {
            if (part.Length == 0 || part.Length > 3)
            {
                return false;
            }

            foreach (var c in part)
            {
                if (c < '0' || c > '9')
                {
                    return false;
                }
            }

            // Reject leading-zero weirdness ("01", "00") — only a lone "0" is a
            // valid zero octet. Avoids ambiguous/octal-looking quads.
            if (part.Length > 1 && part[0] == '0')
            {
                return false;
            }

            if (!int.TryParse(part, out var octet) || octet < 0 || octet > 255)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>True when <paramref name="s"/> is a valid RFC-1123 hostname.</summary>
    public static bool IsValidHostname(string s)
    {
        if (string.IsNullOrEmpty(s) || s.Length > 253)
        {
            return false;
        }

        var labels = s.Split('.');
        var allNumeric = true;
        foreach (var label in labels)
        {
            if (label.Length == 0 || label.Length > 63)
            {
                return false;
            }

            if (label[0] == '-' || label[label.Length - 1] == '-')
            {
                return false;
            }

            foreach (var c in label)
            {
                var ok = (c >= 'a' && c <= 'z')
                    || (c >= 'A' && c <= 'Z')
                    || (c >= '0' && c <= '9')
                    || c == '-';
                if (!ok)
                {
                    return false;
                }

                if (c < '0' || c > '9')
                {
                    allNumeric = false;
                }
            }
        }

        // RFC-1123: a hostname must not be entirely numeric (that space belongs to
        // IPv4). Such strings are rejected here so an invalid dotted-quad like
        // "999.1.1.1" cannot sneak through as a "hostname".
        if (allNumeric)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// True for private/LAN IPv4 ranges (10.*, 172.16–31.*, 192.168.*). Loopback
    /// (127.*) is excluded — it is the recommended value, not a warning case.
    /// </summary>
    public static bool IsPrivateLanIpv4(string s)
    {
        if (!IsValidIpv4(s))
        {
            return false;
        }

        var parts = s.Split('.');
        var a = int.Parse(parts[0]);
        var b = int.Parse(parts[1]);

        if (a == 10)
        {
            return true;
        }

        if (a == 192 && b == 168)
        {
            return true;
        }

        if (a == 172 && b >= 16 && b <= 31)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// True when <paramref name="outDir"/> resolves to the same directory as
    /// <paramref name="clientDir"/> (after full-path normalization, ignoring a
    /// trailing separator). Used to block <c>--out</c> from overwriting source.
    /// </summary>
    private static bool OutDirEqualsClientDir(string clientDir, string outDir)
    {
        string Normalize(string p)
        {
            var full = Path.GetFullPath(p);
            return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return string.Equals(Normalize(clientDir), Normalize(outDir), comparison);
    }

    private static bool HasAnyTarget(string clientDir)
    {
        foreach (var entry in Directory.GetFiles(clientDir))
        {
            var fileName = Path.GetFileName(entry);
            foreach (var canonical in CanonicalTargets)
            {
                if (string.Equals(fileName, canonical, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsPureAscii(string s)
    {
        foreach (var c in s)
        {
            if (c < 0x20 || c > 0x7E)
            {
                return false;
            }
        }

        return true;
    }
}
