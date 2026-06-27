using System.Buffers.Binary;
using Conquer.Packets;
using Xunit;

namespace Conquer.Packets.Tests
{
    /// <summary>
    /// Asserts the exact byte layout of MapStatus(1110) (design.md "Reply B").
    /// Body 16 bytes; minimal net8 form UID=ID=MapID, Type=0.
    /// </summary>
    public class MapStatusTests
    {
        [Fact]
        public void MapStatus_Layout()
        {
            const uint mapId = 1010;

            byte[] body = MapStatus.Build(mapId);

            Assert.Equal(16, body.Length);
            Assert.Equal(16, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(0)));   // length field
            Assert.Equal(1110, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(2))); // type
            Assert.Equal(mapId, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(4))); // UID
            Assert.Equal(mapId, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(8))); // ID
            Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(12)));   // Type
        }
    }
}
