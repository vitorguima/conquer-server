using System;
using System.Buffers.Binary;
using System.Text;
using Conquer.Network;

namespace Conquer.Packets
{
    /// <summary>
    /// Handles inbound MsgTalk(1004) on the local <see cref="ChatType.Talk"/> channel.
    /// World-injected (mirror of <see cref="WalkHandler"/>): guard the payload, resolve the
    /// sender's trusted <see cref="Conquer.World.PlayerEntity"/>, bounds-check the inbound
    /// string-list, sanitize/validate/cap the message, rebuild the 1004 ONCE with a trusted
    /// <c>From = e.Name</c>, and fan it to the sender's 3x3 screen via
    /// <see cref="Conquer.World.MapInstance.Broadcast"/>. Pure in-memory, additive — no new
    /// fan-out, no persistence. Never disconnects on bad input (log + return).
    ///
    /// Payload (2-byte length prefix already stripped; payload[0..1]=typeId 1004):
    /// ChatType u16 LE @6, string-list @22 ([u8 count][u8 len][ASCII]...). Words = index 3.
    /// Min payload = 22 (fixed) + 1 (count) = 23.
    /// </summary>
    public sealed class ChatHandler
    {
        private const int MaxLen = 255;
        private const string AllUsers = "ALLUSERS";

        private readonly Conquer.World.World _world;

        public ChatHandler(Conquer.World.World world)
        {
            _world = world;
        }

        /// <summary>
        /// Guard-first; build-once; fan to the 3x3 screen. Never disconnects on bad input.
        /// </summary>
        public void Handle(ClientSession session, byte[] payload)
        {
            if (payload.Length < 23) return;                                  // length guard
            if (session.WorldEntity is not Conquer.World.PlayerEntity e)      // not in world yet
                return;

            ushort channel = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(6, 2));
            if (channel != (ushort)ChatType.Talk) return;                    // non-Talk no-op

            if (!TryReadMessage(payload, out string raw)) return;            // bounded parse
            string message = Sanitize(raw);                                  // strip < 0x20
            if (message.Length == 0) return;                                 // reject empty
            if (message[0] == '/') return;                                   // GM cmd (future)
            if (message.Length > MaxLen) message = message[..MaxLen];        // cap 255

            // Trusted From (anti-spoof). Build ONCE. Fan to the 3x3 screen.
            byte[] talk = MsgTalk.BuildChat(ChatType.Talk, e.Name, AllUsers, message);
            _world.GetOrAdd(e.MapId).Broadcast(e, talk, includeSelf: false);
        }

        /// <summary>
        /// Pure + static (test target). Walks the string-list @ payload[22]:
        /// <c>[u8 count][u8 len][ASCII]...</c>. Bounded loop (<c>i &lt; count &amp;&amp; i &lt; 8</c>);
        /// each <c>[len]</c> byte and its bytes are bound-checked against
        /// <paramref name="p"/>.Length. Returns index 3 (Words) or false. Never throws.
        /// </summary>
        public static bool TryReadMessage(byte[] p, out string msg)
        {
            msg = string.Empty;
            int o = 22;
            if (o >= p.Length) return false;
            int count = p[o++];
            for (int i = 0; i < count && i < 8; i++)
            {
                if (o >= p.Length) return false;        // bound the length byte
                int len = p[o++];
                if (o + len > p.Length) return false;   // never read past payload
                if (i == 3)
                {
                    msg = Encoding.ASCII.GetString(p, o, len);
                    return true;
                }
                o += len;
            }
            return false;                               // no Words index present
        }

        /// <summary>Drop control chars (&lt; 0x20). ASCII only.</summary>
        private static string Sanitize(string raw)
        {
            var sb = new StringBuilder(raw.Length);
            foreach (char c in raw)
            {
                if (c >= 0x20)
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }
    }
}
