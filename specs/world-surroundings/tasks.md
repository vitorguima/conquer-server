# Tasks: world-surroundings (EPIC 1)

Total: 26 tasks across 5 phases (POC-first, players-only v1).
Phase 1: 7 (send-lock + src/World skeleton) · Phase 2: 6 (Build generalize + register + 114 mutual 1014 — POC) · Phase 3: 7 (walk/jump broadcast + enter/leave diff + despawn) · Phase 4: 4 (xUnit + scope diff) · Phase 5: 2 (CI gate + PR + operator gate).

All builds/tests DOCKERIZED: `scripts/dotnet build src/Conquer.sln` (0 warnings / 0 errors, strict gate) and `scripts/dotnet test src/Conquer.sln`. NEVER bare `dotnet`. Branch: `feat/surroundings` (already checked out). One commit per task; every message ends with a blank line then `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`. ADDITIVE only — see NFR-11 forbidden list in Notes.

---

## Phase 1: Make It Work — send-safety + src/World skeleton (POC foundation)

Focus: the send lock (correctness prerequisite, FR-1/AD-3) + the world layer exists and compiles. No behavior wired yet.

- [x] 1.1 Add per-session send lock + Uid/WorldEntity to ClientSession (FR-1, AD-3)
  - **Do**:
    1. Add `private readonly object _sendLock = new();` to `ClientSession`.
    2. Wrap the existing encrypt+write body of `SendGame` AND `Send` in `lock(_sendLock) { ... }` — purely additive, do NOT alter the copy/encrypt/write logic.
    3. Add a `// AD-3: Send encrypts IN PLACE — never hand it a shared build-once buffer (SendGame copies first, Send does not)` comment above `Send`.
    4. Add `public uint Uid { get; set; }` and `public object? WorldEntity { get; set; }` (object slot so Network needs no World ref).
  - **Files**: src/Network/ClientSession.cs
  - **Done when**: `_sendLock` wraps encrypt+write in both methods; `Uid` + `WorldEntity` exist; no change to handshake/own-position logic.
  - **Verify**: `scripts/dotnet build src/Conquer.sln 2>&1 | grep -qiE "Build succeeded|0 Warning" && grep -q "_sendLock" src/Network/ClientSession.cs && echo PASS`
  - **Commit**: `feat(network): add per-session send lock + Uid/WorldEntity slot`
  - _Requirements: FR-1, AC-5.1, AC-5.3_
  - _Design: AD-3, AD-5_

- [x] 1.2 Create src/World project + sln wiring + ref direction (FR-2, AD-5)
  - **Do**:
    1. Create `src/World/World.csproj` (net8.0, inherits Directory.Build.props strict gate); add `ProjectReference` to `../Network/Network.csproj` and `../Database/Database.csproj`.
    2. Add World project to `src/Conquer.sln`.
    3. Add `<ProjectReference Include="../World/World.csproj" />` to `src/Packets/Packets.csproj` — confirm NO World→Packets edge (World refs only Network+Database).
  - **Files**: src/World/World.csproj, src/Conquer.sln, src/Packets/Packets.csproj
  - **Done when**: solution includes World; Packets→World exists; World refs Network+Database only; no cycle.
  - **Verify**: `scripts/dotnet build src/Conquer.sln 2>&1 | grep -qiE "Build succeeded" && ! grep -qi "Packets" src/World/World.csproj && echo PASS`
  - **Commit**: `feat(world): scaffold src/World project + sln wiring`
  - _Requirements: FR-2, NFR-9, NFR-12, AC-6.1_
  - _Design: AD-5, File Structure_

- [x] 1.3 Create Grid.cs — pure cell math (FR-3, AD-1)
  - **Do**:
    1. Create `src/World/Grid.cs` with `const int CELL = 18`.
    2. Static `int CellOf(ushort coord) => coord / 18;` and `long CellKey(int cx,int cy) => ((long)cx << 32) | (uint)cy;`.
    3. Static `IEnumerable<long> Cells3x3(int cx,int cy)` yielding the 9 keys for dx,dy ∈ {-1,0,+1}.
    4. Instance grid storage: `ConcurrentDictionary<long, ConcurrentDictionary<uint,PlayerEntity>>` with atomic `TryAdd(key,uid,e)` / `TryRemove(key,uid)` helpers (GetOrAdd the cell on add).
  - **Files**: src/World/Grid.cs
  - **Done when**: cell math + Cells3x3 + per-cell atomic add/remove compile; PURE static math is testable in isolation.
  - **Verify**: `scripts/dotnet build src/Conquer.sln 2>&1 | grep -qiE "Build succeeded" && grep -q "Cells3x3" src/World/Grid.cs && echo PASS`
  - **Commit**: `feat(world): add Grid cell math + atomic per-cell storage`
  - _Requirements: FR-3, NFR-2, NFR-4_
  - _Design: AD-1, AD-2, Grid/cell math_

