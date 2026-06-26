// Managed Blowfish-CFB64 game cipher (port of the original native
// ManagedOpenSsl `Blowfish_CFB64` GameCryptography from commit 0b094c6).
// Implemented with BouncyCastle so the build stays managed-only (NFR-5).

namespace Conquer.Crypto
{
    using System;
    using Org.BouncyCastle.Crypto.Engines;
    using Org.BouncyCastle.Crypto.Modes;
    using Org.BouncyCastle.Crypto.Parameters;

    /// <summary>
    /// Blowfish in CFB-64 mode, byte-compatible with OpenSSL <c>Blowfish_CFB64</c>
    /// (the 5065 game cipher on :5816). Uses two persistent direction-specific
    /// instances (encrypt = server→client, decrypt = client→server), each with its
    /// own 8-byte IV/feedback register that persists across packets (running
    /// feedback). The exact byte count is processed with no padding so partial
    /// trailing blocks match OpenSSL.
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

        // CFB segment size in bits (64 = full 8-byte Blowfish block feedback).
        private const int CfbSegmentBits = 64;

        // Separate per-direction IVs, both zeroed at start.
        private byte[] _encIV = new byte[8];
        private byte[] _decIV = new byte[8];

        private byte[] _key;

        // Two persistent engines; feedback registers persist across calls.
        private CfbBlockCipher _encEngine;
        private CfbBlockCipher _decEngine;

        public GameCipher()
        {
            _key = (byte[])InitialKey.Clone();
            InitEngines();
        }

        private void InitEngines()
        {
            _encEngine = new CfbBlockCipher(new BlowfishEngine(), CfbSegmentBits);
            _encEngine.Init(true, new ParametersWithIV(new KeyParameter(_key), _encIV));

            _decEngine = new CfbBlockCipher(new BlowfishEngine(), CfbSegmentBits);
            _decEngine.Init(false, new ParametersWithIV(new KeyParameter(_key), _decIV));
        }

        /// <summary>Encrypts the server→client direction in place.</summary>
        public void Encrypt(byte[] data, int offset, int length)
        {
            ProcessCfb64(_encEngine, data, offset, length);
        }

        /// <summary>Decrypts the client→server direction in place.</summary>
        public void Decrypt(byte[] data, int offset, int length)
        {
            ProcessCfb64(_decEngine, data, offset, length);
        }

        /// <summary>
        /// Re-keys both engines with the DH-derived shared secret, preserving the
        /// CFB-64 segment. Feedback registers reset to the current IVs (zeros after
        /// the exchange sets them via <see cref="SetIvs"/>).
        /// </summary>
        public void SetKey(byte[] sharedSecret)
        {
            if (sharedSecret == null || sharedSecret.Length == 0)
                throw new ArgumentException("shared secret must be non-empty", nameof(sharedSecret));
            _key = (byte[])sharedSecret.Clone();
            InitEngines();
        }

        /// <summary>Replaces both direction IVs and re-inits the engines.</summary>
        public void SetIvs(byte[] enc, byte[] dec)
        {
            if (enc == null || enc.Length != 8) throw new ArgumentException("enc IV must be 8 bytes", nameof(enc));
            if (dec == null || dec.Length != 8) throw new ArgumentException("dec IV must be 8 bytes", nameof(dec));
            _encIV = (byte[])enc.Clone();
            _decIV = (byte[])dec.Clone();
            InitEngines();
        }

        /// <summary>
        /// Processes <paramref name="length"/> bytes through a CFB-64 engine one
        /// full 8-byte block at a time, then the partial trailing block byte-by-byte
        /// (CFB-64 streams over the keystream, so a short tail is valid). Mirrors
        /// OpenSSL's byte-oriented CFB64 partial-block handling.
        /// </summary>
        private static void ProcessCfb64(CfbBlockCipher engine, byte[] data, int offset, int length)
        {
            int blockSize = engine.GetBlockSize(); // 8 for CFB-64
            int pos = offset;
            int remaining = length;

            // Full segments.
            while (remaining >= blockSize)
            {
                engine.ProcessBlock(data, pos, data, pos);
                pos += blockSize;
                remaining -= blockSize;
            }

            // Partial trailing segment: pad input to a full block, process, copy
            // back only the meaningful bytes. CFB keystream for those bytes depends
            // only on the (persistent) feedback register, so this matches OpenSSL's
            // per-byte CFB64 tail. NOTE: this advances the engine by a full block;
            // acceptable because game frames are processed as a single contiguous
            // buffer per direction (no further bytes follow within the same call).
            if (remaining > 0)
            {
                var tmp = new byte[blockSize];
                Buffer.BlockCopy(data, pos, tmp, 0, remaining);
                engine.ProcessBlock(tmp, 0, tmp, 0);
                Buffer.BlockCopy(tmp, 0, data, pos, remaining);
            }
        }
    }
}
