---
spec: character-creation
phase: research
created: 2026-06-26
---

# Research: character-creation

## Executive Summary

Net8 already has every primitive needed for CO 5065 character creation: the enter-world flow works live, `ClientSession` carries `AccountId`, `CharacterRepository.Insert(...)` exists, `init.sql` has a `characters` table with a UNIQUE name key, and outbound builders (`MsgTalk`, `HeroInformation`) are ready. The ONLY missing code is a `case 1001:` in `PacketRouter.Dispatch` → a new `RegisterHandler` that parses the fixed-layout `MsgRegister(1001)`, validates, builds a `DbCharacter`, and `Insert`s it. The original `Process_MsgRegisterPacket` proves the post-create flow is a **client reconnect** (not same-socket enter-world): register only validates+inserts+replies a `MsgTalk(Register=2100,"ANSWER_OK")`; the client then re-auths and re-sends `MsgConnect(1052)`, where the existing-char path drives enter-world.

## Codebase Analysis

### Original reference (ground truth — `src/Redux`)

**`src/Redux/Packets/Game/[1001] Register.cs`** — `RegisterPacket` struct + `byte* → RegisterPacket` reader. EXACT original-body offsets (relative to packet start `ptr`):

| Field | Original body offset | Type | Size | Notes |
|-------|---------------------|------|------|-------|
| (length) | 0 | ushort | 2 | length prefix |
| (type=1001) | 2 | ushort | 2 | |
| AccountName | 4 | char[16] | 16 | `Encoding.Default`, null-padded, `TrimEnd('\0')` |
| CharacterName | 20 | char[16] | 16 | same encoding — **this is the name to validate/insert** |
| AccountPassword | 36 | char[16] | 16 | plain-text password echo |
| Mesh | 52 | ushort | 2 | body, e.g. 1003/1004/2001/2002 |
| Profession | 54 | byte | 1 | base profession (10/20/40/100) |
| (gap) | 55 | — | 3 | bytes 55-57 unread (alignment) |
| UID | 58 | uint | 4 | the auth token / account id echo |

The reader (`implicit operator`): `memcpy accountName ← ptr+4 (16)`, `characterName ← ptr+20 (16)`, `accountPassword ← ptr+36 (16)`, `Mesh = *(ushort*)(ptr+52)`, `Profession = *(byte*)(ptr+54)`, `UID = *(uint*)(ptr+58)`. `Constants.MAX_NAMESIZE = 16` (`src/Redux/Constants.cs:33`).

**Net8-dispatch-payload offsets** (the 2-byte length prefix is stripped by `PacketRouter.ReadPacket`/`GameConnection`, so `payload[0]` = typeId; **payload offset = original body offset − 2**):

| Field | Payload offset | Read as |
|-------|---------------|---------|
| typeId (1001) | 0 | `ReadUInt16LE(payload[0..2])` |
| AccountName | 2 | ASCII, 16 bytes, trim `\0` |
| CharacterName | 18 | ASCII, 16 bytes, trim `\0` |
| AccountPassword | 34 | ASCII, 16 bytes, trim `\0` |
| Mesh | 50 | `ReadUInt16LE(payload[50..52])` |
| Profession | 52 | `payload[52]` |
| UID (token/account) | 56 | `ReadUInt32LE(payload[56..60])` |

Min payload length = 60. Name encoding is **fixed 16-byte null-padded ASCII** (NOT NetString — NetStringPacker is outbound-only).

**`src/Redux/Network/GameServer.cs:174-200` `Process_MsgRegisterPacket`** — validation order:
1. `!Common.ValidChars.IsMatch(name) || name.Length < 3 || name.Length >= 16 || name.ToLower().Contains("admin")` → reject "Invalid character name".
2. `Characters.GetByName(name) != null` → reject "Character name already in use".
3. `!Common.ValidCharacterMeshes.Contains(Mesh)` → reject "Invalid character mesh".
4. `!Common.ValidBaseProfessions.Contains(Profession)` → reject "Invalid character profession".
5. `client.CreateDbCharacter(name, Mesh, Profession)` then `DirectSend(TalkPacket(ChatType.Register, "ANSWER_OK"))`.

**`src/Redux/Common.cs`**:
- `ValidChars = new Regex("^[a-zA-Z0-9]{4,16}$")` (`:20`) — NOTE: regex requires **min 4 chars**, even though the explicit `< 3` check is looser; the regex governs. Alphanumeric only.
- `ValidCharacterMeshes = {1003, 1004, 2001, 2002}` (`:34`).
- `ValidBaseProfessions = {10, 20, 30, 40, 100}` (`:35`).

**`src/Redux/Objects/Player.cs:670-741` `CreateDbCharacter(name, body, profession)`** field defaults:

