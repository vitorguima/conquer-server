---
spec: static-npcs
phase: requirements
epic: 3
created: 2026-06-27
---

# Requirements: static-npcs (EPIC 3)

## Goal
Static NPCs render on the map via the existing surroundings layer, and clicking one opens a static dialog window. Delivered by generalizing the World layer from `PlayerEntity` to `IWorldEntity` (the model EPIC 4 monsters reuse) WITHOUT regressing the player hot path.

## User Stories

### US-1: NPC entity model generalization
**As a** server developer
**I want to** generalize the World layer (Grid/MapInstance/QueryScreen/World/ScreenDiff) from `PlayerEntity` to `IWorldEntity`
**So that** the registry/grid/broadcast can hold non-player entities (NPCs now, monsters later) without duplicating the world layer.

**Acceptance Criteria:**
- [ ] AC-1.1: GIVEN the World layer WHEN compiled THEN `IWorldEntity` exists exposing `Uid`, `MapId`, `ushort X`, `ushort Y`, `CellX`, `CellY`, `Kind` (EntityKind discriminator), and NPC spawn-source fields needed by the Packets branch.
- [ ] AC-1.2: GIVEN `PlayerEntity` WHEN compiled THEN it implements `IWorldEntity` with `Kind == EntityKind.Player`.
- [ ] AC-1.3: GIVEN `Grid<T>` (already generic) WHEN MapInstance is constructed THEN it is reinstantiated as `Grid<IWorldEntity>` with NO change to Grid itself.
- [ ] AC-1.4: GIVEN MapInstance/ScreenDiff/World WHEN compiled THEN `Roster`, `_grid`, `Register`/`Deregister`/`Move`/`QueryScreen`/`Broadcast` params+returns and `ScreenDiff.Entered`/`Left` are typed `IWorldEntity`.
- [ ] AC-1.5: GIVEN a map with a player AND an NPC registered WHEN `QueryScreen` is called near both THEN both are returned (xUnit verified).
- [ ] AC-1.6: GIVEN the retype WHEN `scripts/dotnet test src/Conquer.sln` runs THEN all pre-existing surroundings/chat/walk/spawn tests stay green (no behavior regression).

### US-2: Player path is unchanged (regression-critical)
**As a** player already in the world
**I want to** see other players, my own spawn echo, movement/jump broadcast, and chat exactly as before
**So that** the IWorldEntity generalization is invisible to me.

**Acceptance Criteria:**
- [ ] AC-2.1: GIVEN a players-only map WHEN a player requests surroundings (114) THEN the bytes sent are identical to pre-EPIC-3 (`EntitySpawn.For(player)` produces the same 1014 as the old inline `SpawnEntity.Build`).
- [ ] AC-2.2: GIVEN two players in screen range WHEN one enters the other's screen THEN both still receive each other's mutual 1014 spawn AND the Visible set is seeded for both (unchanged).
- [ ] AC-2.3: GIVEN a player walks/jumps WHEN the move broadcasts THEN movement/jump broadcast to screen is identical (NPCs never walk/jump/broadcast).
- [ ] AC-2.4: GIVEN a player chats WHEN the chat broadcasts THEN chat behavior is identical.
- [ ] AC-2.5: GIVEN `Broadcast`/mutual-spawn/Visible-seed code WHEN an entity is an NPC THEN it is skipped via an `is PlayerEntity` guard (NPCs have no ClientSession; they never receive).
- [ ] AC-2.6: GIVEN the player hot path WHEN broadcasting THEN allocations and the O(N·k) broadcast cost are unchanged from EPIC 1.

### US-3: NpcEntity (static non-player entity)
**As a** server developer
**I want to** model an NPC as a static `IWorldEntity` with no ClientSession
**So that** NPCs sit in the grid as senders-of-spawn-only that never move or re-index.

**Acceptance Criteria:**
- [ ] AC-3.1: GIVEN `NpcEntity` WHEN constructed THEN it carries `Uid`, `MapId`, `X`, `Y`, `Mesh`, `NpcType`, `Name`, `Kind == EntityKind.Npc`, and NO ClientSession.
- [ ] AC-3.2: GIVEN an `NpcEntity` WHEN it is registered THEN it is inserted into its MapInstance roster + grid cell exactly ONCE and is never moved or re-indexed.
- [ ] AC-3.3: GIVEN `NpcEntity` fields (`Mesh`/`NpcType`/`Name`) WHEN accessed by the Packets branch THEN they are public/readable for `EntitySpawn.For`.

### US-4: Load NPCs from the database at startup
**As an** operator
**I want to** seed NPCs in a `cq_npc` table and have them loaded into the world at startup
**So that** NPCs appear on the map without per-packet DB access.

