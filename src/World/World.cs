using System.Collections.Generic;
using System.Collections.Concurrent;

namespace Conquer.World
{
    /// <summary>
    /// The injected world service (AD-5) — a <see cref="ConcurrentDictionary{TKey,TValue}"/> of
    /// maps keyed by mapId. Constructed once in Program.cs and passed through PacketRouter to the
    /// handlers (like CharacterRepository); no static singleton, so the spatial math is unit-testable
    /// with a fresh World per test. Maps are created race-free on first arrival via <see cref="GetOrAdd"/>.
    /// </summary>
    public sealed class World
    {
        private readonly ConcurrentDictionary<int, MapInstance> _maps = new();

        /// <summary>Resolve (creating on first arrival) the <see cref="MapInstance"/> for a map.</summary>
        public MapInstance GetOrAdd(int mapId) =>
            _maps.GetOrAdd(mapId, static _ => new MapInstance());

        /// <summary>Try to resolve an existing map without creating it.</summary>
        public bool TryGetMap(int mapId, out MapInstance? map) => _maps.TryGetValue(mapId, out map);

        /// <summary>Register an entity into its map (creating the map if needed).</summary>
        public void Register(IWorldEntity e) => GetOrAdd(e.MapId).Register(e);

        /// <summary>Deregister a UID from a map; returns its last screen for despawn broadcast.</summary>
        public IReadOnlyCollection<IWorldEntity> Deregister(int mapId, uint uid) =>
            TryGetMap(mapId, out var map) && map is not null
                ? map.Deregister(uid)
                : System.Array.Empty<IWorldEntity>();

        /// <summary>Move an entity within its map, returning the enter/leave diff.</summary>
        public ScreenDiff Move(int mapId, IWorldEntity e, ushort newX, ushort newY) =>
            GetOrAdd(mapId).Move(e, newX, newY);

        /// <summary>Query the 3x3 screen block around a cell on a map (empty if the map is absent).</summary>
        public IEnumerable<IWorldEntity> QueryScreen(int mapId, int cellX, int cellY) =>
            TryGetMap(mapId, out var map) && map is not null
                ? map.QueryScreen(cellX, cellY)
                : System.Array.Empty<IWorldEntity>();
    }
}
