using System;
using System.Buffers.Binary;
using Conquer.Network;

namespace Conquer.Packets
{
    /// <summary>
    /// Handles inbound MsgWalk(1005). Pure in-memory: parses UID/Direction/Mode, applies
    /// the literal 8-direction delta table, validates (length / dir / bounds), and mutates
    /// per-session live coords on <see cref="ClientSession"/>. No echo, no repo, no
    /// SendGame, no per-packet heap allocation (AD-4). Mirrors the guard-first
    /// ActionHandler/RegisterHandler shape. Additive only.
    ///
    /// Payload (2-byte length prefix already stripped; payload[0..1]=typeId 1005):
    /// UID u32 LE @2, Direction u8 @6 (valid 0..7), Mode u8 @7 (logging only).
    /// </summary>
    public sealed class WalkHandler
    {
        // index 0..7 (drop Common.cs index-8 no-move entry); CCW from due-south (+Y).
        private static readonly sbyte[] DeltaX = { 0, -1, -1, -1, 0, 1, 1, 1 };
        private static readonly sbyte[] DeltaY = { 1,  1,  0, -1, -1, -1, 0, 1 };

        private readonly Conquer.World.World _world;

        public WalkHandler(Conquer.World.World world)
        {
            _world = world;
        }

        /// <summary>
        /// Guard-first; mutate session live pos; log. No SendGame, no repo, no echo, no
        /// per-packet alloc. Never disconnects on a bad walk — log + ignore (US-3).
        /// </summary>
        public void Handle(ClientSession session, byte[] payload)
        {
            if (payload.Length < 8)
            {
                Console.WriteLine("[Game] short 1005");
                return;
            }

            if (session.Character == null || !session.PositionLoaded)
                return;

            var (_, rawDir, mode) = ParseWalk(payload);

            // The 5065 client sends the direction as a raw byte whose low 3 bits are the
            // compass direction (high bits are a rolling counter/flags). Normalize with
            // %8 — never reject on direction (any byte maps to a valid 0..7 step).
            byte dir = (byte)(rawDir % 8);

            var (nx, ny) = ComputeStep(session.CurrentX, session.CurrentY, dir);
            if (nx < 0 || ny < 0 || nx > ushort.MaxValue || ny > ushort.MaxValue)
                return; // out of bounds — ignore (trust+bound-check; client enforces collision)

            session.CurrentX = (ushort)nx;
            session.CurrentY = (ushort)ny;

            // ADDITIVE (FR-8/FR-10): after the unchanged own-position update, broadcast the
            // walk to the mover's 3x3 screen and apply the enter/leave diff. Skip if the player
            // isn't registered in the World yet (114 not reached).
            if (session.WorldEntity is not Conquer.World.PlayerEntity e)
                return;

            var mi = _world.GetOrAdd(e.MapId);
            var diff = mi.Move(e, (ushort)nx, (ushort)ny);

            // Build the outbound 1005 ONCE; fan out to the WHOLE screen incl self (other clients
            // render the walk, the mover's own client already predicted it but tolerates the echo).
            byte[] walk = Walk.BuildBroadcast(e.Uid, dir, mode);
            mi.Broadcast(e, walk, includeSelf: true);

            ActionHandler.ApplyDiff(e, diff);
        }

        /// <summary>
        /// Pure parse (no socket/DB). Caller guards <c>payload.Length &gt;= 8</c>.
        /// UID u32 LE @2, Direction u8 @6, Mode u8 @7.
        /// </summary>
        public static (uint uid, byte dir, byte mode) ParseWalk(byte[] payload)
        {
            uint uid = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(2, 4));
            byte dir = payload[6];
            byte mode = payload[7];
            return (uid, dir, mode);
        }

        /// <summary>
        /// Pure delta apply. Caller guards <paramref name="dir"/> in 0..7. Returns candidate
        /// ints for bound-check (reject, not clamp).
        /// </summary>
        public static (int nx, int ny) ComputeStep(int curX, int curY, byte dir)
        {
            return (curX + DeltaX[dir % 8], curY + DeltaY[dir % 8]);
        }
    }
}
