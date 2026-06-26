# Crypto Reference: TQCipher and RC5 from Comet@5017

Source: https://github.com/conquer-online/comet/tree/5017/src/Comet.Network/Security/
Branch: `5017` (not `main` or `master`)
Files: `ICipher.cs`, `TQCipher.cs`, `RC5.cs`

---

## ICipher.cs (verbatim)

```csharp
namespace Comet.Network.Security
{
    using System;

    /// <summary>
    /// Defines generalized methods for ciphers used by
    /// <see cref="Comet.Network.Sockets.TcpServerActor"/> and
    /// <see cref="Comet.Network.Sockets.TcpServerListener"/> for encrypting and decrypting
    /// data to and from the game client. Can be used to switch between ciphers easily for
    /// separate states of the game client connection.
    /// </summary>
    public interface ICipher
    {
        /// <summary>Generates keys using key derivation variables.</summary>
        /// <param name="seeds">Initialized seeds for generating keys</param>
        void GenerateKeys(object[] seeds);

        /// <summary>Decrypts data from the client</summary>
        /// <param name="src">Source span that requires decrypting</param>
        /// <param name="dst">Destination span to contain the decrypted result</param>
        void Decrypt(Span<byte> src, Span<byte> dst);

        /// <summary>Encrypts data to send to the client</summary>
        /// <param name="src">Source span that requires encrypting</param>
        /// <param name="dst">Destination span to contain the encrypted result</param>
        void Encrypt(Span<byte> src, Span<byte> dst);
    }
}
```

---

## TQCipher.cs (verbatim)

