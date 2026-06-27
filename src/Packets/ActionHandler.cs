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
                case 114:
                    HandleGetSurroundings(session);
                    break;
                // GATED (off by default — uncomment per live observation):
                // case 102: HandleInvisibleEntity(session); break;   // FR-7 self-1014 fallback
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

            // ADDITIVE (FR-7): after the unchanged echo + MapStatus, register the player into
            // the World at its LIVE position so 114/movement can see/broadcast it. Guard on a
            // loaded position; back-ref the entity on the session for teardown + later hooks.
            if (!session.PositionLoaded)
                return;

            var entity = new Conquer.World.PlayerEntity(
                (uint)ch.CharacterID, session.CurrentMap, session.CurrentX, session.CurrentY,
                session, ch.Mesh, ch.Avatar, ch.Level, ch.HealthPoints, ch.Name);
            _world.GetOrAdd(entity.MapId).Register(entity);
            session.WorldEntity = entity;
            session.Uid = entity.Uid;
        }

        // GetSurroundings (Action=114, FR-6): the newcomer B asks who is on screen. Resolve B's
        // entity, query its 3x3 cell block, and for every OTHER on-screen player A send a MUTUAL
        // 1014: B <- A's 1014 (A's LIVE coords) AND A <- B's 1014. Seed both Visible sets so the
        // enter/leave diff (Phase 3) is consistent. B's own 1014 is built ONCE and reused for the
        // fan-out (build-once, AD-4). Empty screen -> sends nothing, no error.
        private void HandleGetSurroundings(ClientSession session)
        {
            if (session.WorldEntity is not Conquer.World.PlayerEntity b)
                return;

            byte[] bSpawn = SpawnEntity.Build(b.Uid, b.Mesh, b.Avatar, b.Level, b.Hp, b.X, b.Y, b.Name);

            foreach (var a in _world.GetOrAdd(b.MapId).QueryScreen(b.CellX, b.CellY))
            {
                if (a.Uid == b.Uid)
                    continue;

                session.SendGame(SpawnEntity.Build(a.Uid, a.Mesh, a.Avatar, a.Level, a.Hp, a.X, a.Y, a.Name));
                a.Session.SendGame(bSpawn);

                b.Visible[a.Uid] = 1;
                a.Visible[b.Uid] = 1;
            }
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
