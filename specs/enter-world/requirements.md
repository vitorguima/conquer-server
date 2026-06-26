---
spec: enter-world
phase: requirements
created: 2026-06-26
---

# Requirements: enter-world

## Goal

Get the real 5065 client past the post-login "loading" freeze to a VISIBLE character ("Vitor") standing in the game world. Purely additive on the net8 game-frame dispatch; handshake/crypto stay untouched.

## Assumptions (explicit — NOT silent guesses)

- **A1 — Spawn at DB coords (Map 1010 / X 61 / Y 109).** SetLocation reply uses `DbCharacter.MapID/X/Y` as-is. Operator chose to keep the seeded char rather than re-seed to Twin City. **This intentionally overrides research's recommendation** (research.md recommends 1002/438/381 for first bring-up) — a deliberate operator decision, not an oversight.
  - **RISK:** if the operator's client lacks map 1010 or those coords are invalid, the client may freeze on map load. **Documented fallback:** re-seed/override to Twin City **1002 / 438 / 381** (known-good). Operator-verified live.
- **A2 — 1006 + SetLocation position alone renders the own body.** Research: `MapManager.AddPlayer` never sends self-spawn(1014); body is rendered from HeroInformation(1006) + the SetLocation position.
  - **RISK:** if the body does NOT render after SetLocation + 1110, fall back to self-spawn(1014) (FR-7, InvisibleEntity/102 path). Gated on live observation.
- **A3 — First post-1006 1010 subtype is SetLocation(74).** Action at GeneralData body offset 22 (net8 dispatch payload offset 20). Confirm live by dumping the full frame.

## User Stories

### US-1: Persist logged-in character on session
**As a** game server
**I want to** keep the authenticated `DbCharacter` on the `ClientSession`
**So that** later handlers can read MapID/X/Y/name/look instead of re-querying.

**Acceptance Criteria:**
- [ ] AC-1.1: **Given** MsgConnect(1052) validates token (AccountID 2), **When** the character is looked up, **Then** it is stored on `ClientSession` (e.g. `Character`), not discarded.
- [ ] AC-1.2: **Given** a stored character, **When** the 1010 handler runs, **Then** it reads MapID/X/Y from the session without a new DB query.
- [ ] AC-1.3: **Given** no character found, **Then** existing behavior is unchanged (no crash; no session character set).

### US-2: Dispatch GeneralData(1010) in the net8 path
**As a** game server
**I want to** route inbound 1010/MSG_ACTION frames to a new handler
**So that** I can react to the client's enter-world action.

**Acceptance Criteria:**
- [ ] AC-2.1: **Given** an inbound game frame of type 1010, **When** `PacketRouter.Dispatch` runs, **Then** `case 1010:` invokes the new handler (GameHandler/ActionHandler).
- [ ] AC-2.2: **Given** the dispatch payload (length-prefix stripped), **When** the handler parses Action, **Then** it reads a u16 at payload offset 20.
- [ ] AC-2.3: **Given** an unhandled Action subtype, **Then** the handler logs and no-ops (no crash, no reply).
- [ ] AC-2.4: **Given** any other (existing) packet type, **Then** routing is unchanged.

### US-3: Handle SetLocation(74) → clear loading
**As a** logged-in player
**I want** the server to reply to SetLocation with my map + position
**So that** the loading screen clears.

**Acceptance Criteria:**
- [ ] AC-3.1: **Given** a 1010 with Action == 74, **When** handled, **Then** the server echoes a 1010 with `UID=charUID, Data1=MapID, Data2=(Y<<16)|X` (Data2Low=X, Data2High=Y), Action=74.
- [ ] AC-3.2: **Given** the SetLocation echo is sent, **When** the handler continues, **Then** it sends MapStatusPacket(1110) immediately after.
- [ ] AC-3.3: **Given** both replies, **When** sent, **Then** they go via `ClientSession.SendGame` (8-byte "TQServer" seal + Blowfish).
- [ ] AC-3.4: **Given** the session character, **Then** MapID/X/Y in the echo come from `DbCharacter.MapID/X/Y` (A1).
- [ ] AC-3.5 (OPERATOR-MANUAL, out-of-CI): **Given** the real Windows 5065 client at loading, **When** SetLocation echo + 1110 arrive, **Then** the loading screen clears.

