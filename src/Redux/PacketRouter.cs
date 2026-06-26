using System;
using System.Buffers.Binary;
using System.IO;
using System.Net.Sockets;
using Conquer.Database;
using Conquer.Network;
using Conquer.Packets;
using Microsoft.Extensions.Configuration;

namespace Redux
{
    public sealed class PacketRouter
    {
        private readonly AuthHandler _auth;
        private readonly GameHandler _game;

        public PacketRouter(AccountRepository accounts, CharacterRepository characters, IConfiguration config)
        {
            _auth  = new AuthHandler(accounts, config);
            _game  = new GameHandler(characters, config);
        }

        public (ushort typeId, byte[] payload) ReadPacket(NetworkStream stream)
        {
            // Read 2-byte length prefix
            Span<byte> lenBuf = stackalloc byte[2];
            ReadExact(stream, lenBuf);
            ushort totalLen = BinaryPrimitives.ReadUInt16LittleEndian(lenBuf);

            if (totalLen < 4 || totalLen > 8192)
                throw new IOException($"Invalid packet length {totalLen}");

            // Read the rest of the packet (totalLen - 2 bytes after the length field)
            var payload = new byte[totalLen - 2];
            ReadExact(stream, payload.AsSpan());
            return (BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(0, 2)), payload);
        }

        public void Dispatch(ClientSession session, ushort typeId, byte[] payload)
        {
            if (session.IsAuthenticated)
                session.Cipher.Decrypt(payload, 0, payload.Length);

            // Re-read typeId from decrypted payload if authenticated
            ushort resolvedType = session.IsAuthenticated
                ? BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(0, 2))
                : typeId;

            switch (resolvedType)
            {
                case 1051:
                    _auth.Handle(session, payload);
                    break;
                case 1052:
                    _game.Handle(session, payload);
                    break;
                default:
                    Console.WriteLine($"[Warn] Unknown typeId={resolvedType}");
                    break;
            }
        }

        private static void ReadExact(Stream stream, Span<byte> buf)
        {
            int offset = 0;
            while (offset < buf.Length)
            {
                int n = stream.Read(buf[offset..]);
                if (n == 0) throw new EndOfStreamException();
                offset += n;
            }
        }
    }
}