```csharp
namespace Comet.Network.Security
{
    using System;

    /// <summary>
    /// TQ Digital Entertainment's in-house asymmetric counter-based XOR-cipher. Counters
    /// are separated by encryption direction to create cipher streams. This implementation
    /// implements both directions for encrypting and decrypting data on the server side.
    /// </summary>
    /// <remarks>
    /// This cipher algorithm does not provide effective security, and does not make use
    /// of any NP-hard calculations for encryption or key generation. Key derivations are
    /// susceptible to brute-force or static key attacks. Only implemented for
    /// interoperability with the pre-existing game client. Do not use, otherwise.
    /// </remarks>
    public sealed class TQCipher : ICipher
    {
        // Static fields and properties
        private static byte[] KInit = new byte[0x200];

        // Local fields and properties
        private byte[] K;
        private byte[] K1 = new byte[0x200];
        private byte[] K2 = new byte[0x200];
        private ushort DecryptCounter, EncryptCounter;

        /// <summary>
        /// Add defines how the cipher increments counters. By default, counters are
        /// incremented without thread-safety for synchronous reads and writes.
        /// </summary>
        public Increment Add;

        /// <summary>
        /// Initializes static variables for <see cref="TQCipher"/>. Generates the static
        /// IV using a static, default seed for Conquer Online. Since the seed never
        /// changes across clients or instantiations, only needs to be computed once.
        /// </summary>
        static TQCipher()
        {
            var seed = new byte[] { 0x9D, 0x0F, 0xFA, 0x13, 0x62, 0x79, 0x5C, 0x6D };
            for (int i = 0; i < 0x100; i++)
            {
                TQCipher.KInit[i] = seed[0];
                TQCipher.KInit[i + 0x100] = seed[4];
                seed[0] = (byte)((seed[1] + (seed[0] * seed[2])) * seed[0] + seed[3]);
                seed[4] = (byte)((seed[5] - (seed[4] * seed[6])) * seed[4] + seed[7]);
            }
        }

        /// <summary>
        /// Instantiates a new instance of <see cref="TQCipher"/> using pregenerated
        /// IVs for initializing the cipher's keystreams. Initialized on each server
        /// to start communication. The game server will also require that keys are
        /// regenerated using key derivations from the client's first packet. Increments
        /// counters without thread-safety for synchronous reads and writes.
        /// </summary>
        public TQCipher()
        {
            this.Add = this.DefaultIncrement;
            Buffer.BlockCopy(TQCipher.KInit, 0, this.K1, 0, TQCipher.KInit.Length);
            Buffer.BlockCopy(TQCipher.KInit, 0, this.K2, 0, TQCipher.KInit.Length);
            this.K = this.K1;
        }

        /// <summary>
        /// Generates keys for the game server using the player's server access token
        /// as a key derivation variable. Invoked after the first packet is received on
        /// the game server.
        /// </summary>
        /// <param name="seeds">Array of seeds for generating keys</param>
        public void GenerateKeys(object[] seeds)
        {
            var seed = seeds[0] as ulong?;
            var a = (uint)(seed >> 32);
            var b = (uint)(seed);
            var c = (uint)(((a + b) ^ 0x4321) ^ a);
            var d = (uint)(c * c);
            var temp1 = BitConverter.GetBytes(c);
            var temp2 = BitConverter.GetBytes(d);
            for (int i = 0; i < 0x100; i++)
            {
                this.K2[i] = (byte)(this.K1[i] ^ temp1[i % 4]);
                this.K2[i + 0x100] = (byte)(this.K1[i + 0x100] ^ temp2[i % 4]);
            }
            this.K = this.K2;
            this.EncryptCounter = 0;
        }

        /// <summary>
        /// Decrypts the specified span by XORing the source span with the cipher's
        /// keystream. The source and destination may be the same slice, but otherwise
        /// should not overlap.
        /// </summary>
        /// <param name="src">Source span that requires decrypting</param>
        /// <param name="dst">Destination span to contain the decrypted result</param>
        public void Decrypt(Span<byte> src, Span<byte> dst)
        {
            this.XOR(src, dst, this.K, ref this.DecryptCounter);
        }

        /// <summary>
        /// Encrypt the specified span by XORing the source span with the cipher's
        /// keystream. The source and destination may be the same slice, but otherwise
        /// should not overlap.
        /// </summary>
        /// <param name="src">Source span that requires encrypting</param>
        /// <param name="dst">Destination span to contain the encrypted result</param>
        public void Encrypt(Span<byte> src, Span<byte> dst)
        {
            this.XOR(src, dst, this.K1, ref this.EncryptCounter);
        }

        /// <summary>
        /// XOR sets the destination span of bytes with the result of XORing the source
        /// span with the cipher's keystreams. The source and destination may be the same
        /// slice, but otherwise should not overlap.
        /// </summary>
        /// <param name="src">Source span that requires decrypting</param>
        /// <param name="dst">Destination span to contain the decrypted result</param>
        /// <param name="k">Keystream to be used for XORing data</param>
        /// <param name="c">Counter for the direction of the cipher operation</param>
        private void XOR(Span<byte> src, Span<byte> dst, byte[] k, ref ushort c)
        {
            var x = this.Add(ref c, src.Length);
            for (int i = 0; i < src.Length; i++)
            {
                dst[i] = (byte)(src[i] ^ 0xAB);
                dst[i] = (byte)(dst[i] >> 4 | dst[i] << 4);
                dst[i] = (byte)(dst[i] ^ k[x & 0xff]);
                dst[i] = (byte)(dst[i] ^ k[(x >> 8) + 0x100]);
                x++;
            }
        }

        /// <summary>
        /// Increments the counter used for decryption or encryption using the keystream.
        /// Allows the server to specify thread safety for parallel reads and writes, or
        /// use the default (non-thread safe) increment for synchronized reads and writes.
        /// </summary>
        /// <param name="x">Value to be incremented</param>
        /// <param name="n">Amount to increment by</param>
        /// <returns>Returns the previous value.</returns>
        public delegate ushort Increment(ref ushort x, int n);

        /// <summary>
        /// Increments without thread-safety for <see cref="TQCipher.Increment"/>
        /// </summary>
        /// <param name="x">Value to be incremented</param>
        /// <param name="n">Amount to increment by</param>
        /// <returns>Returns the previous value.</returns>
        public ushort DefaultIncrement(ref ushort x, int n)
            => (ushort)((x = (ushort)(x + n)) - n);
    }
}
```

---

## RC5.cs (verbatim)

