using System;
using Conquer.World;

namespace Conquer.Packets
{
    /// <summary>
    /// THE single kind-&gt;spawn-builder branch point (EPIC-3 generalization). Lives in the
    /// Packets project — which already references World (<see cref="ActionHandler"/>) — so the
    /// branch does NOT introduce a World-&gt;Packets edge (no dependency cycle, NFR-7).
    /// <para>
    /// Regression invariant (AC-2.1): the <see cref="PlayerEntity"/> branch calls
    /// <see cref="SpawnEntity.Build"/> with the IDENTICAL arg order as the previous inline
    /// <c>ActionHandler</c> call sites, so a player's 1014 is byte-identical to before.
    /// </para>
    /// </summary>
    public static class EntitySpawn
    {
        public static byte[] For(IWorldEntity e) => e switch
        {
            NpcEntity n     => SpawnNpc.Build(n.Uid, n.Mesh, n.NpcType, n.X, n.Y, n.Name),
            PlayerEntity p  => SpawnEntity.Build(p.Uid, p.Mesh, p.Avatar, p.Level, p.Hp, p.X, p.Y, p.Name),
            MonsterEntity m => SpawnEntity.Build(m.Uid, m.Mesh, 0, m.Level, (int)m.Life, m.X, m.Y, m.Name),
            _ => throw new ArgumentOutOfRangeException(nameof(e), e?.Kind, "unknown entity kind"),
        };
    }
}
