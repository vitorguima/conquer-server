# Requirements: Conquer Online Classic Private Server — Milestone 1

## Overview

This project builds a Conquer Online (client version 5017) private server in C# / .NET 8. The server is split into two processes: AccountServer (handles authentication) and GameServer (handles the game world). Milestone 1 delivers a vertical slice: a player can authenticate, their character loads into the game world, and they can walk around. Combat, AI, items, chat, and admin tooling are deferred to later milestones.

---

## Glossary

| Term | Definition |
|------|------------|
| **AccountServer** | Standalone process (port 9958) that handles client login, credential validation, and issues one-time tokens |
| **GameServer** | Standalone process (port 5816) that manages the game world, sessions, characters, and movement |
| **DH (Diffie-Hellman)** | Key-exchange algorithm used during the AccountServer handshake to establish a shared secret before encryption begins |
| **RC5** | Symmetric block cipher used to encrypt packets on the AccountServer connection after DH handshake |
| **MD5** | Hash algorithm used to hash the player's password before it is transmitted to AccountServer |
| **One-time token** | Short-lived opaque value issued by AccountServer after successful login; client presents it to GameServer to establish a game session without re-sending credentials |
| **Packet** | Binary message exchanged between client and server; format: 2-byte length + 2-byte type ID + variable payload, little-endian |
| **Packet type ID** | 16-bit unsigned integer identifying the structure and handler for a packet |
| **DMAP (.dmap)** | Binary map format used by Conquer Online client; encodes walkability grid and passability data for each game map |
| **Session** | Server-side object tracking a single connected client across both authentication and game phases |
| **Character** | A player-controlled entity in the game world with position, appearance, and stats persisted to the database |
| **Transfer token** | Synonym for one-time token in the context of AccountServer → GameServer handoff |
| **COServer Redux** | Open-source reference implementation used as primary technical reference for protocol and map parsing |

---

## User Stories

### US-1: Client Connection and Encryption Handshake

**As a** Conquer Online 5017 client
**I want to** connect to AccountServer and complete a DH key exchange
**So that** subsequent login packets are encrypted with a session-specific RC5 key

**Acceptance Criteria:**
- [ ] AC-1.1: AccountServer accepts TCP connections on port 9958
- [ ] AC-1.2: Server initiates DH handshake by sending the server's public key in the first server-to-client packet
- [ ] AC-1.3: Server derives the shared RC5 session key from the client's DH public key response
- [ ] AC-1.4: All subsequent packets on this connection are decrypted/encrypted using the derived RC5 key
- [ ] AC-1.5: Malformed or oversized handshake packets cause the connection to be closed without server crash

---

### US-2: Account Authentication

**As a** player
**I want to** log in with my username and MD5-hashed password
**So that** my identity is verified and I can enter the game

**Acceptance Criteria:**
- [ ] AC-2.1: AccountServer receives and parses the login packet (type ID matching client 5017 protocol)
- [ ] AC-2.2: Server looks up the account in MySQL by username
- [ ] AC-2.3: Server compares the received MD5 hash against the stored hash; mismatches return an error response
- [ ] AC-2.4: Non-existent usernames return a "login failed" response packet, not a server error
- [ ] AC-2.5: Successful authentication generates a one-time token stored server-side with a TTL of 30 seconds
- [ ] AC-2.6: Server sends a redirect response containing the one-time token and GameServer IP/port (5816)

---

### US-3: Game Session Establishment

**As a** player who has authenticated
**I want to** connect to GameServer using my one-time token
**So that** my game session begins without re-entering credentials

**Acceptance Criteria:**
- [ ] AC-3.1: GameServer accepts TCP connections on port 5816
- [ ] AC-3.2: Client presents the one-time token in the first GameServer packet
- [ ] AC-3.3: GameServer validates the token against AccountServer's issued token store; expired or unknown tokens are rejected with a disconnect
- [ ] AC-3.4: Valid token is consumed (single-use); reuse is rejected
- [ ] AC-3.5: On token acceptance, a game session object is created and linked to the account

---

### US-4: Character Selection and Load

**As a** player
**I want to** select my character and have it load into the game world
**So that** I can see my character in the correct map and position

**Acceptance Criteria:**
- [ ] AC-4.1: GameServer sends the character list packet for the authenticated account
- [ ] AC-4.2: Player can select a character; server loads character data from MySQL (name, map ID, X/Y coordinates, appearance fields)
- [ ] AC-4.3: If the account has no characters, server supports creating one character with name and appearance (class/body/hair)
- [ ] AC-4.4: Character spawn packet is sent to the client with correct map ID, coordinates, and appearance data
- [ ] AC-4.5: Client renders the character at the correct spawn point (verified by no client-side "map not found" error)
- [ ] AC-4.6: Character data is persisted to MySQL and survives server restart

---

### US-5: Map Loading

**As a** GameServer process
**I want to** load DMAP files for all maps required by M1 (at minimum the default spawn map)
**So that** walkability checks can be enforced for player movement

