using System.Collections.Generic;
using System.Linq;
using Conquer.World;
using Xunit;

namespace Conquer.World.Tests
{
    /// <summary>
    /// Pure cell-math coverage for <see cref="Grid{T}"/> (NFR-13): the 18-tile cell index
    /// (<see cref="Grid{T}.CellOf"/>), the packed cell key (<see cref="Grid{T}.CellKey"/>),
    /// and the 3x3 screen block (<see cref="Grid{T}.Cells3x3"/>). No socket/DB.
    /// </summary>
    public class GridMathTests
    {
        [Theory]
        [InlineData((ushort)0, 0)]
        [InlineData((ushort)17, 0)]   // last tile of cell 0
        [InlineData((ushort)18, 1)]   // first tile of cell 1
        [InlineData((ushort)35, 1)]   // last tile of cell 1
        [InlineData((ushort)36, 2)]   // first tile of cell 2
        public void CellOf_FloorsAt18TileBoundaries(ushort coord, int expectedCell)
        {
            Assert.Equal(expectedCell, Grid<PlayerEntity>.CellOf(coord));
        }

        [Fact]
        public void CellKey_PacksCxHighCyLow()
        {
            // ((long)cx << 32) | (uint)cy — high 32 bits = cx, low 32 bits = cy.
            long key = Grid<PlayerEntity>.CellKey(3, 7);
            Assert.Equal(3L, key >> 32);
            Assert.Equal(7u, (uint)(key & 0xFFFFFFFFL));
        }

        [Fact]
        public void CellKey_IsDistinctPerCell()
        {
            var keys = new HashSet<long>
            {
                Grid<PlayerEntity>.CellKey(0, 0),
                Grid<PlayerEntity>.CellKey(0, 1),
                Grid<PlayerEntity>.CellKey(1, 0),
                Grid<PlayerEntity>.CellKey(1, 1),
            };
            Assert.Equal(4, keys.Count);
        }

        [Fact]
        public void CellKey_SameCell_SharesKey()
        {
            // Two positions inside the same 18x18 cell map to one key.
            int cx = Grid<PlayerEntity>.CellOf(5);   // 0
            int cy = Grid<PlayerEntity>.CellOf(17);  // 0
            int cx2 = Grid<PlayerEntity>.CellOf(12); // 0
            int cy2 = Grid<PlayerEntity>.CellOf(3);  // 0
            Assert.Equal(
                Grid<PlayerEntity>.CellKey(cx, cy),
                Grid<PlayerEntity>.CellKey(cx2, cy2));
        }

        [Fact]
        public void CellKey_AdjacentCell_DiffersFromOrigin()
        {
            Assert.NotEqual(
                Grid<PlayerEntity>.CellKey(0, 0),
                Grid<PlayerEntity>.CellKey(0, 1));
        }

        [Fact]
        public void CellKey_NegativeCy_IsCollisionFreeViaUintCast()
        {
            // The (uint) cast keeps a negative cy from sign-extending into the cx bits,
            // so a map-edge cell at cy=-1 never aliases another cell.
            long a = Grid<PlayerEntity>.CellKey(0, -1);
            long b = Grid<PlayerEntity>.CellKey(-1, 0);
            long c = Grid<PlayerEntity>.CellKey(0, 0);
            Assert.NotEqual(a, b);
            Assert.NotEqual(a, c);
            Assert.NotEqual(b, c);
        }

        [Fact]
        public void Cells3x3_Yields9DistinctKeysCenteredOnCell()
        {
            var keys = Grid<PlayerEntity>.Cells3x3(5, 5).ToList();
            Assert.Equal(9, keys.Count);
            Assert.Equal(9, keys.Distinct().Count());

            var expected = new HashSet<long>();
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    expected.Add(Grid<PlayerEntity>.CellKey(5 + dx, 5 + dy));
                }
            }
            Assert.Equal(expected, new HashSet<long>(keys));
        }

        [Fact]
        public void Cells3x3_AtMapEdge_IncludesNegativeNeighborKeys()
        {
            // At cell (0,0) the 3x3 block reaches into cx/cy = -1; the uint-cast key keeps
            // those 9 keys distinct (no edge collision), so the block is still 9 keys.
            var keys = Grid<PlayerEntity>.Cells3x3(0, 0).ToList();
            Assert.Equal(9, keys.Distinct().Count());
            Assert.Contains(Grid<PlayerEntity>.CellKey(-1, -1), keys);
            Assert.Contains(Grid<PlayerEntity>.CellKey(0, 0), keys);
        }
    }
}
