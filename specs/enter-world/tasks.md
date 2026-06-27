---
spec: enter-world
phase: tasks
created: 2026-06-26
granularity: coarse
intent: GREENFIELD
branch: feat/enter-world
---

# Tasks: enter-world

Additive net8 stand-still enter-world. POC-first, coarse (one commit per task).
Branch **feat/enter-world** off **feat/character-select** (depends on its working handshake/crypto — NOT on master).
Build/test dockerized: `scripts/dotnet build src/Conquer.sln` / `scripts/dotnet test src/Conquer.sln`.
Deploy: `docker compose -f src/docker-compose.yml up -d --build` on 192.168.0.252; logs `docker compose -f src/docker-compose.yml logs -f server`.

**ADDITIVE-ONLY GUARDRAILS (repeat every task):** do NOT touch the auth path, crypto, `GameConnection` frame split, `SendGame`, `HeroInformation`, `MsgTalk`, the handshake, or DB schema/seed. The ONLY changes allowed: `ClientSession` +1 field, `MsgConnect` +1 line, new `ActionHandler`, `PacketRouter` `case 1010`, 3 new builders, new `Packets.Tests` project. Gated self-1014 / GetSurroundings(114) stay OFF (built-but-commented) by default.

**EXACT LAYOUTS (do NOT re-derive — design.md authoritative):**
- 1010 echo body **28**: type@2=1010, UID@8=`CharacterID`, Data1@12=`MapID`, Data2@16=`(Y<<16)|(X & 0xFFFF)`, Data3@20=0, Action@22=**74**, pad 24-27=0.
- 1110 body **16**: type@2=1110, UID@4=`MapID`, ID@8=`MapID`, Type@12=0.
- 1014 (GATED) body 100+names: UID@4, Lookface@8, Life@48, Level@50, X@52, Y@54, Hair@56, Dir@58=0, Action@59=0(stand), Level@62, name@90.
- Builder convention = **body only** + `AppendHeader(span, bodyLen+8, type)` (length field writes `bodyLen`); `SendGame` adds the 8-byte "TQServer" seal. Use `Span`/`BinaryPrimitives` LE, NO `unsafe`.
- Inbound parse: dispatch payload has the 2-byte length prefix stripped → **Action = u16 @ payload offset 20**.

---

## Phase 1: Make It Work (POC)

Focus: get the SetLocation echo + 1110 onto the wire for a real client login so loading clears. Skip tests until Phase 3.

- [x] 1.1 Branch + persist DbCharacter on ClientSession at 1052
  - **Do**:
    1. From `feat/character-select`, create + checkout `feat/enter-world` (`git checkout feat/character-select && git checkout -b feat/enter-world`).
    2. Ensure `src/Network/Network.csproj` references `src/Database/Database.csproj` (add `<ProjectReference>` only if missing — `DbCharacter` lives in `src/Database/CharacterRepository.cs`).
    3. Add `public DbCharacter? Character { get; set; }` to `src/Network/ClientSession.cs` (nullable, additive — do NOT touch `SendGame`).
    4. In `src/Packets/MsgConnect.cs` GameHandler, after `FindByAccountId(...)` and BEFORE the ANSWER_OK + 1006 sends, add the single line `session.Character = character;`. No-char (NEW_ROLE) path unchanged → `Character` stays null.
  - **Files**: src/Network/ClientSession.cs, src/Network/Network.csproj, src/Packets/MsgConnect.cs
  - **Done when**: Build clean; `Character` field set at 1052 success path; no-char path untouched.
  - **Verify**: `scripts/dotnet build src/Conquer.sln && grep -q 'session.Character = character' src/Packets/MsgConnect.cs && grep -q 'DbCharacter? Character' src/Network/ClientSession.cs && echo PASS`
  - **Commit**: `feat(enter-world): persist DbCharacter on ClientSession at MsgConnect(1052)`
  - _Requirements: FR-1, FR-11, US-1, AC-1.1, AC-1.2, AC-1.3_
  - _Design: ClientSession, GameHandler_

