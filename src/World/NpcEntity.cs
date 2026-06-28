namespace Conquer.World
{
    /// <summary>
    /// A static non-player world presence (EPIC-3). Holds DATA only — its UID, map,
    /// fixed tile coordinates and the 2030 spawn-source fields (<see cref="Mesh"/>,
    /// <see cref="NpcType"/>, <see cref="Name"/>). Unlike <see cref="PlayerEntity"/> it
    /// has NO <c>ClientSession</c>: an NPC is a sender-of-spawn only and NEVER receives a
    /// packet (so <c>MapInstance.Broadcast</c> skips it via <c>is PlayerEntity</c>).
    /// Registered into its <see cref="MapInstance"/> roster/grid ONCE at startup and never
    /// <c>Move</c>d — zero ongoing cost; it just rides the existing 3x3 screen query.
    /// </summary>
    public sealed class NpcEntity : IWorldEntity
    {
        public uint Uid { get; }
        public int MapId { get; }
        public ushort X { get; }
        public ushort Y { get; }

        /// <summary>Cell index set once at construction; <c>Move</c> is never called for NPCs.</summary>
        public int CellX { get; set; }
        public int CellY { get; set; }

        public EntityKind Kind => EntityKind.Npc;

        // 2030 spawn-source fields (public for EntitySpawn.For in Packets).
        public ushort Mesh { get; }
        public ushort NpcType { get; }
        public string Name { get; }

        public NpcEntity(
            uint uid, int mapId, ushort x, ushort y,
            ushort mesh, ushort npcType, string name)
        {
            Uid = uid;
            MapId = mapId;
            X = x;
            Y = y;
            CellX = Grid<IWorldEntity>.CellOf(x);
            CellY = Grid<IWorldEntity>.CellOf(y);
            Mesh = mesh;
            NpcType = npcType;
            Name = name ?? string.Empty;
        }
    }
}
