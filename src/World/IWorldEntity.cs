namespace Conquer.World
{
    /// <summary>
    /// Discriminator for the kind of a world entity. A cheap byte tag so the spawn-builder
    /// branch (<c>EntitySpawn.For</c> in Packets) and the "players only" guard (<c>is PlayerEntity</c>)
    /// can dispatch without a packet-building method on the entity (which would force a
    /// World-&gt;Packets reference = dependency cycle). Reused by EPIC-4 monsters / ground items.
    /// </summary>
    public enum EntityKind : byte
    {
        Player = 0,
        Npc = 1,
        Monster = 2,
        GroundItem = 3,
    }

    /// <summary>
    /// Common shape every world entity exposes so the roster, grid and broadcast are
    /// kind-agnostic (EPIC-3 generalization of the EPIC-1 player-only World layer).
    /// <para>
    /// <see cref="CellX"/>/<see cref="CellY"/> are settable because <c>MapInstance.Move</c>
    /// writes them for the player path; NPCs never Move, so their setters are never called.
    /// There is deliberately NO <c>BuildSpawn()</c> on this interface — packet building lives
    /// in the Packets project (<c>EntitySpawn.For</c>) to keep World packet-free and acyclic.
    /// </para>
    /// </summary>
    public interface IWorldEntity
    {
        uint Uid { get; }
        int MapId { get; }
        ushort X { get; }
        ushort Y { get; }

        /// <summary>Cached cell index (X/18). Written by <c>MapInstance.Move</c> for players.</summary>
        int CellX { get; set; }

        /// <summary>Cached cell index (Y/18). Written by <c>MapInstance.Move</c> for players.</summary>
        int CellY { get; set; }

        /// <summary>Kind discriminator used by the spawn-builder branch and the player-only guards.</summary>
        EntityKind Kind { get; }
    }
}
