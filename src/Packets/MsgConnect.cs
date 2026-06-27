using System;
using System.Buffers.Binary;
using Conquer.Database;
using Conquer.Network;
using Microsoft.Extensions.Configuration;

namespace Conquer.Packets
{
    public sealed class GameHandler
    {
        private readonly CharacterRepository _characters;

        public GameHandler(CharacterRepository characters, IConfiguration config)
        {
            _characters = characters;
        }

        public void Handle(ClientSession session, byte[] payload)
        {
            Console.WriteLine($"[Game] recv MsgConnect payload.Length={payload.Length}");

            // payload has the 2-byte length prefix stripped: type @0, token @2.
            if (payload.Length < 10)
            {
                Console.WriteLine($"[Game] payload too short ({payload.Length}) — disconnecting");
                session.Disconnect();
                return;
            }

            ulong token = BinaryPrimitives.ReadUInt64LittleEndian(payload.AsSpan(2, 8));

            if (!TokenStore.TryConsume(token, out int accountId))
            {
                Console.WriteLine($"[Game] Invalid token {token} — disconnecting");
                session.Disconnect();
                return;
            }

            // NOTE: the game path uses GameCipher (Blowfish-CFB64, already keyed by the
            // DH exchange). Do NOT re-key or cast to TQCipher here — that cast would throw
            // InvalidCastException on a real game connection.
            session.AccountId = accountId;
            session.IsAuthenticated = true;

            Console.WriteLine($"[Game] Connect accountId={accountId}");

            var character = _characters.FindByAccountId(accountId);
            session.Character = character;
            if (character != null)
            {
                // Seed the live authoritative position (in-memory) from the persisted
                // character. WalkHandler mutates these; flushed once on disconnect.
                session.CurrentMap     = character.MapID;
                session.CurrentX       = (ushort)character.X;
                session.CurrentY       = (ushort)character.Y;
                session.PositionLoaded = true;

                // Valid token + existing char: ANSWER_OK then HeroInformation(1006).
                // Use SendGame (NOT Send) — game frames need the 8-byte seal + Blowfish.
                session.SendGame(MsgTalk.Build(ChatType.Entrance, "ANSWER_OK"));
                session.SendGame(HeroInformation.Build(character));
                Console.WriteLine("[Game] ANSWER_OK + 1006 sent");
            }
            else
            {
                // No char: signal the create screen. No create handler this spec (AC-7.2).
                session.SendGame(MsgTalk.Build(ChatType.Entrance, "NEW_ROLE"));
                Console.WriteLine($"[Game] No character for accountId={accountId}; NEW_ROLE sent (creation out of scope)");
            }
        }
    }
}
