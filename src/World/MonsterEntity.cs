namespace Conquer.World
{
    /// <summary>
    /// A monster world presence (EPIC-4, Phase 0). Like <see cref="NpcEntity"/> it has NO
    /// <c>ClientSession</c> (it never receives a packet, so <c>MapInstance.Broadcast</c> skips it
    /// via <c>is PlayerEntity</c>) and rides the existing 3x3 screen query for visibility. Unlike
    /// an NPC it carries combat state seeded from the <c>monstertype</c> row (<see cref="Life"/>,
    /// <see cref="AttackMin"/>/<see cref="AttackMax"/>, <see cref="Defence"/>, <see cref="ViewRange"/>)
    /// and a MUTABLE position (<see cref="SetPosition"/>) so a later AI tick can move it. In Phase
    /// 0.1 it is static — spawned into the grid and rendered (1014, same as a player), no AI yet.
    /// </summary>
    public sealed class MonsterEntity : IWorldEntity
    {
        public uint Uid { get; }
        public int MapId { get; }

        /// <summary>Live tile X. Mutable via <see cref="SetPosition"/> (AI movement, EPIC-4 0.4).</summary>
        public ushort X { get; private set; }
        /// <summary>Live tile Y. Mutable via <see cref="SetPosition"/>.</summary>
        public ushort Y { get; private set; }

        /// <summary>Cached cell index; written by <c>MapInstance.Move</c> once monsters move.</summary>
        public int CellX { get; set; }
        public int CellY { get; set; }

        public EntityKind Kind => EntityKind.Monster;

        // Spawn/appearance + combat fields (from monstertype; public for EntitySpawn.For + combat).
        public uint MonsterTypeId { get; }
        public ushort Mesh { get; }
        public string Name { get; }
        public ushort Level { get; }
        public uint MaxLife { get; }
        /// <summary>Current HP. Mutated by combat (EPIC-4 0.3); 0 = dead.</summary>
        public uint Life { get; set; }
        public uint AttackMin { get; }
        public uint AttackMax { get; }
        public uint Defence { get; }
        public ushort ViewRange { get; }

        public MonsterEntity(
            uint uid, int mapId, ushort x, ushort y, uint monsterTypeId,
            ushort mesh, string name, ushort level, uint maxLife,
            uint attackMin, uint attackMax, uint defence, ushort viewRange)
        {
            Uid = uid;
            MapId = mapId;
            X = x;
            Y = y;
            CellX = Grid<IWorldEntity>.CellOf(x);
            CellY = Grid<IWorldEntity>.CellOf(y);
            MonsterTypeId = monsterTypeId;
            Mesh = mesh;
            Name = name ?? string.Empty;
            Level = level;
            MaxLife = maxLife;
            Life = maxLife;
            AttackMin = attackMin;
            AttackMax = attackMax;
            Defence = defence;
            ViewRange = viewRange;
        }

        /// <summary>Update live coordinates (AI movement). MapInstance.Move only — single-writer.</summary>
        internal void SetPosition(ushort x, ushort y)
        {
            X = x;
            Y = y;
        }
    }
}
