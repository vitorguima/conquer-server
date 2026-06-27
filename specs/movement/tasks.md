---
spec: movement
basePath: specs/movement
phase: tasks
---

# Tasks: Server-Authoritative Movement (MsgWalk 1005)

Workflow: POC-first (GREENFIELD additive). Total: 16 tasks.
Phase 1 (POC) 5 · Phase 2 (Persistence) 4 · Phase 3 (Testing) 3 · Phase 4 (Quality + PR) 4.

Strict gate ON. Every [VERIFY] uses `scripts/dotnet build src/Conquer.sln` (0 warnings / 0 errors — new code nullable-clean) and `scripts/dotnet test src/Conquer.sln` (NEVER bare dotnet). Additive only: MUST NOT touch auth, crypto/handshake, GameConnection, SendGame, ActionHandler spawn logic, MsgConnect token/auth/answer behavior (only ADD seed lines), or character creation. One commit per task; messages end with a blank line then `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`. Branch feat/movement (already checked out) — PR only, NEVER push to master.

## Phase 1: Make It Work (POC)

Focus: prove the server tracks the player's authoritative (x,y) as they walk — logs only, no DB.

- [x] 1.1 Add live-position fields to ClientSession
  - **Do**: In `src/Network/ClientSession.cs` add four mutable auto-props (AD-1): `public int CurrentMap { get; set; }`, `public ushort CurrentX { get; set; }`, `public ushort CurrentY { get; set; }`, `public bool PositionLoaded { get; set; }`. Value types — no null handling. Add the design's doc-comment (live authoritative pos, seeded at 1052, NOT the Character store).
  - **Files**: src/Network/ClientSession.cs
  - **Done when**: Four fields present; file is nullable-clean; no other member touched.
  - **Verify**: `grep -q 'PositionLoaded' src/Network/ClientSession.cs && grep -q 'public ushort CurrentX' src/Network/ClientSession.cs && echo PASS`
  - **Commit**: `feat(network): add live-position fields to ClientSession`
  - _Requirements: FR-5, AC-4.1_ · _Design: AD-1, ClientSession additions_

- [x] 1.2 Create WalkHandler (parse + ComputeStep)
  - **Do**: Create `src/Packets/WalkHandler.cs`, namespace `Conquer.Packets`, `public sealed class WalkHandler`. Add LOCAL `private static readonly sbyte[] DeltaX = { 0,-1,-1,-1,0,1,1,1 };` and `DeltaY = { 1,1,0,-1,-1,-1,0,1 };` (index 8 dropped). `public static (uint uid, byte dir, byte mode) ParseWalk(byte[] payload)` reading UID@2 via `BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(2,4))`, Direction@6 (`payload[6]`), Mode@7 (`payload[7]`). `public static (int nx, int ny) ComputeStep(int curX, int curY, byte dir)` returning `(curX + DeltaX[dir%8], curY + DeltaY[dir%8])` as ints. No `Handle` yet, no SendGame, no repo, no per-call alloc.
  - **Files**: src/Packets/WalkHandler.cs
  - **Done when**: ParseWalk + ComputeStep compile, nullable-clean, no Redux.Common import.
  - **Verify**: `grep -q 'static (uint uid, byte dir, byte mode) ParseWalk' src/Packets/WalkHandler.cs && grep -q 'ComputeStep' src/Packets/WalkHandler.cs && echo PASS`
  - **Commit**: `feat(packets): add WalkHandler ParseWalk + ComputeStep`
  - _Requirements: FR-1, FR-3, AC-1.1, AC-1.2_ · _Design: WalkHandler interfaces, AD-4, Delta Table, Payload Offsets_

- [x] 1.3 Add guard-first WalkHandler.Handle (in-mem mutate + log)
  - **Do**: In `src/Packets/WalkHandler.cs` add `public void Handle(ClientSession session, byte[] payload)`. Guard order (early-return each, log + ignore, NEVER disconnect): `payload.Length < 8` → `[Game] short 1005`; `session.Character == null || !session.PositionLoaded` → return; `ParseWalk` → `dir > 7` → `[Game] 1005 bad dir=N`; `ComputeStep(CurrentX, CurrentY, dir)` → `nx<0 || ny<0 || nx>ushort.MaxValue || ny>ushort.MaxValue` → `[Game] 1005 oob (nx,ny)` (reject, leave pos unchanged); valid → `session.CurrentX=(ushort)nx; session.CurrentY=(ushort)ny;` + log `[Game] walk dir=N mode=M -> (x,y)`. ~≤40 lines. No SendGame, no repo, no echo, no alloc.
  - **Files**: src/Packets/WalkHandler.cs
  - **Done when**: All 5 guards present; valid path mutates CurrentX/Y + logs incl. Mode; bounds = reject not clamp.
  - **Verify**: `grep -q 'payload.Length < 8' src/Packets/WalkHandler.cs && grep -q 'PositionLoaded' src/Packets/WalkHandler.cs && grep -q 'ushort.MaxValue' src/Packets/WalkHandler.cs && echo PASS`
  - **Commit**: `feat(packets): add guard-first WalkHandler.Handle with in-memory mutation`
  - _Requirements: FR-4, FR-9, FR-10, AC-1.3, AC-1.4, AC-3.1, AC-3.2, AC-3.3, AC-3.4_ · _Design: Error Handling, Data Flow (walk hot path), AD-3, AD-4_

