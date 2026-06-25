---
spec: conquer-online-server
phase: research
created: 2026-06-25
---

# Research: conquer-online-server

## Decision (Updated 2026-06-25)

**Forking COServer Redux targeting patch 5065.** Initial research recommended Comet@5017 for its clean architecture, but after review the user chose Redux because it has substantially more game logic already implemented (combat, items, guilds, NPCs) — accepting fewer pending features is preferable to building everything from scratch. The target client is therefore **CO patch 5065**.

Modernization work required on Redux:
- .NET Framework 4.0 → .NET 8 (major effort: async rewrite, API surface changes)
- NHibernate → Dapper or EF Core 8 (remove legacy ORM)
- x86-only → AnyCPU (remove platform constraint)
- `ManagedOpenSsl.dll` → `System.Security.Cryptography` built-in (remove Windows-only binary dep)
- Synchronous sockets → async/await (networking layer rewrite)
- Add Dockerfile + Docker Compose (MariaDB + server)
- Add Linux runtime support (required for Brazilian VPS)
- MySQL 5.6 → MySQL 8 / MariaDB

Redux game features already in place (worth keeping after modernization):
- Auth + login flow (embedded, needs splitting if desired)
- Character system
- Item system (substantially implemented per v3.0 notes)
- Combat (substantially implemented)
- Guilds
- NPC/shops (partial — monster AI is a known TODO)
- Map loading via TinyMap.dll (must port to managed .NET 8 code)

---

## Executive Summary (Original Research)

**COServer Redux** is a .NET Framework 4.0 / C# monolith targeting CO patch 5065. It uses NHibernate (legacy ORM), targets x86 only, and cannot run on Linux without major surgery. **An alternative base was identified: `conquer-online/comet` branch `5017`** — a clean, .NET 6 / async C# skeleton with Docker support and working auth-to-login. However, the user has decided to proceed with Redux (5065) for its more complete game feature set, accepting the modernization cost.

---

## COServer Redux Repository Details

| Field | Value |
|-------|-------|
| Canonical URL | https://github.com/conquer-online/redux |
| Also mirrored at | https://gitlab.com/conquer-online/servers/redux |
| Notable fork | https://github.com/luckymouse0/Redux-Conquer-Online-Server |
| Another fork | https://github.com/menaconan/Redux |
| Original author | Pro4Never |
| Latest release | v3.0.5 (Aug 23, 2023) |
| Stars / forks | 4 / 2 |
| License | Not stated (community release, assumed public domain by author intent) |
| Target patch | **5065** (NOT 5017) |
| Language | C# 100% |
| .NET version | **.NET Framework 4.0** (confirmed via Redux_Fang.csproj `<TargetFrameworkVersion>v4.0`) |
| Platform target | **x86 only** |
| ORM | NHibernate 3.0 |
| Crypto dep | ManagedOpenSsl (DLL, not NuGet) |
| DB | MySQL 5.6 |
| Map util | TinyMap.dll (separate project) |
| Solution structure | Redux/ + TinyMap/ + Redux.sln + Nov_16_Backup.sql |
| Docker support | None |
| Linux support | None (x86 + Windows Forms refs + ManagedOpenSsl.dll) |

### Redux Codebase: What's Inside

The Redux codebase is a **monolithic single-project server** (no Account/Game split). Key subsystems inferred from dependencies and community docs:

- Networking: raw TCP sockets, synchronous
- Auth: embedded in the main server process (no separate auth server)
- ORM: NHibernate with XML mapping files (legacy pattern)
- NPC/AI: partial (known TODO in forks: monster spawns, AI)
- Items/Guilds/Combat: substantially implemented (v3.0 described as "fully functional")
- Map: TinyMap.dll handles DMAP parsing
- Crypto: ManagedOpenSsl.dll (external binary, Windows-only)

---

## Better Starting Point: Comet@5017

| Field | Value |
|-------|-------|
| URL | https://github.com/conquer-online/comet (branch: `5017`) |
| GitLab mirror | https://gitlab.com/world-conquer-online/comet |
| Author | Gareth Jensen ("Spirited") |
| License | Academic/non-profit; DMCA Sec. 103(f) reverse engineering |
| .NET version | **.NET 6** (upgrade path to .NET 8 is a minor version bump) |
| Language | C# 100%, async/await throughout |
| DB | MariaDB / MySQL (legacy auth mode) |
| Docker | Yes — `dockerfile` + `compose.debug.yml` + `compose.release.yml` |
| Linux | Yes — runs natively on dotnet/runtime:6.0 Linux images |
| Auth | Separate `Comet.Account` server (port 9958) |
| Game | Separate `Comet.Game` server (port 5816) |
| RPC | `Comet.Shared` project defines inter-server RPC messages |
| Tests | `Comet.Account.Tests`, `Comet.Core.Tests`, `Comet.Network.Tests` |
| Status | Skeleton — auth + login + character load working; game features minimal |

