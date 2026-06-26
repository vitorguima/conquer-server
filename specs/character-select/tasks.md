---
spec: character-select
basePath: specs/character-select
phase: tasks
updated: 2026-06-26
workflow: POC-first (GREENFIELD)
granularity: coarse
---

# Tasks: character-select

Reach the CO 5065 **character-select** screen: managed Blowfish-CFB64 + DH key exchange (port of `0b094c6` via BouncyCastle, behind `ICipher`) + server-first game state machine + MsgConnect(1052) → ANSWER_OK + HeroInformation(1006). Enter-world + char creation OUT of scope.

**Workflow**: POC-first. The Blowfish-CFB64 **KAT is the FIRST hard gate** (A1) — gate it before wiring anything. Verification = unit tests (KAT / DH round-trip / packet-layout / auth-regression) **in CI** + an **operator-manual** real-client checklist (AC-6.4, OUT of CI). No automated in-process handshake/dev-server VE tasks (no dev server, no automated client).

**Tooling** (from research.md / delegation):
- Build: `scripts/dotnet build src/Conquer.sln`
- Test: `scripts/dotnet test src/Conquer.sln`
- Run (operator): `docker compose -f src/docker-compose.yml up -d --build` on the LAN host; logs `docker compose -f src/docker-compose.yml logs -f server`
- Branch: `feat/character-select`; commit per task; push at [VERIFY]/phase boundaries.

**Build-safety (do NOT violate)**: do NOT delete `src/Redux/Cryptography/{BlowfishExchange,GameCryptographer}.cs` — `Player.cs` (dead but compiled) references them; deleting breaks the build. New impl lives in `Conquer.Crypto` (different namespace). No `GameServer__Ip` change — committed default already `127.0.0.1`.

---

## Phase 1: Make It Work (POC)

Focus: get crypto correct (KAT-gated first), then wire the handshake + char flow end-to-end. Hardcoded/defaulted values OK. Type check must pass; lint deferred to Phase 4.

- [x] 1.1 Add BouncyCastle + ICipher abstraction
  - **Do**:
    1. Add `<PackageReference Include="BouncyCastle.Cryptography" ...>` to `src/Crypto/Crypto.csproj`.
    2. Create `src/Crypto/ICipher.cs` — `interface ICipher { void Encrypt(byte[] data, int offset, int length); void Decrypt(byte[] data, int offset, int length); }`.
    3. Make `TQCipher : ICipher` (signatures already match — no behavior change; do NOT add `GenerateKeys` to the interface).
  - **Files**: `src/Crypto/Crypto.csproj`, `src/Crypto/ICipher.cs`, `src/Crypto/TQCipher.cs`
  - **Done when**: Solution builds with BouncyCastle restored; `TQCipher` implements `ICipher`; auth methods unchanged.
  - **Verify**: `scripts/dotnet build src/Conquer.sln 2>&1 | grep -qi "Build succeeded" && grep -q "ICipher" src/Crypto/TQCipher.cs && echo PASS`
  - **Commit**: `feat(crypto): add BouncyCastle + ICipher abstraction`
  - _Requirements: FR-4, FR-6, FR-7, NFR-2, NFR-5_
  - _Design: ICipher; File Structure (Crypto.csproj, ICipher.cs, TQCipher.cs)_

