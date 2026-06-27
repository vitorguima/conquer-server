---
spec: character-creation
basePath: specs/character-creation
phase: design
updated: 2026-06-26
---

# Design: Character Creation (MsgRegister 1001)

## Overview

Add `case 1001:` to `PacketRouter.Dispatch` → new `RegisterHandler` that parses the fixed-layout `MsgRegister(1001)`, validates, builds a level-1 `DbCharacter`, INSERTs via the existing `CharacterRepository.Insert`, and replies `MsgTalk(ChatType.Register=2100,"ANSWER_OK")`. No enter-world on this socket — the client reconnects and the unchanged 1052 path spawns it. Additive only.

## Architecture

```mermaid
sequenceDiagram
    participant C as 5065 Client
    participant R as PacketRouter
    participant H as RegisterHandler
    participant DB as CharacterRepository
    participant G as GameHandler (1052, unchanged)

    Note over C,G: same game conn already did 1052 → got NEW_ROLE; session.AccountId set
    C->>R: MsgRegister(1001) [name,mesh,prof]
    R->>H: Handle(session, payload)
    alt payload.Length < 60
        H-->>H: log + return (listener stays up)
    else
        H->>H: parse @18/@50/@52 + validate (name/mesh/prof)
        alt validation fails
            H->>C: SendGame MsgTalk(Register, "<reason>")
        else valid
            H->>H: build level-1 DbCharacter (AccountID=session.AccountId)
            H->>DB: Insert(DbCharacter)
            alt INSERT throws (uq_name race)
                DB-->>H: exception
                H->>C: SendGame MsgTalk(Register, "Character name already in use")
            else ok
                H->>C: SendGame MsgTalk(Register, "ANSWER_OK")
            end
        end
    end
    Note over C: client reconnects → full re-auth
    C->>G: MsgConnect(1052) on NEW conn
    G->>DB: FindByAccountId → now finds new char
    G->>C: ANSWER_OK + HeroInformation(1006) → enter-world (unchanged path)
```

## Components

| Component | File | Change | Responsibility |
|-----------|------|--------|----------------|
| `RegisterHandler` | `src/Packets/RegisterHandler.cs` | CREATE | Parse 1001, validate, build+Insert DbCharacter, reply MsgTalk(Register,...) |
| `PacketRouter.Dispatch` | `src/Redux/PacketRouter.cs` | MODIFY | Construct `RegisterHandler(characters)`; add `case 1001:` |
| `ChatType` | `src/Packets/ChatType.cs` | MODIFY | Add `Register = 2100` |
| `CharacterRepository` | `src/Database/CharacterRepository.cs` | REUSE | Existing `Insert(DbCharacter)`; **no** `FindByName` added (see TD-2) |

### RegisterHandler

Mirrors `ActionHandler`/`GameHandler`: stateless, ctor-injected `CharacterRepository`, payload guard, `BinaryPrimitives` LE reads, replies via `session.SendGame`. Public surface:

```csharp
public sealed class RegisterHandler
{
    private readonly CharacterRepository _characters;
    public RegisterHandler(CharacterRepository characters) { _characters = characters; }
    public void Handle(ClientSession session, byte[] payload);
}
```

### PacketRouter wiring (mirror GameHandler injection)

```csharp
private readonly Conquer.Packets.RegisterHandler _register;   // new field
// in ctor:
_register = new Conquer.Packets.RegisterHandler(characters);  // same `characters` GameHandler gets
// in Dispatch:
case 1001:
    _register.Handle(session, payload);
    break;
```

`characters` is the `CharacterRepository` already passed to `PacketRouter`'s ctor (line 18) and into `GameHandler` (line 21). No new wiring beyond one field + one case.

### ChatType

```csharp
public enum ChatType : ushort
{
    Register = 2100,   // creation channel (Talk=2000 + 100)
    Entrance = 2101
}
```

## 1001 Payload Offset Table (net8 dispatch-payload terms)

The 2-byte length prefix is stripped by `ReadPacket`, so `payload[0]` = typeId. Payload offset = original body offset − 2. Min length **60**.

| Field | Payload offset | Read as | Used |
|-------|---------------|---------|------|
| typeId (=1001) | 0 | `ReadUInt16LE(payload[0..2])` | — (already dispatched) |
| AccountName | 2 | ASCII[16], TrimEnd('\0') | ignored (linkage via session) |
| **CharacterName** | **18** | ASCII[16], TrimEnd('\0') | validate + Name |
| AccountPassword | 34 | ASCII[16] | ignored |
| **Mesh** (body) | **50** | `ReadUInt16LE(payload[50..52])` | validate + appearance |
| **Profession** | **52** | `payload[52]` (u8) | validate + stats |
| UID (token echo) | 56 | `ReadUInt32LE(payload[56..60])` | ignored (redundant) |

Name decode: `Encoding.ASCII.GetString(payload, 18, 16).TrimEnd('\0')`.

## Appearance & Stat Formulas (concrete)