### Comet@5017 Directory Structure

```
/
├── dockerfile
├── compose.debug.yml         # MariaDB + phpMyAdmin + Account + Game containers
├── compose.release.yml
├── Comet.sln
├── sql/
│   ├── comet.account.sql
│   ├── comet.game.sql
│   └── upgrades/
├── src/
│   ├── Comet.Account/        # Auth server
│   │   ├── Database/Models/  # DbAccount, etc.
│   │   ├── Packets/          # MsgAccount (1051), MsgConnectEx (1055)
│   │   ├── States/Client.cs
│   │   ├── Kernel.cs, Program.cs, Server.cs
│   ├── Comet.Game/           # Game server
│   │   ├── Database/Models/  # DbCharacter
│   │   ├── Packets/          # MsgConnect, MsgAction, MsgUserInfo, MsgRegister, MsgItem, MsgTalk
│   │   ├── States/Client.cs
│   │   ├── Remote.cs         # RPC callable from Account server
│   │   ├── Kernel.cs, Program.cs, Server.cs
│   ├── Comet.Core/           # Utilities, language extensions
│   ├── Comet.Network/        # TCP foundation, cipher abstraction
│   │   └── Security/
│   │       ├── ICipher.cs
│   │       ├── TQCipher.cs   # TQ proprietary XOR stream cipher
│   │       └── RC5.cs        # RC5 for password decryption
│   └── Comet.Shared/         # Inter-server RPC definitions
└── tests/
    ├── Comet.Account.Tests
    ├── Comet.Core.Tests
    └── Comet.Network.Tests
```

---

## CO 5017 Protocol Overview

### Patch Position

| Patch | Crypto change |
|-------|--------------|
| ≤5017 | TQ Cipher (proprietary XOR) for game traffic; RC5 for login password |
| 5018 | Changed to Blowfish + DH key exchange |
| 5173 | RC5 with seed added to account server |
| ≥5187 | SRP-6A for login (replaces RC5) |

**5017 sits just before the Blowfish/DH transition.** This means crypto is simpler than later patches.

### Auth Handshake Sequence (5017)

```
Client → Account Server (port 9958)
  MsgAccount (1051): Username[16] + Password[16 RC5-encrypted] + Realm[16]

Account Server:
  1. Decrypt password with RC5 (hardcoded 16-byte seed)
  2. Lookup account by username
  3. Validate against salted SHA1 hash in DB
  4. If OK: RPC to Game server → get token
  5. Send MsgConnectEx (1055): Token(ulong) + GameIP(16) + GamePort(uint)
  6. Disconnect client

Client → Game Server (port 5816)
  MsgConnect (1052): Token(ulong) + character name etc.

Game Server:
  1. Validate token against Kernel.Logins cache
  2. Call client.Cipher.GenerateKeys(new object[] { token })
     → TQCipher derives K2 keys from token as seed
  3. Lookup character by AccountID
  4. If exists: send MsgUserInfo (1006) → character select UI
  5. If not: accept MsgRegister (1001) → character creation
```

### Cipher Details

**TQCipher** (game traffic):
- Proprietary TQ Digital XOR stream cipher
- Two 512-byte key tables (K1, K2), each initialized from static seed `{0x9D, 0x0F, 0xFA, 0x13, 0x62, 0x79, 0x5C, 0x6D}`
- Decryption uses K (=K1 or K2), encryption always uses K1
- Key derivation: `GenerateKeys(token)` → derives K2 from token, switches K → K2 after game server auth
- XOR operation: `dst[i] = (src[i] ^ 0xAB) rotated 4 bits ^ K[x&0xff] ^ K[(x>>8)+0x100]`

**RC5** (login password only):
- Standard RC5-32/12/16 (word=16, rounds=12, key=16 bytes)
- Hardcoded key seed; no DH exchange in 5017 account server
- Only used to decrypt the 16-byte password field in MsgAccount

**No DH key exchange in 5017.** DH was added in patch 5018.

### Key Packet Types

| ID | Name | Direction | Purpose |
|----|------|-----------|---------|
| 1051 | MsgAccount | C→AccSrv | Login credentials |
| 1055 | MsgConnectEx | AccSrv→C | Game server redirect |
| 1052 | MsgConnect | C→GameSrv | Token + char name auth |
| 1006 | MsgUserInfo | GameSrv→C | Character info (level, class, mesh, etc.) |
| 1001 | MsgRegister | C→GameSrv | Create character |
| 1010 | MsgAction | Both | Actions incl. movement, spawn, portal |
| 1009 | MsgItem | Both | Item management |
| 2101 | MsgTalk | Both | Chat |

