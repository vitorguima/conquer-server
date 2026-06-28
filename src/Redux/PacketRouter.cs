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
        private readonly Conquer.Packets.ActionHandler _action;
        private readonly Conquer.Packets.RegisterHandler _register;
        private readonly Conquer.Packets.WalkHandler _walk;
        private readonly Conquer.Packets.ChatHandler _chat;
        private readonly Conquer.Packets.NpcHandler _npc;

        public PacketRouter(AccountRepository accounts, CharacterRepository characters, IConfiguration config, Conquer.World.World world)
        {
            _auth     = new AuthHandler(accounts, config);
            _game     = new GameHandler(characters, config);
            _action   = new Conquer.Packets.ActionHandler(world);
            _register = new Conquer.Packets.RegisterHandler(characters);
            _walk     = new Conquer.Packets.WalkHandler(world);
            _chat     = new Conquer.Packets.ChatHandler(world);
            _npc      = new Conquer.Packets.NpcHandler(world);
        }

        public (ushort typeId, byte[] payload) ReadPacket(ClientSession session)
        {
            NetworkStream stream = session.Stream;

            // The client TQCipher-encrypts the entire stream from byte 0, including the
            // 2-byte length prefix. Decrypt as we read so the cipher counter advances
            // continuously across the whole connection.
            var lenBuf = new byte[2];
            ReadExact(stream, lenBuf);
            session.Cipher.Decrypt(lenBuf, 0, 2);
            ushort totalLen = BinaryPrimitives.ReadUInt16LittleEndian(lenBuf);
            Console.WriteLine($"[Read] local={session.TcpClient.Client.LocalEndPoint} totalLen={totalLen}");

            if (totalLen < 4 || totalLen > 8192)
                throw new IOException($"Invalid packet length {totalLen}");

            // Read + decrypt the rest of the packet (totalLen - 2 bytes after the length field)
            var payload = new byte[totalLen - 2];
            ReadExact(stream, payload.AsSpan());
            session.Cipher.Decrypt(payload, 0, payload.Length);
            return (BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(0, 2)), payload);
        }

        public void Dispatch(ClientSession session, ushort typeId, byte[] payload)
        {
            // Payload is already decrypted in ReadPacket, so typeId is the real packet type.
            switch (typeId)
            {
                case 1051:
                    _auth.Handle(session, payload);
                    break;
                case 1052:
                    _game.Handle(session, payload);
                    break;
                case 1010:
                    _action.HandleAction(session, payload);
                    break;
                case 1001:
                    _register.Handle(session, payload);
                    break;
                case 1005:
                    _walk.Handle(session, payload);
                    break;
                case 1004:
                    _chat.Handle(session, payload);
                    break;
                case 2031:
                    _npc.Handle(session, payload);
                    break;
                default:
                    Console.WriteLine($"[Warn] Unknown typeId={typeId}");
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