- [x] 1.2 Implement GameCipher (Blowfish-CFB64, 2 instances, key swap)
  - **Do**:
    1. Create `src/Crypto/GameCipher.cs : ICipher` using `CfbBlockCipher(new BlowfishEngine(), 64)` — **two persistent instances** (enc server→client, dec client→server), **separate** 8-byte `_encIV`/`_decIV` (both zeroed at start).
    2. Initial key ASCII `"DR654dt34trg4UI6"` (16 bytes) set in ctor; process the **exact byte count** (no padding) so partial trailing blocks match OpenSSL; keep the same instance across packets (running feedback register).
    3. Add `SetKey(byte[] sharedSecret)` (re-init both engines with new `KeyParameter`, preserve CFB-64 segment) and `SetIvs(byte[] enc, byte[] dec)`.
  - **Files**: `src/Crypto/GameCipher.cs`
  - **Done when**: Encrypt/Decrypt + SetKey/SetIvs compile; instances persist across calls. (Byte-correctness proven by 1.3 KAT — not here.)
  - **Verify**: `scripts/dotnet build src/Conquer.sln 2>&1 | grep -qi "Build succeeded" && echo PASS`
  - **Commit**: `feat(crypto): implement Blowfish-CFB64 GameCipher`
  - _Requirements: FR-3, NFR-1, NFR-5_
  - _Design: GameCipher; AC-3.1–3.4_

- [x] 1.3 [VERIFY] **A1 GATE** — Blowfish-CFB64 KAT + round-trip (byte-compat vs OpenSSL)
  - **Do**:
    1. Create `src/Crypto.Tests/Crypto.Tests.csproj` (xUnit, references `Crypto.csproj`); add it to `src/Conquer.sln`.
    2. Create `src/Crypto.Tests/BlowfishCfb64Tests.cs`:
       - `BlowfishCfb64_KAT`: embed an **OpenSSL-equivalent ground-truth vector** (priority: `openssl enc -bf-cfb` CLI output OR a tiny `Blowfish_CFB64` program OR captured pre-M1 server bytes — key `"DR654dt34trg4UI6"`, IV zeros, fixed plaintext) and assert `GameCipher` output **byte-for-byte**. Document the chosen source in the test file. Do NOT validate against another managed CFB64 lib (circular).
       - `BlowfishCfb64_RoundTrip`: `Decrypt(Encrypt(x)) == x` across multiple calls (feedback continuity).
    3. **If KAT diverges**: switch `GameCipher` engine to the documented hand-rolled CFB64 loop (ECB-encrypt 8-byte IV register → XOR keystream byte → shift cipher byte into register; per-direction register persists) and re-run until green.
  - **Files**: `src/Crypto.Tests/Crypto.Tests.csproj`, `src/Crypto.Tests/BlowfishCfb64Tests.cs`, `src/Conquer.sln` (+ `src/Crypto/GameCipher.cs` if fallback)
  - **Done when**: KAT passes byte-for-byte AND round-trip passes. **This gate blocks all later wiring.**
  - **Verify**: `scripts/dotnet test src/Conquer.sln --filter "FullyQualifiedName~BlowfishCfb64" 2>&1 | grep -qi "Passed!" && echo PASS`
  - **Commit**: `test(crypto): Blowfish-CFB64 KAT + round-trip (A1 gate)`
  - _Requirements: FR-13, NFR-1, NFR-4_
  - _Design: Test Strategy (BlowfishCfb64_KAT/RoundTrip); AC-3.5_

