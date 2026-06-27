using System;
using System.Buffers.Binary;

namespace Conquer.Packets
{
    /// <summary>
    /// Builds a CO <c>[1010]</c> GeneralData (MSG_ACTION) frame. Used here for the
    /// SetLocation(74) echo that tells the client which map + X/Y to jump to, clearing
    /// the post-login loading freeze. Port of the ORIGINAL layout
    /// <c>[1010] GeneralData.cs</c> echo (GameServer.cs SetLocation reply):
    /// <list type="bullet">
    /// <item>header @0 (4): length, type = 1010</item>
    /// <item>Timestamp @4 (u32 = 0)</item>
    /// <item>UID @8 (u32 = CharacterID)</item>
    /// <item>Data1 @12 (u32 = MapID)</item>
    /// <item>Data2 @16 (u32 = (Y&lt;&lt;16) | (X &amp; 0xFFFF)) — Data2Low=X, Data2High=Y</item>
    /// <item>Data3 @20 (u16 = 0)</item>
    /// <item>Action @22 (u16 = 74 SetLocation)</item>
    /// <item>pad @24-27 = 0</item>
    /// </list>
    /// <para>Body length = 28. Header length field = body length (SendGame appends the
    /// 8-byte seal; AppendHeader writes (size-8) = 28).</para>
    /// </summary>
    public static class GeneralData
    {
        private const ushort MsgGeneralDataType = 1010;
        private const ushort SetLocationAction = 74;
        private const ushort JumpAction = 133;
        private const ushort RemoveEntityAction = 132;

        public static byte[] BuildSetLocation(uint uid, uint mapId, ushort x, ushort y)
        {
            const int bodyLength = 28;
            var buffer = new byte[bodyLength];
            Span<byte> span = buffer;

            PacketBuilder.AppendHeader(span, (ushort)(bodyLength + 8), MsgGeneralDataType);
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(4), 0);                          // Timestamp
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(8), uid);                        // UID
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(12), mapId);                     // Data1 = MapID
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(16),
                (uint)(((uint)y << 16) | (x & 0xFFFFu)));                                        // Data2 = (Y<<16)|X
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(20), 0);                         // Data3
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(22), SetLocationAction);         // Action = 74
            // 24-27 pad (zero)

            return buffer;
        }

        /// <summary>
        /// Builds the jump echo (Action=133): same 1010 layout, the jump target packed in
        /// Data1 (Data1Low=X, Data1High=Y) exactly as the client sent it. The original
        /// re-broadcasts the jump packet including self (SendToScreen(..., true)), so the
        /// jumping client needs this echo for the jump to complete.
        /// </summary>
        public static byte[] BuildJump(uint uid, ushort x, ushort y)
        {
            const int bodyLength = 28;
            var buffer = new byte[bodyLength];
            Span<byte> span = buffer;

            PacketBuilder.AppendHeader(span, (ushort)(bodyLength + 8), MsgGeneralDataType);
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(4), 0);                          // Timestamp
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(8), uid);                        // UID
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(12),
                (uint)(((uint)y << 16) | (x & 0xFFFFu)));                                        // Data1 = (Y<<16)|X
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(16), 0);                         // Data2
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(20), 0);                         // Data3
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(22), JumpAction);                // Action = 133
            // 24-27 pad (zero)

            return buffer;
        }

        /// <summary>
        /// Builds the despawn frame (Action=132 RemoveEntity, FR-12): same 1010 layout as
        /// <see cref="BuildJump"/> with NO coordinates — just the leaver's UID. Broadcast to a
        /// player's last screen on disconnect, or to a viewer when the entity scrolls off-screen
        /// (the enter/leave diff). Body length = 28 (AppendHeader(36) writes 28 @0).
        /// </summary>
        public static byte[] BuildRemoveEntity(uint uid)
        {
            const int bodyLength = 28;
            var buffer = new byte[bodyLength];
            Span<byte> span = buffer;

            PacketBuilder.AppendHeader(span, (ushort)(bodyLength + 8), MsgGeneralDataType);
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(4), 0);                          // Timestamp
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(8), uid);                        // UID (despawn)
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(12), 0);                         // Data1
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(16), 0);                         // Data2
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(20), 0);                         // Data3
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(22), RemoveEntityAction);        // Action = 132
            // 24-27 pad (zero)

            return buffer;
        }
    }
}
