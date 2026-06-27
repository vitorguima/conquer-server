using System;
using System.Buffers.Binary;
using Conquer.Database;

namespace Conquer.Packets
{
    /// <summary>
    /// Builds a CO <c>[1014]</c> SpawnEntity frame for the player's OWN character
    /// (stand-still self-spawn). This is a GATED fallback (FR-7): built but NOT wired
    /// into <see cref="ActionHandler"/> by default — only enabled live if the body
    /// fails to render after the SetLocation echo + 1110 (A2). Port of the ORIGINAL
    /// layout <c>[1014] SpawnEntity.cs</c>:
    /// <list type="bullet">
    /// <item>header @0 (4): length, type = 1014</item>
    /// <item>UID @4 (u32 = CharacterID)</item>
    /// <item>Lookface @8 (u32 = Mesh)</item>
    /// <item>Life @48 (u16 = HealthPoints)</item>
    /// <item>Level @50 (u16 = Level)</item>
    /// <item>PositionX @52 (u16 = X)</item>
    /// <item>PositionY @54 (u16 = Y)</item>
    /// <item>Hair @56 (u16 = Avatar)</item>
    /// <item>Direction @58 (u8 = 0)</item>
    /// <item>Action @59 (u8 = 0 stand)</item>
    /// <item>RebornCount @60 (u8 = 0)</item>
    /// <item>Level @62 (u16 = Level)</item>
    /// <item>Names @90 (NetStringPacker = [Name])</item>
    /// </list>
    /// <para>Body length = 100 + encoded name. Bytes 12-47, 61, 63-89 are zero (no
    /// equipment/guild/status/nobility in the minimal self-spawn). Header length field =
    /// body length (SendGame appends the 8-byte seal). Span/BinaryPrimitives, no unsafe.</para>
    /// </summary>
    public static class SpawnEntity
    {
        private const ushort MsgSpawnEntityType = 1014;
        private const int NameOffset = 90;

        public static byte[] BuildSelf(DbCharacter ch)
        {
            if (ch == null) throw new ArgumentNullException(nameof(ch));

            var names = new NetStringPacker(ch.Name ?? string.Empty);
            int bodyLength = NameOffset + names.Length;

            var buffer = new byte[bodyLength];
            Span<byte> span = buffer;

            PacketBuilder.AppendHeader(span, (ushort)(bodyLength + 8), MsgSpawnEntityType);
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(4), (uint)ch.CharacterID);   // UID
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(8), (uint)ch.Mesh);          // Lookface
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(48), (ushort)ch.HealthPoints); // Life
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(50), (ushort)ch.Level);      // Level
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(52), (ushort)ch.X);          // PositionX
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(54), (ushort)ch.Y);          // PositionY
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(56), (ushort)ch.Avatar);     // Hair
            span[58] = 0;                                                                    // Direction
            span[59] = 0;                                                                    // Action = stand
            span[60] = 0;                                                                    // RebornCount
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(62), (ushort)ch.Level);      // Level
            names.Write(span.Slice(NameOffset));                                             // Names = [Name]

            return buffer;
        }
    }
}
