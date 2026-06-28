using System.Collections.Generic;
using Dapper;

namespace Conquer.Database
{
    /// <summary>
    /// One row of the <c>cq_npc</c> table — a static NPC's spawn-source data (EPIC-3).
    /// Column names align with the <see cref="NpcRepository.All"/> Dapper query.
    /// <c>BaseId</c> is nullable (reserved for EPIC-8, unused in v1).
    /// </summary>
    public sealed class DbNpc
    {
        public int UID { get; init; }
        public string Name { get; init; } = "";
        public int MapID { get; init; }
        public int X { get; init; }
        public int Y { get; init; }
        public int Mesh { get; init; }
        public int Type { get; init; }
        public int? BaseId { get; init; }
    }

    /// <summary>
    /// Dapper read of <c>cq_npc</c>, mirroring <see cref="CharacterRepository"/>: a
    /// <see cref="ConnectionFactory"/>-injected repository whose <see cref="All"/> loads every
    /// static NPC ONCE at startup (no per-packet DB). NPCs never change at runtime in v1.
    /// </summary>
    public sealed class NpcRepository
    {
        private readonly ConnectionFactory _factory;

        public NpcRepository(ConnectionFactory factory)
        {
            _factory = factory;
        }

        public IReadOnlyList<DbNpc> All()
        {
            using var conn = _factory.Create();
            return conn.Query<DbNpc>(
                "SELECT UID, Name, MapID, X, Y, Mesh, Type, BaseId FROM cq_npc").AsList();
        }
    }
}
