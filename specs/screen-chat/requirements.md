---
spec: screen-chat
basePath: specs/screen-chat
phase: requirements
epic: 2
updated: 2026-06-27
---

# Requirements: screen-chat (EPIC 2)

## Goal

Players send local ("screen") chat that everyone on their 3×3 screen sees, by handling
inbound `MsgTalk(1004)` `ChatType.Talk` and fanning a server-rebuilt 1004 over the
**existing EPIC-1 broadcast layer**. First chat consumer — proves the broadcast layer with
a 2nd packet type (after movement). Additive only.

## User Stories

### US-1: See nearby players' local chat
**As a** player on a map
**I want to** see local messages typed by other players standing on my screen
**So that** the world feels populated and players can communicate locally.

**Acceptance Criteria:**
- [ ] AC-1.1: **Given** two players co-located on the same screen (Map 1010, within the 3×3 cell block), **When** player A types a local message, **Then** player B sees A's message in their chat box.
- [ ] AC-1.2: **Given** A's message is broadcast, **When** B renders it, **Then** the displayed sender name is A's **character name** (trusted From = `PlayerEntity.Name`), regardless of any Speaker string A's client sent.
- [ ] AC-1.3: **Given** a player C **not** on A's screen (outside the 3×3 block), **When** A types a local message, **Then** C does **not** receive it.
- [ ] AC-1.4: **Given** the outbound 1004, **When** it is delivered, **Then** the channel = `ChatType.Talk`(2000), To = `"ALLUSERS"`, Emotion suffix = `""`, color = white (`0x00FFFFFF`).

### US-2: Send a local message
**As a** player
**I want to** type a local message and have it reach my screen
**So that** I can talk to players around me.

**Acceptance Criteria:**
- [ ] AC-2.1: **Given** a connected player with a resolved `PlayerEntity` (`session.WorldEntity`), **When** the server receives a `1004` with `ChatType.Talk` and a non-empty `Words` string, **Then** the server rebuilds the 1004 (trusted From) and broadcasts it once to the sender's 3×3 screen via `MapInstance.Broadcast`.
- [ ] AC-2.2: **Given** the `includeSelf` toggle = **false** (default, matches original), **When** A sends a message, **Then** the server does **not** echo the 1004 back to A (A's client self-displays). Operator confirms no double-render live (see US-6).
- [ ] AC-2.3: **Given** a player whose `session.WorldEntity` is not a `PlayerEntity` (not yet registered in the world), **When** a 1004 arrives, **Then** the handler returns without broadcasting (no crash).

### US-3: Reject/ignore malformed or unsupported chat without disconnecting
**As a** server operator
**I want** bad or out-of-scope chat input to be ignored, never to drop the connection
**So that** a malformed packet or unsupported channel can't crash or disconnect a client.

**Acceptance Criteria:**
- [ ] AC-3.1: **Given** a 1004 payload shorter than the minimum (`< 23` bytes), **When** received, **Then** the handler logs + returns; no broadcast, no disconnect.
- [ ] AC-3.2: **Given** any per-string length byte that would read past `payload.Length`, **When** walking the string-list, **Then** the handler returns; no read past bound (Power-of-10 Rule 7).
- [ ] AC-3.3: **Given** a 1004 whose channel is **not** `ChatType.Talk` (e.g. Whisper/Team/World), **When** received, **Then** it is a silent no-op (returns; documented future channel).
- [ ] AC-3.4: **Given** an **empty** message (after sanitize), **When** received, **Then** the handler returns; no blank message is broadcast.
- [ ] AC-3.5: **Given** a message whose first character is `/` (GM/chat command prefix), **When** received, **Then** the handler ignores it (returns; future GM commands).
- [ ] AC-3.6: **Given** ANY malformed/oversized/unsupported chat, **When** received, **Then** the connection is **never** disconnected — log + return (mirrors `WalkHandler`/`ActionHandler`).

### US-4: Trusted, sanitized, length-capped message
**As a** server operator
**I want** the broadcast message to use a server-trusted sender and sanitized content
**So that** clients cannot spoof identity or inject control characters.

**Acceptance Criteria:**
- [ ] AC-4.1: **Given** any inbound 1004, **When** rebuilding the outbound, **Then** From = the sender's `PlayerEntity.Name` (the client's Speaker string at index 0 is never trusted).
- [ ] AC-4.2: **Given** a message containing control characters (`< 0x20`), **When** sanitized, **Then** those characters are stripped before broadcast; the wire encoding is ASCII.
- [ ] AC-4.3: **Given** a message longer than the cap (255 wire cap; tighten if live capture shows a smaller client chatbox limit — see US-6), **When** received, **Then** the message is rejected or clamped to the cap before broadcast (never exceeds the `NetStringPacker` 255-byte per-string limit).

### US-5: Build the talk packet (testable, pure)
**As a** developer
**I want** a pure builder + parser covered by unit tests
**So that** the 1004 wire layout and inbound parse are verified without a socket.

