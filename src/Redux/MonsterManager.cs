using System;
using System.Collections.Generic;
using Conquer.Database;
using Conquer.World;

namespace Redux
{
    /// <summary>
    /// Spawns monsters from the <c>spawns</c> regions into the live <see cref="World"/> grid
    /// (EPIC-4 Phase 0). Each spawn row places up to <see cref="DbSpawn.AmountMax"/> monsters of its
    /// <see cref="DbSpawn.MonsterType"/> at scattered tiles inside the region box; stats come from
    /// the matching <c>monstertype</c> template. Phase 0.1 spawns ONCE at startup (static, no AI);
    /// the respawn/AI tick lands in a later slice. Monster UIDs use a high band (2_000_000+) that
    /// can't collide with characters (low CharacterIDs) or NPCs (1_000_000+).
    /// </summary>
    internal sealed class MonsterManager
    {
        private const int MaxPerSpawn = 200;   // Rule 2: bound the placement loop.
        private uint _nextUid = 2_000_000;

        /// <summary>Populate every spawn region; returns the number of monsters registered.</summary>
        public int SpawnAll(
            World world,
            IReadOnlyList<DbSpawn> spawns,
            IReadOnlyDictionary<int, DbMonsterType> types)
        {
            // Deterministic placement so a restart reproduces the same layout (no Math.random spread).
            var rng = new Random(0x5EED);
            int total = 0;

            foreach (var s in spawns)
            {
                if (!types.TryGetValue(s.MonsterType, out var t))
                    continue;   // unknown monster template — skip, don't crash

                int loX = Math.Min(s.X1, s.X2), hiX = Math.Max(s.X1, s.X2);
                int loY = Math.Min(s.Y1, s.Y2), hiY = Math.Max(s.Y1, s.Y2);
                int amount = Math.Clamp(s.AmountMax, 0, MaxPerSpawn);

                for (int i = 0; i < amount; i++)
                {
                    var x = (ushort)rng.Next(loX, hiX + 1);
                    var y = (ushort)rng.Next(loY, hiY + 1);
                    var m = new MonsterEntity(
                        _nextUid++, s.Map, x, y, (uint)t.Id, (ushort)t.Mesh, t.Name,
                        (ushort)t.Level, (uint)t.Life,
                        (uint)t.AttackMin, (uint)t.AttackMax, (uint)t.Defence, (ushort)t.ViewRange);
                    world.GetOrAdd(m.MapId).Register(m);
                    total++;
                }
            }

            return total;
        }
    }
}
