using System.Collections.Generic;
using Dapper;

namespace Conquer.Database
{
    /// <summary>
    /// One row of the <c>spawns</c> table — a rectangular region (<c>X1,Y1</c>..<c>X2,Y2</c>) on a
    /// map that holds up to <see cref="AmountMax"/> monsters of <see cref="MonsterType"/>, refilled
    /// <see cref="AmountPer"/> at a time every <see cref="Frequency"/> ticks (EPIC-4).
    /// </summary>
    public sealed class DbSpawn
    {
        public int Uid { get; init; }
        public int Map { get; init; }
        public int X1 { get; init; }
        public int Y1 { get; init; }
        public int X2 { get; init; }
        public int Y2 { get; init; }
        public int MonsterType { get; init; }
        public int AmountPer { get; init; }
        public int AmountMax { get; init; }
        public int Frequency { get; init; }
    }

    /// <summary>
    /// Dapper read of <c>spawns</c>, mirroring <see cref="NpcRepository"/>: load every spawn region
    /// ONCE at startup. <see cref="MonsterTypeRepository"/> supplies the stats per region.
    /// </summary>
    public sealed class SpawnRepository
    {
        private readonly ConnectionFactory _factory;

        public SpawnRepository(ConnectionFactory factory)
        {
            _factory = factory;
        }

        public IReadOnlyList<DbSpawn> All()
        {
            using var conn = _factory.Create();
            return conn.Query<DbSpawn>(
                @"SELECT UID AS Uid, Map, X1, Y1, X2, Y2, MonsterType, AmountPer, AmountMax, Frequency
                  FROM spawns").AsList();
        }
    }
}
