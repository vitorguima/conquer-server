using System.Collections.Generic;
using Dapper;

namespace Conquer.Database
{
    /// <summary>
    /// One row of the <c>monstertype</c> table — a monster's stat template (EPIC-4). A subset of
    /// the original 30 columns: the fields combat + spawn actually need in Phase 0. More can be
    /// added (drops, skills, AI tuning) as later slices use them.
    /// </summary>
    public sealed class DbMonsterType
    {
        public int Id { get; init; }
        public string Name { get; init; } = "";
        public int Mesh { get; init; }
        public int Life { get; init; }
        public int AttackMin { get; init; }
        public int AttackMax { get; init; }
        public int AttackRange { get; init; }
        public int ViewRange { get; init; }
        public int Defence { get; init; }
        public int Level { get; init; }
        public int BonusExp { get; init; }
    }

    /// <summary>
    /// Dapper read of <c>monstertype</c>, mirroring <see cref="NpcRepository"/>: load every
    /// template ONCE at startup (no per-packet DB). Keyed by <see cref="DbMonsterType.Id"/> by the
    /// caller so a spawn can resolve its stats.
    /// </summary>
    public sealed class MonsterTypeRepository
    {
        private readonly ConnectionFactory _factory;

        public MonsterTypeRepository(ConnectionFactory factory)
        {
            _factory = factory;
        }

        public IReadOnlyList<DbMonsterType> All()
        {
            using var conn = _factory.Create();
            return conn.Query<DbMonsterType>(
                @"SELECT ID AS Id, Name, Mesh, Life, AttackMin, AttackMax, AttackRange,
                         ViewRange, Defence, Level, BonusExp
                  FROM monstertype").AsList();
        }
    }
}
