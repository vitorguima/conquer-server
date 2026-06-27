using System.Collections.Generic;
using System.Collections.Concurrent;

namespace Conquer.World
{
    /// <summary>
    /// One map's authoritative state (AD-1/AD-2): a UID-&gt;entity <see cref="Roster"/> plus a
    /// fixed 18-tile cell <see cref="Grid{T}"/>. The unit of concurrency — lock-free reads,
    /// atomic per-cell writes, NO global lock. A screen is the 3x3 cell block around an
    /// entity's cell. <see cref="Move"/> only mutates the grid on a cell-boundary cross and
    /// returns the enter/leave <see cref="ScreenDiff"/>; within-cell steps are O(1) no-ops.
    /// </summary>
    public sealed class MapInstance
    {
        /// <summary>Whole-map roster, keyed by UID.</summary>
        public ConcurrentDictionary<uint, IWorldEntity> Roster { get; } = new();

        private readonly Grid<IWorldEntity> _grid = new();

        /// <summary>Add an entity to the roster and its current cell.</summary>
        public void Register(IWorldEntity e)
        {
            Roster[e.Uid] = e;
            _grid.TryAdd(Grid<IWorldEntity>.CellKey(e.CellX, e.CellY), e.Uid, e);
        }

        /// <summary>
        /// Remove an entity from the roster and grid; returns its last-screen occupants
        /// (excluding itself) so the caller can broadcast a despawn. Idempotent.
        /// </summary>
        public IReadOnlyCollection<IWorldEntity> Deregister(uint uid)
        {
            if (!Roster.TryRemove(uid, out var e))
            {
                return System.Array.Empty<IWorldEntity>();
            }

            var screen = new List<IWorldEntity>();
            foreach (var other in QueryScreen(e.CellX, e.CellY))
            {
                if (other.Uid != uid)
                {
                    screen.Add(other);
                }
            }

            _grid.TryRemove(Grid<IWorldEntity>.CellKey(e.CellX, e.CellY), uid);
            return screen;
        }

        /// <summary>
        /// Update an entity's live coordinates. On a cell-boundary cross, atomically move it
        /// between cells and diff the old vs new 3x3 block into a <see cref="ScreenDiff"/>;
        /// within-cell moves return <see cref="ScreenDiff.Empty"/> with no grid write.
        /// </summary>
        public ScreenDiff Move(IWorldEntity e, ushort newX, ushort newY)
        {
            int newCx = Grid<IWorldEntity>.CellOf(newX);
            int newCy = Grid<IWorldEntity>.CellOf(newY);
            int oldCx = e.CellX;
            int oldCy = e.CellY;

            ((PlayerEntity)e).SetPosition(newX, newY);   // Move is player-only (WalkHandler/jump); NPCs never Move

            if (newCx == oldCx && newCy == oldCy)
            {
                return ScreenDiff.Empty;
            }

            var old9 = new HashSet<long>(Grid<IWorldEntity>.Cells3x3(oldCx, oldCy));
            var new9 = new HashSet<long>(Grid<IWorldEntity>.Cells3x3(newCx, newCy));

            // Atomic per-cell move.
            _grid.TryRemove(Grid<IWorldEntity>.CellKey(oldCx, oldCy), e.Uid);
            _grid.TryAdd(Grid<IWorldEntity>.CellKey(newCx, newCy), e.Uid, e);
            e.CellX = newCx;
            e.CellY = newCy;

            var entered = new List<IWorldEntity>();
            foreach (long key in new9)
            {
                if (old9.Contains(key))
                {
                    continue;
                }

                foreach (var other in _grid.Occupants(key))
                {
                    if (other.Uid != e.Uid)
                    {
                        entered.Add(other);
                    }
                }
            }

            var left = new List<IWorldEntity>();
            foreach (long key in old9)
            {
                if (new9.Contains(key))
                {
                    continue;
                }

                foreach (var other in _grid.Occupants(key))
                {
                    if (other.Uid != e.Uid)
                    {
                        left.Add(other);
                    }
                }
            }

            return new ScreenDiff(entered, left);
        }

        /// <summary>Lock-free union of the occupants of the 3x3 cell block around (cellX, cellY).</summary>
        public IEnumerable<IWorldEntity> QueryScreen(int cellX, int cellY)
        {
            foreach (long key in Grid<IWorldEntity>.Cells3x3(cellX, cellY))
            {
                foreach (var e in _grid.Occupants(key))
                {
                    yield return e;
                }
            }
        }

        /// <summary>
        /// Build-once fan-out (AD-4): send the same packet to every player on
        /// <paramref name="center"/>'s 3x3 screen, optionally skipping the center itself.
        /// </summary>
        public void Broadcast(IWorldEntity center, byte[] packet, bool includeSelf)
        {
            foreach (var e in QueryScreen(center.CellX, center.CellY))
            {
                if (!includeSelf && e.Uid == center.Uid)
                {
                    continue;
                }

                if (e is PlayerEntity p)
                {
                    p.Session.SendGame(packet);   // NPCs never receive (no Session)
                }
            }
        }
    }
}
