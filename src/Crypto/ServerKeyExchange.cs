// Managed DH key exchange + server-key packet build/parse (port of the original
// native ManagedOpenSsl `ServerKeyExchange` from commit 0b094c6). Implemented
// with BouncyCastle so the build stays managed-only (NFR-5).

namespace Conquer.Crypto
{
    using System;
    using System.Text;
    using Org.BouncyCastle.Crypto;
    using Org.BouncyCastle.Crypto.Agreement;
    using Org.BouncyCastle.Crypto.Generators;
    using Org.BouncyCastle.Crypto.Parameters;
    using Org.BouncyCastle.Math;
    using Org.BouncyCastle.Security;

    /// <summary>
    /// Server-first Diffie-Hellman key exchange for the 5065 game socket (:5816).
    ///
    /// On accept the server sends <see cref="CreateServerKeyPacket"/> (DH P/G +
    /// server public key, encrypted under the initial Blowfish key). The client
    /// replies with its DH public key; <see cref="HandleClientKeyPacket"/> derives
    /// the shared secret and swaps the game cipher key (AwaitingClientKey →
    /// Established).
    ///
    /// Managed mapping of the original (ManagedOpenSsl <c>DH</c>) to BouncyCastle:
    /// <c>DHParameters(P,G)</c> + <c>GeneratorUtilities.GetKeyPairGenerator("DH")</c>
    /// for key generation, and <c>AgreementUtilities.GetBasicAgreement("DH")</c>
    /// (DHBasicAgreement) for <c>ComputeKey</c>. The shared secret is taken as-is
    /// via <c>ToByteArrayUnsigned()</c> (A3 — no truncation / leading-zero fix).
    /// </summary>
    public sealed class ServerKeyExchange
    {
        // DH parameters — verbatim from the original source (0b094c6).
        private const string PHex =
            "E7A69EBDF105F2A6BBDEAD7E798F76A209AD73FB466431E2E7352ED262F8C558F10BEFEA977DE9E21DCEE9B04D245F300ECCBBA03E72630556D011023F9E857F";
        private const string GHex = "05";

        private const int PAD_LENGTH = 11;
        private const int JUNK_LENGTH = 12;
        private const string TQSERVER = "TQServer";

        private readonly DHParameters _dhp;
        private readonly AsymmetricCipherKeyPair _pair;
        private readonly IBasicAgreement _agreement;

        // Both IVs are sent zeroed in the server-key packet (AC-3.3).
        private readonly byte[] _clientIV = new byte[8];
        private readonly byte[] _serverIV = new byte[8];

        private readonly SecureRandom _random = new SecureRandom();

        public ServerKeyExchange()
        {
            var p = new BigInteger(PHex, 16);
            var g = new BigInteger(GHex, 16);
            _dhp = new DHParameters(p, g);

            var gen = GeneratorUtilities.GetKeyPairGenerator("DH");
            gen.Init(new DHKeyGenerationParameters(_random, _dhp));
            _pair = gen.GenerateKeyPair();

            _agreement = AgreementUtilities.GetBasicAgreement("DH"); // DHBasicAgreement
            _agreement.Init(_pair.Private);
        }

        /// <summary>The shared secret derived in <see cref="HandleClientKeyPacket"/>, or null before the exchange completes.</summary>
        public byte[]? SharedSecret { get; private set; }

        /// <summary>
        /// Builds the server-key packet (exact original layout) and encrypts the
        /// whole buffer under the cipher's <b>initial</b> key before returning
        /// (AC-1.1). The cipher must not yet have been re-keyed via SetKey.
        /// </summary>
        public byte[] CreateServerKeyPacket(GameCipher cipher)
        {
            if (cipher == null) throw new ArgumentNullException(nameof(cipher));

            byte[] packet = BuildServerKeyPacket();
            cipher.Encrypt(packet, 0, packet.Length);
            return packet;
        }

