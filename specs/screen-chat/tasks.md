---
spec: screen-chat
basePath: specs/screen-chat
phase: tasks
epic: 2
updated: 2026-06-27
---

# Tasks: screen-chat (EPIC 2)

Total: 12 tasks — Phase 1 (POC, 6) · Phase 2 (tests + scope, 3) · Phase 3 (CI + operator gate, 3).
POC-first. Additive: reuses EPIC-1 `MapInstance.Broadcast`. Branch `feat/chat` (stacked on `feat/surroundings`, checked out).
STRICT GATE — every [VERIFY] = `scripts/dotnet build src/Conquer.sln` (0 warn/0 err) + `scripts/dotnet test src/Conquer.sln` (green). NEVER bare `dotnet`.
One commit per task; message body ends with a blank line then `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.

## Phase 1: Make It Work (POC)

Focus: a typed local message fans out to the sender's 3×3 screen. ChatType.Talk + BuildChat + ChatHandler + router wiring.

- [ ] 1.1 [P] Add `ChatType.Talk = 2000`
  - **Do**: In `src/Packets/ChatType.cs`, add `Talk = 2000` to the enum. KEEP `Register = 2100`, `Entrance = 2101` unchanged. Update the xml-doc to note Talk is the local channel.
  - **Files**: `src/Packets/ChatType.cs`
  - **Done when**: Enum exposes `Talk=2000, Register=2100, Entrance=2101`; nothing else changed.
  - **Verify**: `grep -q 'Talk = 2000' src/Packets/ChatType.cs && grep -q 'Register = 2100' src/Packets/ChatType.cs && grep -q 'Entrance = 2101' src/Packets/ChatType.cs && echo PASS`
  - **Commit**: `feat(chat): add ChatType.Talk=2000`
  - _Requirements: FR-3, AC-5.4_
  - _Design: ChatType.Talk_

- [ ] 1.2 [P] Add `MsgTalk.BuildChat` overload (keep `Build` byte-identical)
  - **Do**: In `src/Packets/MsgTalk.cs`, add `public static byte[] BuildChat(ChatType channel, string from, string to, string message)`: `new NetStringPacker(from, to, string.Empty, message)` (count=4); `bodyLength = 24 + packer.Length`; `AppendHeader(span, (ushort)(bodyLength + 8), MsgTalkType)`; Color u32@4 = `DefaultColor`; channel u16@8; Unknown0 u16@10=0; Time u32@12=0; HearerLookface u32@16=0; SpeakerLookface u32@20=0; `packer.Write(span.Slice(24))`. DO NOT touch the existing `Build` method (shared with the handshake).
  - **Files**: `src/Packets/MsgTalk.cs`
  - **Done when**: `BuildChat` compiles at the verified offsets; `Build` source bytes unchanged.
  - **Verify**: `grep -q 'BuildChat(ChatType' src/Packets/MsgTalk.cs && grep -q 'NetStringPacker(from, to, string.Empty, message)' src/Packets/MsgTalk.cs && echo PASS`
  - **Commit**: `feat(chat): add MsgTalk.BuildChat overload`
  - _Requirements: FR-4, AC-5.1, AC-5.2_
  - _Design: MsgTalk.BuildChat_

- [ ] 1.3 Create `ChatHandler` (parse + validate + build + broadcast)
  - **Do**: Create `src/Packets/ChatHandler.cs`, namespace `Conquer.Packets`, mirror `WalkHandler`. Inject `Conquer.World.World _world` via ctor. `Handle(ClientSession session, byte[] payload)`: guard `payload.Length < 23 → return`; `session.WorldEntity is not Conquer.World.PlayerEntity e → return`; read `channel = ReadUInt16LE(payload @6)`, `channel != (ushort)ChatType.Talk → return`; `TryReadMessage(payload, out raw)` false → return; `message = Sanitize(raw)` (drop chars `< 0x20`, ASCII); empty → return; `message[0]=='/'` → return; cap `message[..255]` if longer; `byte[] talk = MsgTalk.BuildChat(ChatType.Talk, e.Name, "ALLUSERS", message)`; `_world.GetOrAdd(e.MapId).Broadcast(e, talk, includeSelf: false)`. Add static `public static bool TryReadMessage(byte[] p, out string msg)` walking the string-list @ `p[22]`: `count=p[o++]`, loop `i<count && i<8`, bound the `[len]` byte and `o+len > p.Length → false`, return index-3 (`Words`); else false. Never disconnect.
  - **Files**: `src/Packets/ChatHandler.cs`
  - **Done when**: File compiles; `Handle` + static `TryReadMessage` + `Sanitize` present; never throws on bad input.
  - **Verify**: `grep -q 'public static bool TryReadMessage' src/Packets/ChatHandler.cs && grep -q 'includeSelf: false' src/Packets/ChatHandler.cs && grep -q 'BuildChat(ChatType.Talk, e.Name' src/Packets/ChatHandler.cs && echo PASS`
  - **Commit**: `feat(chat): add World-injected ChatHandler`
  - _Requirements: FR-2, FR-5, FR-6, FR-7, FR-8, AC-2.1, AC-2.3, AC-3.1, AC-3.2, AC-3.3, AC-3.4, AC-3.5, AC-3.6, AC-4.1, AC-4.2, AC-4.3_
  - _Design: ChatHandler_

- [ ] 1.4 Wire `case 1004` in `PacketRouter`
  - **Do**: In `src/Redux/PacketRouter.cs`: add `private readonly Conquer.Packets.ChatHandler _chat;` field; in the ctor (World already a param) add `_chat = new Conquer.Packets.ChatHandler(world);`; in `Dispatch` add `case 1004: _chat.Handle(session, payload); break;` (mirror `_walk` case 1005). NO `Program.cs` change.
  - **Files**: `src/Redux/PacketRouter.cs`
  - **Done when**: 1004 routes to `ChatHandler.Handle`; mirrors the `_walk` wiring.
  - **Verify**: `grep -q 'ChatHandler _chat' src/Redux/PacketRouter.cs && grep -q 'new Conquer.Packets.ChatHandler(world)' src/Redux/PacketRouter.cs && grep -q 'case 1004:' src/Redux/PacketRouter.cs && echo PASS`
  - **Commit**: `feat(chat): route 1004 to ChatHandler`
  - _Requirements: FR-1_
  - _Design: PacketRouter wiring_

- [ ] 1.5 [VERIFY] Build gate: chat path compiles 0/0
  - **Do**: `scripts/dotnet build src/Conquer.sln`. Fix any warning/error in the chat files only.
  - **Verify**: `scripts/dotnet build src/Conquer.sln 2>&1 | tail -5` shows `0 Warning(s)` / `0 Error(s)`.
  - **Done when**: Build is 0 warnings / 0 errors with all four chat changes present.
  - **Commit**: `chore(chat): pass build gate` (only if fixes needed)

- [ ] 1.6 POC checkpoint: 1004 wired end-to-end
  - **Do**: Confirm the inbound→broadcast path is statically wired: `case 1004` dispatches to `ChatHandler.Handle`, which calls `MsgTalk.BuildChat` once and `MapInstance.Broadcast(e, talk, includeSelf:false)`. (Live two-client fan-out is the Phase-3 operator gate; here prove the code path is complete and builds.)
  - **Verify**: `grep -q 'case 1004' src/Redux/PacketRouter.cs && grep -q 'Broadcast(e, talk' src/Packets/ChatHandler.cs && scripts/dotnet build src/Conquer.sln 2>&1 | grep -qE '0 Error' && echo POC_PASS`
  - **Done when**: Full inbound path present and compiling; ready for tests.
  - **Commit**: `feat(chat): complete POC — 1004 fans to screen`

## Phase 2: Testing + Scope Guard

Focus: pure xUnit for the builder layout + the bounded inbound parser; assert `Build` byte-identical + additive-only diff.

- [ ] 2.1 [P] `MsgTalkBuildChatTests` — BuildChat layout + `Build` regression
  - **Do**: Create `src/Packets.Tests/MsgTalkBuildChatTests.cs` (namespace `Conquer.Packets.Tests`, xUnit). Build via `MsgTalk.BuildChat(ChatType.Talk, "Alice", "ALLUSERS", "hi")` and assert: type id u16@2 == 1004; channel u16@8 == 2000; color u32@4 == `0x00FFFFFF`; header length u16@0 == `bodyLength` (= `buffer.Length - 0`... i.e. equals `24 + packer.Length`); string-list @24 = `[count=4][len][ascii]…` parsing back to `["Alice","ALLUSERS","","hi"]`. Add a regression test: capture `MsgTalk.Build(ChatType.Entrance, "ANSWER_OK")` bytes and assert the exact known byte array is unchanged (handshake byte-identical, NFR-8/AC-5.2).
  - **Files**: `src/Packets.Tests/MsgTalkBuildChatTests.cs`
  - **Done when**: Tests assert BuildChat offsets/order + count=4 AND `Build` regression bytes.
  - **Verify**: `scripts/dotnet test src/Conquer.sln --filter MsgTalkBuildChatTests 2>&1 | grep -qE 'Passed!|Passed:' && echo PASS`
  - **Commit**: `test(chat): BuildChat byte layout + Build regression`
  - _Requirements: FR-9, AC-5.1, AC-5.2_
  - _Design: Test Strategy — MsgTalkBuildChatTests_

- [ ] 2.2 [P] `ChatParseTests` — bounded inbound parser
  - **Do**: Create `src/Packets.Tests/ChatParseTests.cs` (xUnit). Build synthetic 1004 payloads with `BinaryPrimitives` (type@0=1004, channel@6, string-list@22). Assert: valid 4-string payload → `TryReadMessage` returns index-3 `Words`; short payload (`<23`) → false, no throw; per-string `[len]` running past `payload.Length` → false, no over-read; `count` < 4 (no Words index) → false; pathological `count=255` does not over-iterate (capped `i<8`), no throw; `Handle(null!, shortPayload)` → no throw (length guard first, mirror `WalkParseTests`). Channel/`/`/empty rejection are covered in `Handle` (non-Talk and `/`-prefix → no broadcast) — assert via the static parser + the guard ordering where socket-free.
  - **Files**: `src/Packets.Tests/ChatParseTests.cs`
  - **Done when**: All inbound-parser bound/reject cases green, no throws.
  - **Verify**: `scripts/dotnet test src/Conquer.sln --filter ChatParseTests 2>&1 | grep -qE 'Passed!|Passed:' && echo PASS`
  - **Commit**: `test(chat): bounded inbound parser tests`
  - _Requirements: FR-5, FR-9, AC-3.2, AC-5.3_
  - _Design: Test Strategy — ChatParseTests_

- [ ] 2.3 [VERIFY] Full suite + additive-scope diff
  - **Do**: `scripts/dotnet build src/Conquer.sln` (0/0) + `scripts/dotnet test src/Conquer.sln` (green). Then confirm the diff is additive: only `ChatType.cs`, `MsgTalk.cs`, `ChatHandler.cs`, `PacketRouter.cs`, and the two new test files changed — and `MsgTalk.Build` is byte-identical (its regression test passes; its source lines unchanged).
  - **Verify**: `scripts/dotnet build src/Conquer.sln 2>&1 | grep -qE '0 Error' && scripts/dotnet test src/Conquer.sln 2>&1 | grep -qE 'Passed!|Passed:' && git diff --name-only origin/feat/surroundings...HEAD -- 'src/**/*.cs' | grep -vqE 'ChatType\.cs|MsgTalk\.cs|ChatHandler\.cs|PacketRouter\.cs|MsgTalkBuildChatTests\.cs|ChatParseTests\.cs' && echo UNEXPECTED_FILE || echo PASS`
  - **Done when**: Build 0/0, all tests green, no non-chat `.cs` touched, `Build` regression test passes.
  - **Commit**: `chore(chat): pass full suite + scope guard` (only if fixes needed)

## Phase 3: Quality Gate + PR + Operator E2E

NEVER push to master directly via raw git on a default branch — already on `feat/chat`. PR target = master.

- [ ] 3.1 [VERIFY] Full local CI gate (0/0 + tests)
  - **Do**: Run the complete local gate: `scripts/dotnet build src/Conquer.sln` then `scripts/dotnet test src/Conquer.sln`. Fix any failure in the chat files only.
  - **Verify**: `scripts/dotnet build src/Conquer.sln 2>&1 | grep -qE '0 Warning' && scripts/dotnet build src/Conquer.sln 2>&1 | grep -qE '0 Error' && scripts/dotnet test src/Conquer.sln 2>&1 | grep -qE 'Passed!|Passed:' && echo CI_PASS`
  - **Done when**: 0 warnings / 0 errors / all tests pass.
  - **Commit**: `chore(chat): pass local CI gate` (only if fixes needed)

- [ ] 3.2 Push `feat/chat` + open PR to master (operator E2E checklist)
  - **Do**:
    1. Confirm branch: `git -C /Users/vitor/conquer-server branch --show-current` == `feat/chat` (stacked on `feat/surroundings`). If on master, STOP — should not happen.
    2. Push: `git push -u origin feat/chat`.
    3. `gh pr create --base master --head feat/chat --title "feat(chat): screen-local chat (EPIC 2)"` with a body summarizing the additive change AND the operator E2E checklist: (a) two 5065 clients co-located on Map 1010 same screen; (b) A types local message → B sees A's name + message (AC-6.1); (c) self-echo — `includeSelf=false`, confirm A sees it exactly once, flip to `true` if A sees nothing (AC-6.2); (d) max-length — type progressively longer, record chatbox truncation point to confirm/tighten the 255 cap (AC-6.3); (e) rebuild server with BOTH compose files: `docker compose -f src/docker-compose.yml -f src/docker-compose.override.yml up -d --build`.
  - **Verify**: `gh pr view --json url,baseRefName 2>&1 | grep -q '"baseRefName": "master"' && echo PASS`
  - **Done when**: PR open against master with the operator E2E checklist in the body.
  - **Commit**: None

- [ ] 3.3 [VERIFY] CI no-op + M2 operator gate (automated proxy)
  - **Do**:
    1. CI: `gh pr checks --watch` (or `gh pr checks`) — all green.
    2. M2 automated proxy (operator E2E is human-run from the 3.2 checklist; the automatable gate here): assert `case 1004` is wired AND the full suite is green — proving inbound chat reaches `ChatHandler` and the build/parse logic is verified. (Two-client self-echo / max-length capture remain operator-captured live-unknowns recorded on the PR.)
  - **Verify**: `gh pr checks 2>&1 | grep -viqE 'fail|error' && grep -q 'case 1004' src/Redux/PacketRouter.cs && scripts/dotnet test src/Conquer.sln 2>&1 | grep -qE 'Passed!|Passed:' && echo M2_PASS`
  - **Done when**: CI green, 1004 wired, suite green. Operator records self-echo + max-length on the PR.
  - **Commit**: None

## Notes

- **Reuses EPIC-1 broadcast 1:1** — `MapInstance.Broadcast(center, packet, includeSelf)` over `QueryScreen`. No new fan-out; build-once `BuildChat`, O(N·k) sends (NFR-1/NFR-6).
- **`MsgTalk.Build` byte-identical guard** — task 2.1 captures `Build(Entrance, "ANSWER_OK")` bytes and asserts unchanged (NFR-8 / AC-5.2; shared with the ANSWER_OK/NEW_ROLE handshake). `BuildChat` is a parameterized clone; `Build` source untouched.
- **2 live-unknowns (operator-capture, non-blocking):** (1) self-echo — `includeSelf` default `false` (matches original `Player.SendToScreen` `_self:false`); flip to `true` only if A sees nothing live (AC-6.2). (2) client max chat length — wire cap 255; tighten per live chatbox truncation (AC-6.3). Both resolved on the PR via the operator checklist, not in code.
- **POC shortcuts:** `includeSelf:false` + cap 255 hardcoded pending live capture. No rate-limit/anti-spam (explicit future hardening). `/`-prefixed + non-Talk channels silently ignored (future GM/whisper).
- **Additive scope:** only `ChatType.cs`, `MsgTalk.cs`, `ChatHandler.cs`, `PacketRouter.cs` + 2 test files. No `Program.cs`, no auth/crypto/handshake/world-internals change — only CALL `Broadcast`.