- [x] 1.4 Implement ServerKeyExchange (managed DH; packet build + parse)
  - **Do**:
    1. Create `src/Crypto/ServerKeyExchange.cs`: `DHParameters(P,G)` with P = the 512-bit hex const + G `"05"`; `GeneratorUtilities.GetKeyPairGenerator("DH")` → `GenerateKeyPair()`; `AgreementUtilities.GetBasicAgreement("DH")`. Constants verbatim: `PAD_LENGTH=11`, `JUNK_LENGTH=12`, `TQSERVER="TQServer"`.
    2. `CreateServerKeyPacket()` — EXACT layout: pad@0(11), `size-PAD`@11(int), junkLen@15(int=12), junk@19(12), clientIVlen@31(int=8), clientIV@35(8 zeros), serverIVlen@43(int=8), serverIV@47(8 zeros), P.Len@55(int=128), P@59(128 ASCII hex), G.Len@187(int=2), G@191("05"), pubKeyLen@193(int), pubKey@197(uppercase ASCII hex), `"TQServer"`@end. Then `GameCipher.Encrypt` under the **initial** key before returning.
    3. `HandleClientKeyPacket(buffer, GameCipher)` — Blowfish-decrypt under initial key, parse `length@7(int)`, `junk@11(int)`, `pubKeyLen@(15+junk)(int)`, `pubKey@(19+junk)` (ASCII hex) → `secret = CalculateAgreement(clientPub).ToByteArrayUnsigned()` → `cipher.SetKey(secret)` + `cipher.SetIvs(zeros, zeros)`; advance state to Established. Guard `len <= 36` → throw/close (AC-2.5).
  - **Files**: `src/Crypto/ServerKeyExchange.cs`
  - **Done when**: Both methods compile; secret derivation wired to `GameCipher.SetKey`.
  - **Verify**: `scripts/dotnet build src/Conquer.sln 2>&1 | grep -qi "Build succeeded" && echo PASS`
  - **Commit**: `feat(crypto): implement managed DH ServerKeyExchange`
  - _Requirements: FR-1, FR-2, FR-8_
  - _Design: ServerKeyExchange; AC-1.1–1.3, AC-2.1–2.3, AC-2.5_

- [x] 1.5 [VERIFY] DH round-trip + server-key packet-layout tests
  - **Do**:
    1. Create `src/Crypto.Tests/DhExchangeTests.cs`:
       - `Dh_RoundTrip`: two parties over `DHParameters(P,G)` derive the **same** secret (US-2 / A3).
       - `ServerKeyPacket_Layout`: assert offsets — int@11/15, IV lens@31/43, P.Len@55=128, P@59, G.Len@187=2, G@191="05", pubKeyLen@193, `"TQServer"`@end (AC-1.2).
  - **Files**: `src/Crypto.Tests/DhExchangeTests.cs`
  - **Done when**: Both tests pass.
  - **Verify**: `scripts/dotnet test src/Conquer.sln --filter "FullyQualifiedName~DhExchange" 2>&1 | grep -qi "Passed!" && echo PASS`
  - **Commit**: `test(crypto): DH round-trip + server-key packet layout`
  - _Requirements: FR-14, FR-1, NFR-4_
  - _Design: Test Strategy (Dh_RoundTrip / ServerKeyPacket_Layout); AC-1.2, US-2_

- [x] 1.6 Wire ClientSession for the game path (cipher-agnostic + seal-aware Send)
  - **Do**:
    1. In `src/Network/ClientSession.cs`: change cipher field to `ICipher Cipher`; add `enum ConnKind { Auth, Game }` + `ConnKind Kind`; add `ExchangeState State` (`AwaitingClientKey`/`Established`); add `ServerKeyExchange Exchange`.
    2. Add a seal-aware game `Send`: allocate `len + 8`, stamp last 8 bytes with `SERVER_SEAL`("TQServer"), then `GameCipher.Encrypt(whole buffer)`, then write. Auth `Send` path unchanged.
  - **Files**: `src/Network/ClientSession.cs`
  - **Done when**: Builds; auth path still uses `ICipher` via identical signatures (no auth behavior change).
  - **Verify**: `scripts/dotnet build src/Conquer.sln 2>&1 | grep -qi "Build succeeded" && grep -q "ConnKind" src/Network/ClientSession.cs && echo PASS`
  - **Commit**: `feat(net): cipher-agnostic ClientSession + seal-aware game Send`
  - _Requirements: FR-5, FR-6, NFR-2_
  - _Design: ClientSession; AC-4.1, AC-4.3_

