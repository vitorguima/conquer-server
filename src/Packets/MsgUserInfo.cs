using System;
using System.Buffers.Binary;
using System.Text;
using Conquer.Database;

namespace Conquer.Packets
{
    // MsgUserInfo (type 1006) — minimal POC layout for CO 5065 character screen.
    // Layout (variable length): [ushort length][ushort type=1006][uint charId]
    //                           [uint lookFace][ushort hair][uint silver]
    //                           [ushort level][ushort job][byte reborn]
    //                           [byte nameLen][ASCII name][byte aliasLen][ASCII alias=name]
    public static class MsgUserInfo
    {
        public static byte[] Build(DbCharacter ch)
        {
            byte[] nameBytes = Encoding.ASCII.GetBytes(ch.Name ?? "Unknown");
            // Total: 2(len)+2(type)+4(charId)+4(lookFace)+2(hair)+4(silver)+2(level)+2(job)+1(reborn)+1(nameLen)+nameBytes+1(aliasLen)+nameBytes
            int totalLen = 2 + 2 + 4 + 4 + 2 + 4 + 2 + 2 + 1 + 1 + nameBytes.Length + 1 + nameBytes.Length;
            var buf = new byte[totalLen];
            int offset = 0;

            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(offset), (ushort)totalLen); offset += 2; // length
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(offset), 1006);             offset += 2; // type
            BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(offset), (uint)ch.CharacterID); offset += 4;
            BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(offset), (uint)ch.Mesh);     offset += 4; // lookFace/Mesh
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(offset), (ushort)ch.Avatar); offset += 2; // hair/Avatar
            BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(offset), (uint)ch.Silver);   offset += 4;
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(offset), (ushort)ch.Level);  offset += 2;
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(offset), 1);                 offset += 2; // job=Trojan placeholder
            buf[offset++] = 0; // reborn
            buf[offset++] = (byte)nameBytes.Length;
            Array.Copy(nameBytes, 0, buf, offset, nameBytes.Length); offset += nameBytes.Length;
            buf[offset++] = (byte)nameBytes.Length;
            Array.Copy(nameBytes, 0, buf, offset, nameBytes.Length);

            return buf;
        }
    }
}
