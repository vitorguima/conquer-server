---
spec: movement
basePath: specs/movement
phase: requirements
---

# Requirements: Server-Authoritative Movement (MsgWalk 1005)

## Goal

Make own-player movement server-authoritative: handle MsgWalk(1005), track live position in memory per session, and persist once on disconnect so relog spawns at the last position — not the spawn tile.

## User Stories

### US-1: Server tracks my movement
**As a** logged-in player walking in the 5065 client
**I want to** have the server update its authoritative position as I move
**So that** my position is no longer frozen at the spawn tile server-side

**Acceptance Criteria:**
- [ ] AC-1.1: **Given** a valid MsgWalk(1005) on the game connection, **When** dispatched, **Then** a `WalkHandler` parses UID@2, Direction@6 (u8), Mode@7 (u8) from the net8 dispatch payload (body offset − 2).
- [ ] AC-1.2: **Given** Direction `d` in 0..7, **When** the handler computes the new tile, **Then** it applies the literal delta table `DeltaX={0,-1,-1,-1,0,1,1,1}`, `DeltaY={1,1,0,-1,-1,-1,0,1}` at index `d`.
- [ ] AC-1.3: **Given** a valid walk, **When** processed, **Then** the session's in-memory `CurrentX`/`CurrentY` are updated (per-session state) and a `[Game] walk dir=N -> (x,y)` line is logged.
- [ ] AC-1.4: **Given** a valid walk, **When** processed, **Then** the handler sends NO packet back to the moving client (client prediction already moved the avatar) and performs NO DB write.

### US-2: Relog spawns me where I logged off
**As a** player who walked to a non-spawn tile and logged off
**I want to** spawn at that last position when I log back in
**So that** my movement persists across sessions

**Acceptance Criteria:**
- [ ] AC-2.1: **Given** a game session with `PositionLoaded == true` and a non-null `Character`, **When** the connection tears down, **Then** exactly one `CharacterRepository.UpdatePosition(characterId, map, x, y)` UPDATE runs in the `ServeGameAsync` finally block.
- [ ] AC-2.2: **Given** a player relogs after a disconnect flush, **When** enter-world runs, **Then** `FindByAccountId` reads the saved MapID/X/Y and the existing spawn path places the player at the last position (no change to spawn logic).
- [ ] AC-2.3: **Given** a session that never loaded a position (`PositionLoaded == false`) or has a null `Character`, **When** teardown runs, **Then** NO position UPDATE is issued.

### US-3: Malformed/invalid walk packets never break my session
**As a** player on a flaky connection (or facing a malformed packet)
**I want to** have invalid walk input ignored, not disconnect me
**So that** a single bad packet does not kick me out

**Acceptance Criteria:**
- [ ] AC-3.1: **Given** a 1005 payload with `Length < 8`, **When** the handler runs, **Then** it logs a short-packet warning and returns without indexing past the bound (guard-first, Power-of-10 Rule 7).
- [ ] AC-3.2: **Given** a Direction byte `> 7`, **When** validated, **Then** the packet is logged and ignored (no position change, no disconnect).
- [ ] AC-3.3: **Given** a resulting `(x,y)` that is negative or exceeds `ushort.MaxValue`, **When** validated, **Then** the move is rejected (logged + ignored), and the in-memory position is unchanged.
- [ ] AC-3.4: **Given** `session.Character == null` or `PositionLoaded == false`, **When** a 1005 arrives, **Then** the handler returns early (no live position to move).

### US-4: Live position seeds correctly at connect
**As a** player entering the world
**I want to** have my live in-memory position initialized from my persisted character row
**So that** the very first walk moves from the correct tile

**Acceptance Criteria:**
- [ ] AC-4.1: **Given** the 1052 connect sets `session.Character = character`, **When** `GameHandler.Handle` runs, **Then** `CurrentMap`/`CurrentX`/`CurrentY` are seeded from `character.MapID/X/Y` and `PositionLoaded = true` is set immediately after the null check.
- [ ] AC-4.2: **Given** the seed, **When** the enter-world `ActionHandler.HandleSetLocation` spawn echo runs, **Then** it still reads `Character.MapID/X/Y` (the seed SOURCE) and is unchanged.