**Acceptance Criteria:**
- [ ] AC-5.1: **Given** `MsgTalk.BuildChat(channel, from, to, message)`, **When** called, **Then** it emits a 1004 with string-list order **Speaker(from), Hearer(to), Emotion(""), Words(message)** (count=4), header length = `bodyLength + 8`, body = `24 + packer.Length`, channel u16 @ body offset 8, color `0x00FFFFFF` @ offset 4.
- [ ] AC-5.2: **Given** the existing `MsgTalk.Build(ChatType, words)` (shared with the NEW_ROLE/ANSWER_OK handshake), **When** `BuildChat` is added, **Then** `Build`'s signature and byte output are **unchanged** (no handshake regression).
- [ ] AC-5.3: **Given** the inbound parser, **When** fed a valid 1004 payload, **Then** it extracts index 3 (`Words`); **When** fed a short, oversized, or out-of-bound payload, **Then** it returns false/empty without throwing.
- [ ] AC-5.4: **Given** `ChatType` in `src/Packets/ChatType.cs`, **When** updated, **Then** `Talk = 2000` is present and existing values (Register=2100, Entrance=2101) are unchanged.

### US-6: Operator E2E verification (live capture of 2 unknowns)
**As an** operator
**I want** a two-client manual test
**So that** I confirm the broadcast end-to-end and resolve the self-echo + max-length unknowns.

