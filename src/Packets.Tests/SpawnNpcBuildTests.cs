using System;
using System.Buffers.Binary;
using System.Text;
using Conquer.Packets;
using Conquer.World;
using Xunit;

namespace Conquer.Packets.Tests
{
    /// <summary>
    /// Byte-layout coverage for the NPC spawn path (AC-5.3, AC-5.4) and the
    /// <see cref="EntitySpawn.For"/> kind-branch regression guarantee (AC-2.1):
    /// the player branch is byte-identical to the inline <see cref="SpawnEntity.Build"/>
    /// call, the NPC branch emits a 2030. Length fields are the on-wire header value
    /// (= whole-frame size MINUS the 8-byte seal; AppendHeader writes size-8).
    /// </summary>
    public class SpawnNpcBuildTests
    {
        [Fact]
        public void SpawnNpc_Build_WritesEveryFieldAtWireOffsets()
        {
            byte[] body = SpawnNpc.Build(uid: 90001, mesh: 1, type: 2, x: 63, y: 109, name: "Guide");

            // header: AppendHeader(span, bodyLen+8, 2030) => len@0 = bodyLen, type@2 = 2030.
            int expectedBody = 18 + NameBytesLen("Guide");      // 18 + NetStringPacker(name)
            Assert.Equal((ushort)expectedBody, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(0))); // len @0 = body
            Assert.Equal(2030, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(2)));                 // type @2 = 2030
            Assert.Equal(90001u, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(4)));               // UID @4
            Assert.Equal((ushort)63, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(8)));           // X @8
            Assert.Equal((ushort)109, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(10)));         // Y @10
            Assert.Equal((ushort)1, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(12)));           // Mesh @12
            Assert.Equal((ushort)2, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(14)));           // Type @14
            // Unknown1 @16 is written as 0u, but Name @18 (NetStringPacker) overlaps its high 2 bytes,
            // so only the low 2 bytes (16,17) remain zero — assert those (actual production layout).
            Assert.Equal((ushort)0, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(16)));           // Unknown1 low half @16 = 0

            // Name @18 via NetStringPacker: [count=1][len=5]["Guide"]
            Assert.Equal(1, body[18]);
            Assert.Equal((byte)"Guide".Length, body[19]);
            Assert.Equal("Guide", Encoding.ASCII.GetString(body, 20, 5));

            // Body length is exactly 18 + encoded name (AC-5.3).
            Assert.Equal(expectedBody, body.Length);
        }

        [Fact]
        public void EntitySpawn_For_Player_IsByteIdenticalToSpawnEntityBuild()
        {
            var player = new PlayerEntity(
                uid: 654321, mapId: 1010, x: 61, y: 109,
                session: null!, mesh: 301003, avatar: 340, level: 5, hp: 100, name: "Vitor");

            byte[] branched = EntitySpawn.For(player);
            byte[] inline = SpawnEntity.Build(
                player.Uid, player.Mesh, player.Avatar, player.Level, player.Hp,
                player.X, player.Y, player.Name);

            Assert.Equal(inline, branched);   // player 1014 byte-identical (regression, AC-2.1)
        }

        [Fact]
        public void EntitySpawn_For_Npc_EmitsType2030()
        {
            var npc = new NpcEntity(uid: 90001, mapId: 1010, x: 63, y: 109, mesh: 1, npcType: 2, name: "Guide");

            byte[] branched = EntitySpawn.For(npc);
            byte[] direct = SpawnNpc.Build(npc.Uid, npc.Mesh, npc.NpcType, npc.X, npc.Y, npc.Name);

            Assert.Equal(2030, BinaryPrimitives.ReadUInt16LittleEndian(branched.AsSpan(2)));  // type @2 = 2030
            Assert.Equal(direct, branched);                                                   // NPC branch == SpawnNpc.Build
        }

        // NetStringPacker encodes one ASCII string as [count u8=1][len u8][bytes].
        private static int NameBytesLen(string s) => 2 + Encoding.ASCII.GetByteCount(s);
    }
}
