using System.Text;

namespace ClientPatcher;

/// <summary>
/// Replacement payload plan (design §4). <see cref="HostBytes"/> is the ASCII
/// payload written into each matched <c>--find</c> slot.
/// </summary>
public sealed class EndpointPlan
{
    /// <summary>ASCII bytes of the new host (the replacement payload).</summary>
    public byte[] HostBytes = Array.Empty<byte>();

    /// <summary>Port value, when a co-located <c>:port</c> slot is known; else null.</summary>
    public int? Port;

    /// <summary>True only when the port was applied (co-located in --find).</summary>
    public bool PortApplied;
}

/// <summary>
/// Builds the replacement byte payload from <c>--ip</c>/<c>--port</c> (design §4,
/// FR-1/FR-2/FR-6). Honest port handling: host-only by default; the port is
/// applied ONLY when the operator's <c>--find</c> carries a co-located
/// <c>:port</c> suffix (proving co-location for this build). A game IP/port is
/// never constructed (FR-2, AC-1.3).
/// </summary>
public static class EndpointBuilder
{
    /// <summary>
    /// Default replacement = ASCII bytes of <c>--ip</c> (host only),
    /// <see cref="EndpointPlan.PortApplied"/> = false. When <c>--find</c> contains
    /// a <c>:port</c> suffix, the replacement becomes <c>&lt;ip&gt;:&lt;port&gt;</c>
    /// and <see cref="EndpointPlan.PortApplied"/> = true.
    /// </summary>
    public static EndpointPlan Build(PatchOptions o)
    {
        var ip = o.Ip;

        // Co-location signal: --find carries a ":<digits>" suffix → apply the port.
        if (HasColocatedPort(o.Find))
        {
            return new EndpointPlan
            {
                HostBytes = Encoding.ASCII.GetBytes($"{ip}:{o.Port}"),
                Port = o.Port,
                PortApplied = true,
            };
        }

        // Default: host only; port left unchanged (not co-located for this build).
        return new EndpointPlan
        {
            HostBytes = Encoding.ASCII.GetBytes(ip),
            Port = null,
            PortApplied = false,
        };
    }

    /// <summary>
    /// True when <paramref name="find"/> ends in a <c>:port</c> suffix (a colon
    /// followed by at least one digit), proving the host and port are co-located
    /// in the matched token for this build.
    /// </summary>
    private static bool HasColocatedPort(string? find)
    {
        if (string.IsNullOrEmpty(find))
        {
            return false;
        }

        var colon = find.LastIndexOf(':');
        if (colon < 0 || colon == find.Length - 1)
        {
            return false;
        }

        for (var i = colon + 1; i < find.Length; i++)
        {
            if (find[i] < '0' || find[i] > '9')
            {
                return false;
            }
        }

        return true;
    }
}
