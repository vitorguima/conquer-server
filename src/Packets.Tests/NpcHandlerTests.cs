using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Conquer.Crypto;
using Conquer.Network;
using Conquer.Packets;
using Conquer.World;
using Xunit;

namespace Conquer.Packets.Tests
{
    /// <summary>
    /// <see cref="NpcHandler"/> guard + dispatch coverage (AC-6.2–6.5). The handler sends via the
    /// sealed <see cref="ClientSession.SendGame"/>, so we drive a REAL loopback socket pair and
    /// swap the session cipher for an identity pass-through, then read the framed bytes back off
    /// the peer to count/verify the emitted controls. Bad/short/wrong-action/unknown-UID input
    /// must produce ZERO sends and never disconnect.
    /// </summary>
    public class NpcHandlerTests : IDisposable
    {
        // Identity cipher: SendGame still allocates+seals, but bytes pass through unchanged so
        // the peer-side reader can frame them by the @0 length header without the Blowfish keystream.
        private sealed class IdentityCipher : ICipher
        {
            public void Encrypt(byte[] data, int offset, int length) { }
            public void Decrypt(byte[] data, int offset, int length) { }
        }

        private readonly TcpListener _listener;
        private readonly TcpClient _serverSide;   // session writes here
        private readonly TcpClient _peer;         // we read frames here
        private readonly ClientSession _session;

        public NpcHandlerTests()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            int port = ((IPEndPoint)_listener.LocalEndpoint).Port;

            _peer = new TcpClient();
            _peer.Connect(IPAddress.Loopback, port);
            _serverSide = _listener.AcceptTcpClient();

            _session = new ClientSession(_serverSide) { Cipher = new IdentityCipher() };
        }

        private static byte[] ClickPayload(uint npcUid, ushort action)
        {
            // 2031 inbound (2-byte length prefix stripped): typeId@0=2031, UID@2, Data@6, Action@10, Type@12.
            var p = new byte[16];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(0), 2031);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(2), npcUid);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(10), action);
            return p;
        }

        private static PlayerEntity Clicker(ClientSession session) =>
            new(uid: 1, mapId: 1010, x: 63, y: 109, session: session,
                mesh: 1, avatar: 1, level: 1, hp: 100, name: "Clicker");

        // Read up to `maxBytes` from the peer within a short window; returns whatever arrived.
        private byte[] DrainPeer(int settleMs = 150)
        {
            var ns = _peer.GetStream();
            ns.ReadTimeout = settleMs;
            var buf = new MemoryStream();
            var tmp = new byte[4096];
            try
            {
                while (true)
                {
                    int n = ns.Read(tmp, 0, tmp.Length);
                    if (n <= 0) break;
                    buf.Write(tmp, 0, n);
                }
            }
            catch (IOException) { /* read timeout = no more data */ }
            return buf.ToArray();
        }

        // Frame the identity-encoded stream by the @0 length header: frame body = len bytes,
        // followed by an 8-byte seal. Returns each frame body (length = len@0).
        private static List<byte[]> Frames(byte[] stream)
        {
            var frames = new List<byte[]>();
            int i = 0;
            while (i + 2 <= stream.Length)
            {
                int bodyLen = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(stream.AsSpan(i));
                int whole = bodyLen + 8;                // body + 8-byte seal
                if (i + whole > stream.Length) break;
                frames.Add(stream.AsSpan(i, bodyLen).ToArray());
                i += whole;
            }
            return frames;
        }

        [Fact]
        public void Handle_ShortPayload_NoSend()
        {
            var world = new Conquer.World.World();
            var map = world.GetOrAdd(1010);
            map.Register(new NpcEntity(90001, 1010, 63, 109, 1, 2, "Guide"));
            _session.WorldEntity = Clicker(_session);
            map.Register((PlayerEntity)_session.WorldEntity!);

            new NpcHandler(world).Handle(_session, new byte[8]);   // payload < 16

            Assert.Empty(Frames(DrainPeer()));
        }

        [Fact]
        public void Handle_NonActivateAction_NoSend()
        {
            var world = new Conquer.World.World();
            var map = world.GetOrAdd(1010);
            map.Register(new NpcEntity(90001, 1010, 63, 109, 1, 2, "Guide"));
            _session.WorldEntity = Clicker(_session);

            new NpcHandler(world).Handle(_session, ClickPayload(90001, action: 1));   // not Activate(0)

            Assert.Empty(Frames(DrainPeer()));
        }

        [Fact]
        public void Handle_UnknownUid_NoSend()
        {
            var world = new Conquer.World.World();
            var map = world.GetOrAdd(1010);
            map.Register(new NpcEntity(90001, 1010, 63, 109, 1, 2, "Guide"));
            _session.WorldEntity = Clicker(_session);

            new NpcHandler(world).Handle(_session, ClickPayload(99999, action: 0));   // no such NPC

            Assert.Empty(Frames(DrainPeer()));
        }

        [Fact]
        public void Handle_ValidNpcClick_SendsFourControls_AvatarTextTextFinish()
        {
            var world = new Conquer.World.World();
            var map = world.GetOrAdd(1010);
            map.Register(new NpcEntity(90001, 1010, 63, 109, 1, 2, "Guide"));
            _session.WorldEntity = Clicker(_session);

            new NpcHandler(world).Handle(_session, ClickPayload(90001, action: 0));

            var frames = Frames(DrainPeer());
            Assert.Equal(4, frames.Count);
            // Every frame is a 2032 control; Action@11 ordered Avatar/Text/Text/Finish.
            foreach (var f in frames)
                Assert.Equal(2032, System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(f.AsSpan(2)));
            Assert.Equal((byte)NpcDialog.DialogAction.Avatar, frames[0][11]);
            Assert.Equal((byte)NpcDialog.DialogAction.Text, frames[1][11]);
            Assert.Equal((byte)NpcDialog.DialogAction.Text, frames[2][11]);
            Assert.Equal((byte)NpcDialog.DialogAction.Finish, frames[3][11]);
        }

        public void Dispose()
        {
            try { _peer.Close(); } catch { }
            try { _serverSide.Close(); } catch { }
            try { _listener.Stop(); } catch { }
        }
    }
}
