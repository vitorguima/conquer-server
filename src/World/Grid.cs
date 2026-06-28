using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Conquer.World
{
    /// <summary>
    /// The fixed 18-tile cell spatial index (AD-1). Pure cell math (<see cref="CellOf"/>,
    /// <see cref="CellKey"/>, <see cref="Cells3x3"/>) plus a lock-free per-cell store
    /// (<c>cellKey -&gt; ConcurrentDictionary&lt;uid,entity&gt;</c> used as a thread-safe set).
    /// A screen is exactly the 3x3 cell block around an entity's cell, so a query touches
    /// at most 9 cells regardless of map population (AD-1/AD-2). Reads take no lock;
    /// cell moves are atomic per-cell <see cref="TryAdd"/>/<see cref="TryRemove"/>.
    /// </summary>
    /// <typeparam name="T">The stored entity type (e.g. PlayerEntity).</typeparam>
    public sealed class Grid<T> where T : class
    {
        /// <summary>Cell edge in tiles. 36x36 screen / 2 = 18-tile radius =&gt; screen = 3x3 cells.</summary>
        public const int CELL = 18;

        private readonly ConcurrentDictionary<long, ConcurrentDictionary<uint, T>> _cells = new();

        /// <summary>Integer cell index for a single tile coordinate.</summary>
        public static int CellOf(ushort coord) => coord / CELL;

        /// <summary>Pack two cell indices into one stable long key (negative-safe via the uint cast).</summary>
        public static long CellKey(int cx, int cy) => ((long)cx << 32) | (uint)cy;

        /// <summary>The 9 cell keys of the 3x3 block centred on (cx, cy).</summary>
        public static IEnumerable<long> Cells3x3(int cx, int cy)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    yield return CellKey(cx + dx, cy + dy);
                }
            }
        }

        /// <summary>Atomically add an entity to a cell (creating the cell on first arrival).</summary>
        public bool TryAdd(long key, uint uid, T entity)
        {
            var cell = _cells.GetOrAdd(key, static _ => new ConcurrentDictionary<uint, T>());
            return cell.TryAdd(uid, entity);
        }

        /// <summary>Atomically remove an entity from a cell. Idempotent (no-op if absent).</summary>
        public bool TryRemove(long key, uint uid)
        {
            return _cells.TryGetValue(key, out var cell) && cell.TryRemove(uid, out _);
        }

        /// <summary>Lock-free occupants of a single cell; empty if the cell is unoccupied.</summary>
        public IEnumerable<T> Occupants(long key)
        {
            return _cells.TryGetValue(key, out var cell) ? cell.Values : System.Array.Empty<T>();
        }
    }
}