**Lookface → `DbCharacter.Mesh`** = `body + face*10000`:

| body | face range | example Mesh |
|------|-----------|--------------|
| 1003 / 1004 | `Random.Next(0,50)` → 0..49 | body=1003, face=12 → 121003 |
| 2001 / 2002 | `Random.Next(201,250)` → 201..249 | body=2001, face=210 → 2102001 |

> Original uses `Random.Next(50)` (0..49) and `Random.Next(201,250)` (201..249) — `.Next(int)` upper bound is exclusive. Requirements' "0..50 / 201..250" are inclusive-shorthand; implement with the original exclusive bounds (0..49 / 201..249).

**Hair → `DbCharacter.Avatar`** = `Random.Next(3,9)*100 + Random.Next(30,51)` → colour 3..8, style 30..50. e.g. colour=4,style=42 → 442.

**Stats (TD-1: single shared level-1 default, profession-agnostic for MVP):**

| Stat | Value |
|------|-------|
| Strength | 4 |
| Agility | 6 |
| Vitality | 12 |
| Spirit | 0 |

Derived (`Constants.cs:102-103`, STR×3 AGI×3 VIT×24 SPI×3 / mana SPI×5):
- `HealthPoints = 4*3 + 6*3 + 12*24 + 0*3 = 12 + 18 + 288 + 0 = 318`
- `ManaPoints = 0*5 = 0`

## Technical Decisions

| Decision | Options | Choice | Rationale |
|----------|---------|--------|-----------|
| TD-1 Level-1 base stats | per-profession table / single shared default | **single shared default STR4/AGI6/VIT12/SPI0 (Life 318, Mana 0)** | net8 has NO Stats table/reader (`ini/stats.ini` absent); the canonical CO per-profession values aren't derivable from in-repo code. A single defensible level-1 melee-ish set is simplest, renders/spawns for all 4 meshes, and is enough to enter-world. **Operator can tune later** (and a per-profession switch is a one-method change if the live test shows casters need Mana). Assumption stated explicitly. |
| TD-2 Dup-name handling | `FindByName` pre-check / `uq_name` UNIQUE + catch INSERT | **catch INSERT exception only (no FindByName)** | Minimal surface area (NFR-1/NFR-3: no new repo method). The `uq_name` constraint is the authoritative race-safe guard; a pre-check still races and would need the catch anyway. Catch → `MsgTalk(Register,"Character name already in use")`. Satisfies AC-2.2 and AC-4.2 with one path. |
| TD-3 Reconnect vs same-socket enter-world | same-socket 1006/SetLocation / ANSWER_OK only + reconnect | **ANSWER_OK only; reconnect** | Matches the proven original `Process_MsgRegisterPacket` (validate+INSERT+ANSWER_OK, no Populate). The existing live 1052 path spawns the char on the client's reconnect — no duplicated enter-world code. Out-of-scope per NFR-1. |
| TD-4 Spawn map | original 1002/438/381 / net8 1010/61/109 | **1010 / 61 / 109** | Map 1002 may not be loaded (maps come from operator `.cqmap` via `MapRegistry.Load`); 1010 is proven-good (seeded "Vitor" spawns live). These are also the `DbCharacter` defaults. |
| TD-5 Account linkage | packet UID@56 / `session.AccountId` | **`session.AccountId`** | Already set at 1052 on this same game conn; UID@56 is a redundant token echo. Avoids token re-resolution. |

## Validation (exact order + rejection messages)

Mirror `GameServer.cs:174-200`. First failure returns after sending one `MsgTalk(Register, reason)`; no INSERT.

| # | Rule | On fail → `MsgTalk(Register, ...)` |
|---|------|------------------------------------|
| 1 | name matches `^[a-zA-Z0-9]{4,16}$` AND `!name.ToLower().Contains("admin")` | `"Invalid character name"` |
| 2 | `Mesh ∈ {1003,1004,2001,2002}` | `"Invalid character mesh"` |
| 3 | `Profession ∈ {10,20,30,40,100}` | `"Invalid character profession"` |
| 4 | uniqueness — enforced at INSERT via `uq_name` (TD-2) | catch → `"Character name already in use"` |

Use a compiled `Regex("^[a-zA-Z0-9]{4,16}$")` and `HashSet<ushort>` literals local to the handler (no dependency on `Redux.Common`, which `Packets.csproj` does not reference — mirrors how `ClientSession` re-declares `SERVER_SEAL`).

## DbCharacter build (FR-7)

```csharp
var ch = new DbCharacter {
    AccountID = session.AccountId,
    Name      = name,
    Mesh      = body + face * 10000,
    Avatar    = colour * 100 + style,
    Level     = 1,
    MapID     = 1010, X = 61, Y = 109,
    Silver    = 1000,
    Strength  = 4, Agility = 6, Vitality = 12, Spirit = 0,
    HealthPoints = 318, ManaPoints = 0
};
```
(`Level/MapID/X/Y/Silver` equal the DTO defaults but set explicitly for clarity. `DbCharacter` has `init` setters → object-initializer only.)

