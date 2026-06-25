# Epic: conquer-online-server

## Vision

Build a playable Conquer Online classic private server (client version 5017) in C# / .NET 8 with MySQL, running locally on Windows 11 during development and deployable to a Brazilian Linux VPS via Docker. The server covers the full player journey: authentication, character management, open-world movement, item handling, melee combat, and basic monster AI — all implemented cleanly against a well-understood protocol reference (COServer Redux) rather than forked from legacy code.

## Success Criteria

- A CO 5017 client can connect, authenticate, load a character, walk around a map, fight monsters, and pick up loot
- Server runs identically on Windows 11 (dev) and Docker on Linux (VPS) via config-only changes
- All subsystem boundaries are enforced via interfaces; no module reaches into another's DB layer
- Packet encryption (DH + RC5) passes round-trip validation against the real client

---

## Specs

### 1. project-foundation

**Goal:** As a developer, I can clone the repo, run `docker compose up`, and have a MySQL instance + two placeholder server processes start without error, so the full team has a working local environment from day one.

**Size:** S

**Depends on:** none

**Acceptance criteria:**
- [ ] Git repo initialized with `.gitignore` covering `bin/`, `obj/`, `.env`, `*.user`
- [ ] .NET 8 solution with two projects: `ConquerServer.Auth` and `ConquerServer.Game`, plus shared `ConquerServer.Core` class library
- [ ] `docker-compose.yml` starts MySQL 8, Auth process, Game process; all three reach healthy state
- [ ] MySQL schema baseline applied via init SQL script (empty tables: `accounts`, `characters`, `items`, `maps`, `monsters`, `npcs`)
- [ ] `appsettings.json` + env-var override for all ports, DB connection string, server IPs
- [ ] GitHub Actions (or equivalent) CI pipeline: `dotnet build` + `dotnet test` on push
- [ ] `README.md` with local setup steps (clone → docker compose → connect)

**Interface contracts (outputs this spec provides to later specs):**
- Solution structure: `ConquerServer.Core`, `ConquerServer.Auth`, `ConquerServer.Game` projects
- `appsettings.json` schema (ports, DB DSN, game server advertised IP)
- MySQL schema baseline (empty tables, foreign keys, indexes)
- `docker-compose.yml` service names and environment variable contract
- CI pipeline definition

---

### 2. network-core

**Goal:** As a server developer, I have a reusable async TCP server abstraction with packet framing, DH key exchange, and RC5 encryption so all higher-level servers share one networking foundation.

**Size:** M

**Depends on:** project-foundation

**Acceptance criteria:**
- [ ] `TcpServer` class: accepts connections, manages `TcpSession` lifecycle, async read loop
- [ ] Packet framing: reads `[2-byte length][2-byte type][N-byte payload]` from stream; handles partial reads and multi-packet buffers
- [ ] DH key exchange: server generates ephemeral DH pair, sends public key to client, derives shared secret
- [ ] RC5 cipher: encrypt/decrypt with the derived session key; passes test vectors from COServer Redux
- [ ] `IPacketHandler` interface: `Task HandleAsync(TcpSession session, ushort packetType, ReadOnlyMemory<byte> payload)`
- [ ] `PacketDispatcher`: routes incoming `packetType` to registered `IPacketHandler` implementations
- [ ] `TcpSession`: exposes `SendAsync(ushort type, byte[] payload)` with encryption applied
- [ ] Unit tests: framing round-trip, RC5 encrypt/decrypt, DH shared-secret agreement
- [ ] Integration test: loopback client sends encrypted packet, server dispatches to handler

**Interface contracts (outputs this spec provides to later specs):**
- `IPacketHandler` interface (all packet logic in auth + game implements this)
- `TcpServer` class (Auth and Game server processes instantiate this)
- `TcpSession` class with `SendAsync`, `Disconnect`, `RemoteEndPoint`
- `PacketDispatcher` with `Register<T>()` method
- `PacketReader` / `PacketWriter` helpers (span-based, little-endian)
- RC5 + DH implementations (usable by auth and game independently)

---

### 3. auth-server

**Goal:** As a CO 5017 client, I can enter my account credentials and be redirected to the game server with a one-time token, so the authentication flow is fully functional end-to-end.

**Size:** M

**Depends on:** network-core, project-foundation

**Acceptance criteria:**
- [ ] `ConquerServer.Auth` process listens on port 9958 (configurable)
- [ ] Handles packet `0x3ED` (1005) login request: reads account + MD5-hashed password
- [ ] Validates credentials against `accounts` table via Dapper; rejects unknown/wrong-password with error response
- [ ] Generates a cryptographically random one-time token; stores in `accounts` table or in-memory cache with TTL
- [ ] Sends redirect packet `0x3F2` (1010) containing game server IP, port 5816, and token
- [ ] Failed login sends packet `0x3EE` (1006) with error code
- [ ] Account lockout after N failed attempts (configurable, default 5)
- [ ] Accounts table: `id`, `username`, `password_md5`, `last_login`, `banned`, `token`, `token_expiry`
- [ ] Integration test: mock CO client performs DH handshake, sends login packet, receives redirect

