# Requirements: character-select

## Goal

Get a real CO 5065 client past the "loading" hang to the **character-selection screen** (seeded char renders + is selectable) by implementing the server-first DH key exchange + Blowfish-CFB64 game cipher + MsgConnect(1052) char flow on the game port (:5816). Entering the world is out of scope.

## User Stories

### US-1: Server sends DH server-key packet on game connect
**As a** 5065 client opening the game socket (:5816)
**I want to** receive the server's DH server-key packet immediately on TCP connect
**So that** I can derive the Blowfish session key instead of waiting forever ("loading")

**Acceptance Criteria:**
- [ ] AC-1.1: **Given** a TCP connection to :5816, **when** it is accepted, **then** the server sends the server-key packet before reading any client bytes (server-first), Blowfish-encrypted with the initial key `"DR654dt34trg4UI6"`.
- [ ] AC-1.2: **Given** the server-key packet, **then** its byte layout matches commit `0b094c6` `ServerKeyExchange.GeneratePacket` exactly (offsets DERIVED from field order — there are 7 four-byte ints = 28 bytes overhead, and `size = 28 + PAD(11) + JUNK(12) + clientIV(8) + serverIV(8) + P(128) + G(2) + pubKeyLen + 8`): pad@0(11), `size-PAD`@11(int), junkLen@15(int=12), junk@19(12), clientIVlen@31(int=8), clientIV@35(8 zeros), serverIVlen@43(int=8), serverIV@47(8 zeros), P.Len@55(int=128), P@59(128 ASCII hex), G.Len@187(int=2), G@191("05"), pubKeyLen@193(int), pubKey@197(ASCII hex), `"TQServer"` trailer@end(8).
- [ ] AC-1.3: **Given** P/G/public-key fields, **then** they are sent as **uppercase ASCII hex strings** (matching original `ToHexString`), not raw bytes; P = the 512-bit constant from research; G = `"05"`.
- [ ] AC-1.4: **Given** the packet is generated, **then** a diagnostic log line confirms "server-key packet sent" with its length.

### US-2: Server parses client public key and derives shared secret
**As a** game server mid-handshake
**I want to** parse the client's reply packet and compute the DH shared secret
**So that** subsequent packets use the negotiated Blowfish key

**Acceptance Criteria:**
- [ ] AC-2.1: **Given** the first inbound buffer on a game connection in state `AwaitingClientKey`, **then** it is Blowfish-decrypted with the initial key, then parsed via the original `CompleteExchange` framing: `length@7`, `junk@11`, `pubKeyLen@(15+junk)`, `pubKey@(19+junk)` (ASCII hex string).
- [ ] AC-2.2: **Given** the client public key, **then** the shared secret = `DHBasicAgreement.CalculateAgreement(clientPubKey)` over `DHParameters(P,G)`, and `.ToByteArrayUnsigned()` becomes the Blowfish key.
- [ ] AC-2.3: **Given** the secret is derived, **then** both cipher directions swap from the initial key to the derived key (`SetKey`), and connection state advances to `Established`.
- [ ] AC-2.4: **Given** derivation completes, **then** a diagnostic log line confirms "CompleteExchange derived key".
- [ ] AC-2.5: **Given** a malformed/short client key buffer, **then** the connection is closed with a logged error (no crash of the listener).

### US-3: Blowfish-CFB64 game cipher byte-compatible with original OpenSSL
**As a** game connection
**I want to** encrypt/decrypt with Blowfish-CFB64 producing the same bytes as the original ManagedOpenSsl cipher
**So that** the real client can read server packets and the server can read client packets

**Acceptance Criteria:**
- [ ] AC-3.1: **Given** the game cipher, **then** it uses Blowfish in **CFB-64** mode (`CfbBlockCipher(BlowfishEngine, 64)`) via BouncyCastle.
- [ ] AC-3.2: **Given** two directions, **then** encrypt (server→client) and decrypt (client→server) each keep a **separate** persistent cipher instance and **separate** 8-byte IV; instances are reused across packets (no re-init per packet).
- [ ] AC-3.3: **Given** the handshake, **then** both IVs start zeroed (as sent in the server-key packet).
- [ ] AC-3.4: **Given** the cipher, **then** the initial key is ASCII `"DR654dt34trg4UI6"` (16 bytes) until `SetKey(sharedSecret)` swaps it post-exchange.
- [ ] AC-3.5 (correctness, critical): **Given** a known-answer test vector for OpenSSL Blowfish-CFB64, **when** the managed cipher processes it byte-by-byte, **then** output bytes match the OpenSSL reference exactly.

