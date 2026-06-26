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
    }
}