- [x] 1.7 GameConnection state machine + listener game path (server-first)
  - **Do**:
    1. Create `src/Redux/GameConnection.cs`: `OnAccept` sends `CreateServerKeyPacket()` then state `AwaitingClientKey` (log key sent); first inbound → decrypt(initial) → `HandleClientKeyPacket` → state `Established` (log derived key); Established inbound → `GameCipher.Decrypt(whole buffer)` → split: `bodyLen = ReadUInt16LE(off)`, frame = `bodyLen+8`, `typeId = ReadUInt16LE(off+2)` → `PacketRouter.Dispatch`. Loop multiple frames; close+log on malformed/oversized (AC-2.5, NFR-3).
    2. In `src/Redux/NetworkListener.cs`: add `RunGameAsync` (:5816, `Kind=Game`, **server-first send on accept**) + `ServeGameAsync` loop driving `GameConnection`. Leave `RunAuthAsync` / `ServeClientAsync` / `ReadPacket` BYTE-FOR-BYTE unchanged.
    3. In `src/Redux/PacketRouter.cs`: keep auth `ReadPacket`; expose `Dispatch(session, typeId, frame)` for game frames.
  - **Files**: `src/Redux/GameConnection.cs`, `src/Redux/NetworkListener.cs`, `src/Redux/PacketRouter.cs`
  - **Done when**: Builds; game port server-first; auth ReadPacket untouched.
  - **Verify**: `scripts/dotnet build src/Conquer.sln 2>&1 | grep -qi "Build succeeded" && grep -q "RunGameAsync" src/Redux/NetworkListener.cs && echo PASS`
  - **Commit**: `feat(net): server-first GameConnection state machine + game listener`
  - _Requirements: FR-1, FR-5, FR-6, FR-8, NFR-3_
  - _Design: GameConnection; Listener hook; AC-2.5, AC-4.1–4.3_

- [x] 1.8 [VERIFY] Quality checkpoint: build + crypto tests after wiring
  - **Do**: Run build + all crypto tests; confirm handshake wiring compiles and KAT/DH still green.
  - **Verify**: `scripts/dotnet build src/Conquer.sln 2>&1 | grep -qi "Build succeeded" && scripts/dotnet test src/Conquer.sln 2>&1 | grep -qi "Passed!" && echo PASS`
  - **Done when**: Build succeeds; all crypto tests pass.
  - **Commit**: `chore(character-select): pass quality checkpoint` (only if fixes needed)

- [x] 1.9 Port packet helpers (NetStringPacker, PacketBuilder, ChatType, MsgTalk, HeroInformation)
  - **Do**:
    1. `src/Packets/NetStringPacker.cs` — `[count][len][bytes]...` packer (managed `Span`/`BinaryPrimitives`, no unsafe/memcpy).
    2. `src/Packets/PacketBuilder.cs` — `AppendHeader` writing length = `size - 8`, type at +2.
    3. `src/Packets/ChatType.cs` — `enum ChatType : ushort` with `Entrance = 2101`.
    4. `src/Packets/MsgTalk.cs` — `[1004]` layout (Color@4=0x00FFFFFF, Type@8, Time@12, lookfaces, NetStringPacker@24 = [Speaker="SYSTEM", Hearer="ALLUSERS", Emotion="", Words=text]); total `24 + packer + 8`.
    5. `src/Packets/HeroInformation.cs` — `[1006]` ORIGINAL layout: Id@4, Lookface@8, Hair@12, Money@14, CP@18, Exp@22(u64), Str@50, Agi@52, Vit@54, Spi@56, Stats@58, Life@60, Mana@62, PK@64, Lvl@66, Class@67, Reborn@69, ShowName@70=1, NetStringPacker@71=[Name, Spouse=""]; total `71 + strings + 8`. Mapping (AC-6.3): `Mesh`→Lookface, `Avatar`→Hair, `Silver`→Money; CP/Exp/Class/Spouse defaulted.
  - **Files**: `src/Packets/NetStringPacker.cs`, `src/Packets/PacketBuilder.cs`, `src/Packets/ChatType.cs`, `src/Packets/MsgTalk.cs`, `src/Packets/HeroInformation.cs`
  - **Done when**: Builds; builders produce `len+8` frames; ChatType.Entrance=2101.
  - **Verify**: `scripts/dotnet build src/Conquer.sln 2>&1 | grep -qi "Build succeeded" && grep -q "Entrance = 2101" src/Packets/ChatType.cs && echo PASS`
  - **Commit**: `feat(packets): port NetStringPacker/PacketBuilder/MsgTalk/HeroInformation(1006)`
  - _Requirements: FR-10, FR-11_
  - _Design: NetStringPacker/PacketBuilder/MsgTalk/HeroInformation; AC-6.2, AC-6.3_

