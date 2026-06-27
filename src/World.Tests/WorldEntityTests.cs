using System.Linq;
using Conquer.World;
using Xunit;

namespace Conquer.World.Tests
{
    /// <summary>
    /// IWorldEntity grid COEXISTENCE coverage (AC-1.5, AC-2.5): a <see cref="PlayerEntity"/>
    /// and a static <see cref="NpcEntity"/> share the roster/grid kind-agnostically. The
    /// retype is a pure generalization — a player <c>Move</c> does not disturb the static NPC,
    /// and <c>Broadcast</c> never dereferences the NPC's (absent) Session. NO socket/DB: the
    /// math path never touches <see cref="PlayerEntity.Session"/>, so a null session is fine.
    /// </summary>
    public class WorldEntityTests
    {
        private const int MapId = 1010;

        private static PlayerEntity Player(uint uid, ushort x, ushort y) =>
            new(uid, MapId, x, y, session: null!, mesh: 1, avatar: 1, level: 1, hp: 100, name: "P" + uid);

        private static NpcEntity Npc(uint uid, ushort x, ushort y) =>
            new(uid, MapId, x, y, mesh: 1, npcType: 2, name: "Guide");

        [Fact]
        public void QueryScreen_PlayerAndNpcOnSameScreen_ReturnsBoth()
        {
            var mi = new MapInstance();
            var player = Player(1, 20, 20);   // cell (1,1)
            var npc = Npc(90001, 22, 22);     // cell (1,1) — same screen

            mi.Register(player);
            mi.Register(npc);

            var seen = mi.QueryScreen(player.CellX, player.CellY).Select(e => e.Uid).ToHashSet();

            Assert.Contains(1u, seen);
            Assert.Contains(90001u, seen);
        }

        [Fact]
        public void Register_PlayerAndNpc_CoexistInRosterWithDistinctKinds()
        {
            var mi = new MapInstance();
            var player = Player(1, 20, 20);
            var npc = Npc(90001, 22, 22);

            mi.Register(player);
            mi.Register(npc);

            Assert.Equal(EntityKind.Player, mi.Roster[1u].Kind);
            Assert.Equal(EntityKind.Npc, mi.Roster[90001u].Kind);
        }

        [Fact]
        public void Move_PlayerWithinScreen_DoesNotDisturbStaticNpc()
        {
            var mi = new MapInstance();
            var player = Player(1, 20, 20);   // cell (1,1)
            var npc = Npc(90001, 22, 22);     // cell (1,1)

            mi.Register(player);
            mi.Register(npc);

            // Player steps within its own cell — NPC must keep its fixed coords/cell.
            mi.Move(player, 24, 24);

            Assert.Equal((ushort)22, npc.X);
            Assert.Equal((ushort)22, npc.Y);
            Assert.Equal(1, npc.CellX);
            Assert.Equal(1, npc.CellY);
            // NPC still queryable in its cell after the player moved.
            Assert.Contains(npc, mi.QueryScreen(1, 1));
        }

        [Fact]
        public void Deregister_Player_LeavesStaticNpcInGridAndRoster()
        {
            var mi = new MapInstance();
            var player = Player(1, 20, 20);
            var npc = Npc(90001, 22, 22);

            mi.Register(player);
            mi.Register(npc);

            var lastScreen = mi.Deregister(player.Uid);

            // The NPC was on the player's last screen, reported for despawn fan-out.
            Assert.Contains(npc, lastScreen);
            // Player gone; NPC untouched in both roster and grid.
            Assert.False(mi.Roster.ContainsKey(player.Uid));
            Assert.True(mi.Roster.ContainsKey(npc.Uid));
            Assert.Contains(npc, mi.QueryScreen(1, 1));
        }

        [Fact]
        public void Broadcast_WithNpcOnScreen_SkipsNpc_HitsOnlyPlayer_NoThrow()
        {
            var mi = new MapInstance();
            var npc = Npc(90001, 22, 22);     // no Session — must be skipped (no NPE)
            mi.Register(npc);

            // Center on the NPC's screen; includeSelf irrelevant (NPC has no Session).
            // The only entity present is the NPC: Broadcast must be a no-op, never deref Session.
            var ex = Record.Exception(() => mi.Broadcast(npc, new byte[] { 1, 2, 3 }, includeSelf: true));

            Assert.Null(ex);
        }
    }
}
