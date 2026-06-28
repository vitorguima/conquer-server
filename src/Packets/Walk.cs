using System;
using System.Buffers.Binary;

namespace Conquer.Packets
{
    /// <summary>
    /// Builds the OUTBOUND <c>[1005]</c> MsgWalk frame the server fans out to a mover's
    /// 3x3 screen so OTHER players render the walk (FR-8, AD-4). This is the FRAMED wire
    /// layout (matches the reference <c>[1005] Walk.cs</c> 20-byte body) — NOT the
    /// prefix-stripped inbound payload <see cref="WalkHandler.ParseWalk"/> consumes.
    /// <list type="bullet">
    /// <item>header @0 (4): length, type = 1005 (AppendHeader writes (size-8)=12 @0)</item>
    /// <item>UID @4 (u32 = mover)</item>
    /// <item>Direction @8 (u8 = 0..7)</item>
    /// <item>Mode @9 (u8)</item>
    /// <item>Unknown1 @10 (u16 = 0)</item>
    /// </list>
    /// <para>Body length = 20. Span/BinaryPrimitives, no unsafe. SendGame appends the
    /// 8-byte seal and copies before encrypting, so a build-once buffer is safe across
    /// N recipients (AD-3).</para>
    /// </summary>
    public static class Walk
    {
        private const ushort MsgWalkType = 1005;

        public static byte[] BuildBroadcast(uint uid, byte dir, byte mode)
        {
            const int bodyLength = 20;
            var buffer = new byte[bodyLength];
            Span<byte> span = buffer;

            PacketBuilder.AppendHeader(span, (ushort)(bodyLength + 8), MsgWalkType);
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(4), uid);  // UID (mover)
            span[8] = dir;                                                 // Direction (0..7)
            span[9] = mode;                                                // Mode
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(10), 0);   // Unknown1
            // 12-19 pad (zero)

            return buffer;
        }
    }
}