**Interface contracts (outputs this spec provides to later specs):**
- `accounts` table schema (game server reads `token` + `token_expiry` for validation)
- One-time token format: `ulong`, 60-second TTL
- Game server redirect payload format (IP string, port ushort, token ulong)

---

### 4. game-session

**Goal:** As a logged-in player, I can connect to the game server with my token, have my character loaded from the database, and maintain a live session with heartbeat, so the game server can track who is online.

**Size:** M

**Depends on:** auth-server, network-core

**Acceptance criteria:**
- [ ] `ConquerServer.Game` process listens on port 5816 (configurable)
- [ ] On new connection, expects token-validation packet; validates token against `accounts` table (expiry check, single-use: invalidate on success)
- [ ] Loads character row from `characters` table via Dapper; populates `CharacterEntity` in-memory
- [ ] Session state machine: `HANDSHAKE → AUTHENTICATED → IN_WORLD → DISCONNECTED`
- [ ] Heartbeat/ping packet handled; session dropped after configurable idle timeout (default 60s)
- [ ] `SessionManager`: thread-safe dictionary of active sessions keyed by character ID
- [ ] On disconnect: session removed from `SessionManager`, character state saved to DB
- [ ] `characters` table: `id`, `account_id`, `name`, `map_id`, `x`, `y`, `level`, `exp`, `hp`, `mp`, `str`, `dex`, `spi`, `vit`, `money`, `pk_points`
- [ ] Integration test: mock client handshakes auth server, then connects to game server with valid token

**Interface contracts (outputs this spec provides to later specs):**
- `TcpSession` extended with `Character CharacterEntity` property
- `SessionManager.GetById(uint charId)`, `Broadcast(IEnumerable<TcpSession>, packet)`
- `CharacterEntity` POCO (all stat fields; used by character-system, combat-system, item-system)
- `characters` table schema
- Session state enum (world-map and character-system check `IN_WORLD` state)

---

### 5. world-map

**Goal:** As a game developer, I can load `.dmap` map files into memory, query tile passability, and look up which entities are near a given coordinate, so all movement and spatial systems have a foundation.

**Size:** M

**Depends on:** project-foundation

**Acceptance criteria:**
- [ ] `.dmap` binary parser: reads header, width/height, passability grid (0=blocked, 1=open), portal definitions
- [ ] `MapManager`: loads all maps at startup from a configured directory; exposes `GetMap(ushort mapId)`
- [ ] `GameMap`: exposes `IsPassable(int x, int y)`, `GetEntitiesInRadius(int x, int y, int radius)`
- [ ] Spatial entity index: sector-based (e.g., 18×18 cell sectors); supports add/remove/query by radius
- [ ] Portal definitions loaded: `from_map`, `from_x`, `from_y`, `to_map`, `to_x`, `to_y`
- [ ] `maps` table: `id`, `name`, `filename`, `weather`, `flags` (used for map metadata; binary data read from filesystem)
- [ ] Unit tests: parse a sample `.dmap` file; passability grid matches known tile data; radius query returns correct set

**Interface contracts (outputs this spec provides to later specs):**
- `MapManager` singleton (character-system, monster-AI, combat all call `GetMap`)
- `GameMap.IsPassable(x, y)` — movement validation
- `GameMap.AddEntity / RemoveEntity / GetEntitiesInRadius` — used by character-system for spawn/despawn/AoE
- `Portal` record type
- Map sector size constant (affects aggro radius calculation in monster-AI)

---

### 6. character-system

**Goal:** As a player, I can spawn into the world at my saved position, walk/run to new tiles, and have my position and stats persisted, so basic gameplay is functional.

**Size:** M

**Depends on:** game-session, world-map

**Acceptance criteria:**
- [ ] On entering world, character spawned into `GameMap` at `(map_id, x, y)`; nearby players receive entity-spawn packet `0x1388` (5000)
- [ ] Walk packet `0x13B4` (5044): server validates destination is passable via `MapManager`, updates position, broadcasts to players in view range
- [ ] Run packet: same as walk with 2-tile step; validated against same passability check
- [ ] View range: 18 tiles; players outside range do not receive movement updates
- [ ] Stat calculation: derived stats (ATK, DEF, HP max, MP max) computed from base stats + equipment; recalculated on equip change
- [ ] Level/exp: `GainExp(uint amount)` method; triggers level-up if threshold crossed; sends stat-update packet
- [ ] Character save: periodic auto-save every 60s + save on disconnect
- [ ] Despawn on disconnect: nearby players receive entity-remove packet
- [ ] Integration test: two mock clients connect, one walks, the other receives the movement broadcast

