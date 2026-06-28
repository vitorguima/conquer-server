using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace Conquer.Packets.Tests
{
    /// <summary>
    /// Pure unit tests for the bounded inbound chat parser <see cref="ChatHandler.TryReadMessage"/>
    /// — no socket, no DB. Covers the valid index-3 (Words) extraction plus every bound/reject
    /// path: short payload, per-string length running past the buffer, too-few strings, and a
    /// pathological count that must NOT over-iterate (capped at i &lt; 8). Mirrors WalkParseTests:
    /// bad input never throws (FR-5, AC-3.2, AC-5.3).
    /// </summary>
    public class ChatParseTests
    {
        // Synthetic inbound 1004 payload: type@0=1004, ChatType@6, string-list@22.
        private static byte[] BuildChatPayload(ushort channel, params string[] words)
        {
            var list = new List<byte> { (byte)words.Length };
            foreach (var w in words)
            {
                byte[] ascii = Encoding.ASCII.GetBytes(w);
                list.Add((byte)ascii.Length);
                list.AddRange(ascii);
            }

            var payload = new byte[22 + list.Count];
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0), 1004);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(6), channel);
            list.CopyTo(payload, 22);
            return payload;
        }

        // (1) Valid 4-string payload → TryReadMessage returns the index-3 Words.
        [Fact]
        public void TryReadMessage_ValidPayload_ReturnsWords()
        {
            byte[] payload = BuildChatPayload(2000, "Alice", "ALLUSERS", "", "hello world");

            bool ok = ChatHandler.TryReadMessage(payload, out string msg);

            Assert.True(ok);
            Assert.Equal("hello world", msg);
        }

        // (2) Short payload (< 23) → false, no throw (length-guard equivalent in the parser).
        [Fact]
        public void TryReadMessage_ShortPayload_ReturnsFalse()
        {
            var payload = new byte[22]; // no count byte (o=22 >= length)

            bool ok = ChatHandler.TryReadMessage(payload, out string msg);

            Assert.False(ok);
            Assert.Equal(string.Empty, msg);
        }

        // (3) A per-string [len] running past payload.Length → false, no over-read.
        [Fact]
        public void TryReadMessage_LengthPastEnd_ReturnsFalse()
        {
            // count=4; strings 0..2 ok; string 3 claims len=50 but no bytes follow.
            var bytes = new List<byte> { 4, 1, (byte)'A', 1, (byte)'B', 0, 50 };
            var payload = new byte[22 + bytes.Count];
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0), 1004);
            bytes.CopyTo(payload, 22);

            bool ok = ChatHandler.TryReadMessage(payload, out string msg);

            Assert.False(ok);
            Assert.Equal(string.Empty, msg);
        }

        // (4) Fewer than 4 strings (no Words index) → false.
        [Fact]
        public void TryReadMessage_TooFewStrings_ReturnsFalse()
        {
            byte[] payload = BuildChatPayload(2000, "Alice", "ALLUSERS", ""); // only 3

            bool ok = ChatHandler.TryReadMessage(payload, out string msg);

            Assert.False(ok);
        }

        // (5) Pathological count=255 must not over-iterate (capped at i < 8) and must not throw.
        [Fact]
        public void TryReadMessage_PathologicalCount_DoesNotOverIterateOrThrow()
        {
            // count=255 but only one 1-byte string actually present → bound check returns false.
            var bytes = new List<byte> { 255, 1, (byte)'X' };
            var payload = new byte[22 + bytes.Count];
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0), 1004);
            bytes.CopyTo(payload, 22);

            var ex = Record.Exception(() => ChatHandler.TryReadMessage(payload, out _));

            Assert.Null(ex);
        }

        // (6) Handle(null! session, short payload) → no throw (length guard returns first).
        [Fact]
        public void Handle_NullSession_ShortPayload_DoesNotThrow()
        {
            var world = new Conquer.World.World();
            var handler = new ChatHandler(world);
            var shortPayload = new byte[10]; // < 23 → guard returns before touching session

            var ex = Record.Exception(() => handler.Handle(null!, shortPayload));

            Assert.Null(ex);
        }
    }
}
