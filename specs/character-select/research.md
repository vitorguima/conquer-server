---
spec: character-select
phase: research
created: 2026-06-26
---

# Research: character-select

## Executive Summary

The 5065 GAME connection (:5816) needs a server-first Diffie-Hellman exchange that derives a **Blowfish-CFB64** session key; M1 stubbed both (`ServerKeyExchange`, `GameCryptography`) when it removed native ManagedOpenSsl. The **exact original protocol still exists in this repo's git history** (fork commit `0b094c6`) — P/G, packet byte layout (pad=11, junk=12, IVs, P, G, pubkey, "TQServer" trailer) and `Blowfish_CFB64` cipher are fully documented below; it can be ported 1:1 to managed .NET 8 via **BouncyCastle** (`BlowfishEngine` + `CfbBlockCipher(...,64)` + `DHBasicAgreement`). After the exchange, the char flow is trivial: `MsgConnect(1052)` → if character exists send `MsgTalk("ANSWER_OK")` then `HeroInformation(1006)`; the client then shows char-select/enters. Recommend **Option A** (wire the exchange + Blowfish into the existing net8 `ClientSession`/`PacketRouter`), staged in 3 milestones.

## Critical Finding: References

Comet@5017 (where our TQCipher came from) **does NOT implement DH/Blowfish** — its `Security/` dir has only `ICipher.cs`, `RC5.cs`, `TQCipher.cs`. The DH+Blowfish game cipher is **specific to the 5065 Redux lineage**, and the authoritative reference is **the original Redux source already in this repo** (predates the M1 stub). Do not chase Comet/CoEmu for this; use the in-repo git history.

| Reference | Verdict |
|-----------|---------|
| `git show 0b094c6:src/Redux/Cryptography/BlowfishExchange.cs` | **PRIMARY** — exact P/G, layout, params |
| `git show 0b094c6:src/Redux/Cryptography/GameCryptographer.cs` | **PRIMARY** — `Cipher.Blowfish_CFB64`, key/IV handling |
| Comet@5017 `Security/` | TQCipher only; no DH. Not applicable to game cipher |
| CoEmu (shekohex) | Rust, patch 5017; no DH game cipher. Not applicable |
| BouncyCastle.NET | Managed `BlowfishEngine`/`CfbBlockCipher(64)`/`DHBasicAgreement` — port target |

## Deliverable 1: The Game Key-Exchange Protocol (server sends first)

Source: `0b094c6:src/Redux/Cryptography/BlowfishExchange.cs` (`ServerKeyExchange`). Parameters:

```
P (hex, 512-bit/128 hex chars):
E7A69EBDF105F2A6BBDEAD7E798F76A209AD73FB466431E2E7352ED262F8C558
F10BEFEA977DE9E21DCEE9B04D245F300ECCBBA03E72630556D011023F9E857F   (concatenated, no newline)
G = "05"
PAD_LENGTH  = 11
JUNK_LENGTH = 12
TQSERVER trailer = "TQServer"  (8 bytes, == Common.SERVER_SEAL)
```

**Server key packet byte layout** (`GeneratePacket`, all ints are 4-byte LE, strings are ASCII; P/G/pubkey sent as **hex strings**, not raw bytes):

| Offset | Field | Bytes |
|--------|-------|-------|
| 0 | pad (random) | PAD_LENGTH = 11 |
| 11 | `size - PAD_LENGTH` (int) | 4 |
| 15 | JUNK_LENGTH (int = 12) | 4 |
| 19 | junk (random) | 12 |
| 31 | clientIV.Length (int = 8) | 4 |
| 35 | clientIV (zeros) | 8 |
| 43 | serverIV.Length (int = 8) | 4 |
| 47 | serverIV (zeros) | 8 |
| 55 | P.Length (int = 128) | 4 |
| 59 | P (ASCII hex) | 128 |
| 187 | G.Length (int = 2) | 4 |
| 191 | G (ASCII hex "05") | 2 |
| 193 | pubKey.Length (int) | 4 |
| 197 | pubKey (ASCII hex) | var |
| end | "TQServer" trailer | 8 |

`size = 28 + PAD_LENGTH + JUNK_LENGTH + 8 + 8 + P.Length + G.Length + pubKey.Length + 8`. Note both IVs are sent **zeroed** (`new byte[8]`), so encrypt/decrypt IVs both start at 0.