### US-4: Game-stream framing handled separately from auth path
**As a** game connection
**I want to** frame packets with the 8-byte "TQServer" seal (outbound) and `size = body + 8` (inbound)
**So that** game packets are split correctly without reusing the auth TQCipher 2-byte-prefix logic

**Acceptance Criteria:**
- [ ] AC-4.1: **Given** an outbound game packet (post-`Established`), **then** its last 8 bytes are `Common.SERVER_SEAL` (`"TQServer"`) and builders allocate `len + 8`.
- [ ] AC-4.2: **Given** an inbound game buffer (post-`Established`), **then** it is Blowfish-decrypted, then split by `ushort` body length with `+ 8` for the seal — NOT via `PacketRouter.ReadPacket`'s 2-byte-prefix/TQCipher stream logic.
- [ ] AC-4.3: **Given** any connection, **then** the server distinguishes game (:5816, Blowfish, server-first) from auth (:9958, TQCipher) by listener/port, and applies the correct cipher + framing.
- [ ] AC-4.4: **Given** the auth path, **then** it is unchanged (TQCipher, 2-byte prefix, :9958 still authenticates) — this work is additive to the game path only.

### US-5: MsgConnect(1052) handled under the derived Blowfish cipher
**As a** game server receiving MsgConnect(1052) after handshake
**I want to** validate the auth token and look up the character by account
**So that** I can decide char-select vs creation

**Acceptance Criteria:**
- [ ] AC-5.1: **Given** an `Established` game connection, **then** MsgConnect(1052) is decrypted under the **derived** Blowfish key and dispatched to the game handler.
- [ ] AC-5.2: **Given** the token in MsgConnect, **then** it is validated via `TokenStore.TryConsume` (the 8-byte token set during auth `MsgConnectEx`); valid → session marked authenticated.
- [ ] AC-5.3: **Given** an invalid/already-consumed token, **then** the connection is rejected and logged (no char data sent).
- [ ] AC-5.4: **Given** a valid token, **then** the server looks up the character by `AccountId` via the character repository.
- [ ] AC-5.5: **Given** MsgConnect decodes, **then** a diagnostic `[Game] Connect ...` log line is emitted.

### US-6: Seeded char reaches the character-select screen
**As a** 5065 client with a seeded character (account `vitor` / AccountID 2 / char `Vitor`)
**I want to** receive ANSWER_OK + HeroInformation(1006)
**So that** the loading screen clears and the character renders on the selection screen

**Acceptance Criteria:**
- [ ] AC-6.1: **Given** a valid token and an existing character, **then** the server sends `MsgTalk(ChatType.Entrance, "ANSWER_OK")` followed by `HeroInformation(1006)`.
- [ ] AC-6.2: **Given** packet 1006, **then** it uses the **original** `[1006]HeroInformation.cs` layout (offsets: Id@4, Lookface@8, Hair@12, Money@14, CP@18, Exp@22(u64), Str@50, Agi@52, Vit@54, Spi@56, Stats@58, Life@60, Mana@62, PK@64, Lvl@66, Class@67, Reborn@69, ShowName@70, NetStringPacker@71 = [Name, Spouse], total `71 + strings + 8`) — NOT the guessed `MsgUserInfo.Build`.
- [ ] AC-6.3: **Given** `DbCharacter` lacks Lookface/CP/Exp/Class/Spouse, **then** fields map as: `Mesh`→Lookface, `Avatar`→Hair, `Silver`→Money; CP/Exp/Class/Spouse defaulted for POC; Name/Level/stats from DB.
- [ ] AC-6.4 (operator-verified, out of CI): **Given** the real Windows 5065 client pointed at 192.168.0.252, **when** it logs in to the seeded account, **then** it leaves the "loading" state and displays the selectable seeded character `Vitor`.

### US-7: No character → NEW_ROLE (no create handler)
**As a** game server when no character exists for the account
**I want to** emit NEW_ROLE without building a creation flow
**So that** the absent-char path is signalled but creation stays out of scope

**Acceptance Criteria:**
- [ ] AC-7.1: **Given** a valid token and no character, **then** the server sends `MsgTalk(ChatType.Entrance, "NEW_ROLE")`.
- [ ] AC-7.2: **Given** the NEW_ROLE path, **then** NO MsgUserCreate / Register(1001) handler is implemented (out of scope; layout documented in research only).

## Functional Requirements

