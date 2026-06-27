using System;
using System.Buffers.Binary;

namespace Conquer.Packets
{
    /// <summary>
    /// Builds a CO <c>[2030]</c> SpawnNpc (MSG_NPC_SPAWN) frame for an on-screen NPC.
    /// Port of the ORIGINAL <c>[2030] SpawnNpc.cs</c> layout:
    /// <list type="bullet">
    /// <item>header @0 (4): length, type = 2030</item>
    /// <item>UID @4 (u32)</item>
    /// <item>X @8 (u16 = tile X)</item>
    /// <item>Y @10 (u16 = tile Y)</item>
    /// <item>Mesh @12 (u16 = lookface/model)</item>
    /// <item>Type @14 (u16 = NpcType; Task=2 = clickable dialog)</item>
    /// <item>Unknown1 @16 (u32 = 0)</item>
    /// <item>Name @18 (NetStringPacker = [Name])</item>
    /// </list>
    /// <para>Body length = 18 + encoded name (the original over-allocates a fixed
    /// <c>byte[48]</c>). Header length field = body length (SendGame appends the 8-byte
    /// seal). Span/BinaryPrimitives, no unsafe.</para>
    /// </summary>
    public static class SpawnNpc
    {
        private const ushort MsgNpcSpawnType = 2030;
        private const int NameOffset = 18;

        public static byte[] Build(uint uid, ushort mesh, ushort type, ushort x, ushort y, string name)
        {
            var names = new NetStringPacker(name ?? string.Empty);
            int bodyLength = NameOffset + names.Length;

            var buffer = new byte[bodyLength];
            Span<byte> span = buffer;

            PacketBuilder.AppendHeader(span, (ushort)(bodyLength + 8), MsgNpcSpawnType);
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(4), uid);     // UID
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(8), x);       // X
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(10), y);      // Y
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(12), mesh);   // Mesh (lookface)
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(14), type);   // Type (NpcType)
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(16), 0u);     // Unknown1
            names.Write(span.Slice(NameOffset));                             // Name

            return buffer;
        }
    }
}