- [x] 1.10 Update GameHandler char flow (ANSWER_OK + 1006 / NEW_ROLE)
  - **Do**:
    1. In `src/Packets/MsgConnect.cs` (GameHandler): **drop the TQCipher `GenerateKeys` call** (game path uses Blowfish; key already swapped by exchange).
    2. Token via `TokenStore.TryConsume`; valid + char → `session.Send(MsgTalk.Build(ChatType.Entrance, "ANSWER_OK"))` then `session.Send(HeroInformation.Build(char))` (lookup char by `AccountId`).
    3. No char → `session.Send(MsgTalk.Build(ChatType.Entrance, "NEW_ROLE"))`; **no create handler** (AC-7.2). Invalid/consumed token → log + disconnect, no char data (AC-5.3).
    4. Emit diagnostic logs at each stage: server-key sent / derived key / `[Game] Connect accountId=…` / `ANSWER_OK + 1006 sent` (FR-12).
  - **Files**: `src/Packets/MsgConnect.cs`
  - **Done when**: Builds; valid token+char path sends ANSWER_OK + 1006; no-char sends NEW_ROLE; no TQCipher key-gen on game path.
  - **Verify**: `scripts/dotnet build src/Conquer.sln 2>&1 | grep -qi "Build succeeded" && grep -q "ANSWER_OK" src/Packets/MsgConnect.cs && grep -q "NEW_ROLE" src/Packets/MsgConnect.cs && echo PASS`
  - **Commit**: `feat(packets): GameHandler char flow — ANSWER_OK+1006 / NEW_ROLE`
  - _Requirements: FR-9, FR-10, FR-11, FR-12_
  - _Design: GameHandler; AC-5.1–5.5, AC-6.1, AC-7.1, AC-7.2_

- [ ] 1.11 [VERIFY] POC Checkpoint: full build + all unit tests green
  - **Do**: Run full build + full test suite (KAT / round-trip / DH / packet-layout). Confirm the end-to-end handshake + char flow compiles and all crypto/exchange invariants hold. (Real-client E2E is the operator-manual gate in Phase 4 — out of CI.)
  - **Verify**: `scripts/dotnet build src/Conquer.sln 2>&1 | grep -qi "Build succeeded" && scripts/dotnet test src/Conquer.sln 2>&1 | grep -qi "Passed!" && echo POC_PASS`
  - **Done when**: Build succeeds; all unit tests pass; handshake + char flow wired end-to-end in code.
  - **Commit**: `feat(character-select): complete POC handshake + char flow`
  - _Requirements: FR-1, FR-2, FR-3, FR-9, FR-10, FR-13, FR-14_

---

## Phase 2: Refactoring

After POC validated. Clean up structure + harden error handling. No new features. Type check must pass.

