using System;
using System.Net.Sockets;
using Conquer.Crypto;

namespace Conquer.Network
{
    public sealed class ClientSession
    {
        public TcpClient TcpClient { get; }
        public NetworkStream Stream { get; }
        public TQCipher Cipher { get; }
        public ulong SessionToken { get; set; }
        public int AccountId { get; set; }
        public bool IsAuthenticated { get; set; }

        public ClientSession(TcpClient tcp)
        {
            TcpClient = tcp;
            Stream = tcp.GetStream();
            Cipher = new TQCipher();
        }

        public void Send(byte[] packet)
        {
            if (!Stream.CanWrite) return;
            if (IsAuthenticated && packet.Length > 2)
                Cipher.Encrypt(packet, 2, packet.Length - 2);  // skip length prefix, encrypt rest
            Stream.Write(packet, 0, packet.Length);
        }

        public void Disconnect()
        {
            try { TcpClient.Close(); } catch { }
        }
    }
}
