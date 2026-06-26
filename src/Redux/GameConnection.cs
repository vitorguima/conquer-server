using System;
using System.Buffers.Binary;
using Conquer.Crypto;
using Conquer.Network;

namespace Redux
{
    /// <summary>
    /// Server-first game-connection state machine for the 5065 game socket (:5816).
    ///
    /// Lifecycle (distinct from auth's <see cref="PacketRouter.ReadPacket"/> 2-byte-prefix
    /// TQCipher stream):
    ///   1. OnAccept → create the session's <see cref="GameCipher"/> (initial key) +
    ///      <see cref="ServerKeyExchange"/>, send the server-key packet (encrypted under
    ///      the initial key), state = AwaitingClientKey.
    ///   2. First inbound buffer → Blowfish-decrypt under the initial key →
    ///      <see cref="ServerKeyExchange.HandleClientKeyPacket"/> (derives the shared
    ///      secret, swaps the cipher key) → state = Established.
    ///   3. Established inbound → Blowfish-decrypt the whole buffer → split into
    ///      <c>ushort + 8</c> frames → <see cref="PacketRouter.Dispatch"/> per frame.
    /// Malformed / short / oversized input closes the session + logs; the listener
    /// loop survives (NFR-3, AC-2.5).
    /// </summary>
    public sealed class GameConnection
    {
        private readonly ClientSession _session;
        private readonly PacketRouter _router;
        private readonly GameCipher _cipher;

        public GameConnection(ClientSession session, PacketRouter router)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _router = router ?? throw new ArgumentNullException(nameof(router));

            // One GameCipher per game ClientSession — the SAME instance encrypts the
            // server-key packet (initial key), decrypts the client key buffer (initial
            // key), is SetKey'd to the DH secret by HandleClientKeyPacket, and then
            // decrypts/encrypts all subsequent game frames.
            _cipher = new GameCipher();
            _session.Cipher = _cipher;
            _session.Exchange = new ServerKeyExchange();
            _session.State = ExchangeState.AwaitingClientKey;
        }

        /// <summary>
        /// Server-first: build + send the server-key packet (encrypted under the
        /// initial Blowfish key) and enter AwaitingClientKey.
        /// </summary>
        public void OnAccept()
        {
            byte[] packet = _session.Exchange.CreateServerKeyPacket(_cipher);
            _session.Stream.Write(packet, 0, packet.Length);
            _session.State = ExchangeState.AwaitingClientKey;
            Console.WriteLine($"[Game] server-key packet sent (len={packet.Length})");
        }

        /// <summary>
        /// Drives the state machine for one inbound buffer (already read from the
        /// stream). Returns nothing; closes the session on malformed input.
        /// </summary>
        public void OnReceive(byte[] buffer, int length)
        {
            if (_session.State == ExchangeState.AwaitingClientKey)
            {
                HandleClientKey(buffer, length);
                return;
            }

            HandleFrames(buffer, length);
        }

        private void HandleClientKey(byte[] buffer, int length)
        {
            // Short / malformed pre-exchange buffer (AC-2.5).
            if (length <= 36)
            {
                Console.WriteLine($"[Game] client-key buffer too short ({length}) — disconnecting");
                _session.Disconnect();
                return;
            }

            // Decrypt under the INITIAL key, then derive + swap.
            _cipher.Decrypt(buffer, 0, length);

            // HandleClientKeyPacket inspects buffer.Length; pass an exact-size copy when
            // the read buffer is larger than the bytes received.
            byte[] keyBuf = buffer;
            if (length != buffer.Length)
            {
                keyBuf = new byte[length];
                Buffer.BlockCopy(buffer, 0, keyBuf, 0, length);
            }

            _session.Exchange.HandleClientKeyPacket(keyBuf, _cipher);
            _session.State = ExchangeState.Established;
            Console.WriteLine("[Game] CompleteExchange derived key");
        }

        private void HandleFrames(byte[] buffer, int length)
        {
            // Decrypt the whole received buffer once (stream-stateful cipher).
            _cipher.Decrypt(buffer, 0, length);

            int off = 0;
            while (off + 4 <= length)
            {
                int bodyLen = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(off, 2));
                int frameLen = bodyLen + 8; // header length field excludes the 8-byte seal
                int remaining = length - off;

                if (frameLen <= 0 || frameLen > remaining)
                {
                    Console.WriteLine($"[Game] oversized/garbled frame (bodyLen={bodyLen}, remaining={remaining}) — disconnecting");
                    _session.Disconnect();
                    return;
                }

                ushort typeId = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(off + 2, 2));

                // Mirror the auth payload contract: PacketRouter.Dispatch receives the
                // frame with the 2-byte length prefix stripped, so payload[0]=typeId,
                // payload[2]=first field — GameHandler offsets stay valid.
                int payloadLen = frameLen - 2;
                var payload = new byte[payloadLen];
                Buffer.BlockCopy(buffer, off + 2, payload, 0, payloadLen);
                _router.Dispatch(_session, typeId, payload);

                off += frameLen;
            }
        }
    }
}