- [x] 1.2 Add 1010 SetLocation-echo + 1110 packet builders
  - **Do**:
    1. Create `src/Packets/GeneralData.cs` (namespace `Conquer.Packets`): `BuildSetLocation(uint uid, uint mapId, ushort x, ushort y)` → body **28** per layout (UID@8=uid, Data1@12=mapId, Data2@16=`(uint)((y<<16)|(x & 0xFFFF))`, Action@22=74, pad 24-27=0); call `AppendHeader(span, bodyLen+8, 1010)`.
    2. Create `src/Packets/MapStatus.cs`: `Build(uint mapId)` → body **16** (UID@4=mapId, ID@8=mapId, Type@12=0); `AppendHeader(span, bodyLen+8, 1110)`.
    3. Match the existing `MsgTalk`/`HeroInformation` body-only convention; `Span`/`BinaryPrimitives` LE; NO `unsafe`.
  - **Files**: src/Packets/GeneralData.cs, src/Packets/MapStatus.cs
  - **Done when**: Both builders compile and return body-only byte arrays of length 28 / 16.
  - **Verify**: `scripts/dotnet build src/Conquer.sln && grep -q 'BuildSetLocation' src/Packets/GeneralData.cs && grep -q '1110' src/Packets/MapStatus.cs && echo PASS`
  - **Commit**: `feat(enter-world): add GeneralData(1010) echo + MapStatus(1110) builders`
  - _Requirements: FR-4, FR-5, FR-6, NFR-4, US-3, AC-3.1, AC-3.2_
  - _Design: Packet builders, Exact Packet Layouts (Reply A/B)_

- [x] 1.3 [VERIFY] Quality checkpoint: build clean after session + builders
  - **Do**: Run dockerized build; fix any errors introduced by 1.1–1.2.
  - **Verify**: `scripts/dotnet build src/Conquer.sln && echo PASS`
  - **Done when**: 0 build errors.
  - **Commit**: `chore(enter-world): pass quality checkpoint` (only if fixes needed)
  - _Requirements: NFR-1_

- [x] 1.4 Create ActionHandler (parse Action@20; SetLocation(74) → echo + 1110)
  - **Do**:
    1. Create `src/Packets/ActionHandler.cs` (`public sealed class ActionHandler`) with `HandleAction(ClientSession session, byte[] payload)`.
    2. Guard `payload.Length < 22` → log `[Game] short 1010` + `return` (no disconnect). Read `ushort action = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(20, 2));`.
    3. `switch(action)`: `case 74:` → `HandleSetLocation`; **GATED (leave commented):** `// case 102: HandleInvisibleEntity(...)` (FR-7 self-1014), `// case 114: /* no-op empty surroundings */` (FR-8); `default:` log `[Game] 1010 Action={action} unhandled — no-op` + no reply.
    4. `HandleSetLocation`: null-check `session.Character` (null → log `[Game] 1010 SetLocation but no session character` + return); else read `MapID/X/Y`, `session.SendGame(GeneralData.BuildSetLocation(...))` then `session.SendGame(MapStatus.Build(MapID))`.
    5. Diagnostics (FR-9): at `HandleAction` entry dump the full inbound 1010 frame as hex (for A3 confirm); in `HandleSetLocation` log `[Game] SetLocation -> map={MapID} x={X} y={Y}`.
  - **Files**: src/Packets/ActionHandler.cs
  - **Done when**: Compiles; 74 branch sends echo+1110 via `SendGame`; unknown/null/short paths log + no-op; gated cases present but commented.
  - **Verify**: `scripts/dotnet build src/Conquer.sln && grep -q 'AsSpan(20' src/Packets/ActionHandler.cs && grep -q 'case 74' src/Packets/ActionHandler.cs && grep -q '// case 102' src/Packets/ActionHandler.cs && echo PASS`
  - **Commit**: `feat(enter-world): add ActionHandler for GeneralData(1010) SetLocation(74)`
  - _Requirements: FR-2, FR-3, FR-4, FR-5, FR-7, FR-8, FR-9, FR-11, US-2, US-3, AC-2.2, AC-2.3, AC-3.1, AC-3.2, AC-3.3, AC-3.4_
  - _Design: ActionHandler, Error Handling, Diagnostics_

