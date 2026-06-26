using System;
using System.Buffers.Binary;
using System.Text;

namespace Conquer.Packets
{
    public static class MsgConnectEx
    {
        public static byte[] Build(ulong token, string gameServerIp, ushort gamePort)
        {
            // Layout matches Comet's MsgConnectEx (1055), 34 bytes total:
            // len(2) type(2) token(8) ip[16] port(4) padding(2)
            var buf = new byte[34];
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(0), 34);        // length (incl. trailing pad)
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(2), 1055);      // type
            BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan(4), token);     // token (ulong)
            var ipBytes = Encoding.ASCII.GetBytes(gameServerIp ?? "");
            Array.Copy(ipBytes, 0, buf, 12, Math.Min(ipBytes.Length, 15));      // IP, 16-byte field @12, null-padded
            BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(28), gamePort); // port @28
            // buf[32..34] left as 0 (trailing padding)
            return buf;
        }
    }
}