- [x] 1.4 [VERIFY] Quality checkpoint: build strict gate (1.1-1.3)
  - **Do**: Run dockerized build; confirm 0 warnings / 0 errors (new code nullable-clean).
  - **Verify**: `scripts/dotnet build src/Conquer.sln 2>&1 | grep -qiE "Build succeeded" && ! scripts/dotnet build src/Conquer.sln 2>&1 | grep -qi "warning" && echo PASS`
  - **Done when**: Build succeeds with 0 warnings.
  - **Commit**: `chore(world): pass build gate after send-lock + grid` (only if fixes needed)

- [x] 1.5 Create PlayerEntity.cs + ScreenDiff.cs (FR-4, AD-5)
  - **Do**:
    1. Create `src/World/ScreenDiff.cs`: `public readonly record struct ScreenDiff(IReadOnlyList<PlayerEntity> Entered, IReadOnlyList<PlayerEntity> Left)` + a static `Empty`.
    2. Create `src/World/PlayerEntity.cs`: `Uid`, `MapId`, live `X/Y` (ushort, private set), cached `CellX/CellY`, `ClientSession Session`, appearance `Mesh/Avatar/Level/Hp/Name`, `ConcurrentDictionary<uint,byte> Visible`.
    3. `internal void SetPosition(ushort x, ushort y)` (mutated only via MapInstance.Move). World holds DATA only — NO packet builders (handlers in Packets build from public fields, avoiding World→Packets).
  - **Files**: src/World/PlayerEntity.cs, src/World/ScreenDiff.cs
  - **Done when**: entity exposes public appearance + live coords + Visible set; ScreenDiff record-struct compiles; no Packets dependency in World.
  - **Verify**: `scripts/dotnet build src/Conquer.sln 2>&1 | grep -qiE "Build succeeded" && grep -q "Visible" src/World/PlayerEntity.cs && echo PASS`
  - **Commit**: `feat(world): add PlayerEntity + ScreenDiff`
  - _Requirements: FR-4, AC-1.2_
  - _Design: PlayerEntity sketch, ScreenDiff, AD-5_

- [x] 1.6 Create MapInstance.cs + World.cs (FR-2, FR-10, AD-1/AD-2/AD-4)
  - **Do**:
    1. Create `src/World/MapInstance.cs`: `ConcurrentDictionary<uint,PlayerEntity> Roster` + a `Grid`; `Register(e)` (roster + grid add); `Deregister(uid)` returns the last-screen occupants then removes; `Move(e,newX,newY)` (set live coords always; on cell cross do atomic TryRemove old + TryAdd new, diff old-9 vs new-9 → ScreenDiff; within-cell → ScreenDiff.Empty, no grid write); `QueryScreen(cellX,cellY)` union of the 9 cells; `Broadcast(center,packet,includeSelf)` build-once fan-out over the 3×3 block calling each recipient `Session.SendGame`. Lock-free reads, atomic per-cell writes, NO global lock.
    2. Create `src/World/World.cs`: `ConcurrentDictionary<int,MapInstance>` + `GetOrAdd(mapId)`; facade `Register/Deregister/Move/QueryScreen` by `(mapId,uid)`.
  - **Files**: src/World/MapInstance.cs, src/World/World.cs
  - **Done when**: roster+grid registry compiles; Move returns ScreenDiff with enter/leave; Broadcast fans out O(k) build-once; World facade resolves maps via GetOrAdd.
  - **Verify**: `scripts/dotnet build src/Conquer.sln 2>&1 | grep -qiE "Build succeeded" && grep -q "ScreenDiff Move" src/World/MapInstance.cs && grep -q "GetOrAdd" src/World/World.cs && echo PASS`
  - **Commit**: `feat(world): add MapInstance registry/grid/broadcast + World facade`
  - _Requirements: FR-2, FR-3, FR-10, FR-11, FR-14, NFR-1, NFR-2, NFR-3, NFR-4, AC-3.4, AC-3.5_
  - _Design: AD-1, AD-2, AD-4, MapInstance sketch, Grid algorithm_