**Client response → `CompleteExchange`** (`src/Redux/Objects/Player.cs:1382`) reads, after the client's own pad, with the SAME field framing: `length@7`, `junk@11`, `publicKeyLength@(15+junk)`, `publicKey@(19+junk)`. The client public key is an ASCII **hex** string. (These offsets match our stub's documented offsets exactly → confirms format is unchanged for 5065.)

**Key derivation** (`HandleClientKeyPacket`): `sharedKey = DH.ComputeKey(BigNumber.FromHex(clientPubKeyHex))`. The raw DH shared secret bytes become the **Blowfish key** (`crypto.SetKey(key)`), and `SetIvs(clientIV, serverIV)` sets the two 8-byte IVs (both zero from the packet).

Managed port: `DHBasicAgreement` with a `DHParameters(P, G)`; `GenerateKeyPair()` for server keys; `agreement.CalculateAgreement(clientPubKey)` → `BigInteger` → `.ToByteArrayUnsigned()` is the Blowfish key. Send/parse the public keys as uppercase hex strings to match `ToHexString()`/`FromHexString()`.

## Deliverable 2: The Game Cipher

Source: `0b094c6:src/Redux/Cryptography/GameCryptographer.cs` (`GameCryptography`).

| Property | Value |
|----------|-------|
| Algorithm | **Blowfish, CFB-64 mode** (`Cipher.Blowfish_CFB64`) |
| Key | DH shared secret (stored as `byte[128]` buffer; actual key = secret length) |
| Encrypt IV | `byte[8]` `_encryptIV` (server→client) |
| Decrypt IV | `byte[8]` `_decryptIV` (client→server), **separate** from encrypt IV |
| Initial seed key | `Common.ENCRYPTION_KEY = ASCII("DR654dt34trg4UI6")` (16 bytes) — set in `GameCryptography` ctor *before* DH completes |
| IV init | Both IVs come from the server key packet (sent as zeros) |

CFB is a **stream-style** mode: it keeps a running cipher feedback register, so each call to `Decrypt`/`Encrypt` must reuse the *same* `BlowfishEngine`+`CfbBlockCipher` instances per direction (do not re-init between packets). BouncyCastle: `new CfbBlockCipher(new BlowfishEngine(), 64)`, wrapped per direction with its own `KeyParameter`+`ParametersWithIV`, processed byte-by-byte (`ProcessByte`) so partial blocks work like OpenSSL CFB64.