**Acceptance Criteria:**
- [ ] AC-5.1: Server parses .dmap binary files at startup without crash
- [ ] AC-5.2: Each parsed map exposes a walkability query: given (x, y), returns passable/impassable
- [ ] AC-5.3: At minimum, the Twin City spawn map (map ID 1002 in standard CO datasets) loads successfully
- [ ] AC-5.4: Missing or corrupt .dmap files produce a logged error and controlled shutdown, not an unhandled exception

---

### US-6: Player Movement

**As a** player in the game world
**I want to** walk to adjacent tiles
**So that** I can navigate the map

**Acceptance Criteria:**
- [ ] AC-6.1: Server receives and parses the movement packet from the client
- [ ] AC-6.2: Server validates the target tile is within one step of the current position (8-directional movement)
- [ ] AC-6.3: Server validates the target tile is passable according to the loaded DMAP
- [ ] AC-6.4: Valid moves update the character's server-side position and broadcast a move confirmation packet back to the client
- [ ] AC-6.5: Invalid moves (out-of-bounds or impassable tile) are silently discarded; character position is unchanged
- [ ] AC-6.6: Character position is persisted to MySQL on a save interval (not necessarily on every step)
- [ ] AC-6.7: Client-side character moves smoothly in response to the server confirmation (no teleport glitch on valid moves)

---

### US-7: Multi-client Visibility

**As a** player
**I want to** see other players in my vicinity
**So that** the world feels populated during testing

**Acceptance Criteria:**
- [ ] AC-7.1: When player A enters a map, existing players within screen range receive a spawn packet for player A
- [ ] AC-7.2: When player A moves, players within range receive a move broadcast packet
- [ ] AC-7.3: When player A disconnects, players within range receive a despawn packet
- [ ] AC-7.4: "Screen range" is defined as the standard CO viewport distance (configurable constant, default 18 tiles)

---

### US-8: Developer Operations

**As a** developer
**I want to** run the full server stack locally via Docker Compose
**So that** onboarding and reproducible builds require no manual DB setup

**Acceptance Criteria:**
- [ ] AC-8.1: `docker-compose up` starts MySQL 8, AccountServer, and GameServer in the correct order
- [ ] AC-8.2: MySQL schema is applied automatically on first start (migrations or init SQL)
- [ ] AC-8.3: Seed data (accounts, a default character, map references) is loaded automatically for local testing
- [ ] AC-8.4: Both server processes log startup confirmation and listening port to stdout
- [ ] AC-8.5: The same Docker images run on Ubuntu 22.04 without modification (Linux-compatible base image)

---

## Functional Requirements

| ID | Requirement | Priority | Maps To | Acceptance Criteria |
|----|-------------|----------|---------|---------------------|
| FR-1 | AccountServer listens on TCP port 9958 and accepts concurrent client connections using async socket I/O | High | US-1 | Port is configurable via env/config; server handles multiple simultaneous handshakes |
| FR-2 | DH key exchange implemented per client 5017 protocol spec | High | US-1 | Shared secret derived correctly; RC5 key initialized before any login packet is processed |
| FR-3 | RC5 encryption/decryption applied to all AccountServer packets after handshake | High | US-1 | Captured traffic decryptable with derived key |
| FR-4 | Login packet parsed: username (string) + MD5-hashed password (hex or binary per protocol) | High | US-2 | Fields extracted without corruption on valid packets |
| FR-5 | Account lookup via Dapper against MySQL `accounts` table | High | US-2 | Query uses parameterized input; no SQL injection surface |
| FR-6 | One-time token generated (cryptographically random, 128-bit minimum), stored with expiry, sent in redirect packet | High | US-2, US-3 | Token not reusable after first claim; expired token rejected |
| FR-7 | GameServer listens on TCP port 5816 and accepts concurrent client connections | High | US-3 | Port configurable; async socket I/O |
| FR-8 | Token validation on GameServer: check existence, expiry, and mark consumed atomically | High | US-3 | Race condition between two simultaneous claims of same token must be safe |
| FR-9 | Character list retrieval and serialization into client-expected packet format | High | US-4 | Packet byte layout matches client 5017 expectation |
| FR-10 | Character creation: name uniqueness enforced, appearance fields stored, default spawn point assigned | Medium | US-4 | Duplicate name returns error packet, not DB exception |
| FR-11 | Character load from MySQL including map ID, X, Y, and appearance fields | High | US-4 | Loaded via Dapper; missing character returns error |
| FR-12 | DMAP binary parser reads header, tile count, and passability flags | High | US-5 | No exceptions on well-formed files; passability queries return correct results |
| FR-13 | Map registry: maps loaded at startup, accessible by map ID | High | US-5 | Startup fails with clear error if required map files are missing |
| FR-14 | Movement packet parsed: direction or target coordinates per client 5017 spec | High | US-6 | Parsed without error on valid input |
| FR-15 | Server-side movement validation: adjacency check + DMAP passability check | High | US-6 | Invalid moves discarded; valid moves update position |
| FR-16 | Move broadcast to players within screen range | High | US-7 | All in-range sessions receive broadcast within same event loop tick |
| FR-17 | Disconnect handling: session cleanup, despawn broadcast to nearby players | High | US-7 | No resource leak on abrupt disconnect (socket reset) |
| FR-18 | Character position persisted to MySQL on a configurable save interval (default: 60 seconds) | Medium | US-6 | Position recoverable after clean server shutdown |
| FR-19 | Docker Compose configuration for MySQL 8, AccountServer, GameServer with health checks and startup ordering | High | US-8 | `docker-compose up` reaches all three healthy states |
| FR-20 | Database schema migrations applied at server startup (not requiring manual DBA steps) | High | US-8 | Fresh MySQL volume reaches correct schema on first boot |