- [x] 1.7 [VERIFY] Quality checkpoint: full world layer builds (1.5-1.6)
  - **Do**: Run dockerized build; confirm the entire src/World layer compiles nullable-clean.
  - **Verify**: `scripts/dotnet build src/Conquer.sln 2>&1 | grep -qiE "Build succeeded" && ! scripts/dotnet build src/Conquer.sln 2>&1 | grep -qi "warning" && echo PASS`
  - **Done when**: World project builds, 0 warnings; layer exists, no behavior yet.
  - **Commit**: `chore(world): pass build gate — world layer complete` (only if fixes needed)

---

## Phase 2: POC Milestone — login → see players already on screen (114 mutual 1014)

Focus: register-on-SetLocation + un-gate 114 → mutual 1014. POC = a newcomer sees players already standing on screen, and they see the newcomer.

- [x] 2.1 Generalize SpawnEntity.Build for live coords (FR-5, AC-1.2)
  - **Do**:
    1. Add `public static byte[] Build(uint uid,int mesh,int avatar,int level,int hp,ushort x,ushort y,string name)` building the 1014 from explicit fields per the wire layout (UID@4, Lookface@8, Life@48, Level@50, X@52, Y@54, Hair/avatar@56, Dir@58=0, Action@59=0, Level@62, names@90; body = 90 + encoded name). Confirm offsets against existing `BuildSelf`.
    2. Rewrite `BuildSelf(DbCharacter ch)` to delegate: `Build((uint)ch.CharacterID, ch.Mesh, ch.Avatar, ch.Level, ch.HealthPoints, (ushort)ch.X, (ushort)ch.Y, ch.Name)` — byte-identical to before.
  - **Files**: src/Packets/SpawnEntity.cs
  - **Done when**: `Build(...)` produces a 1014 from live coords; `BuildSelf` delegates unchanged in behavior.
  - **Verify**: `scripts/dotnet build src/Conquer.sln 2>&1 | grep -qiE "Build succeeded" && grep -q "public static byte\[\] Build(" src/Packets/SpawnEntity.cs && echo PASS`
  - **Commit**: `feat(packets): generalize SpawnEntity.Build to live coords`
  - _Requirements: FR-5, AC-1.1, AC-1.2_
  - _Design: 1014 wire layout, File Structure_

- [x] 2.2 Inject World via Program + PacketRouter into handlers (FR-2, AC-6.1)
  - **Do**:
    1. `Program.cs`: construct `new World()` (mirror CharacterRepository wiring) and pass it into `PacketRouter` and `NetworkListener` ctors.
    2. `PacketRouter.cs`: ctor takes `World`; pass it to `new ActionHandler(world, ...)` and `new WalkHandler(world, ...)`.
    3. Add the `World` ctor param to `ActionHandler` and `WalkHandler` (store in a field; no behavior yet).
  - **Files**: src/Redux/Program.cs, src/Redux/PacketRouter.cs, src/Packets/ActionHandler.cs, src/Packets/WalkHandler.cs
  - **Done when**: World is constructed once and injected through PacketRouter to both handlers + NetworkListener; no static singleton.
  - **Verify**: `scripts/dotnet build src/Conquer.sln 2>&1 | grep -qiE "Build succeeded" && grep -q "new World()" src/Redux/Program.cs && echo PASS`
  - **Commit**: `feat(redux): inject World via Program + PacketRouter`
  - _Requirements: FR-2, NFR-9, NFR-10, AC-6.1, AC-6.2_
  - _Design: AD-5, Handler hooks_

- [x] 2.3 [VERIFY] Quality checkpoint: build after Build + injection (2.1-2.2)
  - **Do**: Run dockerized build; confirm 0 warnings / 0 errors.
  - **Verify**: `scripts/dotnet build src/Conquer.sln 2>&1 | grep -qiE "Build succeeded" && ! scripts/dotnet build src/Conquer.sln 2>&1 | grep -qi "warning" && echo PASS`
  - **Done when**: Build succeeds, 0 warnings.
  - **Commit**: `chore(packets): pass build gate after Build + World injection` (only if fixes needed)

