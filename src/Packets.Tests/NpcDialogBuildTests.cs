using System;
using System.Buffers.Binary;
using System.Text;
using Conquer.Packets;
using Xunit;

namespace Conquer.Packets.Tests
{
    /// <summary>
    /// Byte-layout coverage for the 2032 NpcDialog control builders (AC-6.6): every control
    /// is a single 2032 frame with Action@11, the avatar face at ID@8, the Finish close-marker
    /// at Linkback@10=255, strings at @12 (omitted when empty), body == 12 + strings. Also
    /// asserts the v1 <see cref="NpcDialog.StaticSequence"/> shape (Avatar+Text+Text+Finish).
    /// </summary>
    public class NpcDialogBuildTests
    {
        private const int StringsOffset = 12;

        private static ushort Type(byte[] f) => BinaryPrimitives.ReadUInt16LittleEndian(f.AsSpan(2));
        private static int NameBytesLen(string s) => 2 + Encoding.ASCII.GetByteCount(s);

        [Fact]
        public void Avatar_Layout_FaceAtId8_ActionAvatar_NoStrings()
        {
            byte[] f = NpcDialog.Avatar(face: 30);

            Assert.Equal(2032, Type(f));                                                         // type @2 = 2032
            Assert.Equal((ushort)30, BinaryPrimitives.ReadUInt16LittleEndian(f.AsSpan(8)));      // ID @8 = face
            Assert.Equal((byte)0, f[10]);                                                        // Linkback @10 = 0
            Assert.Equal((byte)NpcDialog.DialogAction.Avatar, f[11]);                            // Action @11 = 4
            Assert.Equal(StringsOffset, f.Length);                                               // body = 12 (no strings)
        }

        [Fact]
        public void Text_Layout_ActionText_StringAt12_Body12PlusStrings()
        {
            byte[] f = NpcDialog.Text("Welcome");

            Assert.Equal(2032, Type(f));
            Assert.Equal((byte)NpcDialog.DialogAction.Text, f[11]);                              // Action @11 = 1
            // String @12 via NetStringPacker: [count=1][len][bytes]
            Assert.Equal(1, f[StringsOffset]);
            Assert.Equal((byte)"Welcome".Length, f[StringsOffset + 1]);
            Assert.Equal("Welcome", Encoding.ASCII.GetString(f, StringsOffset + 2, "Welcome".Length));
            Assert.Equal(StringsOffset + NameBytesLen("Welcome"), f.Length);                     // body = 12 + strings
        }

        [Fact]
        public void Option_Layout_ActionOption_IdAtLinkback10_LabelAt12()
        {
            byte[] f = NpcDialog.Option("Quests", id: 7);

            Assert.Equal(2032, Type(f));
            Assert.Equal((byte)7, f[10]);                                                        // Linkback @10 = option id
            Assert.Equal((byte)NpcDialog.DialogAction.Option, f[11]);                            // Action @11 = 2
            Assert.Equal(1, f[StringsOffset]);
            Assert.Equal((byte)"Quests".Length, f[StringsOffset + 1]);
            Assert.Equal(StringsOffset + NameBytesLen("Quests"), f.Length);
        }

        [Fact]
        public void Finish_Layout_ActionFinish_Linkback255_NoStrings_Body12()
        {
            byte[] f = NpcDialog.Finish();

            Assert.Equal(2032, Type(f));
            Assert.Equal((byte)255, f[10]);                                                      // Linkback @10 = 255 (close)
            Assert.Equal((byte)NpcDialog.DialogAction.Finish, f[11]);                            // Action @11 = 100
            Assert.Equal(StringsOffset, f.Length);                                               // body = 12 (no strings)
        }

        [Fact]
        public void StaticSequence_IsAvatarTextTextFinish()
        {
            byte[][] seq = NpcDialog.StaticSequence(face: 1, line1: "Hello.", line2: "Bye.");

            Assert.Equal(4, seq.Length);
            Assert.All(seq, f => Assert.Equal(2032, Type(f)));                                   // every frame 2032
            Assert.Equal((byte)NpcDialog.DialogAction.Avatar, seq[0][11]);                       // [0] Avatar
            Assert.Equal((byte)NpcDialog.DialogAction.Text, seq[1][11]);                         // [1] Text
            Assert.Equal((byte)NpcDialog.DialogAction.Text, seq[2][11]);                         // [2] Text
            Assert.Equal((byte)NpcDialog.DialogAction.Finish, seq[3][11]);                       // [3] Finish
            Assert.Equal((ushort)1, BinaryPrimitives.ReadUInt16LittleEndian(seq[0].AsSpan(8)));  // avatar face @8
        }
    }
}