**Acceptance Criteria:**
- [ ] AC-6.1: **Given** the server rebuilt with **both** compose files and two 5065 clients co-located on Map 1010, **When** client A types a local message, **Then** client B sees it in the chat box.
- [ ] AC-6.2: **Given** `includeSelf` default = false, **When** A sends a message, **Then** the operator confirms A sees the message **exactly once** (no double-render). If A sees it twice → keep `false`; if A sees nothing → flip to `true`. **(Operator-capture live-unknown #1.)**
- [ ] AC-6.3: **Given** A types a progressively longer message, **When** the client chatbox truncates or rejects, **Then** the operator records the effective max length to confirm/tighten the 255 cap. **(Operator-capture live-unknown #2.)**

## Functional Requirements

| ID | Requirement | Priority | Acceptance Criteria |
|----|-------------|----------|---------------------|
| FR-1 | Route inbound `1004`: add `_chat` field + `case 1004: _chat.Handle(session, payload)` to `PacketRouter.Dispatch` (mirror `_walk`). No `Program.cs` change (World already injected). | High | 1004 reaches `ChatHandler`; build stays 0/0 |
| FR-2 | New `ChatHandler` in `src/Packets` (World-injected, mirror `WalkHandler`): guard `payload.Length < 23` → resolve `session.WorldEntity as PlayerEntity` → channel == Talk → bounds-checked packer walk → sanitize/cap/reject-empty → ignore `/`-prefix → rebuild → broadcast. | High | US-2, US-3, US-4 ACs |
| FR-3 | Add `ChatType.Talk = 2000` to `src/Packets/ChatType.cs`; keep Register/Entrance unchanged. | High | AC-5.4 |
| FR-4 | Add `MsgTalk.BuildChat(ChatType channel, string from, string to, string message)` emitting `[from, to, "", message]` (count=4) at verified offsets. Keep existing `Build` byte-identical. | High | AC-5.1, AC-5.2 |
| FR-5 | Inbound string-list parser: bounded loop (`i < 8`), bounds-check each length byte vs `payload.Length`, read index 3 = `Words`. Pure/testable. | High | AC-3.2, AC-5.3 |
| FR-6 | Rebuild with **trusted From = `PlayerEntity.Name`**, To = `"ALLUSERS"`, suffix = `""`, color white. Never trust client Speaker. | High | AC-1.2, AC-4.1 |
| FR-7 | Broadcast the 1004 **once** to the sender's 3×3 screen via `MapInstance.Broadcast(e, talk, includeSelf)`; `includeSelf` = toggle, default **false**. Reuse EPIC-1 layer — no new fan-out code. | High | AC-1.1, AC-1.3, AC-2.1, AC-2.2 |
| FR-8 | Sanitize (strip control chars), reject empty, cap message ≤ 255 (tighten per capture), ignore `/`-prefixed, ignore non-Talk channels. Bad chat = log + return, never disconnect. | High | US-3, US-4 ACs |
| FR-9 | xUnit tests in `src/Packets.Tests`: `BuildChat` byte layout (order + header) + inbound parser (extract Words; reject short/oversize/out-of-bound). | High | AC-5.1, AC-5.3 |

## Non-Functional Requirements

| ID | Requirement | Metric | Target |
|----|-------------|--------|--------|
| NFR-1 | Build-once fan-out (reuse `MapInstance.Broadcast`) | Packet builds per message | 1 build, O(N·k) sends; no new fan-out code |
| NFR-2 | Pure in-memory, no persistence | New persisted structures | 0 (reuses existing registry/grid; chat ephemeral) |
| NFR-3 | Validate all wire input (Rule 7) | Coverage | length guard + per-string bound + bounded loop (Rule 2) + cap + control-char strip + reject empty |
| NFR-4 | Strict build gate | Warnings/errors | 0/0; nullable-clean; analyzers as errors |
| NFR-5 | No `unsafe` in new code (Rule 9) | Pointer use | 0 — use `Span<T>` + `BinaryPrimitives` |
| NFR-6 | No hot-path regression (Rule 3) | New allocations | one shared packet per message; no per-recipient build |
| NFR-7 | Small, guard-first functions (Rules 1/4/5) | Method size | ~≤60 lines; early-return guards |
| NFR-8 | No handshake regression | `MsgTalk.Build` bytes | byte-identical (shared with ANSWER_OK/NEW_ROLE) |

## Glossary

- **MsgTalk (1004)**: CO chat packet. Header (len u16@0, type u16@2), Color u32@4, ChatType u16@8, Unknown0 u16@10, Time u32@12, HearerLookface u32@16, SpeakerLookface u32@20, StringList@24. Net8 payload offset = body − 2 (router strips the 2-byte length prefix); StringList @ payload offset 22; min payload = 23.
- **ChatType.Talk (2000)**: the local/"screen" chat channel. The only channel handled in v1.
- **String-list (NetStringPacker)**: `[u8 count][u8 len][ASCII]…`. Order: 0=Speaker(From), 1=Hearer(To), 2=Emotion(suffix), 3=Words(message). count=4 for v1. Per-string cap 255.
- **Screen broadcast**: fan-out to the 3×3 cell block (18-tile radius screen) around the sender via the EPIC-1 `MapInstance.Broadcast` / `QueryScreen`. Reused unchanged.
- **includeSelf toggle**: `Broadcast`'s 3rd arg. Default **false** (matches original — client self-displays). Operator confirms live (double-render test).
- **Trusted From**: the broadcast sender name = `PlayerEntity.Name` (server-owned), never the client-sent Speaker string (anti-spoof).

## Out of Scope (explicit)

- Whisper/private chat (2001), Team (2003), Guild/Syndicate (2004), Friend (2009), World/Broadcast (2021/2500), System.
- GM / chat commands (`/`-prefixed) — ignored in v1.
- Profanity filter.
- Chat history / persistence (chat is ephemeral).
- **Rate-limiting / anti-spam** — explicit **future hardening** (note, not v1).
- Honoring client-chosen chat color (server emits white).
- Cross-map / cross-screen chat. Only local 3×3 screen.

## Dependencies

- **EPIC-1 world layer (DONE, live-verified):** `World.GetOrAdd(mapId)`, `MapInstance.Broadcast(PlayerEntity, byte[], bool)`, `MapInstance.QueryScreen`, `PlayerEntity.Name` / `.MapId` / `.CellX/CellY`, `session.WorldEntity`. Consumed read-only — must NOT change.
- `src/Packets/ChatType.cs` (add `Talk=2000`), `src/Packets/MsgTalk.cs` (add `BuildChat`, keep `Build`), `src/Packets/NetStringPacker.cs`, `src/Packets/PacketBuilder.cs`.
- `src/Redux/PacketRouter.cs` Dispatch (add `case 1004:` + `_chat` field; mirror `_walk`).
- Dockerized build/test: `scripts/dotnet build|test src/Conquer.sln`. Server rebuild uses **both** compose files. New xUnit in `src/Packets.Tests`.

### MUST NOT change
Auth/crypto/handshake, `GameConnection`, enter-world, char-creation, movement, the surroundings broadcast internals. Only **call** `MapInstance.Broadcast`. Keep `MsgTalk.Build` byte-identical. No `Program.cs` change.

## Success Criteria

- Two co-located 5065 clients: A types local chat → B sees it in the chat box (AC-6.1).
- Self-echo confirmed: A sees the message exactly once per `includeSelf` setting (AC-6.2).
- xUnit green for `BuildChat` layout + inbound parser; `scripts/dotnet build|test src/Conquer.sln` stays **0/0**.
- No regression in the character-select handshake (NFR-8) or any EPIC-1 behavior.

## Unresolved Questions

- **Self-echo (live-unknown #1):** does the 5065 client self-display local Talk (server must NOT echo, `includeSelf:false`) or render only on server echo (`includeSelf:true`)? Default false; operator-capture via AC-6.2.
- **Client max chat length (live-unknown #2):** wire cap is 255; the exact 5065 chatbox limit is unknown. Use 255 until AC-6.3 capture shows a tighter bound.

## Next Steps

1. Approve requirements.
2. Proceed to design phase (`/ralph-specum:design`): detail `ChatHandler` shape, `BuildChat` offsets, parser bounds, router wiring, test cases.
3. Tasks → implement M1 (parse + BuildChat, self-echo proof) then M2 (3×3 screen fan-out).
4. Operator E2E to resolve the 2 live-unknowns; set `includeSelf` + final cap.
