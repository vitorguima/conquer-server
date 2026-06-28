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

            // IDEMPOTENT (bugfix): the client may send 1010/74 more than once (teleport, map
            // re-entry). Without this, a second register leaves the OLD entity stuck in the grid
            // cell (Grid.TryAdd keeps the existing uid) while Roster points at the NEW one — grid
            // and roster disagree, so the player goes invisible to others / stops seeing spawns.
            // Deregister the prior entity (idempotent no-op on first call) before re-registering.
            if (session.WorldEntity is Conquer.World.PlayerEntity prior)
                _world.Deregister(prior.MapId, prior.Uid);

            var entity = new Conquer.World.PlayerEntity(
                (uint)ch.CharacterID, session.CurrentMap, session.CurrentX, session.CurrentY,
                session, ch.Mesh, ch.Avatar, ch.Level, ch.HealthPoints, ch.Name);
            _world.GetOrAdd(entity.MapId).Register(entity);
            session.WorldEntity = entity;
            session.Uid = entity.Uid;
        }

        // GetSurroundings (Action=114, FR-6): the player asks who/what is on screen → reconcile
        // its screen (spawns everything visible, mutual 1014 for players / one-way 2030 for NPCs).
        // The client polls 114 to refresh, so force a FULL re-send (ignore dedup) to self-heal any
        // spawn the client previously missed. Same idempotent path movement uses.
        private void HandleGetSurroundings(ClientSession session)
        {
            if (session.WorldEntity is not Conquer.World.PlayerEntity b)
                return;
            SyncScreen(b, _world.GetOrAdd(b.MapId), forceResend: true);
        }

        /// <summary>Client render radius in tiles — the real screen. Entities beyond this are
        /// culled by the client, so the server must not treat them as visible (see VIEW gate).</summary>
        private const int View = 18;

        /// <summary>
        /// Full screen reconciliation (replaces the fragile incremental cell-diff). For the
        /// mover's current screen: spawn every entity not already shown (1014 for players —
        /// mutual; 2030 for NPCs — one-way) and drop every shown entity no longer on screen.
        /// Idempotent + self-healing — a missed cell transition, a jump, or a transient race is
        /// corrected on the next move. NO RemoveEntity(132) on screen-leave (the client view-culls
        /// the off-screen entity; a fresh spawn on re-enter — Visible was cleared — re-renders it);
        /// 132 is reserved for TRUE removal on disconnect (NetworkListener teardown).
        /// </summary>
        public static void SyncScreen(Conquer.World.PlayerEntity mover, Conquer.World.MapInstance mi, bool forceResend = false)
        {
            byte[]? moverSpawn = null;
            var onScreen = new System.Collections.Generic.HashSet<uint>();

            foreach (var other in mi.QueryScreen(mover.CellX, mover.CellY))
            {
                if (other.Uid == mover.Uid)
                    continue;

                // VIEW gate (bugfix): the 3x3 cell block is a coarse CANDIDATE set (entities up
                // to ~36 tiles away). The client only renders ~View tiles, so anything beyond that
                // is culled client-side. If we mark it "visible" on the block alone, the one-time
                // spawn is culled and dedup then blocks the re-send when the player walks back ->
                // the NPC never reappears. Gate on the ACTUAL tile distance so the server's Visible
                // set matches what the client actually shows; Visible clears the moment an entity
                // leaves the real screen, so re-approach re-sends it.
                if (System.Math.Abs(other.X - mover.X) > View || System.Math.Abs(other.Y - mover.Y) > View)
                    continue;

                onScreen.Add(other.Uid);

                bool newlyVisible = mover.Visible.TryAdd(other.Uid, 1);
                if (newlyVisible || forceResend)                    // dedup during movement; full re-send on 114
                    mover.Session.SendGame(EntitySpawn.For(other)); // mover sees other (1014 OR 2030)

                if (other is Conquer.World.PlayerEntity op)         // MUTUAL: the other player sees the mover
                {
                    bool otherNewly = op.Visible.TryAdd(mover.Uid, 1);
                    if (otherNewly || forceResend)
                    {
                        moverSpawn ??= SpawnEntity.Build(
                            mover.Uid, mover.Mesh, mover.Avatar, mover.Level, mover.Hp, mover.X, mover.Y, mover.Name);
                        op.Session.SendGame(moverSpawn);
                    }
                }
            }

            // Drop everything the mover currently shows that's no longer on its screen (no 132).
            foreach (var uid in mover.Visible.Keys)
            {
                if (onScreen.Contains(uid))
                    continue;
                mover.Visible.TryRemove(uid, out _);
                // For players, clear the reverse link so a future re-encounter re-spawns the mover.
                if (mi.Roster.TryGetValue(uid, out var e) && e is Conquer.World.PlayerEntity op2)
                    op2.Visible.TryRemove(mover.Uid, out _);
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

            // ADDITIVE (FR-9/FR-10): after the unchanged own-position update + self-echo, broadcast
            // the jump to the REST of the mover's 3x3 screen and apply the enter/leave diff. Skip if
            // the player isn't registered in the World yet (114 not reached).
            if (session.WorldEntity is not Conquer.World.PlayerEntity e)
                return;

            var mi = _world.GetOrAdd(e.MapId);
            mi.Move(e, x, y);   // updates grid + live position (jump can cross several cells)

            // Reuse the existing BuildJump packet (built ONCE), fanned to the screen EXCLUDING self
            // (the mover already received its own echo above).
            mi.Broadcast(e, GeneralData.BuildJump(e.Uid, x, y), includeSelf: false);

            // Reconcile the full screen after EVERY jump (VIEW-distance gate can change even
            // within a cell; a jump can also cross several cells at once). SyncScreen dedups.
            SyncScreen(e, mi);
        }
    }
}