```csharp
namespace Comet.Network.Security
{
    using System;
    using Comet.Core.Mathematics;

    /// <summary>
    /// Rivest Cipher 5 is implemented for interoperability with the Conquer Online game 
    /// client's login procedure. Passwords are encrypted in RC5 by the client, and decrypted
    /// on the server to be hashed and compared to the database saved password hash. In
    /// newer clients, this was replaced with SRP-6A (a hash based exchange protocol).
    /// </summary>
    public sealed class RC5 : ICipher
    {
        // Constants and static properties
        private const int WordSize = 16;
        private const int Rounds = 12;
        private const int KeySize = RC5.WordSize / 4;
        private const int SubSize = 2 * (RC5.Rounds + 1);

        // Local fields and properties
        private readonly uint[] Key, Sub;

        /// <summary>
        /// Initializes static variables for <see cref="RC5"/> to be interoperable with
        /// the Conquer Online game client. In later versions of the client, a random
        /// buffer is used to seed the cipher. This random buffer is sent to the client
        /// to establish a shared initial key.
        /// </summary>
        public RC5()
        {
            this.Key = new uint[RC5.KeySize];
            this.Sub = new uint[RC5.SubSize];
            this.GenerateKeys(new object[] { new byte[] { 
                0x3C, 0xDC, 0xFE, 0xE8, 0xC4, 0x54, 0xD6, 0x7E, 
                0x16, 0xA6, 0xF8, 0x1A, 0xE8, 0xD0, 0x38, 0xBE  
            } });
        }

        /// <summary>
        /// Generates keys and the subkey words for RC5 using a shared seed, whether
        /// that seed is shared statically or shared using a method of transport. Though
        /// only one seed is expected to generate keys, multiple may be used. Seed must be
        /// divisible by the selected cipher word size (16 bytes in this implementation).
        /// </summary>
        /// <param name="seeds">An array of seeds used to generate keys</param>
        public void GenerateKeys(object[] seeds)
        {
            // Initialize key expansion
            var seedBuffer = seeds[0] as byte[];
            var seedLength = seedBuffer.Length / RC5.WordSize * RC5.WordSize;
            for (int i = 0; i < RC5.KeySize; i++)
                this.Key[i] = BitConverter.ToUInt32(seedBuffer, i * 4);

            // Generate subkey words
            this.Sub[0] = 0xB7E15163;
            for (int i = 1; i < RC5.SubSize; i++)
                this.Sub[i] = this.Sub[i - 1] - 0x61C88647;

            // Generate key vector
            for (uint x = 0, i = 0, j = 0, a = 0, b = 0; x < 3 * RC5.SubSize; x++)
            {
                a = this.Sub[i] = (this.Sub[i] + a + b).RotateLeft(3);
                b = this.Key[j] = (this.Key[j] + a + b).RotateLeft((int)(a + b));
                i = (i + 1) % RC5.SubSize;
                j = (j + 1) % RC5.KeySize;
            }
        }

        /// <summary>
        /// Decrypts bytes from the client. If the buffer passed is not a multiple of
        /// the word size divisor in bytes, then pads the buffer with zeroes. The source 
        /// and destination may not be the same slice.
        /// </summary>
        /// <param name="src">Source span that requires decrypting</param>
        /// <param name="dst">Destination span to contain the decrypted result</param>
        public void Decrypt(Span<byte> src, Span<byte> dst)
        {
            // Pad the buffer
            var length = src.Length / 8;
            if (src.Length % 8 > 0) length = length + 1;
            src.CopyTo(dst);

            // Decrypt the buffer
            for (int word = 0; word < length; word++)
            {
                uint a = BitConverter.ToUInt32(dst[(8 * word)..]);
                uint b = BitConverter.ToUInt32(dst[(8 * word + 4)..]);
                for (int round = RC5.Rounds; round > 0; round--)
                {
                    b = (b - this.Sub[2 * round + 1]).RotateRight((int)a) ^ a;
                    a = (a - this.Sub[2 * round]).RotateRight((int)b) ^ b;
                }

                BitConverter.GetBytes(a - this.Sub[0]).CopyTo(dst[(8 * word)..]);
                BitConverter.GetBytes(b - this.Sub[1]).CopyTo(dst[(8 * word + 4)..]);
            }
        }

        /// <summary>
        /// Encrypts bytes from the server. If the buffer passed is not a multiple of the
        /// word size divisor in bytes, then pads the buffer with zeroes. The source 
        /// and destination may not be the same slice.
        /// </summary>
        /// <param name="src">Source span that requires encrypting</param>
        /// <param name="dst">Destination span to contain the encrypted result</param>
        public void Encrypt(Span<byte> src, Span<byte> dst)
        {
            // Pad the buffer
            var length = src.Length / 8;
            if (src.Length % 8 > 0) length = length + 1;
            dst = new byte[length * 8];
            src.CopyTo(dst[..src.Length]);

            // Encrypt the buffer
            for (int word = 0; word < length; word++)
            {
                uint a = BitConverter.ToUInt32(dst[(8 * word)..]) + this.Sub[0];
                uint b = BitConverter.ToUInt32(dst[(8 * word + 4)..]) + this.Sub[1];
                for (int round = 1; round <= RC5.Rounds; round++)
                {
                    a = (a ^ b).RotateLeft((int)b) + this.Sub[2 * round];
                    b = (b ^ a).RotateLeft((int)a) + this.Sub[2 * round + 1];
                }

                BitConverter.GetBytes(a).CopyTo(dst[(8 * word)..]);
                BitConverter.GetBytes(b).CopyTo(dst[(8 * word + 4)..]);
            }
        }
    }
}
```

