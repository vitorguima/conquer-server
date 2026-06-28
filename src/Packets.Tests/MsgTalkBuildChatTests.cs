using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace Conquer.Packets.Tests
{
    /// <summary>
    /// Pure unit tests for <see cref="MsgTalk.BuildChat"/> — no socket, no DB. Asserts the
    /// 1004 fixed-field byte layout (type id @2, color @4, ChatType @8, header length @0) and
    /// the string-list order/count ([from, to, "", message], count=4). Includes a byte-identical
    /// regression for the existing <see cref="MsgTalk.Build"/> (shared with the ANSWER_OK /
    /// NEW_ROLE handshake — must NOT drift; NFR-8 / AC-5.2).
    /// </summary>
    public class MsgTalkBuildChatTests
    {
        // Parse a NetStringPacker block ([u8 count][u8 len][ASCII]...) back into a string list.
        private static List<string> ParseStringList(byte[] buffer, int offset)
        {
            var result = new List<string>();
            int o = offset;
            int count = buffer[o++];
            for (int i = 0; i < count; i++)
            {
                int len = buffer[o++];
                result.Add(Encoding.ASCII.GetString(buffer, o, len));
                o += len;
            }
            return result;
        }

        // (1) BuildChat writes the 1004 fixed fields at the verified body offsets.
        [Fact]
        public void BuildChat_FixedFieldLayout()
        {
            byte[] body = MsgTalk.BuildChat(ChatType.Talk, "Alice", "ALLUSERS", "hi");

            // Type id @2 == 1004.
            Assert.Equal((ushort)1004, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(2, 2)));
            // Color @4 == 0x00FFFFFF (white).
            Assert.Equal(0x00FFFFFFu, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(4, 4)));
            // ChatType @8 == 2000 (Talk).
            Assert.Equal((ushort)2000, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(8, 2)));
            // Header length @0 == bodyLength (= frame - 8 = body.Length).
            Assert.Equal((ushort)body.Length, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(0, 2)));
        }

        // (2) String-list @24 parses back to [from, to, "", message] with count=4.
        [Fact]
        public void BuildChat_StringListOrderAndCount()
        {
            byte[] body = MsgTalk.BuildChat(ChatType.Talk, "Alice", "ALLUSERS", "hi");

            // count byte @24.
            Assert.Equal((byte)4, body[24]);

            var strings = ParseStringList(body, 24);
            Assert.Equal(4, strings.Count);
            Assert.Equal("Alice", strings[0]);
            Assert.Equal("ALLUSERS", strings[1]);
            Assert.Equal(string.Empty, strings[2]);
            Assert.Equal("hi", strings[3]);
        }

        // (3) bodyLength == 24 + packer.Length (packer = 1 count + per-string(1+len)).
        [Fact]
        public void BuildChat_BodyLengthMatchesPacker()
        {
            byte[] body = MsgTalk.BuildChat(ChatType.Talk, "Alice", "ALLUSERS", "hi");

            int packerLen = 1 + (1 + 5) + (1 + 8) + (1 + 0) + (1 + 2); // Alice/ALLUSERS/""/hi
            Assert.Equal(24 + packerLen, body.Length);
        }

        // (4) Build(Entrance, "ANSWER_OK") is byte-identical to the known handshake bytes.
        // Independent literal — drift in MsgTalk.Build breaks the ANSWER_OK / NEW_ROLE handshake.
        [Fact]
        public void Build_AnswerOk_ByteIdentical()
        {
            byte[] expected =
            {
                // Header @0: length=52 (0x34), type=1004 (0x03EC)
                0x34, 0x00, 0xEC, 0x03,
                // Color @4 = 0x00FFFFFF
                0xFF, 0xFF, 0xFF, 0x00,
                // ChatType @8 = 2101 (Entrance, 0x0835)
                0x35, 0x08,
                // Unknown0 @10
                0x00, 0x00,
                // Time @12
                0x00, 0x00, 0x00, 0x00,
                // HearerLookface @16
                0x00, 0x00, 0x00, 0x00,
                // SpeakerLookface @20
                0x00, 0x00, 0x00, 0x00,
                // String-list @24: count=4
                0x04,
                // "SYSTEM" (len 6)
                0x06, 83, 89, 83, 84, 69, 77,
                // "ALLUSERS" (len 8)
                0x08, 65, 76, 76, 85, 83, 69, 82, 83,
                // "" (len 0)
                0x00,
                // "ANSWER_OK" (len 9)
                0x09, 65, 78, 83, 87, 69, 82, 95, 79, 75,
            };

            byte[] actual = MsgTalk.Build(ChatType.Entrance, "ANSWER_OK");

            Assert.Equal(expected, actual);
        }
    }
}
