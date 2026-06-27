using System;
using System.Buffers.Binary;
using System.Text;
using Conquer.Database;
using Conquer.Network;

namespace Conquer.Packets
{
    /// <summary>
    /// Handles inbound MsgRegister(1001): parse the fixed-layout creation payload,
    /// validate name/mesh/profession, build a level-1 <see cref="DbCharacter"/>,
    /// INSERT it via <see cref="CharacterRepository"/>, and reply
    /// <c>MsgTalk(ChatType.Register, "ANSWER_OK")</c>. No enter-world on this socket —
    /// the client reconnects and the unchanged 1052 path spawns it. Additive only (NFR-1).
    ///
    /// Payload (2-byte length prefix already stripped by ReadPacket; payload[0]=typeId):
    /// CharacterName ASCII[16] @18, Mesh u16 LE @50, Profession u8 @52, UID u32 @56 (ignored).
    /// </summary>
    public sealed class RegisterHandler
    {
        private readonly CharacterRepository _characters;

        public RegisterHandler(CharacterRepository characters)
        {
            _characters = characters;
        }

        public void Handle(ClientSession session, byte[] payload)
        {
            if (payload.Length < 60)
            {
                Console.WriteLine("[Game] short 1001");
                return;
            }

            string name = Encoding.ASCII.GetString(payload, 18, 16).TrimEnd('\0');
            ushort mesh = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(50, 2));
            byte prof = payload[52];
        }
    }
}