- [x] 1.4 [VERIFY] Quality checkpoint: build after WalkHandler
  - **Do**: `scripts/dotnet build src/Conquer.sln`.
  - **Verify**: `scripts/dotnet build src/Conquer.sln` exits 0 with 0 warnings / 0 errors.
  - **Done when**: Build clean; WalkHandler + ClientSession fields compile nullable-clean.
  - **Commit**: `chore(movement): pass build checkpoint (WalkHandler)` (only if fixes needed)

- [x] 1.5 Wire PacketRouter case 1005 + seed live pos at 1052 (POC MILESTONE)
  - **Do**: (a) `src/Redux/PacketRouter.cs`: add `private readonly Conquer.Packets.WalkHandler _walk;`, `_walk = new Conquer.Packets.WalkHandler();` in ctor (no deps), and `case 1005: _walk.Handle(session, payload); break;` in Dispatch — mirror `_action`/`_register`. (b) `src/Packets/MsgConnect.cs` GameHandler.Handle, INSIDE the existing `if (character != null)` branch right after `session.Character = character`: `session.CurrentMap = character.MapID; session.CurrentX = (ushort)character.X; session.CurrentY = (ushort)character.Y; session.PositionLoaded = true;`. Do NOT touch token/auth/answer/HeroInformation lines. Do NOT touch ActionHandler.HandleSetLocation (AC-4.2 — keeps reading Character).
  - **Files**: src/Redux/PacketRouter.cs, src/Packets/MsgConnect.cs
  - **Done when**: 1005 dispatches to WalkHandler; live pos seeded on 1052. POC: server logs the player's updated (x,y) per walk (authoritative tracking proven), no DB.
  - **Verify**: `grep -q 'case 1005' src/Redux/PacketRouter.cs && grep -q 'PositionLoaded = true' src/Packets/MsgConnect.cs && scripts/dotnet build src/Conquer.sln`
  - **Commit**: `feat(movement): wire 1005 dispatch + seed live position at connect`
  - _Requirements: FR-2, FR-6, AC-4.1, AC-4.2_ · _Design: PacketRouter wiring, GameHandler seed, Implementation Steps 3-4_

## Phase 2: Persistence

Focus: one UPDATE per session on disconnect → relog spawns at last position end to end.

- [x] 2.1 Add CharacterRepository.UpdatePosition
  - **Do**: In `src/Database/CharacterRepository.cs` add `public void UpdatePosition(int characterId, int mapId, int x, int y)` running a single Dapper `conn.Execute("UPDATE characters SET MapID=@MapID, X=@X, Y=@Y WHERE CharacterID=@Id", new { MapID = mapId, X = x, Y = y, Id = characterId });` via the existing connection factory (mirror Insert/FindByAccountId). Do not touch existing methods.
  - **Files**: src/Database/CharacterRepository.cs
  - **Done when**: UpdatePosition present, single UPDATE, nullable-clean.
  - **Verify**: `grep -q 'UpdatePosition' src/Database/CharacterRepository.cs && grep -q 'UPDATE characters SET MapID' src/Database/CharacterRepository.cs && echo PASS`
  - **Commit**: `feat(database): add CharacterRepository.UpdatePosition`
  - _Requirements: FR-7, AC-2.2_ · _Design: CharacterRepository.UpdatePosition_

- [x] 2.2 Inject CharacterRepository into NetworkListener + disconnect flush
  - **Do**: `src/Redux/NetworkListener.cs`: add `private readonly CharacterRepository _characters;`, change ctor to `(IConfiguration config, PacketRouter router, CharacterRepository characters)` and assign it. In `ServeGameAsync` finally, BEFORE `session.Disconnect()`: `if (session.PositionLoaded && session.Character != null) _characters.UpdatePosition(session.Character.CharacterID, session.CurrentMap, session.CurrentX, session.CurrentY);`. Exactly one UPDATE/session (AD-2). Do not touch handshake/read-loop logic.
  - **Files**: src/Redux/NetworkListener.cs
  - **Done when**: Flush guarded by `PositionLoaded && Character != null`, runs once before Disconnect.
  - **Verify**: `grep -q 'UpdatePosition' src/Redux/NetworkListener.cs && grep -q 'session.PositionLoaded && session.Character != null' src/Redux/NetworkListener.cs && echo PASS`
  - **Commit**: `feat(network): flush live position to DB on disconnect`
  - _Requirements: FR-8, AC-2.1, AC-2.3_ · _Design: NetworkListener flush, AD-2_