- [x] 1.5 Wire case 1010 in PacketRouter.Dispatch
  - **Do**:
    1. In `src/Redux/PacketRouter.cs`, add `private readonly ActionHandler _action;` and construct `_action = new ActionHandler();` in the ctor (sibling of existing AuthHandler/GameHandler).
    2. Add `case 1010: _action.HandleAction(session, payload); break;` to the `Dispatch` switch. Leave 1051/1052/default and all other cases UNCHANGED.
  - **Files**: src/Redux/PacketRouter.cs
  - **Done when**: Build clean; type-1010 frames reach `ActionHandler.HandleAction`; other routing untouched.
  - **Verify**: `scripts/dotnet build src/Conquer.sln && grep -q 'case 1010' src/Redux/PacketRouter.cs && grep -q '_action.HandleAction' src/Redux/PacketRouter.cs && echo PASS`
  - **Commit**: `feat(enter-world): route GeneralData(1010) to ActionHandler in PacketRouter`
  - _Requirements: FR-2, FR-11, US-2, AC-2.1, AC-2.4_
  - _Design: PacketRouter_

- [x] 1.6 [VERIFY] Quality checkpoint: full build green, POC wired
  - **Do**: Build the full solution; confirm the 1010 path compiles end-to-end (session → router → ActionHandler → builders).
  - **Verify**: `scripts/dotnet build src/Conquer.sln && echo PASS`
  - **Done when**: 0 build errors; whole 1010 flow present.
  - **Commit**: `chore(enter-world): pass quality checkpoint` (only if fixes needed); push branch: `git push -u origin feat/enter-world`
  - _Requirements: NFR-1_

- [x] 1.7 POC checkpoint — operator-manual: loading clears on real client (M1)
  - **Do**: (OPERATOR-MANUAL, out-of-CI — definitive POC gate)
    1. `scripts/dotnet build src/Conquer.sln` then `docker compose -f src/docker-compose.yml up -d --build` on 192.168.0.252.
    2. `docker compose -f src/docker-compose.yml logs -f server` while the operator logs in with the real Windows 5065 client.
    3. **A3 live-confirm:** read the full inbound 1010 frame dump and verify `Action @ payload[20] == 74` (matches the `[Game] SetLocation -> map/x/y` log).
    4. Confirm the post-login loading screen CLEARS after the SetLocation echo + 1110.
    5. If body invisible (A2 fails) → enable gated `case 102` self-1014 (FR-7) and re-verify. If map 1010/61/109 won't load (A1) → fallback re-seed/override to TC 1002/438/381. Record outcome in `.progress.md`.
  - **Files**: (none — runtime verification; record findings in specs/enter-world/.progress.md)
  - **Done when**: Operator confirms `Action==74` (A3) AND loading clears (AC-3.5). Any A1/A2 fallback applied + re-verified.
  - **Verify**: OPERATOR-MANUAL checklist (out-of-CI): A3 frame dump shows Action@20==74; loading screen gone. Documented in .progress.md.
  - **Commit**: `feat(enter-world): complete POC — SetLocation echo + 1110 clears client loading` (+ any fallback applied)
  - _Requirements: FR-9, US-3, US-4, AC-3.5, AC-4.3, A1, A2, A3_
  - _Design: Operator-Manual, Enter-World Flow_

## Phase 2: Refactoring

- [ ] 2.1 Add gated SpawnEntity(1014) self builder + harden ActionHandler edges
  - **Do**:
    1. Create `src/Packets/SpawnEntity.cs`: `BuildSelf(DbCharacter ch)` → 1014 body per layout (UID@4=`CharacterID`, Lookface@8=`Mesh`, Life@48=`HealthPoints`, Level@50/@62=`Level`, X@52=`X`, Y@54=`Y`, Hair@56=`Avatar`, Dir@58=0, Action@59=0 stand, name@90 via NetStringPacker; bytes 12-47/61/63-89 zero). Built but NOT wired (gated fallback).
    2. In `ActionHandler`, confirm all error paths from design are present and clean (short frame, null character, unknown subtype) — refactor for readability without changing behavior; keep gated `// case 102`/`// case 114` commented.
  - **Files**: src/Packets/SpawnEntity.cs, src/Packets/ActionHandler.cs
  - **Done when**: 1014 builder compiles; ActionHandler edges clean; gated paths remain off.
  - **Verify**: `scripts/dotnet build src/Conquer.sln && grep -q 'BuildSelf' src/Packets/SpawnEntity.cs && echo PASS`
  - **Commit**: `refactor(enter-world): add gated SpawnEntity(1014) builder and harden ActionHandler edges`
  - _Requirements: FR-6, FR-7, FR-11, NFR-4, US-4, AC-4.3_
  - _Design: SpawnEntity, Error Handling, Edge Cases_