- [ ] 2.1 Harden game-path error handling + logging
  - **Do**:
    1. `GameConnection`/`ServerKeyExchange`/`ServeGameAsync`: wrap parse + decrypt in try/catch; on malformed/short (`len<=36`) or oversized (`size>buffer`) frame → close session + log, **listener loop survives** (NFR-3, AC-2.5). Mid-exchange drop → `IOException`/`EndOfStream` → finally Disconnect.
    2. Ensure every handshake stage emits a distinguishable log line (FR-12 / NFR-6): `server-key packet sent (len=N)`, `CompleteExchange derived key`, `[Game] Connect accountId=…`, `ANSWER_OK + 1006 sent`.
  - **Files**: `src/Redux/GameConnection.cs`, `src/Crypto/ServerKeyExchange.cs`, `src/Redux/NetworkListener.cs`
  - **Done when**: Malformed input cannot crash the listener; all stages logged.
  - **Verify**: `scripts/dotnet build src/Conquer.sln 2>&1 | grep -qi "Build succeeded" && echo PASS`
  - **Commit**: `refactor(net): harden game-path error handling + stage logging`
  - _Requirements: NFR-3, NFR-6, FR-12_
  - _Design: Error Handling; Edge Cases; AC-2.5, AC-1.4, AC-2.4, AC-5.5_

- [ ] 2.2 Tidy crypto + packet builders for project patterns
  - **Do**:
    1. Extract magic constants (initial key, PAD/JUNK lengths, seal, P/G) into named consts; XML-doc the `ICipher` contract and the key-swap timing.
    2. Confirm `HeroInformation`/`MsgTalk` use `Span`/`BinaryPrimitives` (no unsafe), seal in last 8 bytes, length = `size-8` per `PacketBuilder` convention.
  - **Files**: `src/Crypto/GameCipher.cs`, `src/Crypto/ServerKeyExchange.cs`, `src/Packets/MsgTalk.cs`, `src/Packets/HeroInformation.cs`
  - **Done when**: Constants named; builders follow the `AppendHeader` (`size-8`) convention; no behavior change.
  - **Verify**: `scripts/dotnet build src/Conquer.sln 2>&1 | grep -qi "Build succeeded" && scripts/dotnet test src/Conquer.sln 2>&1 | grep -qi "Passed!" && echo PASS`
  - **Commit**: `refactor(crypto): named constants + builder cleanup`
  - _Requirements: FR-3, FR-10_
  - _Design: Existing Patterns to Follow_

- [ ] 2.3 [VERIFY] Quality checkpoint: build + tests after refactor
  - **Do**: Run build + full test suite; confirm no regression from refactor.
  - **Verify**: `scripts/dotnet build src/Conquer.sln 2>&1 | grep -qi "Build succeeded" && scripts/dotnet test src/Conquer.sln 2>&1 | grep -qi "Passed!" && echo PASS`
  - **Done when**: Build succeeds; all tests pass.
  - **Commit**: `chore(character-select): pass quality checkpoint` (only if fixes needed)

---

## Phase 3: Testing

Add the auth-regression smoke test (NFR-2). KAT / round-trip / DH / packet-layout already written in Phase 1. All tests must pass.

- [ ] 3.1 Auth-regression smoke test (TQCipher golden vector)
  - **Do**:
    1. Create `src/Crypto.Tests/AuthRegressionTests.cs`: `Auth_Regression` — drive `TQCipher` via the `ICipher` surface and assert output bytes equal a pre-refactor golden vector (proves `: ICipher` is a no-op for auth).
  - **Files**: `src/Crypto.Tests/AuthRegressionTests.cs`
  - **Done when**: Test passes; auth bytes byte-identical to golden vector.
  - **Verify**: `scripts/dotnet test src/Conquer.sln --filter "FullyQualifiedName~AuthRegression" 2>&1 | grep -qi "Passed!" && echo PASS`
  - **Commit**: `test(crypto): TQCipher auth-regression smoke test`
  - _Requirements: NFR-2, FR-7, FR-13_
  - _Design: Test Strategy (Auth_Regression); AC-4.4_

- [ ] 3.2 [VERIFY] Quality checkpoint: full test suite green
  - **Do**: Run the full test suite (KAT, round-trip, DH, packet-layout, auth-regression).
  - **Verify**: `scripts/dotnet test src/Conquer.sln 2>&1 | grep -qi "Passed!" && echo PASS`
  - **Done when**: All five unit tests pass.
  - **Commit**: `chore(character-select): pass quality checkpoint` (only if fixes needed)

