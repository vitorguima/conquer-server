using System;
using System.Buffers.Binary;
using Conquer.Database;

namespace Conquer.Packets
{
    /// <summary>
    /// Builds a CO <c>[1006]</c> HeroInformation frame — the packet that renders the
    /// player's character on the 5065 character-select screen. Port of the ORIGINAL
    /// layout <c>0b094c6:src/Redux/Packets/Game/[1006]HeroInformation.cs</c>:
    /// <list type="bullet">
    /// <item>header @0 (4): length, type = 1006</item>
    /// <item>Id @4 (uint = CharacterID)</item>
    /// <item>Lookface @8 (uint = Mesh)</item>
    /// <item>Hair @12 (ushort = Avatar)</item>
    /// <item>Money @14 (uint = Silver)</item>
    /// <item>CP @18 (uint = 0)</item>
    /// <item>Exp @22 (u64 = 0)</item>
    /// <item>Str @50, Agi @52, Vit @54, Spi @56 (ushort)</item>
    /// <item>Stats @58 (ushort = 0)</item>
    /// <item>Life @60 (ushort = HealthPoints), Mana @62 (ushort = ManaPoints)</item>
    /// <item>PK @64 (short = 0)</item>
    /// <item>Lvl @66 (byte = Level), Class @67 (byte = 0), Reborn @69 (byte = 0)</item>
    /// <item>ShowName @70 (byte = 1)</item>
    /// <item>NetStringPacker @71 = [Name, Spouse=""]</item>
    /// </list>
    /// <para>Bytes 30-49 and 68 left zero. Body length = 71 + packer.Length. The header
    /// length field is set to the body length (SendGame appends the 8-byte seal).
    /// Field mapping per AC-6.3: Mesh→Lookface, Avatar→Hair, Silver→Money;
    /// CP/Exp/Class/Spouse defaulted.</para>
    /// </summary>
    public static class HeroInformation
    {
        private const ushort MsgHeroInformationType = 1006;

        public static byte[] Build(DbCharacter ch)
        {
            var packer = new NetStringPacker(ch.Name ?? string.Empty, string.Empty); // Name, Spouse=""
            int bodyLength = 71 + packer.Length;
            var buffer = new byte[bodyLength];
            Span<byte> span = buffer;

            // Header length field = body length (SendGame adds the 8-byte seal; AppendHeader
            // writes (size-8) = bodyLength).
            PacketBuilder.AppendHeader(span, (ushort)(bodyLength + 8), MsgHeroInformationType);
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(4), (uint)ch.CharacterID);   // Id
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(8), (uint)ch.Mesh);          // Lookface
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(12), (ushort)ch.Avatar);     // Hair
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(14), (uint)ch.Silver);       // Money
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(18), 0);                     // CP
            BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(22), 0);                     // Exp
            // 30-49 unknown (zero)
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(50), (ushort)ch.Strength);   // Str
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(52), (ushort)ch.Agility);    // Agi
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(54), (ushort)ch.Vitality);   // Vit
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(56), (ushort)ch.Spirit);     // Spi
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(58), 0);                     // Stats
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(60), (ushort)ch.HealthPoints); // Life
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(62), (ushort)ch.ManaPoints); // Mana
            BinaryPrimitives.WriteInt16LittleEndian(span.Slice(64), 0);                      // PK
            span[66] = (byte)ch.Level;  // Lvl
            span[67] = 0;               // Class
            // 68 unknown (zero)
            span[69] = 0;               // Reborn
            span[70] = 1;               // ShowName
            packer.Write(span.Slice(71));

            return buffer;
        }
    }
}
