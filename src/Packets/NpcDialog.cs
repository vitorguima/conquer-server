using System;
using System.Buffers.Binary;

namespace Conquer.Packets
{
    /// <summary>
    /// Builds CO <c>[2032]</c> NpcDialog (MSG_DIALOG) control frames. Each control is ONE
    /// 2032 packet; a dialog window is a SEQUENCE of controls. Port of the ORIGINAL
    /// <c>[2032] NpcDialog.cs</c> layout (body sized EXACTLY <c>12 + strings</c>; the original
    /// over-allocates <c>24 + strings</c> — the extra 12 is slack, flagged for live-capture):
    /// <list type="bullet">
    /// <item>header @0 (4): length, type = 2032</item>
    /// <item>UID @4 (u32 = window pos <c>(Y&lt;&lt;16)|X</c>; 0 = default-place in v1)</item>
    /// <item>ID @8 (u16 = avatar face; Avatar control only)</item>
    /// <item>Linkback @10 (u8 = button/option id; 255 = close on Finish)</item>
    /// <item>Action @11 (u8 = <see cref="DialogAction"/>)</item>
    /// <item>Strings @12 (NetStringPacker; omitted when empty)</item>
    /// </list>
    /// Span/BinaryPrimitives, no unsafe.
    /// </summary>
    public static class NpcDialog
    {
        private const ushort MsgDialogType = 2032;
        private const int StringsOffset = 12;

        /// <summary>NpcDialog control action discriminator (Action @11).</summary>
        public enum DialogAction : byte
        {
            Text = 1,
            Option = 2,
            Avatar = 4,
            Finish = 100,
        }

        private static byte[] Build(byte action, ushort id, byte linkback, string? text)
        {
            var names = new NetStringPacker();
            if (!string.IsNullOrEmpty(text)) names.AddString(text);

            int bodyLength = StringsOffset + (names.Count > 0 ? names.Length : 0);
            var buffer = new byte[bodyLength];
            Span<byte> span = buffer;

            PacketBuilder.AppendHeader(span, (ushort)(bodyLength + 8), MsgDialogType);
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(4), 0u);  // UID = window pos (0 = default-place v1)
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(8), id);  // ID = avatar face
            span[10] = linkback;                                          // Linkback (255 = close on Finish)
            span[11] = action;                                           // Action = DialogAction
            if (names.Count > 0) names.Write(span.Slice(StringsOffset));  // Strings

            return buffer;
        }

        /// <summary>Avatar control: render <paramref name="face"/> portrait.</summary>
        public static byte[] Avatar(ushort face) => Build((byte)DialogAction.Avatar, face, 0, null);

        /// <summary>Text control: one line of dialog text.</summary>
        public static byte[] Text(string line) => Build((byte)DialogAction.Text, 0, 0, line);

        /// <summary>Option control: a clickable button labeled <paramref name="label"/>, id <paramref name="id"/>.</summary>
        public static byte[] Option(string label, byte id) => Build((byte)DialogAction.Option, 0, id, label);

        /// <summary>Finish control: close-marker; linkback 255 tells the client to render the window.</summary>
        public static byte[] Finish() => Build((byte)DialogAction.Finish, 0, 255, null);

        /// <summary>
        /// The v1 static dialog window (clicker only): Avatar -&gt; Text -&gt; Text -&gt; Finish.
        /// Caller sends each frame in order to the clicking player.
        /// </summary>
        public static byte[][] StaticSequence(ushort face, string line1, string line2) => new[]
        {
            Avatar(face),
            Text(line1),
            Text(line2),
            Finish(),
        };
    }
}