**IMPORTANT — initial vs derived key:** the server-key packet itself is sent through `DirectSend` → `_cryptographer.Encrypt`, i.e. Blowfish-encrypted with the **initial** key `"DR654dt34trg4UI6"` (the DH secret isn't known yet). The client decrypts it with the same hardcoded key, reads the server pubkey, derives the secret, and **only then** does both sides `SetKey(sharedSecret)` for all subsequent packets. So the cipher key is swapped mid-connection.

## Deliverable 3: Handshake Sequence + Cipher-Switch Timing

Original flow (`GameServer.cs` + `Player.cs`):

```
TCP connect (:5816)
  → OnConnect: new Player; StartExchange()
      → DirectSend(CreateServerKeyPacket())   // Blowfish-encrypted w/ INITIAL key "DR654dt34trg4UI6"
  → client derives, replies with its public key
  → OnReceive: Crypto.Decrypt(buffer)         // Blowfish, initial key
      → UseThreading==false ⇒ CompleteExchange(buffer)
          → HandleClientKeyPacket ⇒ SetKey(sharedSecret); SetIvs(...)  // KEY SWAP
          → UseThreading = true
  → all later packets: Crypto.Decrypt under DERIVED Blowfish key
  → MsgConnect(1052): token validate → char flow
```

Two framing/seal details that differ from the auth (TQCipher) path:
- **8-byte trailer ("seal"):** once `UseThreading` is true, every *outbound* packet has `Common.SERVER_SEAL` ("TQServer") copied into its last 8 bytes (`DirectSend`). Original packet builders allocate `len + 8`. Inbound split uses `size = bodyLen + 8`.
- **No 2-byte length-prefix stream model:** the game path Blowfish-decrypts the whole buffer, then splits by `*(ushort*)(ptr+offset)` body length with `+8`. This is **different** from the net8 `PacketRouter.ReadPacket` (which assumes a TQCipher continuous stream + 2-byte prefix). The game connection must NOT reuse `ReadPacket` as-is.

Token note: original validates `MsgConnect.AccountId` against `Accounts.GetByToken(...)`. The net8 `GameHandler` already validates an 8-byte token via `TokenStore.TryConsume` (set during auth `MsgConnectEx`). Keep `TokenStore`; the only change is that the token now arrives **Blowfish-decrypted**, not TQCipher.

## Deliverable 4: Character List / Selection Packets (5065)

After `MsgConnect(1052)` the **server decides** char-select vs creation (`GameServer.Process_MsgConnectPacket`, line 301):

```
character == null  →  MsgTalk(ChatType.Entrance, "NEW_ROLE")     // client shows CREATE screen
character != null  →  MsgTalk(ChatType.Entrance, "ANSWER_OK")    // client proceeds
                      Populate(character)  →  Send(HeroInformation(1006))  + items/etc → enters world
```

5065 has **no separate "character list" packet for a single char** — one account = one character. The trigger strings are `Constants.NEW_ROLE_STR = "NEW_ROLE"` and `REPLY_OK_STR = "ANSWER_OK"` (`src/Redux/Constants.cs:97,99`), sent via `MsgTalk(1004)` with `ChatType.Entrance`. Since a char is seeded, the path is: send `ANSWER_OK` → send `HeroInformation(1006)`.

**HeroInformation (1006)** layout (`src/Redux/Packets/Game/[1006]HeroInformation.cs`): header(4) then `Id@4, Lookface@8, Hair@12, Money@14, CP@18, Experience@22(u64), Strength@50, Agility@52, Vitality@54, Spirit@56, Stats@58, Life@60, Mana@62, PKPoints@64, Level@66, Class@67, Reborn@69, ShowName@70`, then a `NetStringPacker` at @71 holding [Name, Spouse]. Total `71 + strings.Length + 8`. The net8 `MsgUserInfo.Build` (`src/Packets/MsgUserInfo.cs`) is a *guessed* minimal layout — **prefer porting the original 1006 layout** (offsets above) for correctness; the original is the proven 5065 structure.

`DbCharacter` available fields (`src/Database/CharacterRepository.cs` / `src/init.sql` `characters`): CharacterID, AccountID, Name, Mesh, Avatar, Level, MapID, X, Y, Silver, Strength, Agility, Vitality, Spirit, HealthPoints, ManaPoints. Missing vs original 1006: `Lookface` (original = face*10000+body; here `Mesh` ≈ body, `Avatar` ≈ hair), `Money`(=Silver), `CP`, `Experience`, `Class/Profession`, `Spouse`. → For POC, map Mesh→Lookface, Avatar→Hair, Silver→Money, hardcode CP/Exp/Class/Spouse defaults.

**Deferred MsgUserCreate / Register (1001)** layout (`[1001] Register.cs`): `accountName@4(16), characterName@20(16), accountPassword@36(16), Mesh@52(u16), Profession@54(byte), UID@58(u32)`. Server replies `ANSWER_OK` on success. Not needed now (char seeded); documented for the deferred-creation task.

## Deliverable 5: Integration Approach + Trade-offs

| Option | Effort | Reaches char-select? | Notes |
|--------|--------|----------------------|-------|
| **A — port exchange+cipher into net8 stack** | **S–M** | **Yes** | Minimal; recommended |
| B — resurrect original 2243-line GameServer | XL | Yes (eventually) | Drags in world/combat/managers/NHibernate-era DB; high risk |

**Recommend A.** Where to hook (current net8 stack):

- **`src/Network/ClientSession.cs`** — add a notion of connection kind + a game cipher. Currently hardcodes `Cipher = new TQCipher()`. Add: `GameCryptography? GameCipher`, `ServerKeyExchange? Exchange`, and an `ExchangeState` enum (`AwaitingClientKey` → `Established`). Game connections use Blowfish, not TQCipher.
- **`src/Redux/NetworkListener.cs`** — `RunGameAsync`/`ServeClientAsync` must, for the game port, **send the server key packet immediately on connect** (server-first) before the read loop, and read via a game-specific reader.
- **`src/Redux/PacketRouter.cs`** — `ReadPacket` is TQCipher+2-byte-prefix specific. Add a game read path: Blowfish-decrypt buffer, split by `ushort bodyLen` + 8-byte seal; while in `AwaitingClientKey`, route the first inbound buffer to `CompleteExchange` instead of `Dispatch`.
- **`src/Packets/MsgConnect.cs` (`GameHandler`)** — already validates token via `TokenStore` and calls char flow. Replace `MsgUserInfo` stub send with: `MsgTalk("ANSWER_OK")` + ported `HeroInformation(1006)`.

Simplest concrete shape: give the game connection its own per-connection state machine (separate from auth's `ServeClientAsync` reuse), since server-sends-first + Blowfish + 8-byte seal all differ from auth.

## Deliverable 6: Maps / World Dependency (to ENTER world after select)

- `MapRegistry.Load` scans `*.cqmap` (`src/Maps/MapRegistry.cs`); `src/Redux/TinyMapStub.cs` notes the real DMAP parser is unbuilt (task 1.12). `MapsDirectory=/app/maps` mounted read-only in docker-compose.
- **To reach the char-select screen / "ANSWER_OK + HeroInformation":** NO map dependency. The client renders the character and the in-game UI from 1006 alone.
- **To fully ENTER and walk the world:** client needs `HeroInformation` + likely follow-ups the original `Populate` sends (item info, friends, etc.) and a valid `MapID`/coords; the client loads its own map art, so a server-side DMAP isn't strictly required to *spawn standing in place*, but movement/visibility validation needs map data. **Stage map work after select** — milestone 3 can reach "stand in world" with just 1006 + coords; full movement is later.

## Quality Commands

| Type | Command | Source |
|------|---------|--------|
| Build (runtime) | `scripts/dotnet build src/Conquer.sln` | Dockerfile builds Conquer.sln |
| Restore | `scripts/dotnet restore src/Conquer.sln` | Dockerfile |
| Publish | `scripts/dotnet publish src/Redux/Redux.csproj -c Release` | Dockerfile line 16 |
| Test | `scripts/dotnet test src/ClientPatcher.sln` | only ClientPatcher.Tests exists |
| Lint / TypeCheck | Not found | no analyzer/format config |
| Run (full stack) | `docker compose -f src/docker-compose.yml up -d --build` | .progress.md |
| Logs | `docker compose -f src/docker-compose.yml logs -f server` | .progress.md |

**Local CI:** `scripts/dotnet build src/Conquer.sln && scripts/dotnet test src/ClientPatcher.sln`
Note: there is no automated game-server test project. Crypto is a good unit-test candidate (DH round-trip + Blowfish-CFB64 known-answer).

## Verification Tooling

| Tool | Command | Detected From |
|------|---------|---------------|
| Dev Server | `docker compose -f src/docker-compose.yml up -d --build` | docker-compose.yml |
| Ports | auth 9958, game 5816 | docker-compose.yml |
| DB | MySQL 8 (`db` service), `init.sql` seeds account `testplayer` | docker-compose.yml / init.sql |
| Health endpoint | None | n/a |
| Browser automation | None | n/a |
| E2E config | None | n/a |

**Project Type:** Game server (TCP, binary protocol).
**Verification Strategy:** No automated E2E. Real verification = **manual, operator-driven** against the Windows 5065 client pointed at 192.168.0.252. Server-side signal of progress = log lines: server-key packet sent → `CompleteExchange` derived key → `[Game] Connect ...` (MsgConnect decoded under Blowfish) → `ANSWER_OK`/1006 sent. Add diagnostic logs at each handshake stage to localize failures without the client.

## Feasibility Assessment

| Aspect | Assessment | Notes |
|--------|------------|-------|
| Technical Viability | **High** | Exact original protocol in git history; managed crypto = BouncyCastle |
| Effort Estimate | **M** | Port crypto (S) + game state machine wiring (M) + 1006 port (S) |
| Risk Level | **Medium** | CFB64 byte-exactness vs OpenSSL; pubkey hex framing; 8-byte seal offsets; can't auto-test against client |

## Related Specs

| Spec | Relation | mayNeedUpdate |
|------|----------|---------------|
| `client-patcher` (merged) | Predecessor — repoints client to LAN IP; enables this | No |
| `fix/auth-cipher` (branch) | Predecessor — auth handshake + TokenStore feed MsgConnect | No |
| (this) character-select | Current | — |

Classification: predecessors are **High** overlap but already complete; this spec consumes their `TokenStore` token.

## Recommendations for Requirements

1. **Milestone 1 — Handshake completes.** Port `ServerKeyExchange` (DH via BouncyCastle, exact P/G/layout/"TQServer" trailer) + `GameCryptography` (Blowfish-CFB64, two IVs, initial key `"DR654dt34trg4UI6"`). Wire a game-specific connection path in `NetworkListener`/`ClientSession`/`PacketRouter` that sends the server key packet first, runs `CompleteExchange` on the first reply, swaps to the derived key. **Done when:** `MsgConnect(1052)` decodes under the derived Blowfish key and `[Game] Connect ...` logs.
2. **Milestone 2 — Reach char-select.** On valid token + seeded char: send `MsgTalk(Entrance,"ANSWER_OK")` then ported `HeroInformation(1006)` (original offsets, mapping `DbCharacter` fields). **Done when:** client leaves "loading" and shows the character.
3. **Milestone 3 — Enter world (minimal).** Send any `Populate` follow-ups the client requires to stop at "in world" with seeded `MapID`/X/Y. Defer full map/DMAP + movement.
4. **Port, don't re-derive:** reuse the original `[1006]HeroInformation` layout and the git-history `ServerKeyExchange`/`GameCryptographer` rather than the guessed `MsgUserInfo.Build`.
5. **Add a crypto unit test** (DH round-trip + Blowfish-CFB64 known-answer) since client testing is manual.
6. **Defer character CREATION** (`Register`/1001 + `NEW_ROLE`); document layout only.

## Open Questions

- Exact BouncyCastle CFB64 byte-compatibility with OpenSSL `Blowfish_CFB64` — verify with a known-answer test before client testing (low risk, but confirm IV/feedback-size semantics).
- Whether 5065 client needs additional post-1006 packets (item/friend/ServerTime) before it fully renders "in world" — confirm empirically via the original `Populate` sequence.
- Confirm the client public key in `CompleteExchange` is hex (matches `BigNumber.FromHexString`) — original code uses `Encoding.ASCII.GetString` then hex parse, so yes; verify against live bytes.

## Sources

- `git show 0b094c6:src/Redux/Cryptography/BlowfishExchange.cs` (in-repo, original DH ServerKeyExchange — P/G/layout)
- `git show 0b094c6:src/Redux/Cryptography/GameCryptographer.cs` (in-repo, original Blowfish_CFB64 cipher)
- `/Users/vitor/conquer-server/src/Redux/Objects/Player.cs` (StartExchange:1377, CompleteExchange:1382, DirectSend:1336, Populate:746)
- `/Users/vitor/conquer-server/src/Redux/Network/GameServer.cs` (OnConnect:56, OnReceive:65, Process_MsgConnectPacket:301 — NEW_ROLE/ANSWER_OK)
- `/Users/vitor/conquer-server/src/Redux/Packets/Game/[1006]HeroInformation.cs`, `[1052] Connect.cs`, `[1001] Register.cs`
- `/Users/vitor/conquer-server/src/Redux/Constants.cs` (NEW_ROLE_STR, REPLY_OK_STR, MSG ids), `/src/Redux/Common.cs` (ENCRYPTION_KEY, SERVER_SEAL)
- `/Users/vitor/conquer-server/src/Packets/MsgConnect.cs` (net8 GameHandler), `/src/Packets/MsgUserInfo.cs`, `/src/Network/ClientSession.cs`, `/src/Network/TokenStore.cs`, `/src/Redux/PacketRouter.cs`, `/src/Redux/NetworkListener.cs`, `/src/Crypto/TQCipher.cs`
- `/Users/vitor/conquer-server/src/init.sql`, `/src/Database/CharacterRepository.cs`, `/src/docker-compose.yml`, `/scripts/dotnet`, `/src/Dockerfile`
- https://github.com/conquer-online/comet (TQCipher origin; Security/ has NO DH — 5017 uses TQCipher only)
- https://github.com/conquer-online/redux (Pro4Never 5065 — the DH+Blowfish lineage this fork derives from)
- https://github.com/shekohex/coemu (Rust, 5017 — no DH game cipher)
- https://asecuritysite.com/csharp/bc_ciphers_blowfish , BouncyCastle.NET (`BlowfishEngine`, `CfbBlockCipher`, `DHBasicAgreement`)
