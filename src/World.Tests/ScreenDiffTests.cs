using System.Linq;
using Conquer.Network;
using Conquer.World;
using Xunit;

namespace Conquer.World.Tests
{
    /// <summary>
    /// <see cref="MapInstance"/> registry / move / query + enter-leave <see cref="ScreenDiff"/>
    /// coverage (NFR-13). Pure spatial math — no socket/DB: the math path
    /// (Register/Move/QueryScreen/Deregister) never dereferences <see cref="PlayerEntity.Session"/>,
    /// so a null session handle is sufficient (Broadcast, which does send, is not exercised here).
    /// </summary>
    public class ScreenDiffTests
    {
        // CELL = 18; a +18 tile step crosses exactly one cell boundary.
        private const int MapId = 1002;

        private static PlayerEntity Entity(uint uid, ushort x, ushort y) =>
            new(uid, MapId, x, y, session: null!, mesh: 1, avatar: 1, level: 1, hp: 100, name: "P" + uid);

        [Fact]
        public void QueryScreen_ReturnsScreenOccupants_ExcludesFarPlayers()
        {
            var mi = new MapInstance();
            var center = Entity(1, 20, 20);   // cell (1,1)
            var near = Entity(2, 25, 5);      // cell (1,0) — inside the 3x3 block
            var far = Entity(3, 200, 200);    // cell (11,11) — well outside

            mi.Register(center);
            mi.Register(near);
            mi.Register(far);

            var seen = mi.QueryScreen(center.CellX, center.CellY).Select(e => e.Uid).ToHashSet();

            Assert.Contains(1u, seen);
            Assert.Contains(2u, seen);
            Assert.DoesNotContain(3u, seen);
        }

        [Fact]
        public void Register_TwoOnSameScreen_QueryReturnsBoth()
        {
            var mi = new MapInstance();
            var a = Entity(1, 10, 10);  // cell (0,0)
            var b = Entity(2, 12, 14);  // cell (0,0) — same cell

            mi.Register(a);
            mi.Register(b);

            var seen = mi.QueryScreen(a.CellX, a.CellY).Select(e => e.Uid).ToHashSet();
            Assert.Equal(new[] { 1u, 2u }.ToHashSet(), seen);
        }

        [Fact]
        public void Move_WithinCell_ReturnsEmptyDiff_NoGridMutation()
        {
            var mi = new MapInstance();
            var e = Entity(1, 10, 10);  // cell (0,0)
            mi.Register(e);

            // Step inside the same 18x18 cell.
            var diff = mi.Move(e, 15, 17);

            Assert.Empty(diff.Entered);
            Assert.Empty(diff.Left);
            Assert.Equal(0, e.CellX);
            Assert.Equal(0, e.CellY);
            Assert.Equal((ushort)15, e.X);
            Assert.Equal((ushort)17, e.Y);
            // Still queryable in its cell (grid untouched).
            Assert.Contains(e, mi.QueryScreen(0, 0));
        }

        [Fact]
        public void Move_AcrossCellBoundary_LeavesOldCell_AppearsInNewCell()
        {
            var mi = new MapInstance();
            var mover = Entity(1, 10, 10);  // cell (0,0)
            mi.Register(mover);

            mi.Move(mover, 100, 100);       // cell (5,5)

            Assert.Equal(5, mover.CellX);
            Assert.Equal(5, mover.CellY);
            // Gone from the old cell, present in the new one.
            Assert.DoesNotContain(mover, mi.QueryScreen(0, 0));
            Assert.Contains(mover, mi.QueryScreen(5, 5));
        }

        [Fact]
        public void Move_ScrollsInNewPlayer_ReportedAsEntered()
        {
            var mi = new MapInstance();
            // mover starts at cell (0,0); other sits at cell (3,0).
            var mover = Entity(1, 10, 10);
            var other = Entity(2, 54, 5);   // 54/18 = cell (3,0)
            mi.Register(mover);
            mi.Register(other);

            // Far apart: other not on mover's initial 3x3 (cells 0..1 around (0,0)).
            Assert.DoesNotContain(other, mi.QueryScreen(mover.CellX, mover.CellY));

            // Move to cell (2,0): now other's cell (3,0) is in the new 3x3 block.
            var diff = mi.Move(mover, 36, 5);   // cell (2,0)

            Assert.Contains(other, diff.Entered);
            Assert.DoesNotContain(other, diff.Left);
        }

        [Fact]
        public void Move_ScrollsOutPlayer_ReportedAsLeft()
        {
            var mi = new MapInstance();
            var mover = Entity(1, 36, 5);   // cell (2,0)
            var other = Entity(2, 54, 5);   // cell (3,0) — on mover's start 3x3
            mi.Register(mover);
            mi.Register(other);

            Assert.Contains(other, mi.QueryScreen(mover.CellX, mover.CellY));

            // Move down to cell (0,0): other's cell (3,0) drops out of the block.
            var diff = mi.Move(mover, 10, 5);   // cell (0,0)

            Assert.Contains(other, diff.Left);
            Assert.DoesNotContain(other, diff.Entered);
        }

        [Fact]
        public void Move_StationaryOverlap_ReportedAsNeitherEnteredNorLeft()
        {
            var mi = new MapInstance();
            var mover = Entity(1, 18, 18);  // cell (1,1)
            var other = Entity(2, 18, 18);  // cell (1,1) — stays in the overlapping block
            mi.Register(mover);
            mi.Register(other);

            // Step one cell over (to (2,1)); other's cell (1,1) is in BOTH old & new 3x3,
            // so it is neither newly-entered nor left.
            var diff = mi.Move(mover, 36, 18);  // cell (2,1)

            Assert.DoesNotContain(other, diff.Entered);
            Assert.DoesNotContain(other, diff.Left);
        }

        [Fact]
        public void Deregister_ReturnsLastScreen_RemovesFromRosterAndGrid()
        {
            var mi = new MapInstance();
            var leaver = Entity(1, 20, 20);   // cell (1,1)
            var viewer = Entity(2, 22, 22);   // cell (1,1) — on the leaver's screen
            mi.Register(leaver);
            mi.Register(viewer);

            var lastScreen = mi.Deregister(leaver.Uid);

            // Returns the leaver's last-screen occupants (excluding itself).
            Assert.Contains(viewer, lastScreen);
            Assert.DoesNotContain(leaver, lastScreen);
            // Removed from roster and grid.
            Assert.False(mi.Roster.ContainsKey(leaver.Uid));
            Assert.DoesNotContain(leaver, mi.QueryScreen(1, 1));
            // Viewer untouched.
            Assert.Contains(viewer, mi.QueryScreen(1, 1));
        }

        [Fact]
        public void Deregister_UnknownUid_ReturnsEmpty()
        {
            var mi = new MapInstance();
            Assert.Empty(mi.Deregister(999));
        }
    }
}