**Acceptance Criteria:**
- [ ] AC-4.1: GIVEN `init.sql` WHEN applied THEN a `cq_npc` table exists (`UID, Name, MapID, X, Y, Mesh, Type, BaseId`; latin1/InnoDB like `characters`).
- [ ] AC-4.2: GIVEN `init.sql` WHEN applied THEN 2 seed NPCs exist on Map 1010 near spawn (e.g. 63/109 and 60/111), `Type`=2 (Task/clickable), UID band ≥ 90000.
- [ ] AC-4.3: GIVEN `NpcRepository.All()` (Dapper, mirroring CharacterRepository) WHEN called THEN it returns all `cq_npc` rows.
- [ ] AC-4.4: GIVEN startup WHEN the world is built (`Program.cs` after `new World()`) THEN ALL NPCs are loaded and each is `Register`ed into its MapInstance grid ONCE.
- [ ] AC-4.5: GIVEN existing CharacterIDs WHEN NPCs are loaded THEN no NPC UID collides with any live CharacterID (roster is keyed by UID across ALL kinds).
- [ ] AC-4.6: GIVEN an existing db volume WHEN the seed is applied THEN it applies non-destructively via `exec -T db mysql conquer < src/init.sql` (`CREATE TABLE IF NOT EXISTS` + `INSERT IGNORE`), NOT `down -v`.

### US-5: NPCs spawn on a player's screen
**As a** player
**I want to** see NPCs that are within screen range
**So that** the world is populated.

**Acceptance Criteria:**
- [ ] AC-5.1: GIVEN a player requests surroundings (114) WHEN on-screen NPCs exist THEN a SpawnNpc(2030) is sent to the player for each (one-way; NPCs get no mutual spawn).
- [ ] AC-5.2: GIVEN a player crosses a cell boundary WHEN `diff.Entered` contains an NPC THEN a SpawnNpc(2030) is sent to that player for the NPC.
- [ ] AC-5.3: GIVEN SpawnNpc.Build WHEN invoked THEN the wire layout is UID@4, X@8, Y@10, Mesh@12, Type@14, Unknown1@16=0, Name@18 (NetStringPacker), body sized exactly `18 + names.Length` (xUnit byte-layout verified).
- [ ] AC-5.4: GIVEN `EntitySpawn.For(IWorldEntity)` in the Packets project WHEN given an NPC THEN it returns SpawnNpc(2030); WHEN given a Player THEN it returns SpawnEntity(1014).
- [ ] AC-5.5: GIVEN a player scrolls an NPC off-screen WHEN `diff.Left` contains the NPC THEN a RemoveEntity(132) is sent for symmetry with players (operator-capture: confirm the live client needs it).
- [ ] AC-5.6: GIVEN `EntitySpawn.For` lives in the Packets project WHEN compiled THEN it does NOT introduce a World→Packets dependency (no cycle; Packets→World stays the only edge).

### US-6: Clicking an NPC opens a static dialog
**As a** player
**I want to** click an NPC and see a dialog window with text
**So that** NPCs are interactive.

**Acceptance Criteria:**
- [ ] AC-6.1: GIVEN inbound MsgNpc(2031) WHEN `PacketRouter.Dispatch` runs THEN `case 2031:` routes to `NpcHandler`.
- [ ] AC-6.2: GIVEN a 2031 payload WHEN `payload.Length < 16` THEN the handler returns without reading (Rule 7 guard-first).
- [ ] AC-6.3: GIVEN a valid 2031 WHEN read THEN clicked NPC UID is read @4 and Action @12; only `Action == 0` (Activate) is handled, others ignored.
- [ ] AC-6.4: GIVEN the clicked UID WHEN looked up in the player's map roster AND it is an existing NpcEntity THEN a static NpcDialog(2032) sequence is sent to the clicking player ONLY; bad/unknown/non-NPC UID is ignored.
- [ ] AC-6.5: GIVEN the dialog sequence WHEN sent THEN it is Avatar(face) + Text + Text + Finish (≥3 controls so the client renders the window).
- [ ] AC-6.6: GIVEN each NpcDialog control WHEN built THEN its 2032 layout is UID@4 (window-pos, 0 for v1), ID@8 (avatar face), Linkback@10 (u8; 255=close on Finish), Action@11 (DialogAction), Strings@12 (NetStringPacker) (xUnit byte-layout + per-control verified).
- [ ] AC-6.7: GIVEN an inbound 2032 option-click follow-up WHEN received THEN it is ignored/logged (out of scope v1; no branching).

## Functional Requirements

