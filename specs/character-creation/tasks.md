---
spec: character-creation
basePath: specs/character-creation
phase: tasks
updated: 2026-06-26
---

# Tasks: Character Creation (MsgRegister 1001)

Workflow: POC-first (GREENFIELD-style additive). Total: **15 tasks** — Phase 1 (POC) 6, Phase 2 (Refactor) 2, Phase 3 (Testing) 3, Phase 4 (Quality + PR) 4.

Build/test is DOCKERIZED. Every [VERIFY] uses `scripts/dotnet ...` from repo root — NEVER bare `dotnet` (no local SDK on macOS). Branch `feat/character-creation` already checked out. One commit per task; commit messages end with `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`. Additive only — zero edits to auth/crypto/handshake/GameConnection/SendGame/MsgConnect/GameHandler/ActionHandler/CharacterRepository.Insert.

## Phase 1: Make It Work (POC)

Focus: prove the end-to-end create path — enum → handler (parse+validate+build+insert+ANSWER_OK) → router case → build green. Skip tests until Phase 3.

- [x] 1.1 Add `Register = 2100` to ChatType enum
  - **Do**:
    1. Edit `src/Packets/ChatType.cs`: add `Register = 2100,` before `Entrance = 2101`.
  - **Files**: src/Packets/ChatType.cs
  - **Done when**: `ChatType.Register` resolves to `2100`; `ChatType.Entrance` unchanged at `2101`.
  - **Verify**: `grep -q 'Register = 2100' src/Packets/ChatType.cs && grep -q 'Entrance = 2101' src/Packets/ChatType.cs && echo PASS`
  - **Commit**: `feat(packets): add ChatType.Register=2100 for creation channel`
  - _Requirements: FR-10_
  - _Design: ChatType_

- [x] 1.2 Create RegisterHandler — parse 1001 payload
  - **Do**:
    1. CREATE `src/Packets/RegisterHandler.cs`: `public sealed class RegisterHandler` in `namespace Conquer.Packets`, ctor-injected `CharacterRepository _characters`, `public void Handle(ClientSession session, byte[] payload)`.
    2. Guard `if (payload.Length < 60) { Console.WriteLine("[Game] short 1001"); return; }` (mirror ActionHandler).
    3. Parse: `name = Encoding.ASCII.GetString(payload, 18, 16).TrimEnd('\0')`; `mesh = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(50,2))`; `prof = payload[52]`.
  - **Files**: src/Packets/RegisterHandler.cs
  - **Done when**: Handler parses Name@18 / Mesh@50 / Profession@52 with the <60 guard; UID@56 ignored.
  - **Verify**: `grep -q 'payload.Length < 60' src/Packets/RegisterHandler.cs && grep -q 'GetString(payload, 18, 16)' src/Packets/RegisterHandler.cs && echo PASS`
  - **Commit**: `feat(packets): RegisterHandler parse 1001 payload (name/mesh/prof)`
  - _Requirements: FR-2, AC-4.1_
  - _Design: 1001 Payload Offset Table_

- [x] 1.3 RegisterHandler — validation + per-failure MsgTalk
  - **Do**:
    1. Local literals (no Redux ref): compiled `Regex("^[a-zA-Z0-9]{4,16}$")`, `HashSet<ushort>` mesh `{1003,1004,2001,2002}`, `HashSet<byte>` prof `{10,20,30,40,100}`.
    2. Order (return after first fail, no insert): name regex AND `!name.ToLower().Contains("admin")` → `"Invalid character name"`; mesh set → `"Invalid character mesh"`; prof set → `"Invalid character profession"`.
    3. On fail: `session.SendGame(MsgTalk.Build(ChatType.Register, reason)); return;`
  - **Files**: src/Packets/RegisterHandler.cs
  - **Done when**: Each invalid input sends one `MsgTalk(Register, reason)` via SendGame and skips insert.
  - **Verify**: `grep -q 'a-zA-Z0-9' src/Packets/RegisterHandler.cs && grep -q '1003' src/Packets/RegisterHandler.cs && grep -q 'ChatType.Register' src/Packets/RegisterHandler.cs && echo PASS`
  - **Commit**: `feat(packets): RegisterHandler input validation + reject messages`
  - _Requirements: FR-3, FR-5, FR-6, AC-2.1, AC-2.3, AC-2.4_
  - _Design: Validation table_

- [x] 1.4 [VERIFY] Build checkpoint: handler+enum compile
  - **Do**: Run dockerized build (catches enum/handler compile errors early before wiring).
  - **Verify**: `scripts/dotnet build src/Conquer.sln 2>&1 | tee /tmp/build.log; grep -q 'Build succeeded\|0 Error' /tmp/build.log && echo VERIFY_PASS`
  - **Done when**: Solution builds clean with new enum value + RegisterHandler.
  - **Commit**: `chore(quality): pass build checkpoint — handler compiles` (only if fixes needed)