- [x] 2.3 Wire CharacterRepository into NetworkListener ctor in Program.cs
  - **Do**: `src/Redux/Program.cs`: pass the already-constructed `characters` repo into the listener: `new NetworkListener(config, router, characters)`. No other wiring change.
  - **Files**: src/Redux/Program.cs
  - **Done when**: Program.cs compiles with the new 3-arg listener ctor.
  - **Verify**: `grep -q 'new NetworkListener(config, router, characters)' src/Redux/Program.cs && echo PASS`
  - **Commit**: `feat(redux): wire CharacterRepository into NetworkListener`
  - _Requirements: FR-8_ · _Design: NetworkListener flush + wiring, Components_

- [ ] 2.4 [VERIFY] Quality checkpoint: build after persistence wiring
  - **Do**: `scripts/dotnet build src/Conquer.sln`.
  - **Verify**: `scripts/dotnet build src/Conquer.sln` exits 0 with 0 warnings / 0 errors.
  - **Done when**: Full solution builds clean with persistence path wired.
  - **Commit**: `chore(movement): pass build checkpoint (persistence)` (only if fixes needed)

## Phase 3: Testing

- [ ] 3.1 Create WalkParseTests (parse + delta + bounds + guards)
  - **Do**: Create `src/Packets.Tests/WalkParseTests.cs` (xUnit), mirroring `ActionParseTests.cs` (pure static, synth ≥8-byte payload via BinaryPrimitives, no socket/DB). Cover: (1) ParseWalk offsets — write Dir@6, Mode@7, UID@2 → assert returned tuple; (2) ComputeStep all 8 dirs vs the table; (3) bounds-reject — (0,0) dir 3 → negative candidate; (65535,65535) dir 7 → over-max; (4) short-packet guard `payload.Length == 7` → Handle no-ops (no throw); (5) dir>7 (`Direction = 8`) → no move; (6) seed-source regression (AC-4.2): assert/comment SetLocation echo source is `Character`, not `CurrentX/Y`.
  - **Files**: src/Packets.Tests/WalkParseTests.cs
  - **Done when**: Tests cover all 6 cases; no socket/DB.
  - **Verify**: `grep -q 'ParseWalk' src/Packets.Tests/WalkParseTests.cs && grep -q 'ComputeStep' src/Packets.Tests/WalkParseTests.cs && echo PASS`
  - **Commit**: `test(packets): add WalkParseTests for parse, delta, bounds, guards`
  - _Requirements: FR-11, AC-1.1, AC-1.2, AC-3.1, AC-3.2, AC-3.3, AC-4.2_ · _Design: Test Strategy (Unit)_

- [ ] 3.2 [VERIFY] Full test suite green
  - **Do**: `scripts/dotnet test src/Conquer.sln`.
  - **Verify**: `scripts/dotnet test src/Conquer.sln` exits 0; WalkParseTests pass; ActionParseTests + existing suites still green (no regression).
  - **Done when**: All tests pass.
  - **Commit**: `chore(movement): pass full test suite` (only if fixes needed)

- [ ] 3.3 [VERIFY] Additive-scope diff guard
  - **Do**: Verify only the allowed files changed on this branch vs master: WalkHandler.cs, WalkParseTests.cs (new); ClientSession.cs, MsgConnect.cs, PacketRouter.cs, CharacterRepository.cs, NetworkListener.cs, Program.cs (modified). Confirm no change to auth, crypto/handshake, GameConnection, SendGame, ActionHandler, or character creation.
  - **Verify**: `git diff --name-only master...HEAD | sort | tee /tmp/movement-diff.txt; ! grep -Eq 'Crypto|GameConnection|SendGame|ActionHandler|Auth|Register' /tmp/movement-diff.txt && echo SCOPE_PASS`
  - **Done when**: Diff lists only the ~8 allowed files; no forbidden file touched.
  - **Commit**: None

## Phase 4: Quality Gates + PR

NEVER push to protected master — PR only. Already on feat/movement.

- [ ] 4.1 [VERIFY] Full local CI gate
  - **Do**: `scripts/dotnet build src/Conquer.sln` then `scripts/dotnet test src/Conquer.sln`.
  - **Verify**: Build exits 0 with 0 warnings / 0 errors; tests exit 0. Both via `scripts/dotnet` (NEVER bare dotnet).
  - **Done when**: 0/0 build + all tests green.
  - **Commit**: `chore(movement): pass full local CI gate` (only if fixes needed)