| ID | Requirement | Priority | Acceptance Criteria |
|----|-------------|----------|---------------------|
| FR-1 | Server-first DH server-key packet on game connect (exact `0b094c6` layout, hex strings, "TQServer" trailer) | High | US-1 |
| FR-2 | Parse client public-key packet + derive DH shared secret (BouncyCastle `DHBasicAgreement`) | High | US-2 |
| FR-3 | Blowfish-CFB64 cipher: BouncyCastle `BlowfishEngine`+`CfbBlockCipher(64)`, two persistent instances, separate IVs, initial key then swap | High | US-3 |
| FR-4 | Game cipher + exchange live in `Crypto.csproj` (Conquer.Crypto) next to TQCipher | High | US-3 |
| FR-5 | Game-stream framing: 8-byte seal outbound, `body+8` inbound, separate from auth ReadPacket | High | US-4 |
| FR-6 | Game vs auth connection distinguished by listener/port; correct cipher applied | High | US-4 |
| FR-7 | Auth path (TQCipher, :9958) left untouched and working | High | AC-4.4 |
| FR-8 | Game connection state machine: `AwaitingClientKey` → `Established` (server-first send, route first reply to CompleteExchange) | High | US-1, US-2 |
| FR-9 | MsgConnect(1052) under derived cipher: token validate via TokenStore, lookup char by AccountId | High | US-5 |
| FR-10 | Char present → ANSWER_OK + HeroInformation(1006) original layout w/ field mapping | High | US-6 |
| FR-11 | Char absent → NEW_ROLE (no create handler) | Medium | US-7 |
| FR-12 | Diagnostic logs at each handshake stage (key sent, key derived, [Game] Connect, ANSWER_OK/1006 sent) | Medium | AC-1.4, AC-2.4, AC-5.5 |
| FR-13 | Blowfish-CFB64 known-answer + round-trip unit test (vs OpenSSL output) | High | AC-3.5 |
| FR-14 | DH exchange round-trip unit test (client/server agree on same secret) | High | US-2 |

## Non-Functional Requirements

| ID | Requirement | Metric | Target |
|----|-------------|--------|--------|
| NFR-1 | Crypto correctness (primary risk) | Byte equality vs OpenSSL Blowfish-CFB64 KAT | 100% match |
| NFR-2 | Auth-path regression | Auth login after change | Still succeeds (manual + existing flow) |
| NFR-3 | Listener robustness | Malformed game packet / bad key | Connection closed + logged; listener survives |
| NFR-4 | Build/test environment | Dockerized via `scripts/dotnet` (no local SDK) | `scripts/dotnet build src/Conquer.sln` + `scripts/dotnet test ...` pass |
| NFR-5 | No native dependencies | DLLs in build output | Managed-only (BouncyCastle); no ManagedOpenSsl |
| NFR-6 | Observability for manual verification | Handshake stage log lines | Each stage emits a distinguishable log line |

## Glossary

- **5065**: CO (Conquer Online) client patch/version this server targets.
- **DH exchange (server-first)**: Diffie-Hellman where the server sends the first (server-key) packet on connect; client replies with its public key.
- **Blowfish-CFB64**: Blowfish block cipher in 64-bit Cipher Feedback (stream-style) mode; the 5065 game cipher.
- **Initial key**: `Common.ENCRYPTION_KEY` = ASCII `"DR654dt34trg4UI6"`, used to encrypt the server-key packet before the shared secret exists.
- **Shared secret / derived key**: DH output bytes; becomes the Blowfish key for all post-exchange packets.
- **TQServer seal**: `Common.SERVER_SEAL` = `"TQServer"` (8 bytes); appended to every outbound game packet post-handshake; inbound size = body + 8.
- **MsgConnect(1052)**: Game-port packet carrying the auth token; gates the char flow.
- **HeroInformation(1006)**: Packet that conveys the character's data; renders the char on the selection screen (no map needed).
- **ANSWER_OK / NEW_ROLE**: `MsgTalk(ChatType.Entrance, ...)` trigger strings for char-select vs creation screens.
- **TokenStore**: Net8 store of the 8-byte auth token issued during auth `MsgConnectEx`; consumed by MsgConnect.
- **TQCipher**: The auth-path cipher (:9958, 2-byte-prefix stream); unrelated to the game cipher.
- **DbCharacter**: Net8 character row; fields: CharacterID, AccountID, Name, Mesh, Avatar, Level, MapID, X, Y, Silver, Strength, Agility, Vitality, Spirit, HealthPoints, ManaPoints.

## Out of Scope

- **Entering the game world** (map/DMAP loading, spawn, movement, visibility, post-1006 Populate follow-ups). Future work (research milestone 3).
- **Character creation** — MsgUserCreate / Register(1001) handler. Server may emit NEW_ROLE but builds no create flow (char is seeded).
- **Multi-character list packet** — 5065 is one-account/one-char; not applicable.
- **Resurrecting the original 2243-line GameServer** — chosen approach is Option A (port into net8 stack).
- **Automated end-to-end game test** against the real client — verification is manual/operator-driven.
- **Combat, monsters, items, friends, ServerTime and other world managers.**