- [x] 2.4 Register player into World at HandleSetLocation (FR-7, AC-2.1)
  - **Do**:
    1. In `ActionHandler.HandleSetLocation`, AFTER the existing SetLocation echo + MapStatus (do NOT touch the echo), build a `PlayerEntity` from `session.Character` + live `CurrentMap/CurrentX/CurrentY`.
    2. `_world.GetOrAdd(map).Register(e)`; set `session.WorldEntity = e` and `session.Uid = e.Uid`.
    3. Guard: skip register if `session.Character` is null or position not loaded.
  - **Files**: src/Packets/ActionHandler.cs
  - **Done when**: entering a map registers the player at live position with the session back-ref set; spawn echo untouched.
  - **Verify**: `scripts/dotnet build src/Conquer.sln 2>&1 | grep -qiE "Build succeeded" && grep -q "Register(" src/Packets/ActionHandler.cs && echo PASS`
  - **Commit**: `feat(packets): register player into World at SetLocation`
  - _Requirements: FR-7, NFR-11, AC-2.1_
  - _Design: Handler hooks (Register), AD-5_

- [x] 2.5 Un-gate case 114 → mutual 1014 per on-screen player (FR-6, AC-1.1/1.3/1.4)
  - **Do**:
    1. Un-gate `case 114: HandleGetSurroundings(session); break;` in `ActionHandler`.
    2. `HandleGetSurroundings`: resolve requester entity B; `QueryScreen` B's 3×3; for each OTHER player A: `session.SendGame(SpawnEntity.Build(A.Uid,A.Mesh,A.Avatar,A.Level,A.Hp,A.X,A.Y,A.Name))` AND `A.Session.SendGame(SpawnEntity.Build(B...))` (mutual); seed `B.Visible[A.Uid]` and `A.Visible[B.Uid]`.
    3. Exclude B's own UID; zero other players → send nothing, no error.
  - **Files**: src/Packets/ActionHandler.cs
  - **Done when**: 114 replies one 1014 per OTHER on-screen player (live coords) AND pushes B's 1014 to each; visible-sets seeded both ways; empty screen no-ops.
  - **Verify**: `scripts/dotnet build src/Conquer.sln 2>&1 | grep -qiE "Build succeeded" && grep -q "case 114" src/Packets/ActionHandler.cs && echo PASS`
  - **Commit**: `feat(packets): un-gate 114 GetSurroundings -> mutual 1014`
  - _Requirements: FR-6, AC-1.1, AC-1.2, AC-1.3, AC-1.4_
  - _Design: login sequence diagram, Handler hooks (114)_

- [x] 2.6 [VERIFY] POC milestone checkpoint: build + 114-wired proxy (M1)
  - **Do**: Run dockerized build; programmatically confirm the POC wiring exists (register at SetLocation + un-gated 114 + live-coord Build) — the automated proxy for "newcomer sees players already on screen". Operator live-confirm deferred to Phase 5.
  - **Verify**: `scripts/dotnet build src/Conquer.sln 2>&1 | grep -qiE "Build succeeded" && grep -q "case 114" src/Packets/ActionHandler.cs && grep -q "Register(" src/Packets/ActionHandler.cs && grep -q "public static byte\[\] Build(" src/Packets/SpawnEntity.cs && echo POC_PASS`
  - **Done when**: build green; 114 un-gated, register wired, Build live-coord present.
  - **Commit**: `chore(packets): POC milestone — see who is already on screen` (only if fixes needed)

---

## Phase 3: Real-time movement + despawn (walk/jump broadcast + enter/leave diff + disconnect)

Focus: walk(1005)/jump(133) broadcast, enter/leave diff, deregister + RemoveEntity(132) on disconnect.

- [x] 3.1 Add Walk.BuildBroadcast (framed 1005) + BuildRemoveEntity (1010/132)
  - **Do**:
    1. Create `src/Packets/Walk.cs` with `public static byte[] BuildBroadcast(uint uid, byte dir, byte mode)` — framed outbound 1005, `AppendHeader(span,20,1005)` (writes 12@0, 1005@2), UID@4, Dir@8, Mode@9, Unknown1@10=0; body 20. Span + BinaryPrimitives, NO unsafe. NOT the prefix-stripped inbound.
    2. In `GeneralData.cs` add `public static byte[] BuildRemoveEntity(uint uid)` — clone of `BuildJump` with no coords: 1010, UID@8=uid, Action@22=132, body 28 (`AppendHeader(span,36,1010)` → len@0=28). REVIEWER NIT: body length = 28, not 20.
  - **Files**: src/Packets/Walk.cs, src/Packets/GeneralData.cs
  - **Done when**: both builders compile, span-based, correct offsets; RemoveEntity body = 28.
  - **Verify**: `scripts/dotnet build src/Conquer.sln 2>&1 | grep -qiE "Build succeeded" && grep -q "BuildBroadcast" src/Packets/Walk.cs && grep -q "BuildRemoveEntity" src/Packets/GeneralData.cs && echo PASS`
  - **Commit**: `feat(packets): add Walk.BuildBroadcast + GeneralData.BuildRemoveEntity`
  - _Requirements: FR-8, FR-12, AC-3.2, AC-4.1_
  - _Design: 1005 + 132 wire layouts, reviewer nit (132 body=28)_

