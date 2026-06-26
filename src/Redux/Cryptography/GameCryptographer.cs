using System;
// TODO-M1: ManagedOpenSsl removed - using OpenSSL.Crypto;
// TODO-M1: ManagedOpenSsl removed - using OpenSSL.Crypto.EC;

namespace Redux.Cryptography
{
    // TODO-M1: ManagedOpenSsl removed - GameCryptography stubbed out
    public class GameCryptography
    {
        byte[] _key = new byte[128];
        byte[] _encryptIV = new byte[8];
        byte[] _decryptIV = new byte[8];

        public GameCryptography(byte[] key)
        {
            Buffer.BlockCopy(key, 0, _key, 0, key.Length);
        }

        public void Decrypt(byte[] packet)
        {
            throw new NotImplementedException("TODO-M1: ManagedOpenSsl removed - GameCryptography not implemented");
        }

        public void Encrypt(byte[] packet)
        {
            throw new NotImplementedException("TODO-M1: ManagedOpenSsl removed - GameCryptography not implemented");
        }

        public void SetKey(byte[] key)
        {
            Buffer.BlockCopy(key, 0, _key, 0, key.Length);
        }

        public void SetIvs(byte[] i1, byte[] i2)
        {
            Buffer.BlockCopy(i1, 0, _encryptIV, 0, i1.Length);
            Buffer.BlockCopy(i2, 0, _decryptIV, 0, i2.Length);
        }
    }
}