### US-4: Render the character standing in-world
**As an** operator
**I want** "Vitor" visible standing at the spawn position
**So that** enter-world is confirmed complete.

**Acceptance Criteria:**
- [ ] AC-4.1 (OPERATOR-MANUAL, out-of-CI): **Given** loading cleared, **Then** "Vitor" renders standing (stand action) in-world at the spawn map/coords.
- [ ] AC-4.2: **Given** A2 holds (1006 + position render the body), **Then** NO self-spawn(1014) is sent in the minimal path.
- [ ] AC-4.3 (fallback, gated): **Given** the body does NOT render, **Then** FR-7 (self-spawn 1014) is applied and re-verified.

## Functional Requirements

| ID | Requirement | Priority | Acceptance Criteria |
|----|-------------|----------|---------------------|
| FR-1 | Add `DbCharacter? Character` to `ClientSession`; store at MsgConnect(1052) instead of discarding | High | Session exposes character with MapID/X/Y/name/look after 1052 |
| FR-2 | Add `case 1010:` to `PacketRouter.Dispatch` → new GameHandler/ActionHandler | High | Type-1010 frames reach the handler; other types unchanged |
| FR-3 | Parse Action (u16) at net8 payload offset 20 (= GeneralData body offset 22) | High | Handler reads correct subtype; SetLocation==74 branch taken |
| FR-4 | On SetLocation(74): build + send 1010 echo (Data1=MapID, Data2Low=X, Data2High=Y, Action=74, UID=charUID) | High | Echo bytes match original `[1010] GeneralData` layout |
| FR-5 | On SetLocation(74): send MapStatusPacket(1110) after the echo (UID=mapid, ID=mapid, Type=0) | High | 1110 bytes match original `[1110] MapStatus` layout |
| FR-6 | Build 1010-echo / 1110 / 1014 packet builders in net8 packet layer (Conquer.Packets), mirroring original byte layouts | High | Unit-testable layout assertions pass |
| FR-7 | Fallback: if body doesn't render after SetLocation+1110, send SpawnEntityPacket(1014) to self (InvisibleEntity/102 style) | Medium | Gated on live observation; self-1014 sent via SendGame |
| FR-8 | Minimal surroundings: if client still hangs on a follow-up 1010, handle GetSurroundings(114) as empty/no-op | Low | Gated on live observation; no extra entities required |
| FR-9 | Diagnostic logging for new stage(s), e.g. `[Game] SetLocation -> map=… x=… y=…`; dump full inbound 1010 frame to confirm subtype | Medium | Logs show parsed Action + reply flow |
| FR-10 | Keep existing `[Game][DH]` / `[Game][frame]` diagnostics through this work; strip diagnostic logs before final PR | Low | Tracked as deferred cleanup; removed in final PR |
| FR-11 | Auth/handshake + character-select crypto remain UNCHANGED (additive only on game-frame dispatch) | High | No diff to auth path; handshake still passes vs client |

## Non-Functional Requirements

| ID | Requirement | Metric | Target |
|----|-------------|--------|--------|
| NFR-1 | Build clean (dockerized) | `scripts/dotnet build src/Conquer.sln` | 0 errors |
| NFR-2 | Unit tests green | `scripts/dotnet test src/Conquer.sln` | All pass incl. new builder/parse tests |
| NFR-3 | No regression on handshake | Operator login still reaches loading | Unchanged vs feat/character-select |
| NFR-4 | Wire-byte fidelity | Builder output vs original layout | Byte-exact for echoed/known fields |
| NFR-5 | Crypto unchanged | Outbound seal + Blowfish-CFB64 | Reuses existing `SendGame` as-is |