---

## Notes for Redux Port

### TQCipher observations

- **Key table size**: Two 512-byte (0x200) tables — `K1` and `K2`. `KInit` (static) is also 512 bytes.
- **Static seed**: 8-byte seed `{ 0x9D, 0x0F, 0xFA, 0x13, 0x62, 0x79, 0x5C, 0x6D }` generates `KInit` at class-load time.
- **KInit generation**: Two-channel LFSR-like expansion. Lower 256 bytes from `seed[0]`, upper 256 bytes from `seed[4]`; each step updates the seed bytes via byte arithmetic.
- **Two modes**: Pre-auth uses `K1` for both encrypt and decrypt; post-auth switches decrypt to `K2` (derived from session token).
- **Key derivation** (`GenerateKeys`): Takes a `ulong?` session token. Derives `c = ((a+b) ^ 0x4321) ^ a`, `d = c*c`, then XORs `K1` with 4-byte LE representations of `c` and `d` across 256 entries.
- **XOR algorithm** (4 steps per byte):
  1. XOR byte with `0xAB`
  2. Rotate nibbles (`>> 4 | << 4`)
  3. XOR with `K[x & 0xff]` (lower table)
  4. XOR with `K[(x >> 8) + 0x100]` (upper table)
  - Counter `x` is a `ushort`; wraps naturally at 65536.
- **Asymmetric**: Decrypt uses active key (`K` = `K1` or `K2`); Encrypt always uses `K1`.
- **Not thread-safe by default**: `DefaultIncrement` is non-atomic; caller may substitute a thread-safe delegate via `Add`.

### RC5 observations

- **Variant**: RC5-32/12/16 — 32-bit words, 12 rounds, 16-byte (128-bit) key.
- **Constants**: WordSize=16, Rounds=12, KeySize=4 (uint[4]), SubSize=26 (uint[26]).
- **Hardcoded key**: `{ 0x3C, 0xDC, 0xFE, 0xE8, 0xC4, 0x54, 0xD6, 0x7E, 0x16, 0xA6, 0xF8, 0x1A, 0xE8, 0xD0, 0x38, 0xBE }` — static for all 5017-era clients.
- **Magic constants**: P32=`0xB7E15163`, Q32=`0x61C88647` (standard RC5 constants).
- **Block size**: 8 bytes (2 × 32-bit words); input is padded to 8-byte multiple.
- **Usage**: Decrypt only on server path (client encrypts password before sending MsgAccount); `Encrypt` is provided but not used in auth flow.
- **Dependency**: `RotateLeft`/`RotateRight` extension methods from `Comet.Core.Mathematics` — must be ported or reimplemented.
- **`src`/`dst` constraint**: Source and destination **may not** be the same slice (unlike TQCipher which allows it).

### Changes needed for Redux port

| Item | Change |
|------|--------|
| Namespace | `Comet.Network.Security` → `Redux.Cryptography` (or `Redux.Crypto`) |
| `ICipher` interface | Copy verbatim; rename namespace |
| `TQCipher` class | Copy verbatim; update namespace; no other logic changes |
| `RC5` class | Copy verbatim; update namespace; implement `RotateLeft`/`RotateRight` locally |
| `Comet.Core.Mathematics` | Port `RotateLeft(int)` and `RotateRight(int)` as extension methods on `uint` |
| Thread safety | Keep `DefaultIncrement`; one `TQCipher` per `ClientSession` (no sharing) |
| `GenerateKeys` seed type | `TQCipher` takes `ulong?` (session token); `RC5` takes `byte[]` (hardcoded) |
| Auth flow | RC5: decrypt incoming password field only; TQCipher: encrypt all auth-server replies |
| Game flow | TQCipher: call `GenerateKeys(token)` after receiving first game-server packet |

### RotateLeft/RotateRight stub needed

```csharp
// Must add to Redux.Crypto or a shared utility:
public static uint RotateLeft(this uint value, int n)
    => (value << n) | (value >> (32 - n));

public static uint RotateRight(this uint value, int n)
    => (value >> n) | (value << (32 - n));
```

### Key size summary

| Cipher | Key table | Algorithm | Rounds | Block |
|--------|-----------|-----------|--------|-------|
| TQCipher | 2 × 512 bytes (K1, K2) | XOR stream | N/A | 1 byte |
| RC5 | 16-byte input → 26 × uint subkeys | Feistel | 12 | 8 bytes |