- [ ] 4.2 Push branch + open PR to master with operator E2E checklist
  - **Do**: Confirm `git branch --show-current` == feat/movement (if on master STOP — should not happen). `git push -u origin feat/movement`. `gh pr create --base master --title "feat(movement): server-authoritative MsgWalk(1005)" --body "<summary + operator E2E checklist: walk in 5065 client → [Game] walk dir=N -> (x,y) logs; logoff at non-spawn tile → relog → spawn at last position (one UPDATE/session); malformed 1005 does not crash listener>"`. If gh unavailable, output the PR URL for manual creation.
  - **Files**: (none — git/gh only)
  - **Done when**: Branch pushed; PR open against master with E2E checklist in body.
  - **Verify**: `gh pr view --json url,baseRefName --jq '.baseRefName' | grep -q master && echo PR_PASS`
  - **Commit**: None

- [ ] 4.3 [VERIFY] CI pipeline
  - **Do**: Check CI status on the PR.
  - **Verify**: `gh pr checks 2>&1 | tee /tmp/movement-ci.txt; grep -q 'no checks reported\|no required checks' /tmp/movement-ci.txt && echo NO_CI_NOOP || gh pr checks --watch`
  - **Done when**: All checks green, OR no workflow exists → no-op (this repo has no CI workflow).
  - **Commit**: None

- [ ] 4.4 [VERIFY] M2 operator gate (final acceptance)
  - **Do**: Automated proxy (must pass before sign-off): confirm `case 1005` wired (`grep -q 'case 1005' src/Redux/PacketRouter.cs`), tests green (`scripts/dotnet test src/Conquer.sln`), build 0/0 (`scripts/dotnet build src/Conquer.sln`). Then operator live E2E on the dockerized server (192.168.0.252): walk in the real 5065 client → `[Game] walk dir=N -> (x,y)` logs appear (authoritative tracking); log off at a NON-spawn tile → relog → spawn at the LAST position, not the spawn tile (relog persists, one UPDATE/session); send a malformed/short 1005 → listener does NOT crash, session survives. Final = operator sign-off.
  - **Verify**: `grep -q 'case 1005' src/Redux/PacketRouter.cs && scripts/dotnet build src/Conquer.sln && scripts/dotnet test src/Conquer.sln && echo AUTOMATED_PROXY_PASS` (operator confirms live E2E + sign-off)
  - **Done when**: Automated proxy passes AND operator confirms walk-track + relog-at-last-position + malformed-1005-no-crash.
  - **Commit**: None

## Notes

- **POC shortcuts**: Phase 1 milestone proves tracking via LOGS only (no DB). No echo/confirm to the moving client — client prediction already moved the avatar (US-3 / out of scope).
- **Invariants (do not violate)**: NO echo/confirm packet on the walk path (AD-4, NFR-3); NO DB write per walk packet (NFR-1) — exactly one UPDATE/session on disconnect (AD-2, NFR-2); bounds rule = REJECT not clamp; `ActionHandler.HandleSetLocation` keeps reading `Character` as the spawn seed source (AC-4.2 regression — do NOT repoint to CurrentX/Y).
- **AD-1 (plain-fields choice)**: live position is four plain mutable fields on `ClientSession`, not a separate entity object — minimal-now, zero indirection/alloc on the hot path. The per-player-addressable seam is named for the FUTURE surroundings/entity-registry spec (global concurrent registry keyed by UID + spatial query); v1 does NOT build it.
- **AD-2 (disconnect-flush tradeoff)**: one UPDATE/session in `ServeGameAsync` finally. Hard crash/kill loses the session's unsaved walks (the finally never runs) — accepted for v1 (single player, manual E2E). Periodic flush = documented future hardening, reuses the same `UpdatePosition` contract.
- **LIVE-only unknowns** (operator-capture during 4.4, cannot be unit-tested): exact 1005 wire bytes vs a real 5065-client capture (confirm Direction@6 / Mode@7); whether the client needs any server confirm under prediction (recommend none — revert to `SendToScreen(...,true)` echo only if the avatar visibly desyncs); Direction→delta mapping vs what the 5065 client expects (the `{0,-1,-1,-1,0,1,1,1}`/`{1,1,0,-1,-1,-1,0,1}` table is the fork's own convention — verify a few directions visually).
- **VE note**: no automated E2E harness / dev-server proxy in this repo (dockerized server on a separate host, operator-driven). E2E is the manual M2 operator gate (4.4); the automated proxy in 4.4 substitutes for VE check tasks. No standalone VE startup/cleanup tasks apply.
