// TODO-M1: ManagedOpenSsl removed - using OpenSSL.Core;
// TODO-M1: ManagedOpenSsl removed - using OpenSSL.Crypto;
using System;

namespace Redux.Cryptography
{
    // TODO-M1: ManagedOpenSsl removed - ServerKeyExchange stubbed out
    public class ServerKeyExchange
    {
        public byte[] CreateServerKeyPacket()
        {
            throw new NotImplementedException("TODO-M1: ManagedOpenSsl removed - ServerKeyExchange not implemented");
        }

        public void HandleClientKeyPacket(string publicKey, ref GameCryptography crypto)
        {
            throw new NotImplementedException("TODO-M1: ManagedOpenSsl removed - ServerKeyExchange not implemented");
        }

        public byte[] GeneratePacket()
        {
            throw new NotImplementedException("TODO-M1: ManagedOpenSsl removed - ServerKeyExchange not implemented");
        }
    }
}
