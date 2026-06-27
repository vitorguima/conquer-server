using System.Buffers.Binary;
using Conquer.Database;
using Conquer.Packets;
using Xunit;

namespace Conquer.Packets.Tests
{
    /// <summary>
    /// Asserts the exact byte layout of the gated self SpawnEntity(1014) builder
    /// (design.md "Fallback — SpawnEntity(1014) self"). Stand-still: Action=0.
    /// </summary>
    public class SpawnEntityTests
    {
        [Fact]
        public void SpawnEntity_SelfLayout()
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

            byte[] body = SpawnEntity.BuildSelf(ch);

            Assert.Equal(1014, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(2)));        // type
            Assert.Equal((uint)ch.CharacterID, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(4)));  // UID
            Assert.Equal((uint)ch.Mesh, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(8)));         // Lookface
            Assert.Equal((ushort)ch.X, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(52)));         // PositionX
            Assert.Equal((ushort)ch.Y, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(54)));         // PositionY
            Assert.Equal((ushort)ch.Avatar, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(56)));    // Hair
            Assert.Equal(0, body[59]);                                                                    // Action = stand

            // name @90 via NetStringPacker: [count=1][len=5]["Vitor"]
            Assert.True(body.Length > 90, "body must extend past the name offset");
            Assert.Equal(1, body[90]);                          // string count
            Assert.Equal((byte)ch.Name.Length, body[91]);      // name length
            string name = System.Text.Encoding.ASCII.GetString(body, 92, ch.Name.Length);
            Assert.Equal("Vitor", name);
        }
    }
}
