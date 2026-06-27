using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
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
        // Local literals (Packets.csproj does not reference Redux — mirror ClientSession
        // re-declaring SERVER_SEAL). Do NOT import Redux.Common.
        private static readonly Regex NameRegex =
            new Regex("^[a-zA-Z0-9]{4,16}$", RegexOptions.Compiled);
        private static readonly HashSet<ushort> ValidMeshes =
            new HashSet<ushort> { 1003, 1004, 2001, 2002 };
        private static readonly HashSet<byte> ValidProfessions =
            new HashSet<byte> { 10, 20, 30, 40, 100 };

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

            if (!NameRegex.IsMatch(name) || name.ToLower().Contains("admin"))
            {
                session.SendGame(MsgTalk.Build(ChatType.Register, "Invalid character name"));
                return;
            }
            if (!ValidMeshes.Contains(mesh))
            {
                session.SendGame(MsgTalk.Build(ChatType.Register, "Invalid character mesh"));
                return;
            }
            if (!ValidProfessions.Contains(prof))
            {
                session.SendGame(MsgTalk.Build(ChatType.Register, "Invalid character profession"));
                return;
            }

            // Appearance: face range per body mesh (original exclusive .Next bounds).
            var rng = new Random();
            int face = (mesh == 1003 || mesh == 1004) ? rng.Next(50) : rng.Next(201, 250);
            int avatar = rng.Next(3, 9) * 100 + rng.Next(30, 51);

            var ch = new DbCharacter
            {
                AccountID = session.AccountId,
                Name = name,
                Mesh = mesh + face * 10000,
                Avatar = avatar,
                Level = 1,
                Silver = 1000,
                MapID = 1010,
                X = 61,
                Y = 109,
                Strength = 4,
                Agility = 6,
                Vitality = 12,
                Spirit = 0,
                HealthPoints = 318,
                ManaPoints = 0
            };

            try
            {
                _characters.Insert(ch);
            }
            catch (Exception e)
            {
                Console.WriteLine($"[Game] 1001 Insert failed for name={name}: {e.Message}");
                session.SendGame(MsgTalk.Build(ChatType.Register, "Character name already in use"));
                return;
            }

            session.SendGame(MsgTalk.Build(ChatType.Register, "ANSWER_OK"));
        }
    }
}
