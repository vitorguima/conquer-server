using System;
using System.Buffers.Binary;
using System.Text;

namespace Conquer.Packets
{
    public static class MsgConnectEx
    {
        public static byte[] Build(ulong token, string gameServerIp, ushort gamePort)
        {
            var buf = new byte[32];
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(0), 32);        // length
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(2), 1055);      // type
            BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan(4), token);     // token
            var ipBytes = Encoding.ASCII.GetBytes(gameServerIp ?? "");
            Array.Copy(ipBytes, 0, buf, 12, Math.Min(ipBytes.Length, 15));      // IP (null-padded)
            BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(28), gamePort); // port
            return buf;
        }
    }
}