- [x] 3.2 Add shared ApplyDiff helper (enter/leave → mutual 1014 / RemoveEntity 132) (FR-11, FR-15)
  - **Do**:
    1. Add an `ApplyDiff(PlayerEntity mover, ScreenDiff diff)` helper usable by both handlers (e.g. in a shared static or each handler) honoring spawn-before-move ordering (FR-15: send 1014 before any forwarded move).
    2. For each `Entered`: send mutual 1014 (mover→viewer, viewer→mover via `SpawnEntity.Build` live coords) and seed both `Visible` sets.
    3. For each `Left`: `SendGame(GeneralData.BuildRemoveEntity(other.Uid))` to the mover and `BuildRemoveEntity(mover.Uid)` to the other; prune both `Visible` sets.
  - **Files**: src/Packets/ActionHandler.cs (or a shared helper file referenced by both)
  - **Done when**: ApplyDiff emits enter-spawn + leave-despawn mutually, prunes/seeds Visible, and orders spawn before move.
  - **Verify**: `scripts/dotnet build src/Conquer.sln 2>&1 | grep -qiE "Build succeeded" && grep -q "ApplyDiff" src/Packets/ActionHandler.cs && echo PASS`
  - **Commit**: `feat(packets): add enter/leave ApplyDiff (spawn-before-move)`
  - _Requirements: FR-11, FR-15, AC-2.2, AC-2.3, AC-4.1, AC-4.2_
  - _Design: ApplyDiff, ScreenDiff, FR-15 ordering_

- [x] 3.3 [VERIFY] Quality checkpoint: build after builders + ApplyDiff (3.1-3.2)
  - **Do**: Run dockerized build; confirm 0 warnings / 0 errors.
  - **Verify**: `scripts/dotnet build src/Conquer.sln 2>&1 | grep -qiE "Build succeeded" && ! scripts/dotnet build src/Conquer.sln 2>&1 | grep -qi "warning" && echo PASS`
  - **Done when**: Build succeeds, 0 warnings.
  - **Commit**: `chore(packets): pass build gate after broadcast builders` (only if fixes needed)

- [x] 3.4 Walk broadcast: Move + BuildBroadcast fan-out + diff (FR-8, FR-10, AC-3.1)
  - **Do**:
    1. In `WalkHandler`, AFTER the existing bound-checked CurrentX/Y own-position update (do NOT touch it): resolve mover entity; `var diff = mi.Move(e, nx, ny);`.
    2. Build the outbound 1005 ONCE via `Walk.BuildBroadcast(uid,dir,mode)`; `mi.Broadcast(e, packet, includeSelf:true)` — fan out to the 3×3 block.
    3. `ApplyDiff(e, diff)`.
  - **Files**: src/Packets/WalkHandler.cs
  - **Done when**: a walk updates own position (unchanged), grid-moves, broadcasts a BUILT 1005 to the 3×3 block (built once, includeSelf), and applies enter/leave diff.
  - **Verify**: `scripts/dotnet build src/Conquer.sln 2>&1 | grep -qiE "Build succeeded" && grep -q "BuildBroadcast" src/Packets/WalkHandler.cs && grep -q "ApplyDiff" src/Packets/WalkHandler.cs && echo PASS`
  - **Commit**: `feat(packets): broadcast walk 1005 to screen + apply diff`
  - _Requirements: FR-8, FR-10, FR-14, AC-3.1, AC-3.2, AC-3.4, AC-3.5_
  - _Design: walk sequence diagram, Handler hooks (Walk)_