**Interface contracts (outputs this spec provides to later specs):**
- `CharacterEntity.ComputeStats()` — item-system calls after equip/unequip
- `CharacterEntity.GainExp(uint)` — combat-system calls on kill
- `CharacterEntity.TakeDamage(uint)` — combat-system calls; returns bool isDead
- `CharacterEntity.Position` (MapId, X, Y) — combat + AI reads this
- Spawn/despawn packet helpers (reused by monster-AI for NPC/monster entity packets)

---

### 7. item-system

**Goal:** As a player, I can pick up items from the ground, equip them to stat slots, and drop them, so items are a functional part of gameplay.

**Size:** M

**Depends on:** character-system

**Acceptance criteria:**
- [ ] `item_types` table seeded: `id`, `name`, `type`, `subtype`, `equip_slot`, `req_level`, `req_str`, `req_dex`, `atk_min`, `atk_max`, `def`, `dura_max`, `weight`
- [ ] `items` table (character-owned): `id`, `character_id`, `item_type_id`, `position` (inventory slot or equip slot), `dura`, `gem1`, `gem2`, `magical_type`, `magical_val`
- [ ] Inventory: 40 slots; equip slots: Head, Necklace, Ring×2, Weapon, Armor, Boots, Bag, Arrow
- [ ] Item action packet `0x1391` (5009): pick up (ground → inventory), equip (inventory → slot), unequip (slot → inventory), drop (inventory → ground)
- [ ] Drop: creates `GroundItem` entity on map with 60s despawn timer; visible to all players in range
- [ ] Pick up: removes `GroundItem`, adds to inventory; fails if inventory full
- [ ] Equip: validates req_level, req_str, req_dex against character stats; on success calls `CharacterEntity.ComputeStats()`
- [ ] `items` loaded from DB on character load; saved on any change
- [ ] Integration test: character equips a weapon, ATK stat increases

**Interface contracts (outputs this spec provides to later specs):**
- `EquipmentSlot` enum (combat-system reads weapon slot for ATK range)
- `ItemType` record (combat-system reads atk_min/atk_max, def)
- `InventoryManager.GetEquippedItem(EquipmentSlot)` — combat reads weapon stats
- `GroundItem` entity type (monster-AI uses same type for loot drops)
- `item_types` table (admin-tools references for item-give command)

---

### 8. combat-system

**Goal:** As a player, I can attack monsters and other players (in PK mode), deal damage based on stats, die and respawn, so combat is the core gameplay loop.

**Size:** L

**Depends on:** character-system, item-system, world-map

**Acceptance criteria:**
- [ ] Attack packet `0x1393` (5011): validates target in range (melee: 3 tiles), validates attacker not dead
- [ ] Damage formula: `damage = rand(weapon.atk_min, weapon.atk_max) + character.bonus_atk - target.def`; minimum 1; variance ±15%
- [ ] Result broadcast: attack-result packet sent to attacker + all players in view range of target
- [ ] `CharacterEntity.TakeDamage` called on target; if HP ≤ 0, death sequence triggered
- [ ] Death (character): drop 0–3 equipped items at configurable drop rate; lose 10% exp; respawn at map revival point after 3s
- [ ] PK system: attacking a non-PK player sets attacker's PK flag; PK points accumulate; name color changes (white→red→black thresholds); PK points decay 1/min
- [ ] Monster death (see npc-monster-ai for spawn side): broadcasts entity-remove packet, triggers loot drop via `GroundItem`
- [ ] Durability: equipped items lose 1 dura on death; at 0 dura item is destroyed
- [ ] Integration tests: character attacks monster, monster HP decremented; monster dies, loot spawned; character dies, respawn packet received

**Interface contracts (outputs this spec provides to later specs):**
- `CombatProcessor.Attack(attacker, target)` — npc-monster-ai calls this when monster attacks player
- `DeathHandler.OnCharacterDeath(session)` — stateless; admin-tools can also call to simulate death for testing
- `DamageFormula.Calculate(atk_min, atk_max, def)` — exposed as static for unit testing

---

### 9. npc-monster-ai

**Goal:** As a player, I encounter monsters that spawn in the world, chase me when I get close, attack me on a tick, and drop loot when killed, so the world feels alive.

**Size:** L

**Depends on:** combat-system, world-map

