---
spec: character-creation
basePath: specs/character-creation
phase: requirements
updated: 2026-06-26
---

# Requirements: Character Creation (CO 5065, MsgRegister 1001)

## Goal

Handle `MsgRegister(1001)` on the game connection so a fresh account can create a level-1 character via the real 5065 client. Validate → INSERT one `DbCharacter` → reply `ANSWER_OK`; the existing 1052 enter-world path spawns it on reconnect. Additive only.

## User Stories

### US-1: New player creates a character
**As a** player on an account with no character
**I want to** submit the creation form (name + body + profession) and have the server create my character
**So that** I can enter the world and play

**Acceptance Criteria:**
- [ ] AC-1.1: **Given** a game connection that already did `MsgConnect(1052)` and got `NEW_ROLE`, **When** the client sends a valid `MsgRegister(1001)`, **Then** the server INSERTs one row into `characters` with `AccountID = session.AccountId`, the submitted `Name`, derived `Mesh`/`Avatar`, `Level=1`, `Silver=1000`, `MapID=1010`, `X=61`, `Y=109`.
- [ ] AC-1.2: **Given** a successful INSERT, **When** the handler completes, **Then** it replies `MsgTalk(ChatType.Register=2100, "ANSWER_OK")` via `session.SendGame` and sends NOTHING else (no `HeroInformation`/`SetLocation`/enter-world on this socket).
- [ ] AC-1.3: **Given** the client received `ANSWER_OK`, **When** it reconnects and re-auths through to `MsgConnect(1052)`, **Then** the EXISTING existing-character path finds the new char and spawns it in-world (this spec does NOT modify that path).

### US-2: Player gets clear feedback on invalid input
**As a** player entering creation details
**I want to** see a rejection message when my input is invalid
**So that** I can correct it instead of the client silently doing nothing

**Acceptance Criteria:**
- [ ] AC-2.1: **Given** a name not matching `^[a-zA-Z0-9]{4,16}$` OR containing "admin" (case-insensitive), **When** 1001 arrives, **Then** the server sends `MsgTalk(Register, <reason>)` and does NOT create a character.
- [ ] AC-2.2: **Given** a name already present in `characters`, **When** 1001 arrives, **Then** the server sends `MsgTalk(Register, "Character name already in use")` (or equivalent) and does NOT create a character.
- [ ] AC-2.3: **Given** a `Mesh` ∉ {1003,1004,2001,2002}, **When** 1001 arrives, **Then** the server sends `MsgTalk(Register, <reason>)` and does NOT create a character.
- [ ] AC-2.4: **Given** a `Profession` ∉ {10,20,30,40,100}, **When** 1001 arrives, **Then** the server sends `MsgTalk(Register, <reason>)` and does NOT create a character.

### US-3: Character appearance reflects chosen body
**As a** player picking a body/profession
**I want to** my character to render with a valid look
**So that** it appears correctly in-world

**Acceptance Criteria:**
- [ ] AC-3.1: **Given** body 1003 or 1004, **When** the char is built, **Then** `Mesh = body + face*10000` with `face = Random(0,50)`.
- [ ] AC-3.2: **Given** body 2001 or 2002, **When** the char is built, **Then** `Mesh = body + face*10000` with `face = Random(201,250)`.
- [ ] AC-3.3: **Given** any valid body, **When** the char is built, **Then** `Avatar = Random(3,9)*100 + Random(30,51)`.
- [ ] AC-3.4: **Given** the chosen profession, **When** the char is built, **Then** level-1 base stats (Strength/Agility/Vitality/Spirit) are set from hardcoded defaults, `HealthPoints = STR*3 + AGI*3 + VIT*24 + SPI*3`, `ManaPoints = SPI*5`.

### US-4: Malformed packet never crashes the server
**As a** server operator
**I want to** a short/malformed 1001 to be ignored safely
**So that** the listener stays up

**Acceptance Criteria:**
- [ ] AC-4.1: **Given** a 1001 payload shorter than 60 bytes, **When** dispatched, **Then** the handler returns early (guard `payload.Length < 60`, mirroring `ActionHandler`) and the listener keeps accepting connections.
- [ ] AC-4.2: **Given** an INSERT that throws (e.g. `uq_name` race), **When** it fails, **Then** the handler catches it, sends a rejection `MsgTalk(Register, ...)`, and does not crash.

