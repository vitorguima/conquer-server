using System.Buffers.Binary;
using Conquer.Database;
using Conquer.Packets;
using Xunit;

namespace Conquer.Packets.Tests
{
    /// <summary>
    /// Byte-layout coverage for the world-surroundings broadcast builders (NFR-13):
    /// the live-coord <see cref="SpawnEntity.Build"/> (1014), <see cref="Walk.BuildBroadcast"/>
    /// (1005), and <see cref="GeneralData.BuildRemoveEntity"/> (132). Length fields are the
    /// on-wire header value = whole-frame size MINUS the 8-byte seal (AppendHeader writes size-8).
    /// </summary>
    public class SpawnEntityBuildTests
    {
        [Fact]
        public void Build_WritesLiveFieldsAtWireOffsets()
        {
            byte[] body = SpawnEntity.Build(
                uid: 654321, mesh: 301003, avatar: 340, level: 5, hp: 100,
                x: 61, y: 109, name: "Vitor");

            Assert.Equal(1014, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(2)));            // type @2
            Assert.Equal(654321u, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(4)));         // UID @4
            Assert.Equal(301003u, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(8)));         // Lookface @8
            Assert.Equal((ushort)100, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(48)));    // Life @48
            Assert.Equal((ushort)5, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(50)));      // Level @50
            Assert.Equal((ushort)61, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(52)));     // X @52 (live)
            Assert.Equal((ushort)109, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(54)));    // Y @54 (live)
            Assert.Equal((ushort)340, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(56)));    // Avatar/Hair @56

            // name @90 via NetStringPacker: [count=1][len=5]["Vitor"]
            Assert.True(body.Length > 90, "body must extend past the name offset");
            Assert.Equal(1, body[90]);
            Assert.Equal((byte)"Vitor".Length, body[91]);
            Assert.Equal("Vitor", System.Text.Encoding.ASCII.GetString(body, 92, 5));
        }

        [Fact]
        public void BuildSelf_IsByteIdenticalToBuildWithCharFields()
        {
            var ch = new DbCharacter
            {
                CharacterID = 654321,
                Name = "Vitor",
                Mesh = 301003,
                Avatar = 340,
                Level = 5,
                MapID = 1010,
                X = 61,
                Y = 109,
                HealthPoints = 100,
            };

            byte[] self = SpawnEntity.BuildSelf(ch);
            byte[] explicitBuild = SpawnEntity.Build(
                (uint)ch.CharacterID, ch.Mesh, ch.Avatar, ch.Level, ch.HealthPoints,
                (ushort)ch.X, (ushort)ch.Y, ch.Name);

            Assert.Equal(explicitBuild, self);   // enter-world self-spawn unchanged
        }

        [Fact]
        public void Walk_BuildBroadcast_Layout()
        {
            byte[] body = Walk.BuildBroadcast(uid: 654321, dir: 3, mode: 1);

            // AppendHeader(span, 28, 1005) => len@0 = 28 - 8 = 20.
            Assert.Equal(20, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(0)));   // len @0
            Assert.Equal(1005, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(2))); // type @2
            Assert.Equal(654321u, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(4))); // UID @4
            Assert.Equal((byte)3, body[8]);   // Direction @8
            Assert.Equal((byte)1, body[9]);   // Mode @9
            Assert.Equal(20, body.Length);    // body length
        }

        [Fact]
        public void BuildRemoveEntity_Layout_BodyLengthIs28()
        {
            byte[] body = GeneralData.BuildRemoveEntity(uid: 654321);

            // REVIEWER NIT: AppendHeader(span, 36, 1010) => len@0 = 36 - 8 = 28 (NOT 20).
            Assert.Equal(28, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(0)));    // len @0 = 28
            Assert.Equal(1010, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(2)));  // type @2
            Assert.Equal(654321u, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(8))); // UID @8
            Assert.Equal(132, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(22)));  // Action @22 = 132
            Assert.Equal(28, body.Length);   // body length = 28
        }
    }
}