| Field | Value | Net8 column |
|-------|-------|-------------|
| Name | name | `Name` |
| Lookface | `body + face*10000` | `Mesh` |
| face | body 1003/1004 → `Random.Next(50)`; else (2001/2002) → `Random.Next(201,250)` | (folded into Mesh) |
| Hair | `colour*100 + Random.Next(30,51)`, `colour = Random.Next(3,9)` | `Avatar` |
| Level | 1 | `Level` |
| Money | 1000 | `Silver` |
| Map | 1002 | `MapID` — **see risk: use 1010** |
| X | 438 | `X` — **see risk** |
| Y | 381 | `Y` — **see risk** |
| Strength/Vitality/Agility/Spirit | `Stats.GetByProfessionAndLevel(ProfessionType, level)` | same names |
| Life | `STR*3 + AGI*3 + VIT*24 + SPI*3` (`Constants.cs:102`) | `HealthPoints` |
| Mana | `SPI*5` (STR/AGI/VIT factors=0, `Constants.cs:103`) | `ManaPoints` |
| Profession/CP/Exp/Spouse/Pk/etc. | original-only | **no net8 column — drop** |

Original then creates starter items + skills (`CreateDBItem`, `ConquerSkill.Create`) — **OUT OF SCOPE** (no items/skills tables in net8).

**Stats source**: `src/Redux/Database/Readers/StatReader.cs` reads `ini/stats.ini` at runtime (Vitality,Strength,Agility,Spirit per profession+level). **`ini/stats.ini` is NOT in the repo** (operator runtime file), and **net8 has NO Stats table/reader** (grep for `Stats`/`Strength` in `src/Database` hits only `CharacterRepository`). → **Recommend hardcoded level-1 base stats** (see Recommendations).

### Net8 target code (`src/Network`, `src/Packets`, `src/Database`, `src/Redux/PacketRouter.cs`)

| File | State | Role |
|------|-------|------|
| `src/Redux/PacketRouter.cs:48-66` | has 1051/1052/1010 cases; logs `Unknown typeId` default | add `case 1001:` → new `RegisterHandler` |
| `src/Network/ClientSession.cs` | carries `AccountId` (:49, set at 1052), `Character` (:53), `SendGame` seal+Blowfish (:78) | reuse — register handler reads `AccountId`, calls `SendGame` |
| `src/Network/TokenStore.cs` | `Add` / `TryConsume` (single-use, REMOVE) | token consumed at 1052; re-auth mints a fresh token on reconnect |
| `src/Database/CharacterRepository.cs:45-54` | **`Insert(DbCharacter)` ALREADY EXISTS** (carried note saying "only FindByAccountId" is STALE) | reuse; optionally add `FindByName` for friendly dup check |
| `src/init.sql:32-52` | `characters` table; `UNIQUE KEY uq_name (Name)` | DB enforces uniqueness — `Insert` throws on dup |
| `src/Packets/MsgTalk.cs:31` | `Build(ChatType, words)` Entrance channel | reuse for the "ANSWER_OK" register reply |
| `src/Packets/ChatType.cs` | only `Entrance = 2101` | **add `Register = 2100`** (original `Talk(2000)+100`) |
| `src/Packets/HeroInformation.cs`, `ActionHandler.cs` | enter-world builders | reused by the existing 1052/1010 path on reconnect — **NOT called by register** |
| `src/Packets/MsgConnect.cs` (GameHandler) | 1052: token→AccountId→`FindByAccountId`→ANSWER_OK+1006 / NEW_ROLE | unchanged; on reconnect finds the new char → enter-world |

**DbCharacter (net8, `CharacterRepository.cs:6-24`)** columns: `AccountID, Name, Mesh, Avatar, Level, MapID, X, Y, Silver, Strength, Agility, Vitality, Spirit, HealthPoints, ManaPoints` (no Profession/CP/Exp/Class/Spouse). Defaults in the class: Level 1, MapID 1010, X 61, Y 109, Silver 1000.

## Post-create flow (the key unknown — RESOLVED from original)

The original `Process_MsgConnectPacket` (`GameServer.cs:301-328`) and `Process_MsgRegisterPacket` (`:174-200`) prove the architecture:

- **Register does NOT enter-world on the same socket.** It validates, `CreateDbCharacter` (INSERT only — no `Populate`), and replies `TalkPacket(ChatType.Register=2100, "ANSWER_OK")`. No 1006, no SetLocation.
- The client, on register-channel ANSWER_OK, **reconnects / re-runs the login sequence**. On the next `MsgConnect(1052)`, `GetByUID` now finds the char and calls `Populate` → 1006 → enter-world. This is the SAME path the seeded "Vitor" uses today.

