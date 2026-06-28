using System;
using System.Buffers.Binary;

namespace Conquer.Packets
{
    /// <summary>
    /// Builds a CO <c>[1004]</c> MsgTalk frame. Used for the character-select handshake
    /// strings ANSWER_OK / NEW_ROLE on the Entrance channel. Layout (port of the original
    /// <c>0b094c6:src/Redux/Packets/Game/[1004]Talk.cs</c>):
    /// <list type="bullet">
    /// <item>header @0 (4): length = bodyLen (size-8), type = 1004</item>
    /// <item>Color @4 (uint = 0x00FFFFFF)</item>
    /// <item>Type @8 (ushort = ChatType)</item>
    /// <item>Unknown0 @10 (ushort = 0)</item>
    /// <item>Time @12 (uint = 0)</item>
    /// <item>HearerLookface @16 (uint = 0)</item>
    /// <item>SpeakerLookface @20 (uint = 0)</item>
    /// <item>NetStringPacker @24 = [Speaker="SYSTEM", Hearer="ALLUSERS", Emotion="", Words]</item>
    /// </list>
    /// <para>Returns the body only (24 + packer). <see cref="Network.ClientSession.SendGame"/>
    /// appends the 8-byte seal; the header length field is set to the body length, so the
    /// wire frame (bodyLen + 8) yields header length = (bodyLen + 8) - 8 = bodyLen.</para>
    /// </summary>
    public static class MsgTalk
    {
        private const ushort MsgTalkType = 1004;
        private const uint DefaultColor = 0x00FFFFFFu;
        private const string SystemName = "SYSTEM";
        private const string AllUsersName = "ALLUSERS";

        public static byte[] Build(ChatType type, string words)
        {
            var packer = new NetStringPacker(SystemName, AllUsersName, string.Empty, words ?? string.Empty);
            int bodyLength = 24 + packer.Length;
            var buffer = new byte[bodyLength];
            Span<byte> span = buffer;

            // Header length field = body length (SendGame adds the 8-byte seal, making the
            // wire frame bodyLength+8; AppendHeader writes (size-8) = bodyLength).
            PacketBuilder.AppendHeader(span, (ushort)(bodyLength + 8), MsgTalkType);
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(4), DefaultColor);
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(8), (ushort)type);
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(10), 0);  // Unknown0
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(12), 0);  // Time
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(16), 0);  // HearerLookface
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(20), 0);  // SpeakerLookface
            packer.Write(span.Slice(24));

            return buffer;
        }

        /// <summary>
        /// Builds a 1004 MsgTalk with caller-supplied <paramref name="from"/>/<paramref name="to"/>
        /// strings (local screen chat). Same fixed-field layout and offsets as <see cref="Build"/>,
        /// only the Speaker/Hearer strings are parameters. String list (count=4) =
        /// [from, to, "", message]. <see cref="Build"/> is intentionally left untouched (its bytes
        /// are shared with the ANSWER_OK / NEW_ROLE handshake and must stay byte-identical).
        /// </summary>
        public static byte[] BuildChat(ChatType channel, string from, string to, string message)
        {
            var packer = new NetStringPacker(from, to, string.Empty, message); // count=4
            int bodyLength = 24 + packer.Length;
            var buffer = new byte[bodyLength];
            Span<byte> span = buffer;

            PacketBuilder.AppendHeader(span, (ushort)(bodyLength + 8), MsgTalkType);
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(4), DefaultColor);
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(8), (ushort)channel);
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(10), 0);  // Unknown0
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(12), 0);  // Time
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(16), 0);  // HearerLookface
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(20), 0);  // SpeakerLookface
            packer.Write(span.Slice(24));

            return buffer;
        }
    }
}
