// Adapted from Comet@5017 (non-commercial/academic license)
// Source: https://github.com/conquer-online/comet/tree/5017/src/Comet.Network/Security/RC5.cs

namespace Conquer.Crypto
{
    using System;

    /// <summary>
    /// Rivest Cipher 5 implemented for interoperability with the Conquer Online game
    /// client's login procedure. Passwords are encrypted in RC5 by the client, and
    /// decrypted on the server to be hashed and compared to the stored password hash.
    /// Variant: RC5-32/12/16 (32-bit words, 12 rounds, 16-byte key).
    /// </summary>
    public sealed class RC5
    {
        // RC5-32/12/16 constants
        private const int WordSize = 16; // key bytes
        private const int Rounds   = 12;
        private const int KeySize  = WordSize / 4; // 4 uint words
        private const int SubSize  = 2 * (Rounds + 1); // 26 subkey words

        // Magic constants (P32, Q32)
        private const uint P32 = 0xB7E15163u;
        private const uint Q32 = 0x61C88647u;

        // Hardcoded 16-byte key (identical to Comet@5017)
        private static readonly byte[] HardcodedKey = new byte[]
        {
            0x3C, 0xDC, 0xFE, 0xE8, 0xC4, 0x54, 0xD6, 0x7E,
            0x16, 0xA6, 0xF8, 0x1A, 0xE8, 0xD0, 0x38, 0xBE
        };

        private readonly uint[] _sub; // expanded subkey array [SubSize]

        /// <summary>
        /// Initializes RC5 with the hardcoded Conquer Online 5017-era key.
        /// </summary>
        public RC5()
        {
            _sub = new uint[SubSize];
            ExpandKey(HardcodedKey);
        }

        /// <summary>
        /// Performs RC5 key expansion from a 16-byte seed into the subkey table.
        /// </summary>
        private void ExpandKey(byte[] key)
        {
            var k = new uint[KeySize];
            for (int i = 0; i < KeySize; i++)
                k[i] = BitConverter.ToUInt32(key, i * 4);

            // Initialize subkey table with magic constants
            _sub[0] = P32;
            for (int i = 1; i < SubSize; i++)
                _sub[i] = _sub[i - 1] - Q32;

            // Mix key into subkey table (3 * SubSize iterations)
            uint a = 0, b = 0;
            int ii = 0, jj = 0;
            for (int x = 0; x < 3 * SubSize; x++)
            {
                a = _sub[ii] = RotateLeft(_sub[ii] + a + b, 3);
                b = k[jj]    = RotateLeft(k[jj] + a + b, (int)(a + b));
                ii = (ii + 1) % SubSize;
                jj = (jj + 1) % KeySize;
            }
        }

        /// <summary>
        /// Decrypts <paramref name="data"/> using RC5-32/12/16.
        /// Input length must be a multiple of 8 bytes (one RC5 block = 8 bytes).
        /// Returns a new byte array with the decrypted plaintext.
        /// </summary>
        public byte[] Decrypt(byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            // Pad to 8-byte boundary
            int blocks = data.Length / 8;
            if (data.Length % 8 != 0) blocks++;

            var dst = new byte[blocks * 8];
            Array.Copy(data, dst, data.Length);

            for (int word = 0; word < blocks; word++)
            {
                uint a = BitConverter.ToUInt32(dst, 8 * word);
                uint b = BitConverter.ToUInt32(dst, 8 * word + 4);

                for (int round = Rounds; round > 0; round--)
                {
                    b = RotateRight(b - _sub[2 * round + 1], (int)a) ^ a;
                    a = RotateRight(a - _sub[2 * round],     (int)b) ^ b;
                }

                Array.Copy(BitConverter.GetBytes(a - _sub[0]), 0, dst, 8 * word,     4);
                Array.Copy(BitConverter.GetBytes(b - _sub[1]), 0, dst, 8 * word + 4, 4);
            }

            return dst;
        }

        // Rotate helpers (operate on uint / 32-bit words)
        private static uint RotateLeft(uint value, int n)
        {
            n &= 31; // keep rotation in [0..31]
            return (value << n) | (value >> (32 - n));
        }

        private static uint RotateRight(uint value, int n)
        {
            n &= 31;
            return (value >> n) | (value << (32 - n));
        }
    }
}
