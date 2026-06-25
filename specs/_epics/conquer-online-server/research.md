# Research: conquer-online-server

## Executive Summary

The project starts from a greenfield directory (no existing code) and will fork or closely reference an existing C# CO emulator (COServer Redux being the most complete public reference). The classic CO server naturally splits into two separate processes — an Account/Auth server and a Game server — matching how TQ Digital's original architecture works. Subsystem boundaries are clear and well-documented across the private server community, making this a suitable epic for sequential spec-driven development.

---

## External Research

### Available Codebases

| Project | Language | Protocol Version | Database | Notes |
|---------|----------|-----------------|----------|-------|
| **COServer Redux** | C# (.NET Framework/Core) | 5017–5187 | MySQL | Most feature-complete public emu; modular design; active forks on GitHub/elitepvpers |
| **Fanon** | C# | 5017–5076 | MySQL | Older but well-documented; simpler codebase good for learning |
| **COEmu** | C++ | 5017 | MySQL | High performance; Windows-native; harder to port to Linux |
| **OpenCO** | Java | 5017 | MySQL | Less maintained; good async networking via Netty |
| **Project Lotus** | C# | 5187+ | MySQL/MariaDB | Modern .NET 6+; Docker-friendly |

**Recommendation:** Fork **COServer Redux** or **Project Lotus** as the base. Both are C# with MySQL, have clear module separation, and can target Linux via .NET 8.

### Architecture Patterns

All major CO emulators follow a **two-process architecture**:

```
[CO Client]
    |
    | TCP (port 9958 — auth)
    v
[Account Server]  ←→  [MySQL: accounts, characters]
    |
    | Internal redirect (new IP:port sent to client)
    | TCP (port 5816 — game)
    v
[Game Server]  ←→  [MySQL: world state, items, maps]
```

The client connects to the auth server first, authenticates, receives a one-time token + game server IP/port, then reconnects to the game server.

### CO Classic Protocol

**Encryption:**
- DH (Diffie-Hellman) key exchange at handshake
- RC5 symmetric encryption after key exchange
- Some classic versions (5017 era) use a simpler XOR-based cipher before RC5 was adopted; check target client version

**Packet structure (all packets):**
```
[2 bytes] packet length (little-endian, includes header)
[2 bytes] packet type/ID
[N bytes] payload (varies by type)
```

Common classic packet IDs:
- `0x3ED` (1005) — Login request
- `0x3EE` (1006) — Login response / character select
- `0x3F2` (1010) — Transfer to game server
- `0x1388` (5000) — Entity spawn
- `0x13B4` (5044) — Walk/run movement
- `0x138A` (5002) — Chat message
- `0x1393` (5011) — Attack/combat action
- `0x1391` (5009) — Item action (pick up, equip, drop)

### Auth Flow

1. Client connects to Account Server (port 9958)
2. Server sends DH public key
3. Client sends encrypted login packet (account + password, MD5 hashed)
4. Server validates credentials against DB
5. Server selects/creates character, sends redirect: game server IP + port + one-time token
6. Client disconnects from auth, connects to Game Server
7. Game server validates token, loads character, begins world session

### Game Server Architecture (Subsystems)

| Subsystem | Responsibility | Coupling |
|-----------|---------------|----------|
| **Network layer** | Async TCP accept/read/write, packet framing, encryption | Low — pure I/O |
| **Auth module** | Account validation, password hashing, token issuance | DB only |
| **Session manager** | Per-client state machine, packet routing | Network + game modules |
| **World / Map engine** | Region loading (DAT files), entity placement, spatial queries | Item, character modules |
| **Character system** | Load/save, stats, level, attributes | DB, item system |
| **Item system** | Inventory, equipment, item DB (DATa files or MySQL) | Character, world |
| **Combat system** | Melee/magic damage, PK mode, death handling | Character, item, AI |
| **NPC / Monster AI** | Spawn, patrol, aggro, loot tables | World, combat |
| **Chat / Social** | Trade chat, team, guild, private messages | Session manager |
| **Admin / GM tools** | Commands, kick/ban, item spawn | All modules |

### Data Formats

- **Map files:** `.dmap` (COServer Redux format) or `.dat`/`.dma` binary; contain passability grid, portal definitions, region ID
- **Item data:** Usually MySQL table seeded from client DAT files or community-shared SQL dumps
- **Monster/NPC data:** MySQL tables (spawn coords, stats, loot tables)
- **Character data:** MySQL — `characters`, `items`, `skills`, `quests` tables

### NPC / Monster AI Pattern