        /// <summary>
        /// Builds the unencrypted server-key packet (exposed for layout assertions
        /// and reused by <see cref="CreateServerKeyPacket"/>).
        /// </summary>
        public byte[] BuildServerKeyPacket()
        {
            var pad = new byte[PAD_LENGTH];
            var junk = new byte[JUNK_LENGTH];
            _random.NextBytes(pad);
            _random.NextBytes(junk);

            // P and G are written as UPPERCASE ASCII hex strings (not raw bytes).
            string pStr = PHex;        // already uppercase, 128 chars
            string gStr = GHex;        // "05"
            string pubKey = ServerPublicKeyHex();

            byte[] pBytes = Encoding.ASCII.GetBytes(pStr);
            byte[] gBytes = Encoding.ASCII.GetBytes(gStr);
            byte[] pubBytes = Encoding.ASCII.GetBytes(pubKey);

            // size = 28 (7 * 4-byte ints) + PAD + JUNK + clientIV + serverIV + P + G + pubKey + 8 (TQServer)
            int size = 28 + PAD_LENGTH + JUNK_LENGTH + _clientIV.Length + _serverIV.Length
                       + pBytes.Length + gBytes.Length + pubBytes.Length + 8;

            var buffer = new byte[size];
            int offset = 0;

            Buffer.BlockCopy(pad, 0, buffer, offset, PAD_LENGTH);
            offset += PAD_LENGTH;

            WriteInt(buffer, ref offset, size - PAD_LENGTH);
            WriteInt(buffer, ref offset, JUNK_LENGTH);

            Buffer.BlockCopy(junk, 0, buffer, offset, JUNK_LENGTH);
            offset += JUNK_LENGTH;

            WriteInt(buffer, ref offset, _clientIV.Length);
            Buffer.BlockCopy(_clientIV, 0, buffer, offset, _clientIV.Length);
            offset += _clientIV.Length;

            WriteInt(buffer, ref offset, _serverIV.Length);
            Buffer.BlockCopy(_serverIV, 0, buffer, offset, _serverIV.Length);
            offset += _serverIV.Length;

            WriteInt(buffer, ref offset, pBytes.Length);
            Buffer.BlockCopy(pBytes, 0, buffer, offset, pBytes.Length);
            offset += pBytes.Length;

            WriteInt(buffer, ref offset, gBytes.Length);
            Buffer.BlockCopy(gBytes, 0, buffer, offset, gBytes.Length);
            offset += gBytes.Length;

            WriteInt(buffer, ref offset, pubBytes.Length);
            Buffer.BlockCopy(pubBytes, 0, buffer, offset, pubBytes.Length);
            offset += pubBytes.Length;

            byte[] trailer = Encoding.ASCII.GetBytes(TQSERVER);
            Buffer.BlockCopy(trailer, 0, buffer, offset, trailer.Length);

            return buffer;
        }

        /// <summary>
        /// Parses the (already Blowfish-decrypted) client-key buffer, derives the
        /// shared secret, and swaps the cipher key (AC-2.1–2.3). Buffers of
        /// <c>length &lt;= 36</c> are rejected (AC-2.5).
        /// </summary>
        public void HandleClientKeyPacket(byte[] buffer, GameCipher cipher)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (cipher == null) throw new ArgumentNullException(nameof(cipher));
            if (buffer.Length <= 36)
                throw new ArgumentException("client-key buffer too short", nameof(buffer));

            // Original CompleteExchange framing:
            //   length@7(int), junk@11(int), pubKeyLen@(15+junk)(int), pubKey@(19+junk)
            int junk = ReadInt(buffer, 11);
            int pubKeyLenOffset = 15 + junk;
            int pubKeyLen = ReadInt(buffer, pubKeyLenOffset);
            int pubKeyOffset = 19 + junk;

            if (pubKeyLen < 0 || pubKeyOffset + pubKeyLen > buffer.Length)
                throw new ArgumentException("client public key out of range", nameof(buffer));

            string pubKeyHex = Encoding.ASCII.GetString(buffer, pubKeyOffset, pubKeyLen);
            DeriveAndSwap(pubKeyHex, cipher);
        }

        /// <summary>
        /// Derives the shared secret from the client's public key (ASCII hex) and
        /// re-keys the game cipher (key swap + zero IVs). Exposed so callers/tests
        /// can drive the agreement directly.
        /// </summary>
        public byte[] DeriveAndSwap(string clientPublicKeyHex, GameCipher cipher)
        {
            if (cipher == null) throw new ArgumentNullException(nameof(cipher));

            byte[] secret = ComputeSharedSecret(clientPublicKeyHex);
            SharedSecret = secret;
            cipher.SetKey(secret);
            cipher.SetIvs(_clientIV, _serverIV);
            return secret;
        }

        /// <summary>
        /// Computes the DH shared secret from the client's public key (ASCII hex),
        /// returned via <c>ToByteArrayUnsigned()</c> (A3).
        /// </summary>
        public byte[] ComputeSharedSecret(string clientPublicKeyHex)
        {
            var clientY = new BigInteger(clientPublicKeyHex, 16);
            var clientPub = new DHPublicKeyParameters(clientY, _dhp);
            return _agreement.CalculateAgreement(clientPub).ToByteArrayUnsigned();
        }

        /// <summary>The DH parameters (P, G) used by this exchange.</summary>
        public DHParameters Parameters => _dhp;

        /// <summary>
        /// Server public key as an uppercase ASCII hex string (padded to an even
        /// length to match the original ManagedOpenSsl <c>ToHexString()</c>).
        /// </summary>
        public string ServerPublicKeyHex()
        {
            string hex = ((DHPublicKeyParameters)_pair.Public).Y.ToString(16).ToUpperInvariant();
            if ((hex.Length & 1) == 1)
                hex = "0" + hex;
            return hex;
        }

        private static void WriteInt(byte[] buffer, ref int offset, int value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
            offset += 4;
        }

        private static int ReadInt(byte[] buffer, int offset)
        {
            return buffer[offset]
                   | (buffer[offset + 1] << 8)
                   | (buffer[offset + 2] << 16)
                   | (buffer[offset + 3] << 24);
        }
    }
}