- [ ] 2.2 [VERIFY] Quality checkpoint: build clean after refactor
  - **Do**: Build; confirm no behavior change to the live 74 path; gated 1014 unused.
  - **Verify**: `scripts/dotnet build src/Conquer.sln && echo PASS`
  - **Done when**: 0 build errors.
  - **Commit**: `chore(enter-world): pass quality checkpoint` (only if fixes needed)
  - _Requirements: NFR-1_

## Phase 3: Testing

- [ ] 3.1 Create Packets.Tests xUnit project + add to solution
  - **Do**:
    1. Create `src/Packets.Tests/Packets.Tests.csproj` (xUnit, mirror `src/Crypto.Tests/Crypto.Tests.csproj`) referencing `src/Packets/Packets.csproj` and `src/Database/Database.csproj` (for `DbCharacter` fixtures).
    2. Add the project to `src/Conquer.sln` (`dotnet sln` via `scripts/dotnet`, or edit the .sln).
    3. Add one trivial passing smoke test so `dotnet test` discovers the project.
  - **Files**: src/Packets.Tests/Packets.Tests.csproj, src/Packets.Tests/SmokeTest.cs, src/Conquer.sln
  - **Done when**: `scripts/dotnet test src/Conquer.sln` discovers + runs Packets.Tests green.
  - **Verify**: `scripts/dotnet test src/Conquer.sln && echo PASS`
  - **Commit**: `test(enter-world): add Packets.Tests xUnit project to solution`
  - _Requirements: NFR-2_
  - _Design: Test Strategy, File Structure_

- [ ] 3.2 Builder-layout + Action-parse@20 unit tests
  - **Do**:
    1. `GeneralData_SetLocationEcho_Layout`: assert body=28, length@0=28, type@2=1010, UID@8, Data1@12=MapID, Data2@16=`(Y<<16)|X` (assert both byte pairs: low16=X, high16=Y), Action@22=74.
    2. `MapStatus_Layout`: body=16, type@2=1110, UID@4=ID@8=MapID, Type@12=0.
    3. `SpawnEntity_SelfLayout`: type@2=1014, UID@4, Lookface@8, X@52, Y@54, Hair@56, Action@59=0, name@90.
    4. `ActionParse_Offset20`: build a synthetic 1010 dispatch payload (length-prefix stripped) with 74 at offset 20; assert the parse logic reads `u16@20 == 74`.
  - **Files**: src/Packets.Tests/GeneralDataTests.cs, src/Packets.Tests/MapStatusTests.cs, src/Packets.Tests/SpawnEntityTests.cs, src/Packets.Tests/ActionParseTests.cs
  - **Done when**: All 4 tests pass; assertions match the design's exact offsets.
  - **Verify**: `scripts/dotnet test src/Conquer.sln && echo PASS`
  - **Commit**: `test(enter-world): assert 1010/1110/1014 byte layouts + Action@20 parse`
  - _Requirements: FR-3, FR-4, FR-5, FR-6, NFR-2, NFR-4, AC-2.2, AC-3.1, AC-3.2_
  - _Design: Test Strategy (Unit Tests)_

- [ ] 3.3 [VERIFY] Quality checkpoint: build + test green
  - **Do**: Run full build + test suite (existing Crypto.Tests/ClientPatcher.Tests + new Packets.Tests).
  - **Verify**: `scripts/dotnet build src/Conquer.sln && scripts/dotnet test src/Conquer.sln && echo PASS`
  - **Done when**: Build clean; all tests pass.
  - **Commit**: `chore(enter-world): pass quality checkpoint` (only if fixes needed); push branch.
  - _Requirements: NFR-1, NFR-2_

## Phase 4: Quality Gates & PR Lifecycle

NEVER push to master. Stay on `feat/enter-world`. Branch was set at startup (1.1).

