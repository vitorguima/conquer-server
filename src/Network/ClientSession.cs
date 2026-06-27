using System;
using System.Net.Sockets;
using Conquer.Crypto;

namespace Conquer.Network
{
    /// <summary>Which listener port / protocol this connection belongs to.</summary>
    public enum ConnKind
    {
        Auth,
        Game
    }

    /// <summary>Game-path DH handshake state (server-first).</summary>
    public enum ExchangeState
    {
        AwaitingClientKey,
        Established
    }

    public sealed class ClientSession
    {
        // Game-frame trailer ("TQServer"), stamped into the last 8 bytes of every
        // post-Established game packet. Mirrors Redux.Common.SERVER_SEAL, defined
        // locally because Network.csproj does not reference Redux.
        private static readonly byte[] SERVER_SEAL =
            System.Text.Encoding.ASCII.GetBytes("TQServer");

        public TcpClient TcpClient { get; }
        public NetworkStream Stream { get; }

        /// <summary>
        /// Active cipher: <see cref="TQCipher"/> for auth (:9958), <see cref="GameCipher"/>
        /// for game (:5816). Held via <see cref="ICipher"/> so the read/write path is
        /// cipher-agnostic (auth behavior unchanged — NFR-2).
        /// </summary>
        public ICipher Cipher { get; set; }

        /// <summary>Connection kind (set at accept). Defaults to Auth.</summary>
        public ConnKind Kind { get; set; } = ConnKind.Auth;

        /// <summary>Game-path handshake state. Defaults to AwaitingClientKey.</summary>
        public ExchangeState State { get; set; } = ExchangeState.AwaitingClientKey;

        /// <summary>DH key exchange (set for game connections only; null for auth).</summary>
        public ServerKeyExchange? Exchange { get; set; }

        public ulong SessionToken { get; set; }
        public int AccountId { get; set; }
        public bool IsAuthenticated { get; set; }

        /// <summary>Authenticated character, set at MsgConnect(1052); null until then or if none found.</summary>
        public Conquer.Database.DbCharacter? Character { get; set; }

        public ClientSession(TcpClient tcp)
        {
            TcpClient = tcp;
            Stream = tcp.GetStream();
            // Default to the auth cipher so auth construction is unchanged.
            Cipher = new TQCipher();
        }

        public void Send(byte[] packet)
        {
            if (!Stream.CanWrite) return;
            // TQCipher is a continuous counter-based stream cipher: the client decrypts
            // the entire inbound stream (length prefix included) from the very first byte.
            // So encrypt the WHOLE packet from offset 0, on every send, from connect.
            Cipher.Encrypt(packet, 0, packet.Length);
            Stream.Write(packet, 0, packet.Length);
        }

        /// <summary>
        /// Seal-aware game send (post-Established): allocates <c>len + 8</c>, copies the
        /// packet, stamps the last 8 bytes with the "TQServer" seal, Blowfish-encrypts the
        /// whole buffer (under the derived key), then writes (AC-4.1).
        /// </summary>
        public void SendGame(byte[] packet)
        {
            if (!Stream.CanWrite) return;
            int wholeLength = packet.Length + 8;
            var buffer = new byte[wholeLength];
            Buffer.BlockCopy(packet, 0, buffer, 0, packet.Length);
            Buffer.BlockCopy(SERVER_SEAL, 0, buffer, wholeLength - 8, 8);
            Cipher.Encrypt(buffer, 0, wholeLength);
            Stream.Write(buffer, 0, wholeLength);
        }

        public void Disconnect()
        {
            try { TcpClient.Close(); } catch { }
        }
    }
}
