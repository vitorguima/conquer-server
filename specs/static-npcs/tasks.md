---
spec: static-npcs
basePath: specs/static-npcs
phase: tasks
epic: 3
updated: 2026-06-27
---

# Tasks: static-npcs (EPIC 3)

**Workflow**: POC-first (GREENFIELD feature on existing World layer). **Total: 22 tasks.**
Phase 1 (IWorldEntity retype, regression-critical): 8 · Phase 2 (NPCs visible): 5 · Phase 3 (click→dialog): 3 · Phase 4 (testing): 3 · Phase 5 (gate + PR): 3.

**Branch**: `feat/npcs` (stacked, checked out). One commit per task. Every commit message ends with a blank line then `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.

**STRICT GATE — every [VERIFY]**: `scripts/dotnet build src/Conquer.sln` 0/0 AND `scripts/dotnet test src/Conquer.sln` green. NEVER bare `dotnet`.

**MUST NOT change**: auth/crypto/handshake/GameConnection/enter-world-echo/char-creation/movement-own-position/chat behavior. Do NOT touch `GeneralData.cs` (`BuildRemoveEntity` works for any UID as-is). Do NOT stage/commit `src/docker-compose.yml`.

---

## Phase 1: IWorldEntity Generalization (regression-critical — player path BYTE-IDENTICAL)

This phase adds NPC SUPPORT but no NPCs yet. The retype is a pure GENERALIZATION: player behavior must stay byte-identical. The Phase-1 [VERIFY] is the regression gate — ALL existing surroundings/chat/world tests MUST still pass.

- [x] 1.1 Create IWorldEntity + EntityKind
  - **Do**:
    1. Create `src/World/IWorldEntity.cs` with `enum EntityKind : byte { Player=0, Npc=1, Monster=2, GroundItem=3 }`.
    2. Define `interface IWorldEntity { uint Uid; int MapId; ushort X; ushort Y; int CellX {get;set;}; int CellY {get;set;}; EntityKind Kind; }` (per design §IWorldEntity — `CellX/Y` are `{get;set;}` because `MapInstance.Move` writes them).
  - **Files**: `src/World/IWorldEntity.cs`
  - **Done when**: Interface + enum compile in the `Conquer.World` namespace.
  - **Verify**: `scripts/dotnet build src/Conquer.sln`
  - **Commit**: `feat(world): add IWorldEntity interface + EntityKind discriminator`
  - _Requirements: FR-1, AC-1.1_
  - _Design: IWorldEntity_

- [x] 1.2 PlayerEntity implements IWorldEntity
  - **Do**:
    1. Change to `public sealed class PlayerEntity : IWorldEntity`.
    2. Add `public EntityKind Kind => EntityKind.Player;`.
    3. Widen `CellX`/`CellY` from `{ get; internal set; }` → `{ get; set; }` (interface can't be `internal set`). NO other field/behavior change (`Session`, `Mesh`, `Avatar`, `Level`, `Hp`, `Name`, `Visible`, `SetPosition` stay as-is).
  - **Files**: `src/World/PlayerEntity.cs`
  - **Done when**: PlayerEntity satisfies IWorldEntity with `Kind==Player`, no behavior change.
  - **Verify**: `scripts/dotnet build src/Conquer.sln`
  - **Commit**: `feat(world): PlayerEntity implements IWorldEntity (Kind=Player)`
  - _Requirements: FR-1, AC-1.2_
  - _Design: PlayerEntity_

- [x] 1.3 Create NpcEntity
  - **Do**:
    1. Create `src/World/NpcEntity.cs`: `public sealed class NpcEntity : IWorldEntity`, `Kind => EntityKind.Npc`, NO `ClientSession`.
    2. Carry `Uid/MapId/X/Y` + spawn-source `ushort Mesh`, `ushort NpcType`, `string Name`; `CellX/CellY` settable.
    3. Ctor `(uint uid, int mapId, ushort x, ushort y, ushort mesh, ushort npcType, string name)` sets `CellX=Grid<IWorldEntity>.CellOf(x)`, `CellY=CellOf(y)`, `Name = name ?? ""` (per design §NpcEntity).
  - **Files**: `src/World/NpcEntity.cs`
  - **Done when**: NpcEntity compiles, exposes public Mesh/NpcType/Name for the Packets branch.
  - **Verify**: `scripts/dotnet build src/Conquer.sln`
  - **Commit**: `feat(world): add static NpcEntity (Kind=Npc, no Session)`
  - _Requirements: FR-5, AC-3.1, AC-3.3_
  - _Design: NpcEntity_

- [x] 1.4 [VERIFY] Quality checkpoint: World entities compile + existing tests green
  - **Do**: Run strict gate after the new World types land.
  - **Verify**: `scripts/dotnet build src/Conquer.sln` 0/0 AND `scripts/dotnet test src/Conquer.sln` green.
  - **Done when**: Build 0 warnings/0 errors, all existing tests pass (interface added, no retype yet).
  - **Commit**: `chore(world): pass quality checkpoint` (only if fixes needed)

- [x] 1.5 Retype MapInstance + ScreenDiff → IWorldEntity (Broadcast guard + Move cast)
  - **Do**:
    1. `MapInstance.cs`: `Roster` → `ConcurrentDictionary<uint, IWorldEntity>`; `_grid` → `Grid<IWorldEntity>`; retype `Register`/`Deregister`/`Move`/`QueryScreen` params+returns to `IWorldEntity`; all `Grid<PlayerEntity>.*` → `Grid<IWorldEntity>.*` (per design exact diffs).
    2. `Broadcast`: guard `if (e is PlayerEntity p) p.Session.SendGame(packet);` (NPCs never receive).
    3. `Move`: cast `((PlayerEntity)e).SetPosition(newX, newY);` with a "Move is player-only (Walk/jump)" comment; `entered`/`left` lists → `List<IWorldEntity>`.
    4. `ScreenDiff.cs`: `Entered`/`Left`/`Empty` → `IWorldEntity` (per design diff).
  - **Files**: `src/World/MapInstance.cs`, `src/World/ScreenDiff.cs`
  - **Done when**: Pure type substitution; Broadcast skips non-players; Move casts player-only.
  - **Verify**: `scripts/dotnet build src/Conquer.sln`
  - **Commit**: `refactor(world): retype MapInstance/ScreenDiff to IWorldEntity`
  - _Requirements: FR-2, FR-4, AC-1.3, AC-1.4, AC-2.5_
  - _Design: MapInstance / World / ScreenDiff retype_

- [x] 1.6 Retype World.cs → IWorldEntity
  - **Do**:
    1. `World.cs`: retype `Register(IWorldEntity)`, `Deregister(...) → IReadOnlyCollection<IWorldEntity>`, `Move(int, IWorldEntity, ...)`, `QueryScreen(...) → IEnumerable<IWorldEntity>`.
    2. Both `System.Array.Empty<PlayerEntity>()` → `<IWorldEntity>()`. Pure type substitution, no logic change.
  - **Files**: `src/World/World.cs`
  - **Done when**: World facade typed `IWorldEntity` end-to-end; compiles 0/0.
  - **Verify**: `scripts/dotnet build src/Conquer.sln`
  - **Commit**: `refactor(world): retype World facade to IWorldEntity`
  - _Requirements: FR-2, AC-1.4_
  - _Design: World.cs diffs_

- [x] 1.7 Create EntitySpawn.For + SpawnNpc.Build; branch the 2 ActionHandler spawn sites
  - **Do**:
    1. Create `src/Packets/SpawnNpc.cs`: 2030 builder, body `18 + names.Length`, `AppendHeader(span, bodyLen+8, 2030)`, UID@4/X@8/Y@10/Mesh@12/Type@14/Unknown1@16=0/Name@18 (NetStringPacker) — per design §SpawnNpc.
    2. Create `src/Packets/EntitySpawn.cs`: `static byte[] For(IWorldEntity e)` switch — `NpcEntity n => SpawnNpc.Build(...)`, `PlayerEntity p => SpawnEntity.Build(p.Uid,p.Mesh,p.Avatar,p.Level,p.Hp,p.X,p.Y,p.Name)` (IDENTICAL arg order to today's inline → byte-identical 1014), `_ => throw ArgumentOutOfRangeException`. Lives in Packets (already refs World) → no World→Packets cycle.
    3. `ActionHandler.cs` Site 1 (`HandleGetSurroundings`, ~86-104): `session.SendGame(EntitySpawn.For(a))`; keep MUTUAL 1014 + Visible-seed guarded `if (a is PlayerEntity ap)`.
    4. `ActionHandler.cs` Site 2 (`ApplyDiff`, ~115-152): `diff.Entered` → `mover.Session.SendGame(EntitySpawn.For(other))`, mutual/Visible-seed guarded `is PlayerEntity`; `diff.Left` → `mover.Session.SendGame(GeneralData.BuildRemoveEntity(other.Uid))` for player OR npc, reverse 132 + Visible-clear guarded `is PlayerEntity`. `moverSpawn`/`moverRemove` still built once.
  - **Files**: `src/Packets/SpawnNpc.cs`, `src/Packets/EntitySpawn.cs`, `src/Packets/ActionHandler.cs`
  - **Done when**: Both spawn sites branch via `EntitySpawn.For`; player path byte-identical; NPC support compiled (no NPCs registered yet).
  - **Verify**: `scripts/dotnet build src/Conquer.sln`
  - **Commit**: `feat(packets): EntitySpawn.For branch + SpawnNpc(2030) in 2 spawn sites`
  - _Requirements: FR-10, FR-11, FR-12, FR-13, AC-2.1, AC-2.2, AC-2.5, AC-5.1, AC-5.2, AC-5.4, AC-5.6_
  - _Design: EntitySpawn / SpawnNpc / ActionHandler spawn-site branches_

- [x] 1.8 [VERIFY] REGRESSION GATE: retype is byte-identical for players
  - **Do**: Run the full strict gate. The ~56 existing surroundings/chat/world/spawn/walk tests MUST still pass — proves the `PlayerEntity`→`IWorldEntity` generalization caused NO player regression (AC-1.6, AC-2.x).
  - **Verify**: `scripts/dotnet build src/Conquer.sln` 0/0 AND `scripts/dotnet test src/Conquer.sln` green (ZERO regressions).
  - **Done when**: Build 0/0; every pre-existing test green; player path proven unchanged.
  - **Commit**: `chore(world): pass IWorldEntity retype regression gate` (only if fixes needed)
  - _Requirements: AC-1.6, AC-2.1, AC-2.3, AC-2.4, AC-2.6_

---

## Phase 2: NPCs Visible (POC milestone = NPCs appear on screen at login + on walk)

EntitySpawn.For already builds 2030 (Phase 1) — now exercised by loading real NPCs.

- [x] 2.1 Create NpcRepository + DbNpc
  - **Do**:
    1. Create `src/Database/NpcRepository.cs` mirroring `CharacterRepository`: `DbNpc` POCO (`UID/Name/MapID/X/Y/Mesh/Type/BaseId`) + `NpcRepository(ConnectionFactory)`.
    2. `IReadOnlyList<DbNpc> All()` → `conn.Query<DbNpc>("SELECT UID, Name, MapID, X, Y, Mesh, Type, BaseId FROM cq_npc").AsList()` (per design §NpcRepository).
  - **Files**: `src/Database/NpcRepository.cs`
  - **Done when**: `NpcRepository.All()` compiles, mirrors CharacterRepository Dapper shape.
  - **Verify**: `scripts/dotnet build src/Conquer.sln`
  - **Commit**: `feat(database): add NpcRepository.All() (Dapper) + DbNpc`
  - _Requirements: FR-7, AC-4.3_
  - _Design: NpcRepository_

- [x] 2.2 Add cq_npc table + 2 seed NPCs to init.sql
  - **Do**:
    1. `src/init.sql`: add `CREATE TABLE IF NOT EXISTS cq_npc` (`UID` PK, `Name`, `MapID`, `X`, `Y`, `Mesh`, `Type`, `BaseId` nullable; `idx_npc_map`; latin1/InnoDB) before `SET FOREIGN_KEY_CHECKS = 1;`.
    2. `INSERT IGNORE` 2 NPCs: `(90001,'Guide',1010,63,109,Mesh,2,NULL)`, `(90002,'Greeter',1010,60,111,2,NULL)` — UID band ≥90000 (no CharacterID collision), `Type=2` (Task/clickable), a plausible VISIBLE `Mesh` (placeholder; flag live-capture — exact humanoid mesh id is an Unresolved Question).
  - **Files**: `src/init.sql`
  - **Done when**: Idempotent table + 2 seed rows present, non-destructive (CREATE IF NOT EXISTS + INSERT IGNORE).
  - **Verify**: `grep -q 'CREATE TABLE IF NOT EXISTS .cq_npc' src/init.sql && grep -q '90001' src/init.sql && grep -q '90002' src/init.sql && echo PASS`
  - **Commit**: `feat(db): seed cq_npc table + 2 Map1010 NPCs (UID>=90000)`
  - _Requirements: FR-6, FR-9, AC-4.1, AC-4.2, AC-4.6_
  - _Design: DB Schema_

- [x] 2.3 Load NPCs at startup in Program.cs
  - **Do**:
    1. `src/Redux/Program.cs`: after `var world = new World();` (~:34), before listeners, `var npcs = new NpcRepository(factory).All();`.
    2. `foreach` → build `NpcEntity((uint)n.UID, n.MapID, (ushort)n.X, (ushort)n.Y, (ushort)n.Mesh, (ushort)n.Type, n.Name)` and `world.GetOrAdd(npc.MapId).Register(npc)` ONCE.
    3. `Console.WriteLine($"[Startup] Loaded {npcs.Count} NPCs");`.
  - **Files**: `src/Redux/Program.cs`
  - **Done when**: NPCs loaded once into the grid/roster at boot; build 0/0.
  - **Verify**: `scripts/dotnet build src/Conquer.sln`
  - **Commit**: `feat(redux): load + register NPCs at startup (once)`
  - _Requirements: FR-8, AC-4.4, AC-4.5_
  - _Design: Startup Load_

- [x] 2.4 [VERIFY] Quality checkpoint: NPC load path builds + tests green
  - **Do**: Run strict gate after repository/seed/startup-load land.
  - **Verify**: `scripts/dotnet build src/Conquer.sln` 0/0 AND `scripts/dotnet test src/Conquer.sln` green.
  - **Done when**: Build 0/0, all tests pass.
  - **Commit**: `chore(npcs): pass quality checkpoint` (only if fixes needed)

- [x] 2.5 POC milestone checkpoint: NPCs visible (automated proxy)
  - **Do**: Confirm the M1 path is wired end-to-end via automated checks (operator E2E "SEE 2 NPCs" happens at the gate — here we prove the code path is complete): `EntitySpawn.For` returns 2030 for NpcEntity, NPCs are registered at startup, 114 + enter-screen diff call `EntitySpawn.For`.
  - **Verify**: `grep -q 'EntitySpawn.For' src/Packets/ActionHandler.cs && grep -q 'Register(npc)' src/Redux/Program.cs && scripts/dotnet build src/Conquer.sln && echo POC_M1_PASS`
  - **Done when**: NPC spawn path complete; POC M1 = NPCs appear at login (114) + on walk (enter-screen diff). Operator E2E deferred to Phase 5 gate.
  - **Commit**: `feat(npcs): complete M1 POC — NPCs visible on screen`
  - _Requirements: AC-5.1, AC-5.2_

---

## Phase 3: Click → Static Dialog

- [x] 3.1 Create NpcDialog control builders (2032)
  - **Do**:
    1. Create `src/Packets/NpcDialog.cs`: private `Build(byte action, ushort id, byte linkback, string? text)` — body `12 + strings`, `AppendHeader(s, body+8, 2032)`, UID@4=0 (v1 default-place), ID@8, Linkback@10 (u8), Action@11 (u8), Strings@12 (NetStringPacker, omitted if empty) — per design §NpcDialog.
    2. `enum DialogAction`: Dialog/Text=1, Option=2, Avatar=4, Finish=100.
    3. Public `Avatar(ushort face)`, `Text(string line)`, `Option(string label, byte id)`, `Finish()` (Finish linkback=255).
  - **Files**: `src/Packets/NpcDialog.cs`
  - **Done when**: 4 control builders produce 2032 packets with the per-control layout.
  - **Verify**: `scripts/dotnet build src/Conquer.sln`
  - **Commit**: `feat(packets): add NpcDialog(2032) control builders`
  - _Requirements: FR-16, AC-6.6_
  - _Design: NpcDialog_

- [x] 3.2 Create NpcHandler + route case 2031
  - **Do**:
    1. Create `src/Packets/NpcHandler.cs`: World-injected ctor; `Handle(ClientSession, byte[] payload)` — guard `payload.Length < 16` (Rule 7); cast `session.WorldEntity is not PlayerEntity p → return`; read `npcUid=ReadUInt32LE(@4)`, `action=ReadUInt16LE(@12)`; `if (action != 0) return` (Activate only); look up `_world.GetOrAdd(p.MapId).Roster[npcUid]` → must be `NpcEntity` else return; send static sequence `Avatar(1)` + `Text("Hello, I am {npc.Name}.")` + `Text("Welcome, traveler.")` + `Finish()` to `session.SendGame` (clicker only) — per design §NpcHandler.
    2. `src/Redux/PacketRouter.cs`: add `private readonly NpcHandler _npc;`, `_npc = new NpcHandler(world);`, and `case 2031: _npc.Handle(session, payload); break;`.
  - **Files**: `src/Packets/NpcHandler.cs`, `src/Redux/PacketRouter.cs`
  - **Done when**: 2031/Activate(0) → 4-packet static dialog to the clicker; bad/unknown/non-NPC UID ignored.
  - **Verify**: `scripts/dotnet build src/Conquer.sln`
  - **Commit**: `feat(packets): NpcHandler(2031) static dialog + route case 2031`
  - _Requirements: FR-14, FR-15, FR-16, AC-6.1, AC-6.2, AC-6.3, AC-6.4, AC-6.5_
  - _Design: NpcHandler / Router Wiring_

- [x] 3.3 [VERIFY] Quality checkpoint: dialog path builds + tests green
  - **Do**: Run strict gate after dialog builders + handler + route land.
  - **Verify**: `scripts/dotnet build src/Conquer.sln` 0/0 AND `scripts/dotnet test src/Conquer.sln` green.
  - **Done when**: Build 0/0, all tests pass; case 2031 wired (automated proxy for "click → dialog").
  - **Commit**: `chore(npcs): pass quality checkpoint` (only if fixes needed)
  - _Requirements: AC-6.1_

---

## Phase 4: Testing (xUnit — World.Tests + Packets.Tests)

- [x] 4.1 [P] World.Tests: IWorldEntity grid coexistence + Broadcast skips NPCs
  - **Do**:
    1. Register a PlayerEntity + an NpcEntity near each other → `QueryScreen` returns BOTH (AC-1.5).
    2. Map with 1 player + 1 NPC → `Broadcast` hits only the player (NPC has no Session; no throw).
    3. Confirm existing `GridMath`/`ScreenDiff` tests stay green (no edit needed beyond the retype).
  - **Files**: `src/World.Tests/` (new test file, e.g. `WorldEntityTests.cs`)
  - **Done when**: Coexistence + broadcast-skip tests pass.
  - **Verify**: `scripts/dotnet test src/Conquer.sln`
  - **Commit**: `test(world): IWorldEntity grid coexistence + broadcast-skip`
  - _Requirements: AC-1.5, AC-2.5_
  - _Design: Test Strategy (World.Tests)_

- [ ] 4.2 [P] Packets.Tests: EntitySpawn.For branch + SpawnNpc + NpcDialog + NpcHandler layouts
  - **Do**:
    1. `EntitySpawn.For(player)` == `SpawnEntity.Build(player fields)` byte-for-byte (AC-2.1 regression); `For(npc)` → 2030 (type@2==2030).
    2. `SpawnNpc.Build`: assert every offset (UID@4/X@8/Y@10/Mesh@12/Type@14/Unknown1@16=0/Name@18), body == `18 + names.Length` (AC-5.3).
    3. `NpcDialog.{Avatar,Text,Option,Finish}`: Action@11, ID@8 (Avatar), Linkback@10 (Finish=255), Strings@12, body == `12 + strings` (AC-6.6).
    4. `NpcHandler` with a captured ClientSession sink: payload<16 → no send; Action≠0 → no send; unknown UID → no send; valid NPC → 4 sends in order Avatar/Text/Text/Finish (AC-6.2–6.5).
  - **Files**: `src/Packets.Tests/` (new test files)
  - **Done when**: All branch/layout/handler tests pass.
  - **Verify**: `scripts/dotnet test src/Conquer.sln`
  - **Commit**: `test(packets): EntitySpawn.For + SpawnNpc/NpcDialog/NpcHandler layouts`
  - _Requirements: AC-2.1, AC-5.3, AC-5.4, AC-6.2, AC-6.3, AC-6.4, AC-6.5, AC-6.6_
  - _Design: Test Strategy (Packets.Tests)_

- [ ] 4.3 [VERIFY] Full suite green + additive-scope diff
  - **Do**: Run full gate; confirm the diff is additive (no auth/crypto/handshake/GameConnection/char-creation/movement-own-position/chat behavior changed; GeneralData.cs untouched).
  - **Verify**: `scripts/dotnet build src/Conquer.sln` 0/0 AND `scripts/dotnet test src/Conquer.sln` green AND `git diff --name-only origin/feat/npcs..HEAD | grep -qv 'GeneralData.cs' && echo SCOPE_OK`.
  - **Done when**: Full suite green; scope confirmed additive.
  - **Commit**: `chore(npcs): full suite green + additive scope confirmed` (only if fixes needed)
  - _Requirements: AC-1.6, AC-2.6_

---

## Phase 5: Quality Gate + PR Lifecycle

Branch management handled at startup; already on `feat/npcs`. If on the default branch, STOP and alert the user.

- [ ] 5.1 [VERIFY] Full local CI gate
  - **Do**: Run the complete local CI suite.
  - **Verify**: `scripts/dotnet build src/Conquer.sln` 0/0 (nullable-clean strict gate, NFR-6) AND `scripts/dotnet test src/Conquer.sln` green.
  - **Done when**: Build 0/0, all tests pass.
  - **Commit**: `chore(npcs): pass full local CI gate` (only if fixes needed)
  - _Requirements: AC-1.6, NFR-6, NFR-7_

- [ ] 5.2 Push + PR to master with operator E2E checklist
  - **Do**:
    1. Confirm feature branch: `git branch --show-current` (must be `feat/npcs`, NOT default). Do NOT stage `src/docker-compose.yml`.
    2. `git push -u origin feat/npcs`.
    3. `gh pr create --base master --title "EPIC 3: static NPCs (visible + click→dialog)" --body "<summary + operator E2E checklist>"`.
    4. PR body MUST include the operator E2E checklist:
       - Apply seed (non-destructive): `docker compose -f src/docker-compose.yml -f src/docker-compose.override.yml exec -T db mysql -uroot -prootpass conquer < src/init.sql`
       - Rebuild with BOTH compose files.
       - Log in (`testplayer/password123`) → SEE 2 NPCs near spawn on Map 1010 (63/109, 60/111) → click one → dialog window opens with text → walk away (NPC despawns 132) → walk back (reappears 2030).
       - Live-capture the Unresolved Questions: exact visible Mesh id; whether Avatar+Text+Text+Finish opens the window; whether 132 on scroll-off is needed; exact NpcType; 2032 body slack (`12+strings`) tolerance.
  - **Files**: none (git/gh)
  - **Done when**: PR open against master with the operator E2E checklist + live-capture notes.
  - **Verify**: `gh pr view --json state,baseRefName | grep -q master && echo PR_OPEN`
  - **Commit**: none

- [ ] 5.3 [VERIFY] CI no-op + M2 operator gate
  - **Do**: Verify CI passes on the PR (automated proxy = case 2031 wired + tests green); the M2 visual confirmation (SEE + click NPC) is the operator E2E checklist in the PR.
  - **Verify**: `gh pr checks` shows all green (or CI no-op if no pipeline) AND `grep -q 'case 2031' src/Redux/PacketRouter.cs && echo M2_WIRED`.
  - **Done when**: CI green; case 2031 wired; operator E2E checklist present for the human visual gate.
  - **If CI fails**: read `gh pr checks`, fix locally, `git push`, re-verify.
  - **Commit**: none
  - _Requirements: Success Criteria (operator E2E)_

---

## Notes

- **IWorldEntity-retype regression criticality (Phase 1)**: the `PlayerEntity`→`IWorldEntity` change is a pure GENERALIZATION — player behavior MUST be byte-identical. Task 1.8 is the regression gate: ALL ~56 existing surroundings/chat/world/spawn/walk tests MUST stay green. `EntitySpawn.For(player)` calls `SpawnEntity.Build` with the IDENTICAL arg order as today's inline build → byte-identical 1014 (AC-2.1, xUnit-asserted in 4.2).
- **Static-NPC zero cost**: NPCs are `Register`ed ONCE at startup and never `Move`d — no cell churn, no tick, no broadcast, no per-packet DB. They ride the existing 3×3 `QueryScreen`. Added cost is O(NPCs-in-9-cells), negligible (NFR-8). NPCs are senders-of-spawn-only (no Session) — `Broadcast` skips them via `is PlayerEntity`.
- **No World→Packets cycle (Option B)**: `EntitySpawn.For` lives in the **Packets** project (which already refs World via `ActionHandler.cs:17`), NOT as a `BuildSpawn()` on the entity (which would force a World→Packets edge = cycle, NFR-7). The kind-branch is the cheap `Kind`/`is PlayerEntity` discriminator.
- **DB seed apply (existing volume)**: init.sql runs only on a fresh volume. Apply non-destructively (idempotent CREATE IF NOT EXISTS + INSERT IGNORE): `docker compose -f src/docker-compose.yml -f src/docker-compose.override.yml exec -T db mysql -uroot -prootpass conquer < src/init.sql`. NEVER `down -v` (wipes characters).
- **Do NOT touch** `GeneralData.cs` (`BuildRemoveEntity(uid)` is UID-only, works for any kind as-is) or `Grid.cs` (already generic — only the type argument changes). Do NOT stage/commit `src/docker-compose.yml`.
- **Live-unknowns (operator-capture during M1/M2 E2E — all LOW risk)**: exact visible Mesh/lookface id (seed uses a placeholder); whether the minimal Avatar+Text+Text+Finish sequence opens the dialog window or the client needs an Option / non-zero window-position UID; whether static NPCs need RemoveEntity(132) on scroll-off (shipped for symmetry, FR-13); exact `NpcType` the client expects for a clickable NPC (likely Task=2); 2032 body slack — original over-allocates `24+strings` vs our exact `12+strings` (confirm client tolerates).