- [x] 1.5 RegisterHandler — build DbCharacter + Insert + ANSWER_OK
  - **Do**:
    1. Appearance: face = mesh∈{1003,1004} → `Random.Next(50)` (0..49), mesh∈{2001,2002} → `Random.Next(201,250)` (201..249). Avatar = `Random.Next(3,9)*100 + Random.Next(30,51)`.
    2. Build level-1 `DbCharacter` (object-initializer; `init` setters): `AccountID=session.AccountId`, `Name=name`, `Mesh=mesh + face*10000`, `Avatar`, `Level=1`, `Silver=1000`, `MapID=1010`, `X=61`, `Y=109`, `Strength=4, Agility=6, Vitality=12, Spirit=0`, `HealthPoints=318`, `ManaPoints=0`.
    3. `try { _characters.Insert(ch); } catch (Exception e) { Console.WriteLine(...); session.SendGame(MsgTalk.Build(ChatType.Register, "Character name already in use")); return; }`
    4. On success: `session.SendGame(MsgTalk.Build(ChatType.Register, "ANSWER_OK"));` and send nothing else.
  - **Files**: src/Packets/RegisterHandler.cs
  - **Done when**: Valid 1001 inserts one row (AccountID=session, Map 1010) and replies `ANSWER_OK`; dup/throw → "name already in use", no crash; no enter-world on this socket.
  - **Verify**: `grep -q 'session.AccountId' src/Packets/RegisterHandler.cs && grep -q 'ANSWER_OK' src/Packets/RegisterHandler.cs && grep -q '_characters.Insert' src/Packets/RegisterHandler.cs && grep -q 'catch' src/Packets/RegisterHandler.cs && echo PASS`
  - **Commit**: `feat(packets): RegisterHandler build+Insert DbCharacter, reply ANSWER_OK`
  - _Requirements: FR-7, FR-8, FR-9, FR-11, AC-1.1, AC-1.2, AC-3.*, AC-4.2_
  - _Design: DbCharacter build, Appearance & Stat Formulas, Error Handling_

- [x] 1.6 Wire PacketRouter `case 1001:` (POC milestone)
  - **Do**:
    1. Edit `src/Redux/PacketRouter.cs`: add field `private readonly Conquer.Packets.RegisterHandler _register;`.
    2. In ctor: `_register = new Conquer.Packets.RegisterHandler(characters);` (the SAME `characters` instance GameHandler receives — no new ctor param).
    3. In `Dispatch` switch: `case 1001: _register.Handle(session, payload); break;`.
  - **Files**: src/Redux/PacketRouter.cs
  - **Done when**: 1001 routes to RegisterHandler (no longer "Unknown typeId"); only +1 field, +1 ctor line, +1 case. POC end-to-end path complete (parse→validate→build→insert→ANSWER_OK).
  - **Verify**: `grep -q 'case 1001' src/Redux/PacketRouter.cs && grep -q 'new Conquer.Packets.RegisterHandler(characters)' src/Redux/PacketRouter.cs && scripts/dotnet build src/Conquer.sln 2>&1 | grep -q 'Build succeeded\|0 Error' && echo VERIFY_PASS`
  - **Commit**: `feat(redux): route MsgRegister 1001 to RegisterHandler`
  - _Requirements: FR-1, NFR-1_
  - _Design: PacketRouter wiring_

## Phase 2: Refactoring

After POC proves out, make parse/build unit-testable and tidy logging. Keep behavior identical.

- [x] 2.1 Extract testable static `ParseRegister` + `BuildCharacter`
  - **Do**:
    1. In `RegisterHandler.cs`, extract a static `ParseRegister(byte[] payload)` returning `(string name, ushort mesh, byte prof)` (callable without a socket/DB).
    2. Extract a static `BuildCharacter(int accountId, string name, ushort mesh, byte prof, Random rng)` returning the `DbCharacter` (appearance + stats logic). `Handle` now calls both; behavior unchanged.
  - **Files**: src/Packets/RegisterHandler.cs
  - **Done when**: Parse + char-build logic are pure static methods (no live socket/DB); `Handle` delegates to them.
  - **Verify**: `grep -q 'static' src/Packets/RegisterHandler.cs && grep -Eq 'ParseRegister|BuildCharacter' src/Packets/RegisterHandler.cs && echo PASS`
  - **Commit**: `refactor(packets): extract static ParseRegister/BuildCharacter for testability`
  - _Design: Test Strategy (unit-testable surface)_

