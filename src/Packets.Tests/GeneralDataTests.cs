using System.Buffers.Binary;
using Conquer.Packets;
using Xunit;

namespace Conquer.Packets.Tests
{
    /// <summary>
    /// Asserts the exact byte layout of the GeneralData(1010) SetLocation echo
    /// (design.md "Reply A"). Body 28 bytes; AppendHeader writes length = bodyLen.
    /// </summary>
    public class GeneralDataTests
    {
        [Fact]
        public void GeneralData_SetLocationEcho_Layout()
        {
            const uint uid = 123456;
            const uint mapId = 1010;
            const ushort x = 61;
            const ushort y = 109;

            byte[] body = GeneralData.BuildSetLocation(uid, mapId, x, y);

            Assert.Equal(28, body.Length);
            Assert.Equal(28, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(0)));   // length field
            Assert.Equal(1010, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(2))); // type
            Assert.Equal(uid, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(8)));  // UID
            Assert.Equal(mapId, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(12))); // Data1 = MapID

            uint data2 = BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(16));
            Assert.Equal((uint)(((uint)y << 16) | (x & 0xFFFFu)), data2);
            Assert.Equal(x, (ushort)(data2 & 0xFFFF));         // low16 = X
            Assert.Equal(y, (ushort)(data2 >> 16));            // high16 = Y

            Assert.Equal(74, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(22))); // Action = 74
        }
    }
}
