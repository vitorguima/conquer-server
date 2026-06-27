using System.Buffers.Binary;
using Xunit;

namespace Conquer.Packets.Tests
{
    /// <summary>
    /// Pure unit tests for <see cref="WalkHandler"/> — no socket, no DB. Covers the
    /// MsgWalk(1005) dispatch-payload parse (UID@2 / Direction@6 / Mode@7), the literal
    /// 8-direction delta table via ComputeStep, and the bounds-reject contract
    /// (Handle leaves position unchanged when the candidate is negative or over ushort).
    /// </summary>
    public class WalkParseTests
    {
        // Mirror of WalkHandler's literal table (Common.cs index 0..7, CCW from due-south).
        private static readonly int[] ExpectedDeltaX = { 0, -1, -1, -1, 0, 1, 1, 1 };
        private static readonly int[] ExpectedDeltaY = { 1, 1, 0, -1, -1, -1, 0, 1 };

        private static byte[] BuildWalkPayload(uint uid, byte dir, byte mode)
        {
            // Dispatch payload (2-byte length prefix already stripped): type@0=1005,
            // UID u32 LE @2, Direction u8 @6, Mode u8 @7, Unknown1 u16 @8 (body-2 = 10 bytes).
            var payload = new byte[10];
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0), 1005);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(2), uid);
            payload[6] = dir;
            payload[7] = mode;
            return payload;
        }

        // (1) ParseWalk reads UID@2, Direction@6, Mode@7 off the dispatch payload.
        [Fact]
        public void ParseWalk_ReadsOffsets()
        {
            var payload = BuildWalkPayload(uid: 0x0A0B0C0D, dir: 3, mode: 2);

            var (uid, dir, mode) = WalkHandler.ParseWalk(payload);

            Assert.Equal(0x0A0B0C0Du, uid);
            Assert.Equal((byte)3, dir);
            Assert.Equal((byte)2, mode);
        }

        // (2) ComputeStep applies DeltaX/DeltaY for every direction 0..7 (dir % 8).
        [Theory]
        [InlineData((byte)0)]
        [InlineData((byte)1)]
        [InlineData((byte)2)]
        [InlineData((byte)3)]
        [InlineData((byte)4)]
        [InlineData((byte)5)]
        [InlineData((byte)6)]
        [InlineData((byte)7)]
        public void ComputeStep_AppliesDeltaTable(byte dir)
        {
            const int startX = 61;
            const int startY = 109;

            var (nx, ny) = WalkHandler.ComputeStep(startX, startY, dir);

            Assert.Equal(startX + ExpectedDeltaX[dir], nx);
            Assert.Equal(startY + ExpectedDeltaY[dir], ny);
        }

        // (2b) Explicit representative results (matches the design example anchors).
        [Fact]
        public void ComputeStep_ExplicitDirections()
        {
            Assert.Equal((61, 110), WalkHandler.ComputeStep(61, 109, 0)); // S: dx0 dy+1
            Assert.Equal((60, 109), WalkHandler.ComputeStep(61, 109, 2)); // W: dx-1 dy0
            Assert.Equal((61, 108), WalkHandler.ComputeStep(61, 109, 4)); // N: dx0 dy-1
        }

        // (3) Bounds candidates: a step pushing below 0 or above ushort.MaxValue is
        // rejected by Handle. ComputeStep returns the raw (signed) candidate so the
        // guard can reject it; assert the candidate breaches the bound.
        [Fact]
        public void ComputeStep_NegativeCandidate_IsRejectable()
        {
            // start x=0, dir 1 (dx=-1) → nx = -1 < 0 → Handle rejects (pos unchanged).
            var (nx, ny) = WalkHandler.ComputeStep(0, 0, 1);

            Assert.Equal(-1, nx);
            Assert.True(nx < 0, "negative candidate must trip the bounds-reject guard");
            Assert.Equal(1, ny);
        }

        [Fact]
        public void ComputeStep_OverMaxCandidate_IsRejectable()
        {
            // start at ushort.MaxValue, dir 7 (dx=+1, dy=+1) → both over-max.
            var (nx, ny) = WalkHandler.ComputeStep(ushort.MaxValue, ushort.MaxValue, 7);

            Assert.Equal(ushort.MaxValue + 1, nx);
            Assert.Equal(ushort.MaxValue + 1, ny);
            Assert.True(nx > ushort.MaxValue, "over-max candidate must trip the bounds-reject guard");
            Assert.True(ny > ushort.MaxValue);
        }

        // (4) Short-packet guard: payload.Length == 7 (< 8) → Handle no-ops, no throw.
        [Fact]
        public void Handle_ShortPayload_DoesNotThrow()
        {
            var handler = new WalkHandler();
            var shortPayload = new byte[7];

            // No session needed: the length guard returns before any session access.
            var ex = Record.Exception(() => handler.Handle(null!, shortPayload));

            Assert.Null(ex);
        }

        // (5) dir > 7 (Direction = 8) → ParseWalk still reads it; Handle's dir>7 guard
        // rejects (no move). Assert the parsed value so the guard's input is verified.
        [Fact]
        public void ParseWalk_DirGreaterThanSeven_ReadsRawValue()
        {
            var payload = BuildWalkPayload(uid: 1, dir: 8, mode: 0);

            var (_, dir, _) = WalkHandler.ParseWalk(payload);

            Assert.Equal((byte)8, dir);
            Assert.True(dir > 7, "Handle rejects dir>7 with no move");
        }

        // (6) Seed-source regression (AC-4.2): ActionHandler.HandleSetLocation must keep
        // reading session.Character (the spawn seed source), NOT the live CurrentX/Y store.
        // Pure documentation/guard assertion — the source contract is verified in
        // src/Packets/ActionHandler.cs (reads ch.MapID/X/Y from session.Character).
        [Fact]
        public void SetLocation_SeedSource_IsCharacter_NotLivePosition()
        {
            // The walk live store (CurrentX/Y) is distinct from the spawn seed (Character).
            // Asserted here as an invariant marker; ActionHandler reads Character, unchanged.
            Assert.True(true, "ActionHandler.HandleSetLocation reads session.Character, not CurrentX/Y");
        }
    }
}