- [x] 2.2 Tidy operational logging
  - **Do**:
    1. One concise log line on create success (e.g. `[Game] 1001 create name=<name> mesh=<mesh> acct=<id>`) and on reject (reason). No hex dumps — consistent with the lightweight logs kept elsewhere.
  - **Files**: src/Packets/RegisterHandler.cs
  - **Done when**: Create + reject each emit one concise line; no verbose/hex output.
  - **Verify**: `scripts/dotnet build src/Conquer.sln 2>&1 | grep -q 'Build succeeded\|0 Error' && echo VERIFY_PASS`
  - **Commit**: `refactor(packets): concise create/reject logging`
  - _Design: Error Handling_

## Phase 3: Testing

xUnit in `src/Packets.Tests/` (mirror `ActionParseTests.cs`). Tests target only pure parse/build logic — no socket/DB.

- [x] 3.1 Create RegisterParseTests — offset/layout + formula + stat tests
  - **Do**:
    1. CREATE `src/Packets.Tests/RegisterParseTests.cs` (xUnit). Build a synthetic 60-byte payload: type@0=1001, ASCII "TestName"@18, Mesh u16 LE @50=1003, Prof u8 @52=10, UID u32 LE @56.
    2. Test (a) layout: `ParseRegister` yields name=="TestName", mesh==1003, prof==10; `payload.Length>=60`.
    3. Test (b) formula bounds via `BuildCharacter` with seeded `Random`: body 1003 face 12 → `Mesh==121003`; body 2001 face 210 → `Mesh==2102001`; `Avatar` in 330..868; Lookface body+face*10000 in expected per-body range.
    4. Test (c) stat derivation: STR4/AGI6/VIT12/SPI0 → `HealthPoints==318`, `ManaPoints==0` (factors STR3/AGI3/VIT24/SPI3, mana SPI5 as local literals).
  - **Files**: src/Packets.Tests/RegisterParseTests.cs
  - **Done when**: 3 test groups assert layout, Mesh/Avatar formula bounds, and Life=318/Mana=0.
  - **Verify**: `grep -q 'RegisterParseTests' src/Packets.Tests/RegisterParseTests.cs && grep -q '121003' src/Packets.Tests/RegisterParseTests.cs && grep -q '318' src/Packets.Tests/RegisterParseTests.cs && echo PASS`
  - **Commit**: `test(packets): RegisterParseTests — layout, mesh/hair formula, stat derivation`
  - _Requirements: NFR-5, FR-2, AC-3.1, AC-3.2, AC-3.3, AC-3.4_
  - _Design: Test Strategy / Unit tests_

- [x] 3.2 [VERIFY] Full dockerized test suite green
  - **Do**: Run the whole xUnit suite — existing 11 tests + new RegisterParseTests must all pass.
  - **Verify**: `scripts/dotnet test src/Conquer.sln 2>&1 | tee /tmp/test.log; grep -Eq 'Passed!|Failed: *0' /tmp/test.log && ! grep -q 'Failed: *[1-9]' /tmp/test.log && echo VERIFY_PASS`
  - **Done when**: All tests pass (no regressions; new parse tests green).
  - **Commit**: `chore(quality): pass test checkpoint — full suite green` (only if fixes needed)

- [x] 3.3 [VERIFY] Confirm additive scope — no forbidden files changed
  - **Do**: Diff the branch against `master` and confirm only the 4 allowed files changed (RegisterHandler.cs, ChatType.cs, PacketRouter.cs, RegisterParseTests.cs) plus spec files. Catch accidental edits to auth/crypto/GameConnection/SendGame/MsgConnect/GameHandler/ActionHandler/CharacterRepository.
  - **Verify**: `git diff --name-only master -- src | grep -vE 'src/Packets/RegisterHandler.cs|src/Packets/ChatType.cs|src/Redux/PacketRouter.cs|src/Packets.Tests/RegisterParseTests.cs' | grep . && echo FORBIDDEN_CHANGE || echo VERIFY_PASS`
  - **Done when**: No source file outside the 4 allowed appears in the diff (NFR-1).
  - **Commit**: None

## Phase 4: Quality Gates + PR Lifecycle

NEVER push to the protected default `master`. Push `feat/character-creation` and merge via PR.

