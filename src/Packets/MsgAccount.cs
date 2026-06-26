using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Conquer.Crypto;
using Conquer.Database;
using Conquer.Network;
using Microsoft.Extensions.Configuration;

namespace Conquer.Packets
{
    public sealed class AuthHandler
    {
        private readonly AccountRepository _accounts;
        private readonly string _gameIp;
        private readonly ushort _gamePort;

        public AuthHandler(AccountRepository accounts, IConfiguration config)
        {
            _accounts = accounts;
            _gameIp   = config["GameServer:Ip"] ?? config["GameServerIP"] ?? string.Empty;
            _gamePort = ushort.TryParse(config["GameServer:Port"] ?? config["GamePort"], out var p) ? p : (ushort)0;
        }

        public void Handle(ClientSession session, byte[] payload)
        {
            string username = Encoding.Latin1.GetString(payload, 4, 16).TrimEnd('\0');

            // Decrypt RC5-encrypted password (bytes 20..35)
            var encPwd = new byte[16];
            Array.Copy(payload, 20, encPwd, 0, 16);
            var rc5 = new RC5();
            byte[] rawPwd = rc5.Decrypt(encPwd);
            string password = Encoding.Latin1.GetString(rawPwd).TrimEnd('\0');

            Console.WriteLine($"[Auth] username={username}");

            var account = _accounts.FindByUsername(username);
            if (account == null)
            {
                Console.WriteLine($"[Auth] FAIL user not found: {username}");
                SendAuthFail(session);
                session.Disconnect();
                return;
            }

            // Plain-text comparison — accounts.Password is varchar(16), no hash
            if (password != account.Password)
            {
                Console.WriteLine($"[Auth] FAIL bad password for: {username}");
                SendAuthFail(session);
                session.Disconnect();
                return;
            }

            ulong token = BinaryPrimitives.ReadUInt64LittleEndian(RandomNumberGenerator.GetBytes(8));
            TokenStore.Add(token, account.AccountId);
            Console.WriteLine($"[Auth] OK username={username} token={token}");

            byte[] response = MsgConnectEx.Build(token, _gameIp, _gamePort);
            session.Send(response);
            session.Disconnect();
        }

        private void SendAuthFail(ClientSession session)
        {
            // Token=0 signals rejection; client disconnects on receiving it
            byte[] reject = MsgConnectEx.Build(0, "0.0.0.0", 0);
            session.Send(reject);
        }
    }
}