- [x] 3.5 Jump broadcast: Move + BuildJump fan-out + diff (FR-9, AC-3.3)
  - **Do**:
    1. In `ActionHandler.HandleJump` (133), AFTER the existing self-send of `BuildJump` (do NOT remove it) and the position update: resolve mover entity; `var diff = mi.Move(e, x, y);`.
    2. `mi.Broadcast(e, GeneralData.BuildJump(uid,x,y), includeSelf:false)` (already self-sent → fan out to the rest of the screen).
    3. `ApplyDiff(e, diff)`.
  - **Files**: src/Packets/ActionHandler.cs
  - **Done when**: a jump keeps the existing self-send, grid-moves, fans BuildJump to the rest of the 3×3 block, and applies the diff.
  - **Verify**: `scripts/dotnet build src/Conquer.sln 2>&1 | grep -qiE "Build succeeded" && grep -q "includeSelf:false" src/Packets/ActionHandler.cs && echo PASS`
  - **Commit**: `feat(packets): broadcast jump 133 to screen + apply diff`
  - _Requirements: FR-9, FR-10, FR-11, AC-3.3, AC-3.4_
  - _Design: jump note, Handler hooks (Jump)_

- [ ] 3.6 Deregister + RemoveEntity(132) on disconnect (FR-13, AC-4.3/4.4)
  - **Do**:
    1. In `NetworkListener.ServeGameAsync` `finally`, BESIDE the existing position flush, in its OWN try/catch (so teardown never throws and the flush still runs): if `session.WorldEntity is PlayerEntity e`, capture the last screen, `mi.Deregister(e.Uid)`, then broadcast `GeneralData.BuildRemoveEntity(e.Uid)` to that last screen.
    2. `NetworkListener` ctor takes the injected `World`.
  - **Files**: src/Redux/NetworkListener.cs
  - **Done when**: disconnect deregisters from registry+grid and broadcasts 132 to the leaver's last screen; wrapped try/catch; position flush still runs.
  - **Verify**: `scripts/dotnet build src/Conquer.sln 2>&1 | grep -qiE "Build succeeded" && grep -q "Deregister" src/Redux/NetworkListener.cs && grep -q "BuildRemoveEntity" src/Redux/NetworkListener.cs && echo PASS`
  - **Commit**: `feat(redux): deregister + broadcast RemoveEntity(132) on disconnect`
  - _Requirements: FR-13, NFR-11, AC-4.3, AC-4.4_
  - _Design: Handler hooks (Deregister), Error Handling table_

- [ ] 3.7 [VERIFY] Quality checkpoint: full build after movement+despawn (3.4-3.6)
  - **Do**: Run dockerized build; confirm 0 warnings / 0 errors end-to-end.
  - **Verify**: `scripts/dotnet build src/Conquer.sln 2>&1 | grep -qiE "Build succeeded" && ! scripts/dotnet build src/Conquer.sln 2>&1 | grep -qi "warning" && echo PASS`
  - **Done when**: Build succeeds, 0 warnings; movement + despawn wired end-to-end.
  - **Commit**: `chore: pass build gate after movement + despawn` (only if fixes needed)

---

## Phase 4: Testing — xUnit pure math + packet layouts + scope diff

Focus: cover the spatial math + packet byte layouts (NFR-13). No socket/DB.

- [ ] 4.1 Create src/World.Tests project + grid math tests (NFR-13)
  - **Do**:
    1. Create `src/World.Tests/World.Tests.csproj` (xUnit, mirrors Packets.Tests, refs World); add to `src/Conquer.sln`.
    2. Create `src/World.Tests/GridMathTests.cs`: CellKey round-trip + distinct cells → distinct keys + negative-safe `(uint)` cast; `CellOf` boundaries (17→0, 18→1, 35→1, 36→2); `Cells3x3` yields exactly 9 keys centered on the cell.
  - **Files**: src/World.Tests/World.Tests.csproj, src/World.Tests/GridMathTests.cs, src/Conquer.sln
  - **Done when**: grid-math tests pass under dockerized test runner.
  - **Verify**: `scripts/dotnet test src/Conquer.sln 2>&1 | grep -qiE "Passed!|Passed:" && echo PASS`
  - **Commit**: `test(world): add grid cell-key + Cells3x3 tests`
  - _Requirements: NFR-13, AC-3.5_
  - _Design: Test Strategy (unit), Grid math_

