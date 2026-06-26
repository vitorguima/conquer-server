using System;
using System.Text;
using Conquer.Crypto;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Agreement;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Xunit;

namespace Conquer.Crypto.Tests
{
    /// <summary>
    /// DH correctness (US-2 / A3) + server-key packet layout (AC-1.2).
    ///
    /// Dh_RoundTrip stands up the real server <see cref="ServerKeyExchange"/> and an
    /// independently-generated client DH keypair over the SAME DHParameters(P,G),
    /// then proves BOTH sides derive the identical shared-secret bytes — the server
    /// via its HandleClientKeyPacket-derived secret, the client via its own
    /// DHBasicAgreement against the server's public key. This exercises the exact
    /// ToByteArrayUnsigned() path (A3) the live handshake relies on.
    ///
    /// ServerKeyPacket_Layout builds the (encrypted) packet, decrypts it with a
    /// fresh GameCipher under the initial key, and asserts every fixed offset.
    /// </summary>
    public class DhExchangeTests
    {
        [Fact]
        public void Dh_RoundTrip()
        {
            var server = new ServerKeyExchange();
            DHParameters dhp = server.Parameters;

            // Simulate the client: an independent DH keypair over the same P/G.
            var gen = GeneratorUtilities.GetKeyPairGenerator("DH");
            gen.Init(new DHKeyGenerationParameters(new SecureRandom(), dhp));
            AsymmetricCipherKeyPair clientPair = gen.GenerateKeyPair();

            // Client public key, as uppercase ASCII hex (matches what the real
            // client sends, parsed by HandleClientKeyPacket).
            string clientPubHex =
                ((DHPublicKeyParameters)clientPair.Public).Y.ToString(16).ToUpperInvariant();

            // Server side: drive the cipher key-swap path; capture the derived secret.
            var cipher = new GameCipher();
            byte[] serverSecret = server.DeriveAndSwap(clientPubHex, cipher);
            Assert.Equal(serverSecret, server.SharedSecret);

            // Client side: independently compute the agreement against the server's
            // public key, the same ToByteArrayUnsigned() way.
            IBasicAgreement clientAgreement = AgreementUtilities.GetBasicAgreement("DH");
            clientAgreement.Init(clientPair.Private);
            var serverPub = new DHPublicKeyParameters(
                new BigInteger(server.ServerPublicKeyHex(), 16), dhp);
            byte[] clientSecret = clientAgreement.CalculateAgreement(serverPub).ToByteArrayUnsigned();

            // Both parties must derive the SAME shared secret.
            Assert.Equal(clientSecret, serverSecret);
            Assert.NotEmpty(serverSecret);
        }

        [Fact]
        public void ServerKeyPacket_Layout()
        {
            var exchange = new ServerKeyExchange();
            var cipher = new GameCipher();

            // CreateServerKeyPacket encrypts under the initial key; decrypt with a
            // fresh GameCipher (same initial key + zero IV) to inspect the layout.
            byte[] packet = exchange.CreateServerKeyPacket(cipher);

            var decryptor = new GameCipher();
            decryptor.Decrypt(packet, 0, packet.Length);

            int size = packet.Length;
            const int PAD_LENGTH = 11;

            // pad@0 (11 random) — skipped.
            // size - PAD @11 (int LE)
            Assert.Equal(size - PAD_LENGTH, ReadInt(packet, 11));
            // junkLen @15 == 12
            Assert.Equal(12, ReadInt(packet, 15));
            // junk@19 (12) — skipped.
            // clientIV.Length @31 == 8
            Assert.Equal(8, ReadInt(packet, 31));
            // serverIV.Length @43 == 8
            Assert.Equal(8, ReadInt(packet, 43));
            // P.Length @55 == 128
            Assert.Equal(128, ReadInt(packet, 55));

            // P @59 == 128-char uppercase hex of P.
            const string PHex =
                "E7A69EBDF105F2A6BBDEAD7E798F76A209AD73FB466431E2E7352ED262F8C558F10BEFEA977DE9E21DCEE9B04D245F300ECCBBA03E72630556D011023F9E857F";
            Assert.Equal(PHex, Encoding.ASCII.GetString(packet, 59, 128));

            // G.Length @187 == 2
            Assert.Equal(2, ReadInt(packet, 187));
            // G @191 == "05"
            Assert.Equal("05", Encoding.ASCII.GetString(packet, 191, 2));

            // pubKeyLen @193 == actual pubKey length.
            int pubKeyLen = ReadInt(packet, 193);
            string expectedPub = exchange.ServerPublicKeyHex();
            Assert.Equal(expectedPub.Length, pubKeyLen);
            // pubKey @197 == uppercase ASCII hex of the server public key.
            Assert.Equal(expectedPub, Encoding.ASCII.GetString(packet, 197, pubKeyLen));

            // "TQServer" trailer in the last 8 bytes.
            Assert.Equal("TQServer", Encoding.ASCII.GetString(packet, size - 8, 8));
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