## Functional Requirements

| ID | Requirement | Priority | Acceptance Criteria |
|----|-------------|----------|---------------------|
| FR-1 | New `WalkHandler` in `src/Packets` parses 1005 (UID@2, Direction@6, Mode@7) | High | Parses correct offsets; unit test asserts fields |
| FR-2 | `case 1005:` added to `PacketRouter.Dispatch`, `_walk` constructed in ctor (pure in-memory, no repo) | High | 1005 no longer logs "Unknown typeId=1005" |
| FR-3 | Compute new `(x,y)` via literal 8-direction delta table, index = `Direction % 8` (reject `>7` before indexing) | High | Unit test asserts each dir delta |
| FR-4 | Update in-memory `CurrentX`/`CurrentY` per session on valid walk; log the move | High | Live position changes; no DB call |
| FR-5 | `ClientSession` gains `CurrentMap`/`CurrentX(ushort)`/`CurrentY(ushort)`/`PositionLoaded(bool)` mutable fields | High | Fields present, nullable-clean |
| FR-6 | Seed live position from `Character` in `GameHandler.Handle` (MsgConnect 1052) | High | AC-4.1 |
| FR-7 | `CharacterRepository.UpdatePosition(charId, mapId, x, y)` Dapper UPDATE | High | One UPDATE; relog reads it |
| FR-8 | Disconnect flush in `NetworkListener.ServeGameAsync` finally; inject `CharacterRepository` into listener | High | AC-2.1, AC-2.3 |
| FR-9 | Invalid walk (short / dir>7 / out-of-bounds coords) → log + ignore, never disconnect | High | US-3 ACs |
| FR-10 | Mode (`payload[7]`) read for logging only; same single-tile delta for walk and run | Medium | Run does not change step size |
| FR-11 | xUnit tests for delta math + 1005 parse, mirroring `ActionParseTests.cs` (no socket/DB) | High | Tests pass via `scripts/dotnet test` |

## Non-Functional Requirements

| ID | Requirement | Metric | Target |
|----|-------------|--------|--------|
| NFR-1 | Live position is in-memory authoritative — no DB round-trip per movement | DB writes per walk packet | **0** |
| NFR-2 | Persistence is async/batched — exactly one position UPDATE per session | UPDATEs per session (v1) | **1**, on disconnect only; per-step/per-N forbidden |
| NFR-3 | Hot-path allocation discipline — walk path avoids per-packet heap allocs | `new byte[]` / alloc per 1005 | **0** (Span/BinaryPrimitives; no echo buffer in v1) |
| NFR-4 | New code is nullable-clean and passes strict build gate | Build warnings/errors | **0/0** on Crypto/Packets/Database/Network (warnings-as-errors) |
| NFR-5 | No `unsafe`/pointers in new code; parse via `ReadOnlySpan<byte>` + `BinaryPrimitives` | unsafe blocks in new code | **0** (Power-of-10 Rule 9) |
| NFR-6 | Small, guard-first functions | Handler size | ~≤60 lines, early returns (Rules 1, 4, 5) |
| NFR-7 | Live-position abstraction stays per-player addressable so future surroundings/entity-registry + broadcast can build on it without rework | Design constraint (documented, not v1 work) | Position state addressable per-session, not buried |

## Glossary

- **MsgWalk (1005)**: Game-connection packet a moving player sends each step. Net8 dispatch payload: type@0, UID@2, Direction@6 (u8), Mode@7 (u8), Unknown1@8 (u16).
- **Direction**: u8 0..7, the 8-direction step index (counter-clockwise from due-south, +Y).
- **Mode**: u8 distinguishing walk vs run (client animation speed). v1 applies the same single-tile delta regardless.
- **Delta table**: `DeltaX={0,-1,-1,-1,0,1,1,1}`, `DeltaY={1,1,0,-1,-1,-1,0,1}`; `(x,y) += (DeltaX[d], DeltaY[d])` at `d = Direction % 8`.
- **Authoritative position**: the server's own copy of the player's tile, held in memory as the source of truth (client prediction is advisory; v1 trusts and bound-checks the move).
- **In-memory hot state**: per-session live fields (`CurrentMap/X/Y`, `PositionLoaded`) mutated on the walk hot path with no DB I/O.
- **Disconnect-flush**: the single `UpdatePosition` UPDATE issued in the connection teardown (`ServeGameAsync` finally) that persists the session's last live position.