- [ ] 4.2 MapInstance register/move/query + enter/leave diff tests (NFR-13)
  - **Do**:
    1. Create `src/World.Tests/ScreenDiffTests.cs` (+ MapInstance coverage): Register then `QueryScreen` returns only the 9-cell occupants, excludes far players; within-cell Move → `ScreenDiff.Empty` (no grid mutation); boundary cross → atomic remove+add; scroll-in cells → `Entered`, scroll-out → `Left`, stationary overlap → neither.
  - **Files**: src/World.Tests/ScreenDiffTests.cs
  - **Done when**: registry/move/query + enter/leave diff tests pass.
  - **Verify**: `scripts/dotnet test src/Conquer.sln 2>&1 | grep -qiE "Passed!|Passed:" && echo PASS`
  - **Commit**: `test(world): add MapInstance query + enter/leave diff tests`
  - _Requirements: NFR-13, AC-3.4, AC-4.1, AC-4.2_
  - _Design: Test Strategy (unit), MapInstance, ScreenDiff_

- [ ] 4.3 Packet byte-layout tests: 1014 live-coords, 1005, 132 (NFR-13)
  - **Do**:
    1. In `src/Packets.Tests/SpawnEntityBuildTests.cs` (create/extend): `Build(...)` writes X@52/Y@54 from live args; `BuildSelf(ch)` byte-identical to `Build(ch.*)`.
    2. `Walk.BuildBroadcast`: type@2=1005, len@0=12, UID@4, Dir@8, Mode@9.
    3. `BuildRemoveEntity`: UID@8, Action@22=132, **len@0=28** (REVIEWER NIT — assert 28, NOT 20).
  - **Files**: src/Packets.Tests/SpawnEntityBuildTests.cs
  - **Done when**: all three byte-layout assertions pass; RemoveEntity len asserted as 28.
  - **Verify**: `scripts/dotnet test src/Conquer.sln 2>&1 | grep -qiE "Passed!|Passed:" && echo PASS`
  - **Commit**: `test(packets): add 1014/1005/132 byte-layout tests`
  - _Requirements: NFR-13, AC-1.2, AC-3.2, AC-4.1_
  - _Design: wire layouts, reviewer nit (132 body=28)_