## Glossary

- **5065 client**: the target Conquer Online client build (real Windows client).
- **GeneralData / MSG_ACTION (1010)**: action packet; subtype in the `Action` field (body offset 22).
- **DataAction**: enum of 1010 subtypes. **SetLocation=74**, InvisibleEntity=102, GetSurroundings=114, CompleteLogin=130, Hotkeys=75.
- **SetLocation(74)**: the enter-world trigger; server echoes map+X+Y.
- **MapStatusPacket(1110)**: map status reply paired with the SetLocation echo to clear loading.
- **SpawnEntityPacket(1014)**: entity spawn; self-1014 is the fallback render path.
- **HeroInformation(1006)**: already-sent character-data packet; carries look but NOT map/position.
- **SendGame**: net8 outbound (8-byte "TQServer" seal + Blowfish-CFB64).
- **DbCharacter**: persisted character row (MapID/X/Y/name/look); net8 defaults Map 1010 / X 61 / Y 109.
- **OPERATOR-MANUAL**: verification done by the operator vs the real client; out-of-CI.

## Out of Scope

- Movement (MsgWalk), combat, NPCs.
- Items / inventory / equipment.
- Tasks, guild, nobility.
- Character creation.
- CompleteLogin(130) / Hotkeys(75) login-step extras (deferred unless client blocks on them).
- Re-seeding to Twin City (only the documented fallback under A1).
- Loading server-side map files (`TinyMapService.Valid` stub returns true; client loads its own .cqmap).

## Dependencies

- **feat/character-select** branch — handshake + crypto (client-patcher, MsgAccount 1051 → token → MsgConnectEx 1055, DH key exchange, Blowfish-CFB64, MsgConnect 1052, ANSWER_OK + HeroInformation 1006). PREREQUISITE DONE.
- Seeded character **"Vitor"** (AccountID 2; net8 DbCharacter Map 1010 / X 61 / Y 109).
- Dockerized build/run on **192.168.0.252** (`docker compose -f src/docker-compose.yml up -d --build`).
- Real **Windows 5065 client** for operator-manual verification.

## Success Criteria

- CI: build clean (NFR-1), unit tests green incl. builder-layout + Action-parse assertions (NFR-2), handshake non-regressed (NFR-3).
- OPERATOR-MANUAL (definitive): real client leaves loading (AC-3.5) and shows **"Vitor" standing in-world** (AC-4.1).
- Minimal path holds (A2): no self-1014 needed; OR fallback FR-7 applied and verified if body fails to render.

## Testability

- **Unit-testable (CI):** packet builders (1010 echo / 1110 / 1014 layout assertions); Action-field parse at payload offset 20.
- **Operator-manual (out-of-CI):** no automated client exists. Definitive success = operator watching the real Windows client leave loading and show "Vitor" standing. Loop: rebuild via Docker → operator logs in → observe loading-clear + on-screen char → read `[Game]` logs for subtype + reply flow.

## Unresolved Questions

- Exact `Action` value in the client's first 1010 (expected 74 — dump full frame live to confirm). [A3]
- Whether SetLocation echo + 1110 alone clears loading, or the client expects self-1014 / CompleteLogin first. [FR-7/FR-8 gating]
- Whether map 1010 renders in the operator's client, or the A1 fallback (1002/438/381) is needed.
- Whether the client sends follow-up 1010 subtypes (GetSurroundings/CompleteLogin) and blocks awaiting their echoes. [FR-8]

## Next Steps

1. Operator approval of these requirements.
2. Proceed to design: 1010 handler + 3 packet builders + ClientSession character field; iterative live bring-up order (M1 clear-loading → M2 render-standing → M3 minimal surroundings only if needed).
3. Implement FR-1..FR-6 + FR-9; keep FR-7/FR-8 ready behind live observation; FR-10 cleanup before final PR.
4. Operator-manual verify AC-3.5 + AC-4.1 vs the real client; apply A1/A2 fallbacks if triggered.