- [x] 4.1 [VERIFY] Final full local CI gate
  - **Do**: Run the complete dockerized gate: build clean + full test suite green. No lint in this repo (confirmed). Fix any issue and re-run until green.
  - **Verify**: `scripts/dotnet build src/Conquer.sln 2>&1 | grep -q 'Build succeeded\|0 Error' && scripts/dotnet test src/Conquer.sln 2>&1 | grep -Eq 'Passed!|Failed: *0' && echo VERIFY_PASS`
  - **Done when**: Build succeeds AND all tests pass.
  - **Commit**: `chore(quality): pass full local CI gate` (only if fixes needed)

- [x] 4.2 Push branch + create PR with operator E2E checklist
  - **Do**:
    1. Confirm branch: `git branch --show-current` (must be `feat/character-creation`, NOT master — if master, STOP and alert).
    2. `git push -u origin feat/character-creation`.
    3. `gh pr create --base master --title "feat: character creation (MsgRegister 1001)" --body "<body>"`. Body includes: summary (additive 1001 handler → validate → Insert → ANSWER_OK; reconnect spawns via unchanged 1052), the 4 changed files, and the **Operator Manual E2E checklist** (see 4.3). If gh unavailable, print the compare URL.
  - **Files**: (none — git/gh only)
  - **Done when**: PR open `feat/character-creation → master` with E2E checklist in body.
  - **Verify**: `gh pr view --json url,baseRefName 2>/dev/null | grep -q '"baseRefName":"master"' && echo PASS`
  - **Commit**: None
  - _Requirements: NFR-1, Success Criteria_

- [x] 4.3 [VERIFY] CI pipeline passes
  - **Do**: After push, confirm GitHub Actions / CI checks on the PR. Fix locally + push if red; re-check.
  - **Verify**: `gh pr checks 2>&1 | grep -viE 'pass|✓|success|no checks' | grep -iE 'fail|✗|error' && echo CI_RED || echo VERIFY_PASS`
  - **Done when**: All CI checks green (or repo has no CI workflow — then this is a no-op pass).
  - **Commit**: None

- [ ] 4.4 [VERIFY] M2 Operator gate — manual E2E is the real acceptance
  - **Do**: This is the authoritative acceptance, **operator-run on the dockerized server (192.168.0.252) with the real 5065 client — NOT automatable here.** Document the checklist in the PR/issue for the operator and pause for sign-off:
    1. Delete seeded "Vitor" (or use a fresh account) so `NEW_ROLE` fires → creation screen shows.
    2. Create a character in the real client.
    3. Confirm a `characters` row exists with correct `AccountID` / `Name` / `Mesh` / `MapID=1010` / `X=61` / `Y=109`.
    4. Confirm client received `ANSWER_OK` (creation accepted).
    5. Confirm reconnect spawns the new char in-world (unchanged 1052 path).
    6. Confirm relog persists the char.
    7. Confirm a malformed/short 1001 does not crash the listener.
  - **Verify**: Automated proxy — code path is wired and tested: `grep -q 'case 1001' src/Redux/PacketRouter.cs && grep -q 'ANSWER_OK' src/Packets/RegisterHandler.cs && scripts/dotnet test src/Conquer.sln 2>&1 | grep -Eq 'Passed!|Failed: *0' && echo VERIFY_PASS`. Final acceptance = operator sign-off on steps 1-7 (recorded in PR).
  - **Done when**: Operator confirms create → DB row → ANSWER_OK → reconnect spawn → relog persists; listener survives malformed input. Sign-off recorded on the PR.
  - **Commit**: None
  - _Requirements: AC-1.1, AC-1.2, AC-1.3, AC-4.1, Success Criteria_

## Notes

- **POC shortcuts taken**: single shared level-1 stats STR4/AGI6/VIT12/SPI0 (Life 318, Mana 0), profession-agnostic for MVP (TD-1 — operator-tunable; per-profession is a one-method change in `BuildCharacter`). Dup-name handled by catch on `uq_name` only, no `FindByName` (TD-2). Spawn fixed at 1010/61/109 (TD-4, not original 1002/438/381).
- **Production TODOs (operator-capture, non-blocking)**: confirm exact live 1001 byte layout vs capture; confirm client reconnects vs same-socket re-1052 after ANSWER_OK (TD-3); confirm shared stats render/spawn for all 4 meshes; confirm whether map 1002 is loaded (could revisit spawn later).
- **Local literals reminder**: `Packets.csproj` does NOT reference Redux → RegisterHandler + tests re-declare regex / mesh-set / prof-set / stat-factor literals locally (mirrors `ClientSession` re-declaring `SERVER_SEAL`). Do not import `Redux.Common`.
- **E2E note**: no automated E2E harness exists; the dockerized `scripts/dotnet test` + the operator gate (4.4) together constitute acceptance. No VE dev-server tasks apply (this is a library/handler change with no standalone server endpoint to curl).