### Movement / Map Packets (MsgAction 1010)

Key action subtypes for Milestone 1:

| Subtype | Name | Notes |
|---------|------|-------|
| 74 | LoginSpawn | Initial spawn: sends MapID + X + Y back to client |
| 80 | CharacterDirection | Updates facing direction |
| 85 | MapPortal | Portal transition |
| 86 | MapTeleport | Direct teleport |
| 102 | MapQuery | Client requests map info |
| 134 | MapJump | Jump movement |
| 135 | MapRemoveSpawn | Remove from visible area |

---

## Database Schema Overview

### Account DB (`comet.account`)

| Table | Key Columns |
|-------|-------------|
| `account` | AccountID PK, Username(16) unique, Password(70) SHA1+salt, Salt(45), AuthorityID, StatusID, IPAddress |
| `account_authority` | AuthorityID, AuthorityName (Player/Mod/GM/PM/Admin) |
| `account_status` | StatusID, StatusName (Registered/Activated/Limited/Locked/Banned) |
| `logins` | Timestamp, AccountID FK, IPAddress |
| `realm` | RealmID, Name(16), GameIPAddress, RpcIPAddress, GamePort(5816), RpcPort(5817) |

Password hashing: SHA1 with MD5-generated salt, via DB trigger on INSERT.

### Game DB (`comet.game`)

| Table | Key Columns |
|-------|-------------|
| `character` | CharacterID PK (auto_increment from 1000000), AccountID, Name(15) unique, Mesh, Avatar, Hairstyle, Silver, Jewels, CurrentClass, PreviousClass, Rebirths, Level, Experience, MapID, X, Y, Virtue, Strength, Agility, Vitality, Spirit, AttributePoints, HealthPoints, ManaPoints, KillPoints, Registered |

Default spawn: MapID=1002, X=430, Y=380 (Twin City map).

Character creation defaults: Level=1, Silver=1000, Strength=4, Agility=6, Vitality=12, Spirit=0, MapID=1010, X=61, Y=109.

---

## What to Keep vs Rewrite

### Recommended Base: Comet@5017 (NOT Redux)

| Component | Keep As-Is | Upgrade/Modify | Rewrite |
|-----------|-----------|----------------|---------|
| TQCipher.cs | Yes (complete, correct) | — | — |
| RC5.cs | Yes (complete) | — | — |
| ICipher interface | Yes | — | — |
| TcpServerActor (network layer) | Yes (async, solid) | — | — |
| Comet.Account auth flow | Yes | — | — |
| MsgAccount/MsgConnectEx | Yes | — | — |
| MsgConnect (game auth) | Yes | — | — |
| MsgUserInfo | Yes | — | — |
| MsgRegister (char create) | Yes | — | — |
| MsgAction (skeleton) | Keep structure | Add all subtypes | — |
| MsgItem | Keep structure | Implement actions | — |
| MsgTalk | Keep structure | Extend | — |
| Database schema | Yes (extend) | Add items/maps tables | — |
| Docker Compose | Yes | Update .NET 6→8 base images | — |
| .NET 6 → .NET 8 | — | Bump TargetFramework, update images | — |
| NHibernate (Redux only) | No — don't port | Replace with EF Core 8 | — |
| Movement/pathfinding | Not in Comet | — | Build new |
| Map loading (DMAP) | Not in Comet | — | Build new |
| NPC/AI | Not in Comet | — | Build new |
| Item system | Packet skeleton only | — | Build new |
| Combat | Not in Comet | — | Build new |

### What Redux Has That Comet Doesn't

Redux (5065) has substantially more game logic: items, guilds, combat, NPCs, shops. However:
- Wrong patch version (5065 vs 5017)
- Cannot run on Linux
- .NET Framework 4.0 → .NET 8 migration is a major effort (Windows Forms refs, x86-only, ManagedOpenSsl.dll)
- NHibernate ORM is dead/legacy
- Monolithic architecture is harder to modernize

**Verdict: The game logic in Redux is not worth porting. Build on Comet@5017 instead.**

---

## Feasibility Assessment