## Dependencies

- **BouncyCastle NuGet** added to `Crypto.csproj` (Conquer.Crypto) — provides `BlowfishEngine`, `CfbBlockCipher`, `DHBasicAgreement`, `DHParameters`.
- **Auth fixes on branch `fix/auth-cipher`** (TQCipher whole-stream + offset fixes + TokenStore) — this builds on them and consumes the token.
- **Seeded DB state**: account `vitor` / `test123` / AccountID 2, character `Vitor` (CharacterID 1, Mesh 1003, Level 1, MapID 1010).
- **Original protocol source in git history** — commit `0b094c6` `BlowfishExchange.cs` + `GameCryptographer.cs`; original `[1006]HeroInformation.cs`.
- **Build/test dockerized** via `scripts/dotnet` (no local .NET SDK); build target `Conquer.sln`.
- **Runtime**: server runs on 192.168.0.252; ports auth 9958 / game 5816; real Windows 5065 client for manual verification.

## Success Criteria

- DH exchange completes: server-key packet sent, client public key parsed, shared secret derived (logged).
- MsgConnect(1052) decodes under the derived Blowfish key (`[Game] Connect ...` logged).
- For the seeded char: ANSWER_OK + HeroInformation(1006) sent.
- **Definitive (operator-verified, out of CI)**: the real Windows 5065 client leaves "loading" and shows the selectable seeded character `Vitor`.
- Blowfish-CFB64 KAT + DH round-trip unit tests pass; auth path still authenticates.

## Unresolved Questions

> Flagged as explicit assumptions, not silent guesses. To validate during design/implementation.

- **ASSUMPTION A1 (BouncyCastle CFB64 = OpenSSL byte semantics).** Assumed BouncyCastle `CfbBlockCipher(BlowfishEngine, 64)` processed byte-by-byte produces identical bytes to OpenSSL `Blowfish_CFB64`. **Primary risk** — gated by the KAT (AC-3.5) before any client test. If they diverge, fall back to a hand-verified CFB64 feedback loop over `BlowfishEngine`.
- **ASSUMPTION A2 (client public key is ASCII hex).** Assumed the client's `CompleteExchange` public key arrives as an ASCII hex string (original uses `Encoding.ASCII.GetString` + hex parse). Confirm against live client bytes.
- **ASSUMPTION A3 (DH key bytes = Blowfish key as-is).** Assumed `sharedSecret.ToByteArrayUnsigned()` is used directly as the Blowfish key with no truncation/leading-zero adjustment. Confirm via DH round-trip + client interop.
- **ASSUMPTION A4 (no post-1006 packet needed for char-select).** Assumed 1006 alone clears "loading" and renders the selectable char (per research: no map dependency). If the client still hangs, identify the minimal additional packet empirically — but that edges toward enter-world (out of scope).
- **ASSUMPTION A5 (field mapping is cosmetic for POC).** Assumed defaulting CP/Exp/Class/Spouse and mapping Mesh→Lookface / Avatar→Hair / Silver→Money is acceptable to render char-select; visual fidelity (correct face/body) is not a success gate this iteration.
- **OPEN: GameServer__Ip override.** The LAN-IP override (192.168.0.252) for the post-auth handoff is currently uncommitted/local-only — decide whether to commit it as config in this spec or keep it operator-local.
- **OPEN: crypto test host (FR-13/FR-14).** No game-server test project exists today (only `ClientPatcher.Tests` in `ClientPatcher.sln`). Design must decide where the Blowfish-CFB64 KAT + DH round-trip tests live (e.g. a new `Crypto.Tests` project added to `Conquer.sln`) so NFR-4's `scripts/dotnet test` target is unambiguous.

## Next Steps

1. Proceed to design phase (`/design`) for Option A: crypto port + game-connection state machine wiring (`ClientSession` / `NetworkListener` / `PacketRouter` game path / `GameHandler`).
2. Design the BouncyCastle Blowfish-CFB64 + DH classes in `Crypto.csproj`, plus the KAT/round-trip test harness (resolve Assumption A1 first).
3. Design the game-path read/write framing (server-first send, AwaitingClientKey→Established, 8-byte seal) without touching the auth ReadPacket.
4. Port `[1006]HeroInformation` layout + `DbCharacter` field mapping into the net8 GameHandler char flow.
5. Capture the operator-verified manual acceptance step (AC-6.4) as a non-CI checklist item.