---

## Phase 4: Quality Gates, PR, Operator Verification

All local checks pass, create PR, verify CI, then the operator-manual real-client gate (AC-6.4, OUT of CI). Never push to default branch; work is on `feat/character-select`.

- [ ] V4 [VERIFY] Full local CI: build + all tests + managed-only output
  - **Do**:
    1. `scripts/dotnet build src/Conquer.sln` (NFR-4) — confirm no native DLLs in output (managed-only, NFR-5).
    2. `scripts/dotnet test src/Conquer.sln` — KAT / round-trip / DH / packet-layout / auth-regression all green.
  - **Verify**: `scripts/dotnet build src/Conquer.sln 2>&1 | grep -qi "Build succeeded" && scripts/dotnet test src/Conquer.sln 2>&1 | grep -qi "Passed!" && echo PASS`
  - **Done when**: Build succeeds; all tests pass; output managed-only.
  - **Commit**: `chore(character-select): pass local CI` (if fixes needed)
  - _Requirements: FR-13, FR-14, NFR-1, NFR-2, NFR-4, NFR-5_

- [ ] V5 [VERIFY] Create PR and verify CI pipeline
  - **Do**:
    1. Confirm branch: `git branch --show-current` = `feat/character-select` (if on default branch, STOP and alert user).
    2. Push: `git push -u origin feat/character-select`.
    3. `gh pr create --title "feat: 5065 character-select (DH + Blowfish-CFB64 + 1006)" --body "<summary + ACs + manual-test note>"`.
    4. Verify: `gh pr checks --watch`. If CI fails: read `gh pr checks`, fix locally, `git push`, re-verify.
  - **Verify**: `gh pr checks 2>&1 | grep -qiv "fail" && echo PASS` (all checks green)
  - **Done when**: PR open; all CI checks green.
  - **Commit**: None
  - _Requirements: NFR-4_

- [ ] V6 [VERIFY] AC checklist
  - **Do**: Read requirements.md; programmatically confirm each AC is satisfied — grep code for the server-key layout / ICipher / ChatType.Entrance / ANSWER_OK / NEW_ROLE / 1006 offsets; run the relevant test filters (KAT=AC-3.5, DH=US-2, layout=AC-1.2, auth-regression=AC-4.4). Note AC-6.4 is the operator-manual gate (next task).
  - **Verify**: `scripts/dotnet test src/Conquer.sln 2>&1 | grep -qi "Passed!" && grep -q "Entrance = 2101" src/Packets/ChatType.cs && echo PASS`
  - **Done when**: All CI-verifiable ACs confirmed (AC-6.4 deferred to operator gate).
  - **Commit**: None
  - _Requirements: all FR/NFR/AC_

- [ ] M1 [VERIFY] **Operator-manual real-client gate (AC-6.4 — OUT of CI)**
  - **Do** (operator checklist; the executor records the result, does NOT run an automated client — there is no dev server / automated client):
    1. Set the LAN-IP override locally (env/compose `GameServer__Ip` = reachable LAN IP, e.g. 192.168.0.252) — **never commit a personal IP** (committed default stays `127.0.0.1`).
    2. Start the server: `docker compose -f src/docker-compose.yml up -d --build`; tail logs: `docker compose -f src/docker-compose.yml logs -f server`.
    3. On the Windows host: delete `tqantivirus`, point the real 5065 client at the LAN IP, log in to seeded account `vitor`/`test123` (AccountID 2).
    4. Observe server logs progress through all stages: `server-key packet sent (len=N)` → `CompleteExchange derived key` → `[Game] Connect accountId=2` → `ANSWER_OK + 1006 sent`.
    5. Confirm the client leaves "loading" → renders the selectable seeded character **Vitor** on the character-select screen.
    6. Record PASS/FAIL + observed log lines in `.progress.md` under an `## Operator Verification (AC-6.4)` section.
  - **Verify**: Operator confirms client reaches char-select showing "Vitor"; the four stage log lines present. (Manual — definitive gate, explicitly out of CI per delegation.)
  - **Done when**: Real 5065 client reaches the character-select screen with the seeded char rendered + selectable.
  - **Commit**: `docs(character-select): record operator manual verification (AC-6.4)`
  - _Requirements: NFR-6, US-6_
  - _Design: Integration / Manual (out of CI — AC-6.4); Build / Verify_