CO emulators universally implement a tick-based state machine:
```
IDLE → (player in aggro range) → CHASE → (in attack range) → ATTACK → (target dead/fled) → IDLE
Patrol variant: IDLE → WALK_WAYPOINT → IDLE (loop)
```
Tick rate: typically 500ms for AI, 200ms for movement, 100ms for network flush.

### Combat System (Classic CO)

- Melee: ATK stat vs DEF stat, random damage range ±15%
- Magic: spell cost in mana, fixed damage tables per spell level
- PK system: PK flag set on attack; red/black name based on PK points
- Death: drop equipped items at configurable rate; lose exp
- Durability: items degrade on death; repair via NPC

### Pitfalls in CO Emulator Development

1. **Windows-only socket APIs** in older C# emus — use `System.Net.Sockets` async APIs to stay cross-platform
2. **Hardcoded IPs/ports** in config — must externalize for local→VPS migration
3. **Tight DAO coupling** — many emus mix DB logic into game logic; worth separating early
4. **Map file encoding** — DAT files use a non-standard binary encoding; parse carefully
5. **Packet ID drift** — different client versions use different packet IDs; lock down target client version early
6. **Single-threaded game loop** — some emus process all packets on one thread; plan async I/O from the start

---

## Codebase Analysis

### Current State
- **Greenfield** — no existing source code, no git repo, no build files
- Working directory: `C:\Users\Windows\conquer-server`
- Only files present: `.claude/settings.local.json`, `specs/_epics/conquer-online-server/`

### Environment
- **Dev:** Windows 11 Pro x86-64, PowerShell 5.1
- **Deploy target:** Linux VPS (Brazil), likely Ubuntu 22.04 LTS
- **Cross-platform strategy:** .NET 8 with `dotnet publish -r linux-x64` or Docker

### Recommended Tech Stack

| Component | Choice | Reason |
|-----------|--------|--------|
| Language | C# / .NET 8 | Largest CO emu reference pool; cross-platform; strong async networking |
| Database | MySQL 8 / MariaDB | All CO emu SQL schemas are MySQL-compatible |
| ORM | Dapper (micro-ORM) | Most CO emus use raw SQL or Dapper; EF Core too heavy for game-loop |
| Networking | `System.Net.Sockets` (async) | Built-in, cross-platform, adequate throughput |
| Container | Docker | Parity between Windows dev and Linux VPS |
| VCS | Git + GitHub | Standard |

---

## Feasibility Assessment

| Aspect | Assessment | Notes |
|--------|-----------|-------|
| Protocol reverse-engineering | Low risk | Well-documented in community; COServer Redux is reference |
| C# .NET 8 on Linux VPS | Low risk | .NET 8 LTS is first-class Linux |
| Brazilian VPS latency | Medium risk | ~150ms RTT from BR to some EU hosts; choose BR datacenter (AWS sa-east-1 or local providers like HostGator BR, Contabo BR) |
| Map file parsing | Medium risk | Binary format; parsers exist in COServer Redux |
| Classic client compatibility | Medium risk | Must pin to a specific client version; use 5017 or 5076 era client for simplicity |
| Full AI / combat parity | High effort | Can be deferred after auth+movement are working |

---

## Recommendations for Requirements

1. **Pin to client version 5017** — most widely documented; most existing SQL schemas target it
2. **Fork COServer Redux** as the reference; do not blindly copy — use it to understand packet IDs and DB schema, then implement cleanly in .NET 8
3. **Start with the auth server + basic game connection** (player can log in, appear in world, move) — this is the vertical slice that validates the whole stack
4. **Defer AI and combat** until character/item/map systems are stable
5. **Containerize early** — build Docker images from day one so local→VPS migration is a config change, not a porting effort
6. **Externalize all config** (DB connection, server IPs, ports, rates) via `appsettings.json` / env vars

---

## Open Questions

1. Which specific CO client version is the target? (5017, 5076, 5165?)
2. Will the user source CO client files and game data (maps, items) from community dumps, or extract from a client?
3. Single-realm or multi-realm from the start?
4. Target player capacity? (50 concurrent? 500? affects architecture choices)
5. Is the goal a playable private server for personal/community use, or a learning/portfolio project?

---

## Sources

- COServer Redux: community GitHub forks and elitepvpers.com threads
- Fanon CO emulator source and documentation
- CO Protocol wikis (ragezone, epvpscripts community documentation)
- COServer Redux packet handler source (C# reference)
- .NET 8 cross-platform networking docs: learn.microsoft.com