| ID | Requirement | Priority | Acceptance Criteria |
|----|-------------|----------|---------------------|
| FR-1 | `IWorldEntity` interface (Uid/MapId/X/Y/CellX/CellY/Kind + NPC spawn fields); PlayerEntity implements it | High | AC-1.1, AC-1.2 |
| FR-2 | Reinstantiate `Grid<IWorldEntity>`; retype MapInstance/World/ScreenDiff to `IWorldEntity` | High | AC-1.3, AC-1.4 |
| FR-3 | Player+NPC coexist in `QueryScreen`; existing tests stay green | High | AC-1.5, AC-1.6 |
| FR-4 | Player path byte-identical (114/mutual 1014/movement/jump/chat); guard non-players in Broadcast/mutual/Visible-seed | High | AC-2.1–AC-2.6 |
| FR-5 | `NpcEntity` (Kind=Npc; no ClientSession; static, never re-indexed) | High | AC-3.1–AC-3.3 |
| FR-6 | `cq_npc` table + 2 seed NPCs (Map 1010, Type=2, UID≥90000) in `init.sql` | High | AC-4.1, AC-4.2 |
| FR-7 | `NpcRepository.All()` (Dapper, mirror CharacterRepository) | High | AC-4.3 |
| FR-8 | Load all NPCs at startup (`Program.cs` after `new World()`); Register once | High | AC-4.4, AC-4.5 |
| FR-9 | Non-destructive seed apply via `mysql < src/init.sql` | Medium | AC-4.6 |
| FR-10 | `SpawnNpc.Build` (2030) byte layout | High | AC-5.3 |
| FR-11 | `EntitySpawn.For(IWorldEntity)` branch in Packets project (no cycle) | High | AC-5.4, AC-5.6 |
| FR-12 | 114 reply + enter-screen diff send 2030 for on-screen NPCs (one-way) | High | AC-5.1, AC-5.2 |
| FR-13 | RemoveEntity(132) for NPCs in `diff.Left` (symmetry; operator-capture) | Medium | AC-5.5 |
| FR-14 | `PacketRouter` `case 2031:` → `NpcHandler` | High | AC-6.1 |
| FR-15 | `NpcHandler`: guard length, read UID@4/Action@12, Activate-only, validate UID in roster | High | AC-6.2, AC-6.3, AC-6.4 |
| FR-16 | `NpcDialog` builders (Avatar/Text/Finish controls) + static Avatar+Text+Text+Finish sequence to clicker | High | AC-6.5, AC-6.6 |
| FR-17 | Inbound 2032 option follow-up ignored/logged | Low | AC-6.7 |

## Non-Functional Requirements

| ID | Requirement | Metric | Target |
|----|-------------|--------|--------|
| NFR-1 | NPCs static — register once, never move/re-index | Ongoing per-NPC cost | Zero (no Move, no cell churn, no broadcast); ride existing screen query |
| NFR-2 | Build-once spawn/dialog packets; reuse build-once broadcast for screen spawns | Per-recipient packet builds | Build once, encrypt per-stream (as EPIC 1) |
| NFR-3 | No per-packet DB | DB round-trips in hot path | 0 (NPCs loaded once at startup; dialog is in-memory/static) |
| NFR-4 | Player hot path not regressed by the retype | Allocations + broadcast complexity | Identical to EPIC 1; O(N·k) broadcast preserved |
| NFR-5 | Validate all wire input | 2031 bounds | `payload.Length < 16` guard + UID validated against roster bounds |
| NFR-6 | Strict gate clean | Build warnings | 0/0 nullable-clean (`scripts/dotnet build src/Conquer.sln`) |
| NFR-7 | Acyclic layering preserved | Project refs | No World→Packets edge introduced (Packets→World only) |
| NFR-8 | Added screen-query cost | Cost per screen query | O(NPCs-in-9-cells), negligible (handful per screen) |