- [ ] 4.4 [VERIFY] Full suite + additive-scope diff
  - **Do**:
    1. Run the full dockerized test suite — all green.
    2. Verify NFR-11 additive scope: the diff must NOT touch forbidden files (auth/crypto/handshake/GameConnection, enter-world spawn ECHO logic, character creation, movement's OWN-position update). The send-lock + register/broadcast hooks are additive.
  - **Verify**: `scripts/dotnet test src/Conquer.sln 2>&1 | grep -qiE "Passed!|Passed:" && ! git diff master --name-only | grep -qiE "Handshake|GameConnection|CharacterCreation|DiffieHellman|Blowfish" && echo PASS`
  - **Done when**: full suite green; no forbidden file in the diff vs master.
  - **Commit**: `chore: pass full test suite + additive-scope check` (only if fixes needed)

---

## Phase 5: Quality Gate + PR + Operator Gate

> NEVER push to master/main directly. Already on `feat/surroundings`. If on the default branch, STOP and alert the user.

- [ ] 5.1 [VERIFY] Full local CI gate + push + PR
  - **Do**:
    1. Run full dockerized gate: `scripts/dotnet build src/Conquer.sln` (0/0 strict) && `scripts/dotnet test src/Conquer.sln` (all pass).
    2. Confirm branch: `git branch --show-current` is `feat/surroundings` (NOT master); if master, STOP.
    3. Push: `git push -u origin feat/surroundings`.
    4. `gh pr create --base master --title "EPIC 1: world-surroundings — players see each other" --body "<summary + operator E2E checklist>"`. If gh unavailable, output the PR URL for manual creation.
  - **Verify**: `scripts/dotnet build src/Conquer.sln 2>&1 | grep -qiE "Build succeeded" && scripts/dotnet test src/Conquer.sln 2>&1 | grep -qiE "Passed!|Passed:" && git branch --show-current | grep -q "feat/surroundings" && echo PASS`
  - **Done when**: gate green (0 warnings, tests pass), branch pushed, PR open with the operator E2E checklist.
  - **Commit**: `chore(world-surroundings): pass local CI gate` (only if fixes needed)
  - _Requirements: NFR-7, NFR-12, Success Criteria_

- [ ] 5.2 [VERIFY] M2 operator gate — two clients see each other live
  - **Do**:
    1. AUTOMATED proxy (the part this agent can verify): 114 un-gated + register-on-SetLocation + walk/jump broadcast + deregister/132 wired + full xUnit suite green (already asserted Phases 2-4).
    2. CI: if no CI workflow exists, this is a no-op (note it); else `gh pr checks` must be green.
    3. OPERATOR E2E (final sign-off, on 192.168.0.252): two clients (vitor=hawkk, account1=hawkk2) log in same map → SEE each other (mutual 1014) → see each other WALK (1005) and JUMP (133) live → one walks out of the 3×3 screen → vanishes (132), walks back → reappears (fresh 1014) → one logs off → vanishes for the other. Capture the 3 live-only unknowns (other-player pose, edge pop-in, move-before-spawn ordering).
  - **Verify**: `grep -q "case 114" src/Packets/ActionHandler.cs && grep -q "BuildBroadcast" src/Packets/WalkHandler.cs && grep -q "BuildRemoveEntity" src/Redux/NetworkListener.cs && scripts/dotnet test src/Conquer.sln 2>&1 | grep -qiE "Passed!|Passed:" && echo AUTOMATED_PASS`
  - **Done when**: automated proxy green (114 + broadcast + despawn wired, tests pass); CI green or no-op; operator confirms two-client see/move/despawn E2E and records the 3 live-only unknowns.
  - **Commit**: None
  - _Requirements: Success Criteria, AC-1.1, AC-3.1, AC-3.3, AC-4.1, AC-4.3_

---

## Notes

### Architecture Decisions (AD-1..AD-5 summary)
- **AD-1 — Registry + 18-tile cell grid.** Per-map roster (`ConcurrentDictionary<uint,PlayerEntity>`) + spatial grid of fixed 18-tile cells (`cellX=X/18`, `cellKey=((long)cx<<32)|(uint)cy`); screen query = union of the 3×3 cell block = ≤9 cells, O(occupants-of-9-cells) not O(N). Replaces the reference O(N²) LINQ QueryScreen.
- **AD-2 — Concurrency: lock-free reads, atomic per-cell writes, no global lock.** Reads take no lock; cell move = atomic `TryRemove(old)`+`TryAdd(new)`. Eventually consistent (a query may miss a mover for one frame — re-diffed next move). Single-writer-per-entity invariant (each serve loop mutates only its own entity + Visible set).
- **AD-3 — Per-session send lock (correctness prerequisite, FR-1).** Wraps the existing encrypt+write in `SendGame`/`Send`. `SendGame` copies input before encrypting → a build-once broadcast byte[] handed to N recipients is safe. Queue upgrade deferred until profiled.
- **AD-4 — Build-once fan-out, O(N·k).** Each broadcast packet built once; `Broadcast` iterates the 3×3 block, sends the same byte[] to each recipient (skip self when needed). No ArrayPool yet (measure first).
- **AD-5 — src/World boundary + injection seam.** New `src/World` refs Network+Database; injected via PacketRouter (like CharacterRepository), no static singleton. Network must NOT ref World → `ClientSession` gets only primitives (`uint Uid`, `object? WorldEntity`). World holds DATA, Packets holds BUILDERS (handlers call `SpawnEntity.Build` from entity public fields) → avoids World→Packets.

### Send-lock-first ordering
- Phase 1 implements the send lock (1.1) FIRST — it is a correctness prerequisite, not an optimization. Broadcast (Phase 3) corrupts the stateful CFB cipher keystream + interleaves socket writes without it. Nothing fans out to foreign threads until the lock exists.

### The 2 reviewer nits (carried)
1. **RemoveEntity body length = 28** (not 20). `BuildRemoveEntity` uses `AppendHeader(span,36,1010)` → len@0=28. Task 4.3 asserts `len@0==28`.
2. **AD-3 caveat: `ClientSession.Send` encrypts IN PLACE** → never hand `Send` a shared build-once buffer (only `SendGame` copies first). Commented at the send-lock site (task 1.1); broadcast always goes through `SendGame`.

### Live-only unknowns (operator capture in 5.2)
- **Other-player 1014 pose**: Dir/Action 0 (stand) on spawn vs the mover's live facing — low risk, verify on enter-screen.
- **Screen-edge pop-in**: the 18-tile 3×3 block slightly over-covers the 36×36 box — confirm no visible artifact; tighten to exact-box filter only if seen.
- **Move-before-spawn under race**: client tolerance of a forwarded foreign-UID 1005/133 before its 1014 — FR-15 mandates spawn-first; confirm ordering holds live under load.

### Additive scope (NFR-11 — MUST NOT change)
auth, crypto/handshake, GameConnection, enter-world spawn ECHO logic, character creation, movement's OWN-position update. Only ADD: send-lock (additive), register-after-echo, broadcast-after-position-update, grid-move, deregister-in-finally. Verified by the scope diff in 4.4.