| Aspect | Assessment | Notes |
|--------|------------|-------|
| Fork + modernize (Comet@5017 → .NET 8) | High | .NET 6→8 is a 2-line change in csproj + image tag update |
| Docker on Linux VPS | High | Dockerfile already exists, just update base image tag |
| Auth handshake milestone | High | Already fully implemented in Comet@5017 |
| Login + char load milestone | High | MsgConnect + MsgUserInfo + MsgRegister all implemented |
| Movement milestone | Medium | MsgAction packet exists; map loading + pathfinding logic must be built |
| From-scratch alternative | Low value | Would take months; Comet already has 2,000+ lines of network/crypto done |

### Effort Estimate

| Phase | Effort | What's needed |
|-------|--------|--------------|
| Fork + .NET 8 upgrade | XS (1-2 days) | Bump .csproj TargetFramework, update Docker image tags, verify build |
| Auth → login → char load (M1) | S (1-2 weeks) | Already in Comet; wire up map data, fix any 5017 packet deltas |
| Movement system | M (2-4 weeks) | Implement DMAP loader, MsgMove packet, basic pathfinding |
| Map + spawns (NPC/monsters) | L (4-8 weeks) | Full map/NPC system, DAT file parsing |
| Item system | L (4-8 weeks) | Inventory, equipment, shops |
| Combat | XL (ongoing) | Skill system, damage formulas, PK |

---

## .NET 8 Upgrade Path (Comet@5017)

1. In each `.csproj`: change `<TargetFramework>net6.0</TargetFramework>` to `<TargetFramework>net8.0</TargetFramework>`
2. In `dockerfile`: change `mcr.microsoft.com/dotnet/sdk:6.0` → `mcr.microsoft.com/dotnet/sdk:8.0` and runtime image similarly
3. Run `dotnet restore && dotnet build` — no breaking changes expected between .NET 6 and .NET 8 for this codebase
4. Linux ARM64: use `mcr.microsoft.com/dotnet/sdk:8.0` (multi-arch, supports arm64 for Brazilian VPS)

---

## Risks and Unknowns

| Risk | Severity | Mitigation |
|------|----------|------------|
| Comet@5017 has no map loading | High | Must implement DMAP parser (format documented in wiki) |
| 5017 client legality for testing | Medium | Client is abandonware-era; community distributes via cooldown.dev; TQ has not pursued takedowns of educational servers |
| Comet license is "non-commercial" | Medium | Fork for personal/private use is fine; running a public server commercially would violate it |
| RC5 seed hardcoded — same for all Comet installs | Low | For private use this is fine; no key rotation |
| MsgAction movement subtypes incomplete | Medium | Must implement all jump/walk subtypes (134, 135, etc.) |
| Redux game data (DAT files) | High | Client DAT files contain ItemType.dat, Monster.dat, GameMap.dat — these are TQ IP, cannot redistribute; must ship server without them or have player supply from their client |
| MariaDB legacy auth mode required | Low | `--default-authentication-plugin=mysql_native_password` needed in Docker Compose; already handled in Comet's compose file |
| Brazilian VPS latency | Low | São Paulo VPS providers (OVHcloud, UltaHost) have dotnet8 support; no special issues |

---

## Verification Tooling

### How to Test the Server Works with a Real Client

**Client version**: Conquer Online 5017 client is available from the CO private server community (cooldown.dev guide). It is not sold commercially — TQ moved past this version. The community at cooldown.dev maintains download guides for specific patch clients for development/testing purposes.

**Client loader**: The 5017 client needs a loader (e.g., `CIDLoader`) that patches the client to connect to a custom IP instead of TQ's servers. Set `GameIP` and `AccountIP` in the loader config.

**Verification sequence for M1**:
1. Start MariaDB container (`docker compose -f compose.debug.yml up db`)
2. Import `comet.account.sql` and `comet.game.sql`
3. Insert a test account into `account` table (DB trigger auto-hashes password)
4. Insert a `realm` row pointing to your game server IP/port
5. Start Account server → Game server
6. Launch 5017 client via loader pointing to Account server IP:9958
7. Log in with test credentials → should receive game server redirect
8. On game server: create or load character
9. Should spawn on Twin City map (MapID 1002)
10. Verify movement by walking (MsgAction 134 / 74)

**Tools**:
- Wireshark + custom CO dissector to inspect raw packets
- `netcat` to probe TCP ports
- `mysql` CLI or phpMyAdmin (Comet Docker exposes port 8081 for phpMyAdmin)

## Verification Tooling (Quality Commands)

The Comet project uses dotnet CLI directly:

| Type | Command | Source |
|------|---------|--------|
| Build | `dotnet build` | Comet project convention |
| Restore | `dotnet restore` | Comet project convention |
| Unit Test | `dotnet test` | tests/ directory present |
| Run Account | `dotnet run --project src/Comet.Account` | Standard dotnet |
| Run Game | `dotnet run --project src/Comet.Game` | Standard dotnet |
| Docker up | `docker compose -f compose.debug.yml up` | compose.debug.yml |
| TypeCheck | (included in `dotnet build`) | — |
| Lint | Not configured | — |

**No package.json, no Makefile found in Comet project.**

---

## Related Specs

No other specs exist in this project yet.

---

## Recommended Next Steps

1. **Fork `conquer-online/comet` at branch `5017`** into this repo — do NOT fork Redux.
2. **Bump .NET 6 → .NET 8**: update all `.csproj` TargetFramework tags and Dockerfile base images.
3. **Verify Docker build locally on Windows 11**: `docker compose -f compose.debug.yml up` should bring up MariaDB + both servers.
4. **Obtain 5017 client**: follow cooldown.dev client download guide; get CIDLoader; configure to point at `127.0.0.1:9958`.
5. **M1 goal — auth → login → char load**: all packet handling already exists in Comet@5017; test the full flow end-to-end with the real client.
6. **M2 goal — movement**: implement DMAP map file loader (format documented at conquer-online.github.io/wiki), add MsgMove/MsgWalk packet handlers, implement LoginSpawn (ActionType 74) fully.
7. **Reference the wiki** at https://conquer-online.github.io/wiki/ for all packet IDs, DAT file formats, and game constants.
8. **Reference crypto snippets** at https://gitlab.com/conquer-online/wiki/-/snippets for TQCipher, RC5, DH reference implementations.
9. **Keep Redux as reference only** — useful for understanding game mechanics/formulas but don't port code.

---

## Sources

| Source | Key Point |
|--------|-----------|
| https://github.com/conquer-online/redux | Canonical Redux repo — 5065, .NET Fx 4.0 |
| https://github.com/luckymouse0/Redux-Conquer-Online-Server/blob/master/Redux/Redux_Fang.csproj | Confirmed .NET Framework 4.0, NHibernate, ManagedOpenSsl, x86 |
| https://github.com/conquer-online/comet | Comet repo — .NET 6, Docker, 5017 branch |
| https://github.com/conquer-online/comet/tree/5017 | 5017 branch structure |
| https://github.com/conquer-online/comet/blob/5017/dockerfile | .NET 6 multi-stage Docker build |
| https://github.com/conquer-online/comet/blob/5017/compose.debug.yml | Docker Compose: ports 9958 (account), 5816 (game), 8081 (phpMyAdmin) |
| https://github.com/conquer-online/comet/blob/5017/src/Comet.Network/Security/TQCipher.cs | Full TQCipher source |
| https://github.com/conquer-online/comet/blob/5017/src/Comet.Network/Security/RC5.cs | RC5 for login password decryption |
| https://github.com/conquer-online/comet/blob/5017/src/Comet.Account/Packets/MsgAccount.cs | Auth packet 1051 — RC5 decrypt, salted SHA1 validation |
| https://github.com/conquer-online/comet/blob/5017/src/Comet.Account/Packets/MsgConnectEx.cs | Auth response 1055 — token + game server IP/port |
| https://github.com/conquer-online/comet/blob/5017/src/Comet.Game/Packets/MsgConnect.cs | Game auth 1052 — token validation, cipher key gen |
| https://github.com/conquer-online/comet/blob/5017/src/Comet.Game/Packets/MsgAction.cs | Movement/action subtypes |
| https://github.com/conquer-online/comet/blob/5017/src/Comet.Game/Packets/MsgUserInfo.cs | Char info packet 1006 |
| https://github.com/conquer-online/comet/blob/5017/src/Comet.Game/Packets/MsgRegister.cs | Char creation 1001 |
| https://github.com/conquer-online/comet/blob/5017/sql/comet.account.sql | Full account DB schema |
| https://github.com/conquer-online/comet/blob/5017/sql/comet.game.sql | Full game DB schema (character table) |
| https://conquer-online.github.io/wiki/ | Official CO dev wiki: 200+ packet types, 6 crypto mechanisms |
| https://gitlab.com/conquer-online/wiki/-/snippets | Crypto snippets: TQCipher, RC5, DH, Blowfish, CAST5 |
| https://cooldown.dev/topic/41-5017-login-sequence/ | Community 5017 login sequence (packet IDs 1051→1055→1052) |
| https://github.com/bausshf/5017-server | D-language 5017 server (alternative reference) |
| https://github.com/sgbj/conquer | ASP.NET Core + EF Core 4330 server (modern C# pattern reference) |
| https://spirited.io/project/comet/ | Comet project page — .NET Core async/await architecture notes |