## Glossary
- **IWorldEntity**: Common interface for any entity in the world layer (Uid/MapId/X/Y/CellX/CellY/Kind + spawn-source fields). The generalization that lets the grid/registry/broadcast hold non-players. Reused by EPIC 4 monsters.
- **EntityKind / Kind**: byte discriminator (Player/Npc/Monster/GroundItem) on each entity; cheap branch for "players only" logic without a builder switch.
- **NpcEntity**: Static `IWorldEntity` with Kind=Npc, no ClientSession; carries Mesh/NpcType/Name. Sender-of-spawn-only, never receives, never moves.
- **EntitySpawn.For**: Static helper in the **Packets** project that branches an `IWorldEntity` to its spawn builder (Npc→SpawnNpc 2030, Player→SpawnEntity 1014). Lives in Packets (not on the entity) to avoid a World→Packets cycle.
- **SpawnNpc (2030)**: Outbound NPC spawn packet. Layout UID@4, X@8, Y@10, Mesh@12, Type@14, Unknown1@16=0, Name@18. Sent one-way to players with the NPC on-screen.
- **MsgNpc (2031)**: Inbound NPC-interaction packet. A click arrives as Action=Activate(0); UID@4 = clicked NPC, Action@12 = NpcEvent, Type@14 = linkback. Min payload 16.
- **NpcDialog (2032) control sequence**: Outbound dialog window built from a SEQUENCE of 2032 control packets. Each control: UID@4 (window-pos), ID@8 (avatar face), Linkback@10, Action@11 (DialogAction: Dialog=1/Option=2/Avatar=4/Finish=100), Strings@12. v1 static = Avatar + Text + Text + Finish (≥3 controls to render).
- **cq_npc**: New DB table holding static NPCs (UID/Name/MapID/X/Y/Mesh/Type/BaseId). Loaded once at startup.
- **UID band**: Reserved UID range (≥ 90000) for seeded NPCs to avoid collision with CharacterIDs in the shared roster keyspace.

## Out of Scope
- Quest logic (EPIC 8).
- Shops / vendors / storage / warehouse / booth.
- Scripted, branching, or multi-step dialog; dialog OPTION follow-ups; the inbound 2032 option-click handling.
- NPC movement / AI / wandering.
- NPC combat / HP.
- `NpcType` values beyond Task(2); `BaseId`/dialog-script wiring (`BaseId` column reserved but unused in v1).
- Cross-map NPC visibility.

## Dependencies
- **EPIC 1 World layer** (`src/World/{Grid,MapInstance,World,ScreenDiff,PlayerEntity}.cs`) — the registry/grid/broadcast NPCs plug into; the generalization target.
- **The 2 existing spawn sites** in `src/Packets/ActionHandler.cs` — 114 reply (`HandleGetSurroundings`) + `ApplyDiff` — that build per-entity spawns and gain the `EntitySpawn.For` branch.
- **CharacterRepository pattern** (`src/Database/CharacterRepository.cs`, `ConnectionFactory.cs`) — mirrored by `NpcRepository` (Dapper).
- **`src/init.sql`** — owns schema/seed; gains the `cq_npc` table + 2 seed rows; applied non-destructively to existing volumes.
- **PacketRouter** (`src/Redux/PacketRouter.cs`) — gains `case 2031`.
- **Reference (original, read-only)**: `src/Redux/Packets/Game/[2030] SpawnNpc.cs`, `[2031] Npc.cs`, `[2032] NpcDialog.cs`; `src/Redux/Enum/{NpcType,NpcEvent,DialogAction}.cs`.
- Dockerized build/test (`scripts/dotnet`); xUnit.

## Success Criteria
- Operator E2E: log in (`testplayer/password123`) → SEE 2 NPCs standing near spawn on Map 1010 → click one → a dialog window opens with text. Walk away → NPC despawns; walk back → reappears.
- `scripts/dotnet test src/Conquer.sln` green, including: player+NPC coexistence in `QueryScreen`/`ApplyDiff` (players still get 1014, NPCs get 2030, NPCs never receive) + pure byte-layout tests for `SpawnNpc.Build` and each `NpcDialog.*` control + the `EntitySpawn.For` branch.
- `scripts/dotnet build src/Conquer.sln` 0 warnings / 0 errors (nullable-clean strict gate).
- No regression: pre-existing surroundings/chat/walk/spawn tests stay green; auth/crypto/handshake/GameConnection/enter-world-spawn-echo/char-creation/movement/chat behavior unchanged.

## Unresolved Questions (operator-capture during M1/M2 E2E — all LOW risk, protocol is recovered from the in-repo original)
- Exact visible `Mesh`/lookface id that renders a humanoid NPC in the 5065 client (seed uses a placeholder until captured).
- Whether the minimal Avatar+Text+Text+Finish sequence opens the dialog window, or the client needs an Option/specific UID-position.
- Whether static NPCs require RemoveEntity(132) on scroll-off, or tolerate persistence (ship 132 for symmetry; confirm live).
- Exact `NpcType` value the client expects for a clickable dialog NPC (likely Task=2; confirm).

## Next Steps
1. Approve requirements (`awaitingApproval` is set — coordinator stops here).
2. Run `/ralph-specum:design` to produce the technical design (IWorldEntity surface, EntitySpawn.For branch, SpawnNpc/NpcDialog builders, NpcHandler, NpcRepository, cq_npc schema, Program.cs load, PacketRouter wiring).
3. M1 (NPCs visible) before M2 (click→dialog), per research milestones.