- [ ] 4.1 Strip diagnostics (FR-10)
  - **Do**: Remove the diagnostic logs added/kept through this work: the new `[Game] SetLocation -> map/x/y`, the `[Game] 1010 Action=... unhandled` / short / no-char logs, and the full inbound 1010 frame dump in `ActionHandler`; also strip `[Game][DH]` and `[Game][frame]` in `GameConnection` (logging lines ONLY — do NOT touch the frame-split/crypto logic). Keep all functional behavior.
  - **Files**: src/Packets/ActionHandler.cs, src/Redux/GameConnection.cs
  - **Done when**: No `[Game][DH]`, `[Game][frame]`, or new `[Game] SetLocation`/frame-dump log lines remain; build clean; behavior unchanged.
  - **Verify**: `! grep -rn '\[Game\]\[DH\]\|\[Game\]\[frame\]\|SetLocation ->' src/Packets/ActionHandler.cs src/Redux/GameConnection.cs && scripts/dotnet build src/Conquer.sln && echo PASS`
  - **Commit**: `chore(enter-world): strip diagnostic logging before PR`
  - _Requirements: FR-10, FR-11, NFR-1_
  - _Design: Diagnostics / Cleanup_

- [ ] 4.2 [VERIFY] Full local CI gate
  - **Do**: Run the complete local suite after diagnostics strip.
  - **Verify**: `scripts/dotnet build src/Conquer.sln && scripts/dotnet test src/Conquer.sln && echo PASS`
  - **Done when**: Build clean (NFR-1); all tests green (NFR-2); auth/handshake path shows zero diff (FR-11 — `git diff feat/character-select -- src/Crypto src/Packets/MsgAccount* src/Redux/GameConnection.cs` limited to log-strip only).
  - **Commit**: `chore(enter-world): pass full local CI` (only if fixes needed)
  - _Requirements: NFR-1, NFR-2, NFR-3, NFR-5, FR-11_

- [ ] 4.3 Operator-manual final gate — Vitor stands in-world (M2) + create PR
  - **Do**: (OPERATOR-MANUAL, out-of-CI — definitive)
    1. Rebuild + redeploy: `scripts/dotnet build src/Conquer.sln` → `docker compose -f src/docker-compose.yml up -d --build` (192.168.0.252).
    2. Operator logs in with the real Windows 5065 client; confirm loading clears (AC-3.5) AND **"Vitor" renders standing** at the spawn map/coords (AC-4.1). If A2 failed, confirm the gated FR-7 self-1014 fallback rendered the body (AC-4.3).
    3. Record the AFTER state (loading cleared + char standing, Action==74 confirmed) in `specs/enter-world/.progress.md`.
    4. Push branch; create PR: `gh pr create --base feat/character-select --title "feat(enter-world): stand-still enter-world (SetLocation echo + 1110)" --body "<summary + operator-manual confirmation>"`.
    5. Verify CI: `gh pr checks --watch`. If red, fix locally, push, re-watch.
  - **Files**: specs/enter-world/.progress.md
  - **Done when**: Operator confirms Vitor standing in-world; PR open against `feat/character-select`; `gh pr checks` all green.
  - **Verify**: OPERATOR-MANUAL (out-of-CI) for AC-4.1; `gh pr checks` green for CI.
  - **Commit**: `docs(enter-world): record operator-manual enter-world confirmation` (progress note)
  - _Requirements: US-4, AC-3.5, AC-4.1, AC-4.2, AC-4.3, NFR-3_
  - _Design: Operator-Manual, Test Strategy_

## Notes

- **POC shortcuts (deliberate):** spawn at DB coords Map 1010/61/109 as-is (A1); minimal 1110 (`UID=ID=MapID, Type=0`, no DbMap); no self-1014 in the minimal path (A2). Diagnostic logs kept through Phase 1–3, stripped in 4.1.
- **Gated/deferred (off by default, behind live observation):** self-spawn 1014 on InvisibleEntity(102) (FR-7), GetSurroundings(114) no-op (FR-8), CompleteLogin(130)/Hotkeys(75). A1 fallback = re-seed/override TC 1002/438/381.
- **Production TODOs (out of this spec):** movement/combat/NPCs/items, real surroundings, server-side map loading, CompleteLogin login-step extras.
- **No automated VE tasks** (no automated 5065 client); end-to-end is operator-manual (1.7 M1 loading-clear, 4.3 M2 stands-in-world). CI = builder-layout + Action-parse unit tests only.
- **Branch:** `feat/enter-world` off `feat/character-select`; PR targets `feat/character-select` (handshake/crypto not yet on master).

## Total: 15 tasks (10 work + 5 [VERIFY] checkpoints) across 4 phases.
