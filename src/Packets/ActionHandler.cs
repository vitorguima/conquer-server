using System;
using System.Buffers.Binary;
using Conquer.Network;

namespace Conquer.Packets
{
    /// <summary>
    /// Handles inbound GeneralData(1010) (MSG_ACTION) frames. Parses the Action subtype
    /// at payload offset 20 (original body offset 22 minus the 2-byte length prefix
    /// stripped by GameConnection) and branches. The only live subtype is
    /// SetLocation(74): echo the 1010 with the char's map + X/Y, then send MapStatus(1110)
    /// to clear the post-login loading freeze. Self-spawn(1014) on InvisibleEntity(102)
    /// and GetSurroundings(114) are gated OFF (commented) fallbacks. Additive only (FR-11).
    /// </summary>
    public sealed class ActionHandler
    {
        public void HandleAction(ClientSession session, byte[] payload)
        {
            // Diagnostic (A3 confirm): dump the full inbound 1010 frame head as hex so the
            // operator can verify Action @payload[20] == 74. Stripped before final PR (FR-10).
            Console.WriteLine($"[Game] 1010 frame: {BitConverter.ToString(payload, 0, Math.Min(28, payload.Length))}");

            if (payload.Length < 22)
            {
                Console.WriteLine("[Game] short 1010");
                return;
            }

            ushort action = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(20, 2));
            switch (action)
            {
                case 74:
                    HandleSetLocation(session, payload);
                    break;
                // GATED (off by default — uncomment per live observation):
                // case 102: HandleInvisibleEntity(session); break;   // FR-7 self-1014 fallback
                // case 114: /* no-op empty surroundings */ break;    // FR-8 GetSurroundings
                default:
                    Console.WriteLine($"[Game] 1010 Action={action} unhandled — no-op");
                    break;
            }
        }

        private void HandleSetLocation(ClientSession session, byte[] payload)
        {
            var ch = session.Character;
            if (ch == null)
            {
                Console.WriteLine("[Game] 1010 SetLocation but no session character");
                return;
            }

            Console.WriteLine($"[Game] SetLocation -> map={ch.MapID} x={ch.X} y={ch.Y}");

            session.SendGame(GeneralData.BuildSetLocation(
                (uint)ch.CharacterID, (uint)ch.MapID, (ushort)ch.X, (ushort)ch.Y));
            session.SendGame(MapStatus.Build((uint)ch.MapID));
        }
    }
}