**Token lifecycle is NOT a blocker.** Net8 `TokenStore.TryConsume` removes the token at 1052, but the client re-runs full auth on reconnect (`MsgAccount.cs:58` mints a fresh random token per auth → `MsgConnectEx` → new game connect). The consumed token is irrelevant; the reconnect carries a new one.

**Recommendation: option (b) — reconnect/converge.** The register handler should ONLY validate + INSERT + reply `MsgTalk(Register, "ANSWER_OK")`, then let the existing 1052 path (already live) spawn the char on reconnect. This is minimal (no new enter-world code) and matches the proven original. Option (a) same-socket enter-world is NOT how the 5065 client behaves and would duplicate logic.

**Live-unknown to confirm**: that the 5065 client truly reconnects (full re-auth) after register ANSWER_OK rather than re-sending `MsgConnect` on the same already-Established game socket. Operator capture during the milestone-1 test resolves this. If it re-sends 1052 on the same socket, that socket's token is already consumed — handle by either (i) not consuming when no char exists, or (ii) caching `AccountId` on the session (already present) and re-reading the char without the token. Caching is the safer fallback.

## Account linkage

`ClientSession.AccountId` is set at `MsgConnect(1052)` (`MsgConnect.cs:42`) from the consumed token. The register handler runs on the SAME game connection that already did 1052 (which sent NEW_ROLE), so **`session.AccountId` is already populated** — use it directly for `DbCharacter.AccountID`. The packet's `UID @56` (token echo) is a redundant cross-check, NOT needed for linkage. This avoids any token re-resolution.

## Spawn coords risk (Map/X/Y)

Original spawns at Map 1002 / X438 / Y381 (Twin City). **But the proven-good net8 spawn is Map 1010** — the seeded "Vitor" (AccountID 2, MapID 1010, Mesh 1003, Hair 340) reaches the world live (per `specs/enter-world/.progress.md` + `specs/character-select/requirements.md:145`). Maps load from operator `.cqmap` files (`MapRegistry.Load`), so 1002 may not be present. **Recommend the new char use MapID 1010 with X/Y = the net8 DbCharacter defaults (61/109) — the same values the working enter-world path exercises** — NOT the original 1002/438/381, until 1002 is confirmed loaded live.

## Quality Commands

| Type | Command | Source |
|------|---------|--------|
| Build | `scripts/dotnet build src/Conquer.sln` | scripts/dotnet (dockerized SDK 8.0) |
| Test | `scripts/dotnet test src/Conquer.sln` | scripts/dotnet |
| Lint | Not found | — |
| TypeCheck | (compile = build) | — |

Net8 server projects build under `src/Conquer.sln` (Redux host + Crypto/Maps/Database/Network/Packets + Crypto.Tests/Packets.Tests). Tests = xUnit layout-assertion tests in `src/Packets.Tests/*.cs` (pattern to mirror for a register-parse test). No local dotnet on macOS — must use `scripts/dotnet` (Docker).

**Local CI**: `scripts/dotnet build src/Conquer.sln && scripts/dotnet test src/Conquer.sln`

## Verification Tooling

No automated E2E tooling detected (no dev-server scripts, no Playwright/Cypress, no health endpoints; the "app" is a TCP game server). Server runs dockerized on 192.168.0.252 vs the real Windows 5065 client.

**Project Type**: TCP game server (.NET 8)
**Verification Strategy**: OPERATOR-MANUAL vs the real client. (1) Delete seeded "Vitor" / use a fresh account so NEW_ROLE fires; (2) create a char in the client; (3) confirm a `characters` DB row inserted; (4) confirm in-world spawn; (5) relog → existing-char path finds it. CI tier = xUnit register-parse layout assertion (offsets above) + build green.

## Feasibility Assessment

| Aspect | Assessment | Notes |
|--------|------------|-------|
| Technical Viability | High | All primitives exist; only parse+validate+insert+reply is new |
| Effort Estimate | S | ~1 handler + 1 ChatType enum value + `case 1001` + optional `FindByName` + 1 unit test |
| Risk Level | Low-Medium | Live unknowns: exact 1001 bytes, reconnect-vs-same-socket, mesh→face ranges, hardcoded stats sufficiency, map 1010 vs 1002 |

## Related Specs

| Spec | Relation | mayNeedUpdate |
|------|----------|---------------|
| enter-world | High — register converges into its 1052/1010 enter-world flow; provides proven Map 1010 spawn | No (additive; reused as-is) |
| character-select | High — same handshake; defines seeded Vitor / AccountID 2 baseline | No |
| (auth/crypto/handshake specs) | Medium — token lifecycle (`TokenStore`, `MsgAccount`) underpins reconnect | No (do not modify) |

## Recommendations for Requirements

