using System;
using System.Text;
using Conquer.Crypto;
using Xunit;

namespace Conquer.Crypto.Tests
{
    /// <summary>
    /// A1 GATE — Blowfish-CFB64 byte-compatibility against an OpenSSL-equivalent
    /// ground-truth vector. This is the load-bearing crypto correctness gate
    /// (NFR-1 / AC-3.5). It must pass before any handshake wiring.
    ///
    /// GROUND-TRUTH SOURCE (NOT another managed CFB64 lib — that would be circular):
    /// the reference ciphertexts below were produced by the OpenSSL CLI inside the
    /// dockerized .NET 8 SDK toolchain (mcr.microsoft.com/dotnet/sdk:8.0, OpenSSL
    /// 3.0.20). Blowfish lives in OpenSSL 3.x's "legacy" provider, so the exact
    /// command was:
    ///
    ///   echo -n '&lt;plaintext&gt;' | openssl enc -bf-cfb \
    ///       -K 44523635346474333474726734554936 \
    ///       -iv 0000000000000000 \
    ///       -provider legacy -provider default | od -An -tx1 | tr -d ' '
    ///
    /// where the key hex 44523635346474333474726734554936 is ASCII
    /// "DR654dt34trg4UI6" (Common.ENCRYPTION_KEY, the initial game key) and the
    /// IV is 8 zero bytes. OpenSSL's "bf-cfb" IS Blowfish CFB-64 (full 64-bit /
    /// 8-byte feedback) — it is the only Blowfish CFB mode OpenSSL exposes
    /// (bf-cfb == bf-cfb64). Confirmed by re-running the command and observing
    /// identical output, and that bf-cfb1/bf-cfb8 are not provided.
    /// </summary>
    public class BlowfishCfb64Tests
    {
        // ASCII "DR654dt34trg4UI6" -> the initial Blowfish key used by GameCipher's ctor.
        private static readonly byte[] Key = Encoding.ASCII.GetBytes("DR654dt34trg4UI6");

        // Block-aligned 24-byte plaintext (3 full 8-byte blocks) — the primary KAT.
        // plaintext  = "Blowfish-CFB64 KAT v#1!!"
        // plain hex  = 426c6f77666973682d4346423634204b4154207623312121
        private const string KatPlaintext = "Blowfish-CFB64 KAT v#1!!";
        // openssl enc -bf-cfb -K <key> -iv 0 (legacy provider) reference:
        private const string KatCipherHex = "661b62b31a88901d556a69cfc068e179605939c49b2e596c";

        // 13-byte plaintext (1 full block + 5-byte partial tail) — exercises the
        // CFB-64 partial-block path so the trailing-segment handling is also gated.
        // plaintext  = "PartialTail13"
        private const string TailPlaintext = "PartialTail13";
        private const string TailCipherHex = "74167fb015808f21eaad60ee5e";

        // 64-byte (128-hex) Blowfish key — the LIVE DH shared secret. This gates the
        // full-length-key path that the real 5065 client uses (the post-exchange key).
        // BouncyCastle's BlowfishEngine rejected keys > 56 bytes, so GameCipher used to
        // truncate to 16 bytes → wrong key schedule → garbage decrypt. Conquer.Crypto.Blowfish
        // reproduces OpenSSL BF_set_key for the full key.
        private const string Key64Hex =
            "5CEFE509C75E740720814692166149FF675EB34996A253F7A06DEB7244F28B1B" +
            "F8E69C87A0759A9A2FEBC67D28F426BCAB6F74C387F3434A8855625E33A05E08";

        // GROUND TRUTH for the 64-byte key was produced by calling OpenSSL's legacy
        // Blowfish primitives DIRECTLY (NOT the `openssl enc` CLI). The CLI caps `-K`
        // at the EVP cipher's default 16-byte key length ("hex string is too long,
        // ignoring excess"), so it CANNOT exercise the long-key path. The reference
        // below comes from a tiny C program linked against libcrypto:
        //
        //   BF_KEY bk;
        //   BF_set_key(&bk, 64, key64);                 // FULL 64-byte key
        //   unsigned char iv[8] = {0};  int num = 0;
        //   BF_cfb64_encrypt(pt, out, len, &bk, iv, &num, BF_ENCRYPT);
        //
        // compiled+run inside mcr.microsoft.com/dotnet/sdk:8.0 (OpenSSL 3.0.x legacy
        // headers): gcc bfkat.c -o bfkat -lcrypto. This is exactly what the real
        // client does (full DH secret → BF_set_key → BF_cfb64_encrypt).
        private const string Kat64Plaintext = "Blowfish 64-byte DH key KAT v#2 spanning several blocks!!!";
        private const string Kat64CipherHex =
            "872c9339f06d190bcfd942c2c9a5caafe840446639baec2bd5ddca5fa04a2e4e" +
            "93d5a487ceabbc8c9b752d79499c4408b5a025d4e6a4ea9aea5e";

