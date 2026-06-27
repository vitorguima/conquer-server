using System.Collections.Concurrent;
using Conquer.Network;

namespace Conquer.World
{
    /// <summary>
    /// A connected player's world presence (AD-5). Holds DATA only — appearance snapshot
    /// fields, live coordinates and the cached cell, the owning <see cref="ClientSession"/>
    /// (for sends), and the current <see cref="Visible"/> set used for enter/leave diffing.
    /// NO packet building lives here: handlers in Packets build the 1014 from these public
    /// fields via <c>SpawnEntity.Build</c>, which keeps World free of a Packets dependency.
    /// Live coordinates are mutated only through <see cref="MapInstance.Move"/>.
    /// </summary>
    public sealed class PlayerEntity
    {
        public uint Uid { get; }
        public int MapId { get; }

        /// <summary>Live authoritative X. Mutated only via <see cref="SetPosition"/> (MapInstance.Move).</summary>
        public ushort X { get; private set; }
        /// <summary>Live authoritative Y. Mutated only via <see cref="SetPosition"/> (MapInstance.Move).</summary>
        public ushort Y { get; private set; }

        /// <summary>Cached cell index (X/18), used to detect a boundary cross without recomputing.</summary>
        public int CellX { get; internal set; }
        /// <summary>Cached cell index (Y/18).</summary>
        public int CellY { get; internal set; }

        /// <summary>Owning session — World refs Network, so a ClientSession field is legal.</summary>
        public ClientSession Session { get; }

        // Appearance snapshot (the 1014 source fields).
        public int Mesh { get; }
        public int Avatar { get; }
        public int Level { get; }
        public int Hp { get; }
        public string Name { get; }

        /// <summary>Current visible-set (other UIDs this entity can see), single-writer (its own loop).</summary>
        public ConcurrentDictionary<uint, byte> Visible { get; } = new();

        public PlayerEntity(
            uint uid, int mapId, ushort x, ushort y,
            ClientSession session,
            int mesh, int avatar, int level, int hp, string name)
        {
            Uid = uid;
            MapId = mapId;
            X = x;
            Y = y;
            CellX = Grid<PlayerEntity>.CellOf(x);
            CellY = Grid<PlayerEntity>.CellOf(y);
            Session = session;
            Mesh = mesh;
            Avatar = avatar;
            Level = level;
            Hp = hp;
            Name = name;
        }

        /// <summary>Update live coordinates. MapInstance.Move only — single-writer per entity.</summary>
        internal void SetPosition(ushort x, ushort y)
        {
            X = x;
            Y = y;
        }
    }
}
