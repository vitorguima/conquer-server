using System;
using System.Threading;
using System.Threading.Tasks;
using Conquer.Database;
using Conquer.Maps;
using Conquer.Network;
using Conquer.Packets;
using Microsoft.Extensions.Configuration;
using World = Conquer.World.World;

namespace Redux
{
    internal static class Program
    {
        private static async Task Main(string[] args)
        {
            // Build IConfiguration from appsettings.json + environment variables
            IConfiguration config = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                .AddEnvironmentVariables()
                .Build();

            // Load maps (optional — server starts even without map files)
            string mapsDir = config["MapsDirectory"] ?? "./maps";
            MapRegistry.Load(mapsDir);

            // Manual DI — instantiate all components
            var factory   = new ConnectionFactory(config);
            Console.WriteLine("[Startup] Database connected");

            var accounts   = new AccountRepository(factory);
            var characters = new CharacterRepository(factory);
            var world      = new World();

            // Load static NPCs ONCE into the grid/roster (EPIC-3); they never Move.
            var npcs = new NpcRepository(factory).All();
            foreach (var n in npcs)
            {
                var npc = new Conquer.World.NpcEntity((uint)n.UID, n.MapID, (ushort)n.X, (ushort)n.Y,
                                                      (ushort)n.Mesh, (ushort)n.Type, n.Name);
                world.GetOrAdd(npc.MapId).Register(npc);
            }
            Console.WriteLine($"[Startup] Loaded {npcs.Count} NPCs");

            var router     = new PacketRouter(accounts, characters, config, world);
            var listener   = new NetworkListener(config, router, characters, world);

            // Graceful shutdown on Ctrl+C
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            Console.WriteLine("[Startup] Starting listeners...");
            await Task.WhenAll(
                listener.RunAuthAsync(cts.Token),
                listener.RunGameAsync(cts.Token)
            ).ConfigureAwait(false);

            Console.WriteLine("[Shutdown] Server stopped.");
        }
    }
}
