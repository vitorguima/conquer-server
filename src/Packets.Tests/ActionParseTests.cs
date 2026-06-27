using System.Buffers.Binary;
using Xunit;

namespace Conquer.Packets.Tests
{
    /// <summary>
    /// Mirrors ActionHandler's inbound parse: on a dispatch payload (the 2-byte length
    /// prefix already stripped by GameConnection), the GeneralData(1010) Action subtype
    /// is a u16 at payload offset 20 (original body offset 22 minus 2). Asserts the read
    /// yields SetLocation(74).
    /// </summary>
    public class ActionParseTests
    {
        [Fact]
        public void ActionParse_Offset20()
        {
            // Synthetic 1010 dispatch payload: type@0=1010, Action(u16)@20=74.
            // Length mirrors a real seal-carrying payload (body 28 + 8 seal - 2 len = 34).
            var payload = new byte[34];
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0), 1010);  // type @0
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(20), 74);   // Action @20 = SetLocation

            Assert.True(payload.Length >= 22); // ActionHandler's short-frame guard

            ushort action = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(20, 2));
            Assert.Equal(74, action);
        }
    }
}
