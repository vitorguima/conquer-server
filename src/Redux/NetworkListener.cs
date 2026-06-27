using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Conquer.Database;
using Conquer.Network;
using Microsoft.Extensions.Configuration;

namespace Redux
{
    public sealed class NetworkListener
    {
        private readonly IConfiguration _config;
        private readonly PacketRouter _router;
        private readonly CharacterRepository _characters;

        public NetworkListener(IConfiguration config, PacketRouter router, CharacterRepository characters)
        {
            _config = config;
            _router = router;
            _characters = characters;
        }

        public async Task RunAuthAsync(CancellationToken ct)
        {
            int port = int.TryParse(_config["AuthPort"], out var ap) ? ap : 9958;
            var listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            Console.WriteLine($"[Startup] Auth listening on :{port}");
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                    _ = Task.Run(() => ServeClientAsync(client, ct), ct);
                }
            }
            finally
            {
                listener.Stop();
            }
        }

        public async Task RunGameAsync(CancellationToken ct)
        {
            int port = int.TryParse(_config["GamePort"], out var gp) ? gp : 5816;
            var listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            Console.WriteLine($"[Startup] Game listening on :{port}");
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                    _ = Task.Run(() => ServeGameAsync(client, ct), ct);
                }
            }
            finally
            {
                listener.Stop();
            }
        }

        // Auth serve loop — byte-for-byte unchanged (NFR-2): 2-byte-prefix TQCipher
        // stream via PacketRouter.ReadPacket. Game connections use ServeGameAsync.
        private async Task ServeClientAsync(TcpClient tcp, CancellationToken ct)
        {
            var session = new ClientSession(tcp);
            string endpoint = tcp.Client?.RemoteEndPoint?.ToString() ?? "unknown";
            string local = tcp.Client?.LocalEndPoint?.ToString() ?? "?";
            Console.WriteLine($"[Connect] {endpoint} -> {local}");
            ushort typeId = 0;
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    (typeId, var payload) = _router.ReadPacket(session);
                    _router.Dispatch(session, typeId, payload);
                }
            }
            catch (EndOfStreamException)
            {
                // clean client disconnect
            }
            catch (ObjectDisposedException)
            {
                // session was closed by a handler (e.g. auth redirect) — clean
            }
            catch (IOException ex)
            {
                Console.WriteLine($"[Error] {endpoint} typeId={typeId} IO: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error] {endpoint} typeId={typeId} {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                session.Disconnect();
                Console.WriteLine($"[Disconnect] {endpoint}");
            }
            await Task.CompletedTask.ConfigureAwait(false);
        }

        // Game serve loop (:5816, Kind=Game, server-first). Sends the server-key packet
        // on accept, then drives the GameConnection state machine. Distinct from the auth
        // ServeClientAsync loop / ReadPacket (AC-4.2/4.3, NFR-3).
        private async Task ServeGameAsync(TcpClient tcp, CancellationToken ct)
        {
            var session = new ClientSession(tcp) { Kind = ConnKind.Game };
            string endpoint = tcp.Client?.RemoteEndPoint?.ToString() ?? "unknown";
            string local = tcp.Client?.LocalEndPoint?.ToString() ?? "?";
            Console.WriteLine($"[Connect] (game) {endpoint} -> {local}");

            var connection = new GameConnection(session, _router);
            var buffer = new byte[8192];
            try
            {
                // Server-first: send the server-key packet immediately on accept.
                connection.OnAccept();

                while (!ct.IsCancellationRequested)
                {
                    int n = await session.Stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)
                                                .ConfigureAwait(false);
                    if (n == 0) break; // clean client disconnect
                    connection.OnReceive(buffer, n);
                }
            }
            catch (EndOfStreamException)
            {
                // clean client disconnect
            }
            catch (ObjectDisposedException)
            {
                // session was closed by a handler / malformed-frame guard — clean
            }
            catch (IOException ex)
            {
                Console.WriteLine($"[Error] (game) {endpoint} IO: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error] (game) {endpoint} {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                // Flush the session's live position exactly once (AD-2). Guard against
                // a never-loaded/null position. Wrapped so a DB failure can't throw out
                // of teardown.
                try
                {
                    if (session.PositionLoaded && session.Character != null)
                        _characters.UpdatePosition(session.Character.CharacterID,
                                                   session.CurrentMap, session.CurrentX, session.CurrentY);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Error] (game) {endpoint} position flush: {ex.Message}");
                }

                session.Disconnect();
                Console.WriteLine($"[Disconnect] (game) {endpoint}");
            }
        }
    }
}
