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
