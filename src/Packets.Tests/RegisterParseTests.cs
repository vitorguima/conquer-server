using System;
using System.Buffers.Binary;
using System.Text;
using Conquer.Database;
using Conquer.Packets;
using Xunit;

namespace Conquer.Packets.Tests
{
    /// <summary>
    /// Covers the pure parse/build surface of <see cref="RegisterHandler"/> (no socket/DB):
    /// (a) the fixed 1001 layout (Name@18 / Mesh@50 / Prof@52), (b) the appearance formula
    /// via a seeded <see cref="Random"/> so faces are deterministic, and (c) level-1 stat
    /// derivation (Life 318 / Mana 0). Mirrors ActionParseTests for project test style.
    /// </summary>
    public class RegisterParseTests
    {
        // Synthetic 60-byte 1001 payload: type@0=1001, ASCII "TestName"@18,
        // Mesh u16 LE @50=1003, Prof u8 @52=10, UID u32 LE @56.
        private static byte[] BuildPayload()
        {
            var payload = new byte[60];
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0), 1001);   // type @0
            Encoding.ASCII.GetBytes("TestName").CopyTo(payload, 18);             // Name @18
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(50), 1003);  // Mesh @50
            payload[52] = 10;                                                    // Prof @52
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(56), 1234);  // UID @56 (ignored)
            return payload;
        }

        [Fact]
        public void ParseRegister_ReadsFixedLayout()
        {
            var payload = BuildPayload();

            Assert.True(payload.Length >= 60); // RegisterHandler's short-frame guard

            var (name, mesh, prof) = RegisterHandler.ParseRegister(payload);

            Assert.Equal("TestName", name);
            Assert.Equal((ushort)1003, mesh);
            Assert.Equal((byte)10, prof);
        }

        [Fact]
        public void BuildCharacter_BodyMesh_FaceFormula()
        {
            // Seed 1: rng.Next(50) == 12 → Mesh == 1003 + 12*10000 == 121003; Avatar == 339.
            var ch = RegisterHandler.BuildCharacter(7, "TestName", 1003, 10, new Random(1));

            Assert.Equal(121003, ch.Mesh);
            Assert.Equal(339, ch.Avatar);
            Assert.InRange(ch.Mesh, 1003, 491003);   // body 1003, face 0..49
            Assert.InRange(ch.Avatar, 330, 868);
        }

        [Fact]
        public void BuildCharacter_HighBodyMesh_FaceFormula()
        {
            // Seed 43: rng.Next(201,250) == 210 → Mesh == 2001 + 210*10000 == 2102001; Avatar == 547.
            var ch = RegisterHandler.BuildCharacter(7, "TestName", 2001, 10, new Random(43));

            Assert.Equal(2102001, ch.Mesh);
            Assert.Equal(547, ch.Avatar);
            Assert.InRange(ch.Avatar, 330, 868);
        }

        [Fact]
        public void BuildCharacter_Level1StatDerivation()
        {
            var ch = RegisterHandler.BuildCharacter(7, "TestName", 1003, 10, new Random(1));

            Assert.Equal(1, ch.Level);
            Assert.Equal(4, ch.Strength);
            Assert.Equal(6, ch.Agility);
            Assert.Equal(12, ch.Vitality);
            Assert.Equal(0, ch.Spirit);

            // Life factors STR3/AGI3/VIT24/SPI3 → 4*3 + 6*3 + 12*24 + 0*3 == 12+18+288+0 == 318.
            Assert.Equal(318, ch.HealthPoints);
            // Mana SPI*5 → 0*5 == 0.
            Assert.Equal(0, ch.ManaPoints);
        }
    }
}