## Functional Requirements

| ID | Requirement | Priority | Acceptance Criteria |
|----|-------------|----------|---------------------|
| FR-1 | Add `case 1001:` to `PacketRouter.Dispatch` → new `RegisterHandler` (mirror `ActionHandler(1010)`; inject `CharacterRepository` like `GameHandler`) | High | 1001 no longer logs "Unknown typeId"; routes to RegisterHandler |
| FR-2 | Parse fixed 1001 payload: `typeId@0`, `CharacterName@18` (16-byte ASCII, null-padded, `TrimEnd`), `Mesh@50` (u16 LE), `Profession@52` (u8), `UID@56` (u32 LE); min len 60 | High | xUnit layout-assert test parses known bytes to expected fields |
| FR-3 | Validate name `^[a-zA-Z0-9]{4,16}$` AND not containing "admin"; reject via `MsgTalk(Register, reason)` on fail | High | AC-2.1 |
| FR-4 | Validate name uniqueness (pre-check via repo `FindByName` if present, else rely on `uq_name` + catch dup) | High | AC-2.2 |
| FR-5 | Validate `Mesh` ∈ {1003,1004,2001,2002} | High | AC-2.3 |
| FR-6 | Validate `Profession` ∈ {10,20,30,40,100} | High | AC-2.4 |
| FR-7 | Build level-1 `DbCharacter`: `AccountID=session.AccountId`, `Mesh=body+face*10000`, `Avatar`, `Level=1`, `Silver=1000`, `MapID=1010`, `X=61`, `Y=109`, hardcoded stats, derived Life/Mana | High | AC-1.1, AC-3.* |
| FR-8 | INSERT via existing `CharacterRepository.Insert(DbCharacter)` (do NOT re-create it) | High | AC-1.1 (DB row appears) |
| FR-9 | Reply `MsgTalk(ChatType.Register=2100, "ANSWER_OK")` via `session.SendGame`; send nothing else | High | AC-1.2 |
| FR-10 | Add `Register = 2100` to the `ChatType` enum | High | enum compiles; used by FR-3/9 |
| FR-11 | Use `session.AccountId` (set at 1052) for `AccountID`; ignore packet AccountName/Password/UID for linkage | Medium | AC-1.1 |

## Non-Functional Requirements

| ID | Requirement | Metric | Target |
|----|-------------|--------|--------|
| NFR-1 | Additive integration — new code only on the game path | Files touched | Only: `ChatType` enum (+1 value), new `RegisterHandler`, `PacketRouter.Dispatch` (+1 case); zero edits to auth/crypto/handshake/GameConnection/SendGame/MsgConnect/GameHandler/ActionHandler |
| NFR-2 | Crash safety on malformed input | Listener uptime | Short/malformed 1001 (`len < 60`) never throws to the listener; guard present (AC-4.1) |
| NFR-3 | Reuse existing primitives | New infra | No new repo method except optional `FindByName`; reuse `Insert`, `MsgTalk`, `SendGame` |
| NFR-4 | Build/test dockerized | Commands | `scripts/dotnet build src/Conquer.sln` && `scripts/dotnet test src/Conquer.sln` both green (no local dotnet) |
| NFR-5 | Unit-test coverage of the parse | Test | One xUnit layout-assert test in `src/Packets.Tests/*.cs` for 1001 offsets (Name@18, Mesh@50, Profession@52, UID@56, min len 60) |
| NFR-6 | Outbound framing unchanged | Path | Replies go through `session.SendGame` (8-byte "TQServer" seal + Blowfish); no new framing code |

## Glossary

