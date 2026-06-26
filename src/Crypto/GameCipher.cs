// Managed Blowfish-CFB64 game cipher (port of the original native
// ManagedOpenSsl `Blowfish_CFB64` GameCryptography from commit 0b094c6).
// Implemented with BouncyCastle so the build stays managed-only (NFR-5).

namespace Conquer.Crypto
{
    using System;
    using Org.BouncyCastle.Crypto;
    using Org.BouncyCastle.Crypto.Engines;
    using Org.BouncyCastle.Crypto.Parameters;

    /// <summary>
    /// Blowfish in CFB-64 mode, byte-compatible with OpenSSL <c>Blowfish_CFB64</c>
    /// (the 5065 game cipher on :5816).
    ///
    /// Implementation note (A1 / NFR-1): this uses the documented <b>hand-rolled
    /// CFB-64 loop</b> over a bare <see cref="BlowfishEngine"/> rather than
    /// BouncyCastle's <c>CfbBlockCipher(64)</c>. The library mode matches OpenSSL
    /// for single, block-aligned calls (the KAT), but its block-granular API does
    /// NOT replicate OpenSSL's byte-oriented partial-block streaming across
    /// successive packets of arbitrary length — the game wire carries frames of
    /// any byte count, so the cipher must keep a persistent <i>position</i> inside
    /// the 8-byte feedback register between calls. The hand-rolled loop is the
    /// canonical, byte-defined CFB-64 algorithm (BF_cfb64_encrypt):
    ///
    ///   for each byte:
    ///     if position == 0: register = BlowfishEcbEncrypt(register)
    ///     out  = in XOR register[position]
    ///     register[position] = (encrypt ? out : in)   // shift cipher byte in
    ///     position = (position + 1) &amp; 7
    ///
    /// Two persistent direction-specific engines/registers are kept (encrypt =
    /// server→client, decrypt = client→server), each with its own 8-byte register
    /// and position that persist across packets (running feedback). No padding —
    /// the exact byte count is processed, so partial trailing blocks match OpenSSL
    /// byte-for-byte (gated by BlowfishCfb64_KAT / _PartialTail / _RoundTrip).
    ///
    /// The cipher swaps its key mid-connection: the server-key packet is encrypted
    /// under the initial key "DR654dt34trg4UI6"; after the DH exchange both engines
    /// are re-keyed via <see cref="SetKey"/> with the derived shared secret.
    /// </summary>
    public sealed class GameCipher : ICipher
    {
        /// <summary>Initial Blowfish key (Common.ENCRYPTION_KEY), ASCII, 16 bytes.</summary>
        private static readonly byte[] InitialKey =
            System.Text.Encoding.ASCII.GetBytes("DR654dt34trg4UI6");

        private byte[] _key;

        // Per-direction ECB engines used as the CFB-64 keystream generator.
        private IBlockCipher _encEngine;
        private IBlockCipher _decEngine;

        // Per-direction feedback registers (8 bytes) + position within the register.
        // Separate registers per direction, both zeroed at start.
        private readonly byte[] _encReg = new byte[8];
        private readonly byte[] _decReg = new byte[8];
        private int _encPos;
        private int _decPos;

        public GameCipher()
        {
            _key = (byte[])InitialKey.Clone();
            InitEngines();
        }

        private void InitEngines()
        {
            _encEngine = new BlowfishEngine();
            _encEngine.Init(true, new KeyParameter(_key)); // ECB-encrypt the register

            _decEngine = new BlowfishEngine();
            _decEngine.Init(true, new KeyParameter(_key)); // CFB always ECB-ENCRYPTs

            _encPos = 0;
            _decPos = 0;
        }

        /// <summary>Encrypts the server→client direction in place.</summary>
        public void Encrypt(byte[] data, int offset, int length)
        {
            Cfb64(_encEngine, _encReg, ref _encPos, data, offset, length, encrypting: true);
        }

        /// <summary>Decrypts the client→server direction in place.</summary>
        public void Decrypt(byte[] data, int offset, int length)
        {
            Cfb64(_decEngine, _decReg, ref _decPos, data, offset, length, encrypting: false);
        }

        /// <summary>Effective Blowfish key length in bytes — OpenSSL <c>EVP_bf_cfb</c>
        /// uses its default 128-bit (16-byte) key, so only the leading 16 bytes of the
        /// key material are consumed (matches the original native GameCryptography,
        /// which loaded the DH secret into a 128-byte buffer fed to EVP at the default
        /// 16-byte key length, and is consistent with the 16-byte initial key KAT).</summary>
        private const int KeyLength = 16;

        /// <summary>
        /// Re-keys both engines with the DH-derived shared secret. Resets both
        /// feedback registers (zeroed) and positions; IVs default to zero unless
        /// subsequently overridden via <see cref="SetIvs"/>.
        ///
        /// The shared secret can exceed Blowfish's key range; only the leading
        /// <see cref="KeyLength"/> (16) bytes are used — OpenSSL's effective bf-cfb
        /// key length and the byte-compatible behaviour of the original cipher.
        /// </summary>
        public void SetKey(byte[] sharedSecret)
        {
            if (sharedSecret == null || sharedSecret.Length == 0)
                throw new ArgumentException("shared secret must be non-empty", nameof(sharedSecret));

            int len = Math.Min(sharedSecret.Length, KeyLength);
            var key = new byte[len];
            Buffer.BlockCopy(sharedSecret, 0, key, 0, len);
            _key = key;

            Array.Clear(_encReg, 0, _encReg.Length);
            Array.Clear(_decReg, 0, _decReg.Length);
            InitEngines();
        }

        /// <summary>
        /// Replaces both direction feedback registers (IVs) and resets positions.
        /// Both IVs are 8 bytes (sent zeroed in the server-key packet, AC-3.3).
        /// </summary>
        public void SetIvs(byte[] enc, byte[] dec)
        {
            if (enc == null || enc.Length != 8) throw new ArgumentException("enc IV must be 8 bytes", nameof(enc));
            if (dec == null || dec.Length != 8) throw new ArgumentException("dec IV must be 8 bytes", nameof(dec));
            Buffer.BlockCopy(enc, 0, _encReg, 0, 8);
            Buffer.BlockCopy(dec, 0, _decReg, 0, 8);
            _encPos = 0;
            _decPos = 0;
        }

        /// <summary>
        /// Canonical OpenSSL-equivalent CFB-64 stream loop. The position within the
        /// 8-byte register persists across calls so arbitrary byte counts stream
        /// correctly (matches OpenSSL BF_cfb64_encrypt).
        /// </summary>
        private static void Cfb64(
            IBlockCipher engine, byte[] reg, ref int pos,
            byte[] data, int offset, int length, bool encrypting)
        {
            int p = pos;
            for (int i = 0; i < length; i++)
            {
                if (p == 0)
                    engine.ProcessBlock(reg, 0, reg, 0); // ECB-encrypt the register in place

                int idx = offset + i;
                byte inByte = data[idx];
                byte outByte = (byte)(inByte ^ reg[p]);
                data[idx] = outByte;

                // Feed the CIPHERTEXT byte back into the register.
                reg[p] = encrypting ? outByte : inByte;

                p = (p + 1) & 7;
            }
            pos = p;
        }
    }
}