**Acceptance criteria:**
- [ ] `monster_types` table: `id`, `name`, `level`, `hp`, `atk_min`, `atk_max`, `def`, `move_speed`, `aggro_radius`, `attack_range`, `exp_reward`, `money_drop`
- [ ] `monster_spawns` table: `id`, `map_id`, `type_id`, `x`, `y`, `count`, `respawn_seconds`
- [ ] `npc_types` + `npcs` tables: `id`, `map_id`, `type_id`, `x`, `y`, `name`, `dialog_script`
- [ ] Spawn manager: loads spawn tables at startup; places `MonsterEntity` instances on maps; respawns after delay on death
- [ ] AI tick (500ms): per-monster state machine — `IDLE → CHASE (aggro_radius) → ATTACK (attack_range) → IDLE`
- [ ] Movement tick (200ms): CHASE state moves monster 1 tile toward target; uses `MapManager.IsPassable`
- [ ] Monster attacks player: calls `CombatProcessor.Attack(monster, playerSession)` on attack tick
- [ ] Death: `MonsterEntity` removed from map, loot `GroundItem`s spawned (money + random item from loot table), exp awarded to attacker via `CharacterEntity.GainExp`
- [ ] `loot_tables` table: `monster_type_id`, `item_type_id`, `drop_chance` (0.0–1.0)
- [ ] NPC interaction: player sends NPC-talk packet; server sends canned dialog packet (no quest logic required for MVP)
- [ ] Integration test: monster spawns, player approaches, monster enters CHASE, player receives attack damage packet

**Interface contracts (outputs this spec provides to later specs):**
- `MonsterEntity` type (admin-tools can spawn/kill monsters via GM commands)
- `SpawnManager.SpawnMonster(typeId, mapId, x, y)` — admin-tools uses this
- `NpcEntity` type + NPC-talk packet handler (quest system, if ever added, hooks here)

---

### 10. admin-tools

**Goal:** As a game master, I can run in-game commands to manage the server (kick, ban, give items, teleport, spawn monsters) and view basic health metrics from a console endpoint, so the server is operable.

**Size:** S

**Depends on:** npc-monster-ai, item-system, character-system, game-session

**Acceptance criteria:**
- [ ] GM command prefix `@` parsed from chat packet; only characters with `gm_level > 0` in DB can execute
- [ ] Commands: `@kick <name>`, `@ban <name>`, `@item <name> <item_type_id> [count]`, `@tp <name> <map_id> <x> <y>`, `@spawn <monster_type_id> <count>`, `@hp`, `@clearinv <name>`
- [ ] `@kick`: calls `session.Disconnect()`
- [ ] `@ban`: sets `accounts.banned = 1`, calls `session.Disconnect()` if online
- [ ] `@item`: creates item row in DB + adds to character inventory in memory
- [ ] `@tp`: validates map + coords passable, moves character, sends teleport packet
- [ ] `@spawn`: calls `SpawnManager.SpawnMonster` at GM's current position
- [ ] HTTP health endpoint (ASP.NET Core minimal API on game server, port 9000): `GET /health` returns `{ "sessions": N, "uptime_seconds": N }`
- [ ] Server console: typed commands mirror GM in-game commands (same handler, no session required for console path)
- [ ] Integration test: GM account sends `@item` command, item appears in inventory

**Interface contracts (outputs this spec provides to later specs):**
- None — this is a terminal spec; no further specs depend on admin-tools

---

## Dependency Graph

```
project-foundation
    └── network-core
            ├── auth-server
            │       └── game-session ─────────────────────────┐
            └── (also used by game-session)                    │
                                                               │
world-map (parallel to auth-server; only needs foundation)    │
    └── (joined at) character-system ←──────────────── game-session
                └── item-system
                        └── combat-system
                                └── npc-monster-ai
                                        └── admin-tools
                                                ↑
                                    (also needs: item-system,
                                     character-system, game-session)
```

**Critical path:** project-foundation → network-core → auth-server → game-session → character-system → item-system → combat-system → npc-monster-ai

**Parallelism opportunities:**
- `world-map` can be built in parallel with `auth-server` (both only need `project-foundation`)
- `admin-tools` development can begin in parallel with `npc-monster-ai` once `combat-system` and `item-system` ship

---

## Size Summary

| # | Spec | Size | Est. Days |
|---|------|------|-----------|
| 1 | project-foundation | S | 3–5 |
| 2 | network-core | M | 7–10 |
| 3 | auth-server | M | 7–10 |
| 4 | game-session | M | 7–10 |
| 5 | world-map | M | 7–10 |
| 6 | character-system | M | 7–10 |
| 7 | item-system | M | 7–10 |
| 8 | combat-system | L | 14–20 |
| 9 | npc-monster-ai | L | 14–20 |
| 10 | admin-tools | S | 3–5 |
| | **Total (sequential)** | | **76–110 days** |
| | **With parallelism** | | **~55–80 days** |