---

## Phase 5: PR Lifecycle

Autonomous PR loop until all completion criteria met. Spec is NOT done when Phase 4 PR is created.

- [ ] 5.1 PR lifecycle loop
  - **Do**: Loop: monitor CI (`gh pr checks --watch`) → resolve review comments → fix locally → `git push` → re-verify. Repeat until all checks green AND no unresolved review comments AND zero test regressions.
  - **Verify**: `gh pr checks 2>&1 | grep -qiv "fail" && gh pr view --json reviewDecision -q .reviewDecision 2>&1 | grep -qiv "CHANGES_REQUESTED" && echo PASS`
  - **Done when**: CI green; review comments resolved; zero regressions; PR ready to merge.
  - **Commit**: `fix(character-select): address CI/review feedback` (per fix)
  - _Requirements: NFR-2, NFR-4_

---

## Notes

**POC shortcuts taken**:
- HeroInformation(1006) defaults CP/Exp/Class/Spouse to 0/""; maps Mesh→Lookface, Avatar→Hair, Silver→Money (A5 — cosmetic for POC, not a fidelity gate).
- Both DH IVs sent zeroed (AC-3.3); secret used as-is via `ToByteArrayUnsigned()` (A3, no truncation/leading-zero fix).
- Diagnostic logging via `Console.WriteLine` (existing pattern), not a structured logger.

**Production TODOs (out of scope this spec)**:
- Enter-world / map / DMAP / movement (research milestone 3).
- Character creation (MsgUserCreate / Register 1001) — NEW_ROLE emitted, no create flow.
- Correct visual-fidelity field mapping for 1006.

**Risks / assumptions to validate at the operator gate (M1)**:
- **A1** (BouncyCastle CFB64 == OpenSSL bytes) — gated in CI by 1.3 KAT; hand-rolled CFB64 fallback if it diverges.
- **A2** (client pubkey is ASCII hex) / **A4** (1006 alone clears "loading") — confirmable only against the live client at M1.

**Build-safety (must hold)**:
- Do NOT delete `src/Redux/Cryptography/{BlowfishExchange,GameCryptographer}.cs` — referenced by dead-but-compiled `Player.cs`; deleting breaks the build. New impl is `Conquer.Crypto.*` (no namespace conflict).
- No `GameServer__Ip` code change — committed default already `127.0.0.1`; LAN IP is an uncommitted operator-local override only.
- Auth path (`RunAuthAsync` / `ServeClientAsync` / `PacketRouter.ReadPacket` / TQCipher / :9958) stays byte-for-byte unchanged (NFR-2 / AC-4.4).

**Execution policy**: commit per task; push at [VERIFY]/phase boundaries on `feat/character-select`.

---

**Total tasks: 21** — Phase 1: 11 (1.1–1.11, incl. 4 [VERIFY] gates: 1.3/1.5/1.8/1.11), Phase 2: 3 (2.1–2.3), Phase 3: 2 (3.1–3.2), Phase 4: 4 (V4, V5, V6, M1 operator gate), Phase 5: 1 (5.1). No automated VE1/VE2/VE3 (no dev server / automated client — replaced by the M1 operator-manual gate per delegation). No VF task (no `## Reality Check (BEFORE)` — GREENFIELD, not a fix goal).