- **MsgRegister (1001)**: Client→server packet sent when the player clicks "create" on the creation screen. Fixed struct (NOT NetString). Net8 dispatch-payload offsets: `typeId@0`, `CharacterName@18` (16-byte ASCII, null-padded), `Mesh@50` (u16), `Profession@52` (u8), `UID@56` (u32). Min payload 60 bytes.
- **Lookface / Mesh**: The `Mesh` DB column = body model + face index (`body + face*10000`). Determines character appearance.
- **Avatar / Hair**: The `Avatar` DB column = `Random(3,9)*100 + Random(30,51)`; hair colour + style.
- **ChatType.Register (2100)**: The chat channel the creation screen listens on. `MsgTalk` on this channel carries either a rejection reason or `"ANSWER_OK"`.
- **NEW_ROLE**: `MsgTalk(Entrance, "NEW_ROLE")` the server already sends at `MsgConnect(1052)` when the account has no character → client shows the creation screen.
- **ANSWER_OK**: `MsgTalk(Register, "ANSWER_OK")` the server replies after a successful INSERT → client treats creation as accepted.
- **Reconnect flow**: After `ANSWER_OK`, the client reconnects and re-auths → new `MsgConnect(1052)` → the existing-character path finds the new char and spawns it in-world. The register handler does NOT enter-world on its socket.
- **session.AccountId**: Authenticated account id, already set on the `ClientSession` from the 1052 on this same game connection; used as `DbCharacter.AccountID`.

## Out of Scope

- Same-socket enter-world from the register handler (no `HeroInformation`/`SetLocation`/`MapStatus` here) — spawning happens on reconnect via the unchanged 1052 path.
- Deep stat / skill / inventory systems beyond the level-1 defaults `CreateDbCharacter` sets.
- The character-SELECT screen (5065 has none).
- Movement, account creation.
- The original's `Profession`/`CP`/`Exp`/`Class`/`Spouse` columns (net8 schema lacks them).
- Map 1002 / X438 / Y381 (original Twin City spawn) — using proven-good 1010/61/109 instead.
- Any change to auth, crypto/handshake (DH/Blowfish), `GameConnection`, `SendGame`, `MsgConnect`/`GameHandler`, or the enter-world `ActionHandler`.

## Dependencies

- **Live handshake + enter-world stack** (auth 1051→token→1055, DH key exchange, Blowfish-CFB64, MsgConnect 1052, HeroInformation 1006, GeneralData 1010/SetLocation 74) — already working live; consumed as-is.
- **`CharacterRepository.Insert(DbCharacter)`** — already implemented (`src/Database/CharacterRepository.cs:45-54`); reused, not re-created.
- **Proven enter-world path on reconnect** — the existing 1052 (`GameHandler`) + 1010 (`ActionHandler`) flow that spawns the seeded "Vitor"; relied on to spawn the new char after reconnect.
- **`ClientSession.AccountId`** — populated at 1052; provides `AccountID` linkage.
- **`characters` table + `uq_name`** (`src/init.sql:32-52`) — DB enforces name uniqueness.
- **Dockerized build/test** (`scripts/dotnet`) — required for compile/test.

## Success Criteria

- Operator deletes seeded "Vitor" (or uses a fresh account) so `NEW_ROLE` fires; creating a char in the real Windows client yields:
  - (a) a row in `characters` with correct `AccountID` / `Name` / `Mesh` / `MapID=1010`,
  - (b) the client accepts creation (`ANSWER_OK`),
  - (c) after reconnect the new char spawns in-world,
  - (d) relog still finds it (persisted).
- `scripts/dotnet build src/Conquer.sln` green; `scripts/dotnet test src/Conquer.sln` green including the new 1001 parse test.
- A malformed/short 1001 does not crash the listener.

## Unresolved Questions

- **Hardcoded level-1 base stats per profession**: exact STR/AGI/VIT/SPI values are not in the net8 repo (no Stats table; `ini/stats.ini` absent). A sane default set must be chosen (e.g. melee `Str=5,Agi=2,Vit=3,Spi=0`; Taoist/100 Spi-weighted) and confirmed to render/spawn acceptably for all 4 meshes during operator test. Flag for design.
- **Live confirmation** (operator capture, not blocking design): (1) client reconnects/full-re-auths vs re-sends 1052 same-socket after `ANSWER_OK`; (2) exact 1001 byte layout vs real capture (Mesh@50/Profession@52/len 60); (3) whether map 1002 is loaded (if so, original spawn could be revisited later).
- **Friendly dup message**: rely on `uq_name` + catch, or add a `FindByName` pre-check? (Cosmetic — design decision.)

## Next Steps

1. Operator/user reviews and approves these requirements.
2. Run design phase: define `RegisterHandler` shape, the parse method, hardcoded stat table, validation order, and the `case 1001:` wiring.
3. Plan tasks: enum value + handler + router case + xUnit parse test (POC-first), then operator-manual verification milestones (INSERT → reconnect spawn → persistence).