1. **Parse `MsgRegister(1001)` at payload offsets** Name@18 (16-byte ASCII), Mesh@50 (u16), Profession@52 (u8); guard `payload.Length >= 60`. Ignore AccountName/Password/UID for M1 (linkage via `session.AccountId`).
2. **Validate** (mirror `Process_MsgRegisterPacket`): name `^[a-zA-Z0-9]{4,16}$` + not containing "admin"; mesh ∈ {1003,1004,2001,2002}; profession ∈ {10,20,30,40,100}. On any failure send `MsgTalk(Register, "<reason>")` and return.
3. **Build `DbCharacter`**: `AccountID = session.AccountId`; `Mesh = body + face*10000` (face: body 1003/1004 → `Random(0,50)`, body 2001/2002 → `Random(201,250)`); `Avatar = Random(3,9)*100 + Random(30,51)`; `Level=1`, `Silver=1000`, **`MapID=1010, X=61, Y=109`** (proven-good, NOT 1002/438/381). Stats: **hardcode level-1 base** (no net8 Stats table) — use standard CO level-1 base set, e.g. STR/AGI/VIT/SPI per the chosen profession (sane default `Str=5, Agi=2, Vit=3, Spi=0` for melee; `Spi`-weighted for Taoist 100). Derive `HealthPoints = STR*3+AGI*3+VIT*24+SPI*3`, `ManaPoints = SPI*5` (`Constants.cs:102-103`).
4. **Insert** via existing `CharacterRepository.Insert(...)`; rely on `uq_name` for uniqueness (catch dup → "Character name already in use"), optionally add `FindByName` for a pre-check.
5. **Reply** `MsgTalk(Register=2100, "ANSWER_OK")` via `SendGame`. Do NOT enter-world here. Add `Register = 2100` to `ChatType`.
6. **Wire** `case 1001:` in `PacketRouter.Dispatch` → `new RegisterHandler(characters).Handle(session, payload)` (mirror `ActionHandler`; inject `CharacterRepository` like `GameHandler`).
7. **Reuse, don't rebuild**, enter-world: the existing 1052 (GameHandler) + 1010 (ActionHandler) path spawns the char on reconnect — confirm this is the client's behavior live.
8. **Milestone order**: (M1) parse 1001 + validate + INSERT + log + ANSWER_OK; (M2) confirm client reconnect → existing path spawns the new char in-world; (M3) confirm persistence (relog finds it). Additive only — do NOT touch auth/crypto/handshake/enter-world.

## Open Questions (live-only)

- Does the 5065 client **reconnect (full re-auth)** after register ANSWER_OK, or re-send `MsgConnect(1052)` on the same Established game socket? (Determines whether the consumed token matters.)
- Exact byte layout of the live 5065 `MsgRegister(1001)` — confirm Mesh@50 / Profession@52 / payload length against a real capture.
- mesh→face ranges and whether the chosen hardcoded level-1 stats render/spawn acceptably for all 4 meshes.
- Is map 1002 loaded on the live server, or must the new char spawn on 1010?

## Sources

- `src/Redux/Packets/Game/[1001] Register.cs` (wire layout)
- `src/Redux/Network/GameServer.cs:174-200` (validation), `:301-328` (connect/reconnect flow)
- `src/Redux/Objects/Player.cs:670-741` (CreateDbCharacter defaults)
- `src/Redux/Common.cs:20,34,35` (ValidChars, ValidCharacterMeshes, ValidBaseProfessions)
- `src/Redux/Constants.cs:33,97,99,102,103` (MAX_NAMESIZE, ANSWER_OK/NEW_ROLE strings, Life/Mana factors)
- `src/Redux/Enum/ChatType.cs:8,83,88` (Talk=2000, Register=2100, Entrance=2101)
- `src/Redux/Database/Readers/StatReader.cs` (Stats from ini/stats.ini — absent)
- `src/Redux/PacketRouter.cs:48-66` (Dispatch), `src/Redux/GameConnection.cs:120-126`
- `src/Network/ClientSession.cs:49,53,78`; `src/Network/TokenStore.cs`; `src/Packets/MsgAccount.cs:58-59`
- `src/Database/CharacterRepository.cs:6-54` (DbCharacter + Insert)
- `src/Packets/MsgConnect.cs` (GameHandler), `MsgTalk.cs`, `HeroInformation.cs`, `ActionHandler.cs`, `ChatType.cs`, `NetStringPacker.cs`
- `src/init.sql:32-52` (characters schema, uq_name); `src/Nov_16_Backup.sql:167-168` (legacy 1002/438/381 newbie rows)
- `specs/enter-world/.progress.md:13,37,40,47`; `specs/character-select/requirements.md:145`
- `scripts/dotnet` (dockerized build/test)
