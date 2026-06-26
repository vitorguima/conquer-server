using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Conquer.Network;
using Microsoft.Extensions.Configuration;

namespace Redux
{
    public sealed class NetworkListener
    {
        private readonly IConfiguration _config;
        private readonly PacketRouter _router;

        public NetworkListener(IConfiguration config, PacketRouter router)
        {
            _config = config;
            _router = router;
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
                    _ = Task.Run(() => ServeClientAsync(client, ct), ct);
                }
            }
            finally
            {
                listener.Stop();
            }
        }

        private async Task ServeClientAsync(TcpClient tcp, CancellationToken ct)
        {
            var session = new ClientSession(tcp);
            string endpoint = tcp.Client?.RemoteEndPoint?.ToString() ?? "unknown";
            Console.WriteLine($"[Connect] {endpoint}");
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
    }
}