        [Fact]
        public void BlowfishCfb64_KAT()
        {
            byte[] data = Encoding.ASCII.GetBytes(KatPlaintext);
            var cipher = new GameCipher(); // ctor sets the initial key + zero IVs
            cipher.Encrypt(data, 0, data.Length);

            Assert.Equal(KatCipherHex, ToHex(data));
        }

        [Fact]
        public void BlowfishCfb64_KAT_64ByteKey()
        {
            // The LOAD-BEARING gate for this fix: GameCipher, re-keyed with the FULL
            // 64-byte DH secret, must match OpenSSL BF_set_key byte-for-byte. If this
            // fails the hand-rolled Blowfish key schedule / endianness is wrong.
            byte[] key = FromHex(Key64Hex);
            Assert.Equal(64, key.Length);

            byte[] data = Encoding.ASCII.GetBytes(Kat64Plaintext);
            var cipher = new GameCipher();
            cipher.SetKey(key); // full secret, no truncation
            cipher.Encrypt(data, 0, data.Length);

            Assert.Equal(Kat64CipherHex, ToHex(data));
        }

        [Fact]
        public void BlowfishCfb64_KAT_PartialTail()
        {
            byte[] data = Encoding.ASCII.GetBytes(TailPlaintext);
            var cipher = new GameCipher();
            cipher.Encrypt(data, 0, data.Length);

            Assert.Equal(TailCipherHex, ToHex(data));
        }

        [Fact]
        public void BlowfishCfb64_RoundTrip()
        {
            // Two independent ciphers with identical initial state stand in for the
            // server (encrypt) and the client-side decrypt path. Encrypting then
            // decrypting must recover the plaintext across MULTIPLE sequential calls,
            // proving the persistent per-direction feedback registers stay in sync
            // (feedback continuity).
            var server = new GameCipher();
            var client = new GameCipher();

            byte[][] messages =
            {
                Encoding.ASCII.GetBytes("first message over the wire"),
                Encoding.ASCII.GetBytes("2nd"),
                Encoding.ASCII.GetBytes("a considerably longer third payload spanning blocks"),
                Encoding.ASCII.GetBytes("x"),
            };

            foreach (byte[] msg in messages)
            {
                byte[] original = (byte[])msg.Clone();

                // server encrypts (server->client uses the ENC engine)...
                server.Encrypt(msg, 0, msg.Length);
                Assert.NotEqual(original, msg); // actually transformed

                // ...client decrypts the server->client stream. The client's DEC
                // engine mirrors the server's ENC engine (same key, same zero IV,
                // same running feedback), so it recovers the plaintext.
                client.Decrypt(msg, 0, msg.Length);
                Assert.Equal(original, msg);
            }
        }

        [Fact]
        public void BlowfishCfb64_SelfRoundTrip_AcrossCalls()
        {
            // Single GameCipher: Decrypt(Encrypt(x)) over its OWN two engines.
            // enc and dec are separate engines/IVs, so a single instance can both
            // encrypt the server->client direction and decrypt the client->server
            // direction; here we prove a paired enc/dec instance round-trips a
            // sequence (each engine advances independently and deterministically).
            var a = new GameCipher();
            var b = new GameCipher();

            for (int i = 1; i <= 5; i++)
            {
                byte[] payload = Encoding.ASCII.GetBytes(new string((char)('A' + i), i * 7));
                byte[] original = (byte[])payload.Clone();
                a.Encrypt(payload, 0, payload.Length);
                b.Decrypt(payload, 0, payload.Length);
                Assert.Equal(original, payload);
            }
        }

        private static byte[] FromHex(string hex)
        {
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return bytes;
        }

        private static string ToHex(byte[] data)
        {
            var sb = new StringBuilder(data.Length * 2);
            foreach (byte b in data)
                sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
