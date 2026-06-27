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
        private readonly Conquer.World.World _world;

        public ActionHandler(Conquer.World.World world)
        {
            _world = world;
        }

        public void HandleAction(ClientSession session, byte[] payload)
        {
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
                case 133:
                    HandleJump(session, payload);
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

        // Jump (Action=133): the client sends the target packed in Data1 — Data1Low=X
        // (payload @10), Data1High=Y (payload @12). Update the live in-memory position
        // (same store WalkHandler uses → persists via the disconnect flush) and echo the
        // jump back so it completes (the original includes self in SendToScreen). No
        // collision check (out of scope — trust the client, the client enforces its own).
        private void HandleJump(ClientSession session, byte[] payload)
        {
            var ch = session.Character;
            if (ch == null || !session.PositionLoaded)
                return;

            ushort x = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(10, 2));
            ushort y = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(12, 2));

            session.CurrentX = x;
            session.CurrentY = y;
            session.SendGame(GeneralData.BuildJump((uint)ch.CharacterID, x, y));
        }
    }
}
