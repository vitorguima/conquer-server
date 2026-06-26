// Adapted from Comet@5017 (non-commercial/academic license)
// Source: https://github.com/conquer-online/comet/tree/5017/src/Comet.Network/Security/TQCipher.cs

namespace Conquer.Crypto
{
    using System;

    /// <summary>
    /// TQ Digital Entertainment's in-house asymmetric counter-based XOR-cipher.
    /// Counters are separated by encryption direction to create cipher streams.
    /// This implementation provides both directions for encrypting and decrypting
    /// data on the server side.
    /// </summary>
    /// <remarks>
    /// This cipher algorithm does not provide effective security and does not use
    /// any NP-hard calculations for encryption or key generation. Only implemented
    /// for interoperability with the pre-existing Conquer Online game client.
    /// Do not use for any other purpose.
    /// </remarks>
    public sealed class TQCipher : ICipher
    {
        // Static initialization vector — generated once from the static 8-byte seed.
        private static readonly byte[] KInit = new byte[0x200]; // 512 bytes

        // Per-instance key tables (512 bytes each).
        private byte[] _k1 = new byte[0x200];
        private byte[] _k2 = new byte[0x200];

        // Active key for decryption (switches to _k2 after GenerateKeys).
        private byte[] _activeKey;

        // Independent counters for each direction (wrap naturally as ushort).
        private ushort _decryptCounter;
        private ushort _encryptCounter;

        /// <summary>
        /// Class constructor: generates the static KInit table from the hardcoded
        /// 8-byte seed. Called once per AppDomain the first time TQCipher is used.
        /// </summary>
        static TQCipher()
        {
            // Static 8-byte seed identical to Comet@5017
            var seed = new byte[] { 0x9D, 0x0F, 0xFA, 0x13, 0x62, 0x79, 0x5C, 0x6D };
            for (int i = 0; i < 0x100; i++)
            {
                KInit[i]          = seed[0];
                KInit[i + 0x100]  = seed[4];
                // Two-channel LFSR-like expansion (verbatim from Comet@5017)
                seed[0] = (byte)((seed[1] + (seed[0] * seed[2])) * seed[0] + seed[3]);
                seed[4] = (byte)((seed[5] - (seed[4] * seed[6])) * seed[4] + seed[7]);
            }
        }

        /// <summary>
        /// Initialises a new TQCipher instance for a single client connection.
        /// Both K1 and K2 are seeded from KInit; the active decrypt key starts as K1.
        /// </summary>
        public TQCipher()
        {
            Buffer.BlockCopy(KInit, 0, _k1, 0, KInit.Length);
            Buffer.BlockCopy(KInit, 0, _k2, 0, KInit.Length);
            _activeKey = _k1;
        }

        /// <summary>
        /// Derives K2 from the session token received in MsgConnect (packet 1052).
        /// After this call, Decrypt uses K2 instead of K1. Encrypt always uses K1.
        /// </summary>
        /// <param name="seeds">seeds[0] must be a boxed ulong session token.</param>
        public void GenerateKeys(object[] seeds)
        {
            ulong token = (ulong)seeds[0];
            var a = (uint)(token >> 32);
            var b = (uint)(token);
            var c = (uint)(((a + b) ^ 0x4321) ^ a);
            var d = (uint)(c * c);

            var temp1 = BitConverter.GetBytes(c); // 4 bytes
            var temp2 = BitConverter.GetBytes(d); // 4 bytes

            for (int i = 0; i < 0x100; i++)
            {
                _k2[i]          = (byte)(_k1[i]          ^ temp1[i % 4]);
                _k2[i + 0x100]  = (byte)(_k1[i + 0x100]  ^ temp2[i % 4]);
            }

            _activeKey       = _k2;
            _encryptCounter  = 0;
        }

        /// <summary>
        /// Decrypts <paramref name="length"/> bytes of <paramref name="data"/> starting
        /// at <paramref name="offset"/> in-place using the active key (K1 pre-auth,
        /// K2 post-auth).
        /// </summary>
        public void Decrypt(byte[] data, int offset, int length)
        {
            XOR(data, offset, length, _activeKey, ref _decryptCounter);
        }

        /// <summary>
        /// Encrypts <paramref name="length"/> bytes of <paramref name="data"/> starting
        /// at <paramref name="offset"/> in-place using K1 (always K1 for outbound).
        /// </summary>
        public void Encrypt(byte[] data, int offset, int length)
        {
            XOR(data, offset, length, _k1, ref _encryptCounter);
        }

        /// <summary>
        /// Applies the 4-step XOR algorithm to each byte in the slice.
        /// The counter wraps naturally because it is a ushort.
        /// </summary>
        private static void XOR(byte[] data, int offset, int length, byte[] k, ref ushort counter)
        {
            // Advance counter by length and capture the previous value as the start index.
            ushort x = counter;
            counter = (ushort)(counter + length);

            for (int i = 0; i < length; i++)
            {
                byte b = data[offset + i];
                // Step 1: XOR with 0xAB
                b = (byte)(b ^ 0xAB);
                // Step 2: swap nibbles
                b = (byte)((b >> 4) | (b << 4));
                // Step 3: XOR with lower table using low byte of x
                b = (byte)(b ^ k[x & 0xFF]);
                // Step 4: XOR with upper table using high byte of x
                b = (byte)(b ^ k[(x >> 8) + 0x100]);
                data[offset + i] = b;
                x++;
            }
        }
    }
}