---

## Non-Functional Requirements

| ID | Requirement | Metric | Target |
|----|-------------|--------|--------|
| NFR-1 | **Throughput** — GameServer handles concurrent sessions | Concurrent connections | 200 sessions without packet loss or crash (eventual target; M1 tested at 10+) |
| NFR-2 | **Latency** — Movement round-trip (client send → server validate → broadcast received) | P95 RTT on localhost | < 50 ms on local dev machine |
| NFR-3 | **Reliability** — Server must not crash on malformed or truncated packets | Crash rate on fuzz input | Zero unhandled exceptions; all malformed packets result in connection close + log entry |
| NFR-4 | **Security** — No plaintext passwords stored or logged | Password storage | Passwords stored as MD5 hash at minimum; never written to logs |
| NFR-5 | **Portability** — Codebase compiles and runs on both Windows 11 and Ubuntu 22.04 | CI target | No OS-specific APIs outside of explicitly isolated adapters |
| NFR-6 | **Maintainability** — Each server is a separate .NET 8 project within one solution | Project structure | `AccountServer.csproj` and `GameServer.csproj` in a single `.sln`; shared code in a `Common` library project |
| NFR-7 | **Observability** — Structured logging for connection events, auth results, and errors | Log output | Startup, auth success/fail, token issue/consume, disconnect logged at INFO or ERROR level to stdout |
| NFR-8 | **Reproducibility** — Dev environment setup requires only Docker and .NET 8 SDK | Setup steps | README documents ≤ 5 commands to reach running server |

---

## Out of Scope (Milestone 1)

- Combat system (melee, magic, skills, death/respawn)
- Monster and NPC spawning, pathfinding, and AI
- Item system (inventory, equipment, drops, shops)
- Chat system (map chat, private message, guild, team)
- Guild system
- Trade system
- Quest system
- Admin / GM command tools
- Anti-cheat or rate-limiting beyond basic packet validation
- Web-based account registration portal
- Character deletion
- Multiple character slots per account (single character per account sufficient for M1)
- VIP / premium features
- Client patcher or version enforcement beyond protocol compatibility

---

## Dependencies

| Dependency | Type | Notes |
|------------|------|-------|
| Conquer Online client version 5017 | External binary | Player-supplied; not redistributable; required for end-to-end testing |
| Community SQL dump (items, maps, monster data) | Data file | Sourced from CO private-server community; exact source URL to be confirmed |
| .dmap files for spawn maps | Binary data | Extracted from client or sourced from community; Twin City (map 1002) required at minimum |
| COServer Redux source | Reference code | Read-only reference for packet structures and DMAP parser logic; not a dependency of the built binary |
| MySQL 8 / MariaDB | Infrastructure | Provided via Docker Compose for dev; target VPS must have Docker |
| .NET 8 SDK | Build toolchain | Required on dev machine and in Docker build stage |
| Docker + Docker Compose | Dev infrastructure | Required for local stack; Docker Compose v2 syntax assumed |

---

## Open Questions

1. **Token storage backend:** Should one-time tokens be stored in MySQL (adds a DB round-trip on GameServer validation) or in a shared in-process cache / Redis? For M1 with two processes on the same host, an in-process approach requires IPC or a shared store. Decision needed before FR-8 implementation.

2. **DMAP source licensing:** Are the community-sourced .dmap files freely redistributable in a public Git repo, or must the repo document extraction steps and keep the files out of source control?

3. **Account registration:** M1 assumes accounts are seeded manually (Docker init SQL). Is a bare-minimum registration packet handler needed for M1 so the client's register flow works, or is manual DB insertion acceptable for the milestone?

4. **Packet encryption on GameServer:** The research summary specifies RC5 on AccountServer. Does client 5017 also encrypt GameServer packets (with a different key negotiated during token exchange), or are GameServer packets unencrypted? This affects FR-7 scope.

5. **Screen-range broadcast implementation:** Proximity queries over all connected sessions could bottleneck at scale. For M1 (≤ 10 concurrent), a linear scan is acceptable — but should the data structure be designed for future spatial indexing (e.g., map-partitioned session lists) from the start?

6. **Character save on shutdown:** FR-18 covers interval saves. Should a clean shutdown trigger a final save for all active characters, or is data loss on unclean shutdown acceptable for M1?