## Out of Scope

- Server-side map collision/walkability (no `.cqmap` loaded; client enforces its own — trust the move, only bound-check coords).
- Anti-speedhack / move-timing validation.
- **Broadcasting movement to other players** (needs the surroundings/entity system — separate spec).
- `GetSurroundings(114)` and any surroundings echo.
- The `NewCoordinates` snap-back correction (nothing to reject without server collision in v1).
- Jump (not carried on 1005 — no x/y fields; separate packet/action).
- Echo/confirm to the moving client (client prediction already moved the avatar).
- Periodic/per-N-step flush (noted as future hardening for crash-loss windows, not v1).
- Any change to auth, crypto/handshake, GameConnection, SendGame, enter-world spawn logic, or character creation.

## Dependencies

- Live working stack: auth (1051→token→1055), game DH + Blowfish-CFB64 handshake, MsgConnect(1052)/HeroInformation(1006), enter-world (GeneralData 1010 / SetLocation 74 spawn), character creation — all merged M1, must remain unchanged.
- `CharacterRepository` (`src/Database`): existing `FindByAccountId` (reads MapID/X/Y on relog) + `Insert`; this spec adds `UpdatePosition`.
- `NetworkListener.ServeGameAsync` teardown hook (`finally`): the single per-connection flush point; requires injecting `CharacterRepository` (constructed in `Program.cs`).
- `PacketRouter` dispatch + handler-injection pattern (mirror `_action`/`_register`).
- `ClientSession` (`src/Network`): host for the new mutable live-position fields.
- Dockerized build/test only (`scripts/dotnet build|test src/Conquer.sln`) — no local SDK.

## Success Criteria

- Operator-manual E2E (authoritative acceptance): walk around in the real 5065 client, log off at a non-spawn tile, relog → character spawns at the LAST position, not the spawn tile.
- 1005 no longer logs "Unknown typeId=1005"; `[Game] walk dir=N -> (x,y)` appears on valid walks.
- xUnit covers delta math (all 8 directions) + 1005 parse (Direction@6, Mode@7); `scripts/dotnet test src/Conquer.sln` green.
- `scripts/dotnet build src/Conquer.sln` is 0 warnings / 0 errors.
- Exactly one position UPDATE per session, on disconnect; zero DB writes per walk packet.

## Unresolved Questions

- LIVE-only: exact 1005 bytes vs a real 5065-client capture — confirm Direction lands at payload[6] and Mode at payload[7] on the wire (offsets are from in-repo `WalkPacket`).
- LIVE-only: whether the 5065 client needs ANY server confirm under prediction (recommend none; revert to original `SendToScreen(...,true)` echo only if the avatar visibly desyncs).
- LIVE-only: Direction→delta mapping vs what the 5065 client expects (the `{0,-1,-1,-1,0,1,1,1}` / `{1,1,0,-1,-1,-1,0,1}` table is the fork's own convention — verify a few directions visually).
- Crash-durability: disconnect-only flush loses unsaved walks on a hard crash/kill. Accepted for v1 (single player, manual E2E); periodic flush is the documented future hardening.

## Next Steps

1. Approve requirements (coordinator stops here — `awaitingApproval = true`).
2. Run the design phase: WalkHandler parse/validate/delta + ClientSession fields + UpdatePosition + disconnect-flush wiring, with the NFR-1..7 scalability/perf constraints first-class.
3. Generate tasks (POC-first): M-walk-1 (parse+validate+in-mem update+log), then M-walk-2 (UpdatePosition + disconnect flush + relog).
4. Implement; verify via Dockerized build + xUnit + operator-manual live walk-and-relog.
