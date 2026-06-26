using System;
using System.Buffers.Binary;

namespace Conquer.Packets
{
    /// <summary>
    /// Builds a CO <c>[1110]</c> MapStatus frame — sent after the SetLocation echo to
    /// finalize the client's map entry. Port of the ORIGINAL layout
    /// <c>[1110] MapStatus.cs</c>. net8 has no DbMap loaded, so this is minimal:
    /// <list type="bullet">
    /// <item>header @0 (4): length, type = 1110</item>
    /// <item>UID @4 (u32 = MapID)</item>
    /// <item>ID @8 (u32 = MapID)</item>
    /// <item>Type @12 (u32 = 0)</item>
    /// </list>
    /// <para>Body length = 16. Header length field = body length (SendGame appends the
    /// 8-byte seal; AppendHeader writes (size-8) = 16).</para>
    /// </summary>
    public static class MapStatus
    {
        private const ushort MsgMapStatusType = 1110;

        public static byte[] Build(uint mapId)
        {
            const int bodyLength = 16;
            var buffer = new byte[bodyLength];
            Span<byte> span = buffer;

            PacketBuilder.AppendHeader(span, (ushort)(bodyLength + 8), MsgMapStatusType);
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(4), mapId);   // UID = MapID
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(8), mapId);   // ID  = MapID
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(12), 0);      // Type = 0

            return buffer;
        }
    }
}
