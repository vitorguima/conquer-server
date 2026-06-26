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
            if (payload.Length < 12)
            {
                session.Disconnect();
                return;
            }

            ulong token = BinaryPrimitives.ReadUInt64LittleEndian(payload.AsSpan(4, 8));

            if (!TokenStore.TryConsume(token, out int accountId))
            {
                Console.WriteLine($"[Game] Invalid token {token} — disconnecting");
                session.Disconnect();
                return;
            }

            session.AccountId = accountId;
            session.Cipher.GenerateKeys(new object[] { token });
            session.IsAuthenticated = true;

            Console.WriteLine($"[Game] Connect accountId={accountId} token={token}");

            var character = _characters.FindByAccountId(accountId);
            if (character != null)
                SendMsgUserInfo(session, character);
            else
                Console.WriteLine($"[Game] No character for accountId={accountId}; character creation not yet implemented");
        }

        private void SendMsgUserInfo(ClientSession session, DbCharacter ch)
        {
            byte[] packet = MsgUserInfo.Build(ch);
            session.Send(packet);
        }
    }
}