## Error Handling

| Scenario | Strategy | Result |
|----------|----------|--------|
| `payload.Length < 60` | `Console.WriteLine("[Game] short 1001"); return;` (mirror ActionHandler guard) | listener stays up (AC-4.1); nothing sent |
| name fails regex / "admin" | reject before INSERT | `MsgTalk(Register,"Invalid character name")` (AC-2.1) |
| bad mesh | reject before INSERT | `MsgTalk(Register,"Invalid character mesh")` (AC-2.3) |
| bad profession | reject before INSERT | `MsgTalk(Register,"Invalid character profession")` (AC-2.4) |
| `Insert` throws (uq_name dup/race) | `try { Insert } catch (Exception e) { log; SendGame reject; return; }` | `MsgTalk(Register,"Character name already in use")`, no crash (AC-2.2, AC-4.2) |
| INSERT ok | — | `MsgTalk(Register,"ANSWER_OK")`, nothing else (AC-1.2) |

## File Structure

| File | Action | Precise change |
|------|--------|----------------|
| `src/Packets/RegisterHandler.cs` | CREATE | New `RegisterHandler` (parse + validate + build + Insert + reply) |
| `src/Redux/PacketRouter.cs` | MODIFY | `+ _register` field; `_register = new RegisterHandler(characters)` in ctor; `case 1001: _register.Handle(session, payload); break;` |
| `src/Packets/ChatType.cs` | MODIFY | Add `Register = 2100,` |
| `src/Packets.Tests/RegisterParseTests.cs` | CREATE | xUnit: offset/layout assert + Mesh/Hair formula + Life/Mana derivation |

Zero edits to auth/crypto/handshake/`GameConnection`/`SendGame`/`MsgConnect`/`GameHandler`/`ActionHandler`/`CharacterRepository` (NFR-1).

## Test Strategy

xUnit in `src/Packets.Tests/` (mirror `ActionParseTests.cs`). Build/test dockerized: `scripts/dotnet build src/Conquer.sln` && `scripts/dotnet test src/Conquer.sln` (no local dotnet).

### Unit tests (`RegisterParseTests.cs`)
1. **Offset/layout assert** — build a 60-byte payload: type@0=1001, ASCII "TestName" @18, Mesh u16 @50=1003, Profession u8 @52=10, UID u32 @56. Assert: `ReadUInt16LE(@50)==1003`, `payload[52]==10`, decoded name@18=="TestName", `payload.Length>=60` (guard). (NFR-5, FR-2)
2. **Mesh/Hair formula** — body 1003, face 12 → `Mesh==121003`; body 2001, face 210 → `Mesh==2102001`; colour 4, style 42 → `Avatar==442`. (AC-3.1/3.2/3.3)
3. **Stat derivation** — STR4/AGI6/VIT12/SPI0 → `Life==318`, `Mana==0` using the `STAT_MAXLIFE_*`/`STAT_MAXMANA_*` factors. (AC-3.4)

Factor formulas/constants are duplicated as literals in the test (no Redux reference), matching how `RegisterHandler` itself will hold them.

### Operator-manual E2E (real acceptance)
Delete seeded "Vitor" / use fresh account → NEW_ROLE → create char in 5065 client → assert (a) `characters` row with correct AccountID/Name/Mesh/MapID=1010, (b) client accepts (ANSWER_OK), (c) reconnect spawns in-world, (d) relog persists.

## Unresolved Questions (operator-capture, NOT design blockers)

- Exact live 1001 byte layout (Mesh@50 / Profession@52 / len 60) vs a real capture.
- Client reconnects (full re-auth) vs re-sends 1052 same-socket after ANSWER_OK. Fallback if same-socket: `session.AccountId` is already cached, so re-reading the char without a token is trivial — but design assumes reconnect (TD-3).
- Whether the shared STR4/AGI6/VIT12/SPI0 stats render/spawn acceptably for all 4 meshes (TD-1) — tune the table live if not.

## Implementation Steps

1. `ChatType.cs` — add `Register = 2100`.
2. `RegisterHandler.cs` — create: payload guard (<60→log+return); parse name@18 / mesh@50 / prof@52; validate (regex+admin, mesh set, prof set) with per-failure MsgTalk; gen face/colour/style; build DbCharacter (AccountID=session.AccountId, derived Mesh/Avatar, 1/1000/1010/61/109, stats 4/6/12/0, Life 318/Mana 0); `try Insert / catch → "name in use"`; on success `SendGame(MsgTalk.Build(ChatType.Register,"ANSWER_OK"))`.
3. `PacketRouter.cs` — add `_register` field, construct with `characters`, add `case 1001:`.
4. `RegisterParseTests.cs` — add the 3 tests above.
5. `scripts/dotnet build src/Conquer.sln && scripts/dotnet test src/Conquer.sln` → green.
6. Hand to operator for the manual E2E milestones (INSERT → reconnect spawn → persistence).
