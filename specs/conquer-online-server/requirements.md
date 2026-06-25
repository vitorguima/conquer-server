# Requirements: Conquer Online Server (5065 Modernization)

## Goal

Port COServer Redux (C# / .NET Framework 4.0 / x86 / NHibernate) to .NET 8 / MySQL 8 / Docker using surgical, minimal changes so a CO 5065 client can authenticate and enter the game world on both Windows 11 (dev) and a Brazilian x64 Linux VPS (prod).

---

## Decision Note

The research phase evaluated two candidate codebases and recommended **Comet** (patch 5017, `https://github.com/conquer-online/comet`) for its clean .NET 6 async architecture and pre-built auth implementation (TQCipher, RC5, MsgAccount already present).

The user chose **COServer Redux** (patch 5065, `https://github.com/conquer-online/redux`) instead, because Redux has substantially more game logic already implemented — combat, items, guilds, NPCs, and shops — which are all out of scope to rewrite from scratch. This decision trades a cleaner starting architecture for a much larger head start on game functionality.

Consequences for this spec:

- The feasibility ratings for auth (FR-6 through FR-9) assumed Comet's pre-built implementation. On Redux, the auth flow (TQCipher, RC5, MsgAccount parsing) must be ported from scratch or adapted from Comet source.
- This carries slightly higher effort than the research estimated, but the auth protocol is well-documented in the CO developer wiki (`https://conquer-online.github.io/wiki/`) and Comet source serves as a clean reference implementation.
- Any code adapted from Comet falls under Comet's non-commercial / academic license, which is acceptable for private and educational use.

---

## User Stories

### US-1: Developer — Initial Setup
**As a** developer
**I want to** clone the repo, run one command, and have the server and database running
**So that** I can begin development without manual environment configuration

**Acceptance Criteria:**
- [ ] AC-1.1: `git clone` + `docker compose up` starts MySQL 8 and the game server with no manual steps
- [ ] AC-1.2: Server log contains the string `Listening` and the configured port number within 30 seconds of container start; `docker compose ps` shows `server` service status as `running`.
- [ ] AC-1.3: README contains a section titled `Getting Started` with verbatim `git clone` and `docker compose up` shell commands.

### US-2: Developer — Local Windows 11 Build
**As a** developer
**I want to** build and run the server locally on Windows 11 without Docker
**So that** I can attach a debugger, inspect logs, and iterate quickly during development

**Acceptance Criteria:**
- [ ] AC-2.1: `dotnet build` completes with zero errors on Windows 11 x64 with .NET 8 SDK installed
- [ ] AC-2.2: Server binds to the configured port (default 5816); `netstat -tlnp | grep <port>` shows LISTEN state after `dotnet run`.
- [ ] AC-2.3: No x86-only or Windows-native binaries are required at runtime (ManagedOpenSsl.dll absent)
- [ ] AC-2.4: A local MySQL 8 instance (or Docker Desktop MySQL container) can be used as the database

### US-3: Player — Authentication and Login
**As a** CO 5065 client player
**I want to** enter my account credentials and reach the character selection screen
**So that** I can log into the game world

**Acceptance Criteria:**
- [ ] AC-3.1: Server log emits a line containing `[Auth]` and the username upon receipt of MsgAccount (packet 1051).
- [ ] AC-3.2: Server decrypts the password field using RC5 and validates against SHA1 hash stored in DB
- [ ] AC-3.3: Server responds with MsgConnectEx (packet 1055) containing a valid session token on success
- [ ] AC-3.4: Client reconnects to game port; server receives MsgConnect (1052) and activates TQCipher keyed from the session token
- [ ] AC-3.5: Character selection screen is reachable (character list returned); player can enter the game world
- [ ] AC-3.6: Invalid credentials produce a rejection response and do not crash the server

### US-4: Ops — Deploy to Linux VPS
**As a** server operator
**I want to** deploy the server to a Brazilian x64 Linux VPS using Docker Compose
**So that** players can connect to a stable hosted instance

**Acceptance Criteria:**
- [ ] AC-4.1: `docker compose up` on an x64 Linux host starts both MySQL 8 and the game server containers
- [ ] AC-4.2: The game server container runs the .NET 8 binary compiled as AnyCPU (no x86-only artifacts)
- [ ] AC-4.3: MySQL schema is applied automatically on first start (init script or migration)
- [ ] AC-4.4: Server and database are connected via Docker internal network; no host-network hacks required
- [ ] AC-4.5: Container restart policy keeps services up across VPS reboots

---

## Functional Requirements

| ID | Requirement | Priority | Acceptance Criteria | Traces to |
|----|-------------|----------|---------------------|-----------|
| FR-1 | Replace NHibernate ORM with Dapper | High | All DB reads/writes use Dapper + explicit SQL; NHibernate packages absent from `.csproj` | US-2, US-4 |
| FR-2 | Retarget all projects from .NET Framework 4.0 / x86 to .NET 8 / AnyCPU | High | `dotnet build` succeeds; `TargetFramework` = `net8.0`; `PlatformTarget` not set to x86 | US-2, US-4 |
| FR-3 | Remove ManagedOpenSsl.dll dependency | High | No P/Invoke or DllImport referencing ManagedOpenSsl; TLS/crypto uses `System.Security.Cryptography` | US-2, US-4 |
| FR-4 | Port TinyMap.dll (DMAP parser) to managed .NET 8 | High | Managed `TinyMap` class reads `.dmap` files and returns passability data; no native DLL referenced | US-2, US-4 |
| FR-5 | Wrap TCP accept loop in async/await | Medium | `TcpListener.AcceptTcpClientAsync()` (or equivalent) used; accept loop is non-blocking; handler bodies may remain synchronous | US-2 |
| FR-6 | Implement TQCipher (XOR stream cipher) in managed code | High | Cipher correctly encrypts/decrypts packets matching the CO 5065 protocol; no native crypto DLL | US-3 |
| FR-7 | Implement RC5 password decryption in managed code | High | Server decrypts MsgAccount password field using RC5 matching the 5065 key schedule | US-3 |
| FR-8 | Implement SHA1 credential validation | High | Decrypted password is hashed with SHA1 and compared to stored hash; uses `System.Security.Cryptography.SHA1` | US-3 |
| FR-9 | Auth flow: MsgAccount → MsgConnectEx → MsgConnect handshake | High | Full three-message handshake completes; session token issued; game cipher activated | US-3 |
| FR-10 | MySQL 8 schema compatibility | High | All DDL and DML execute without error on MySQL 8.0; no MySQL 5.6-only syntax used | US-1, US-4 |
| FR-11 | Docker Compose service definition for game server | High | `docker-compose.yml` defines `server` service built from `Dockerfile`, with env vars for DB connection | US-1, US-4 |
| FR-12 | Docker Compose service definition for MySQL 8 | High | `docker-compose.yml` defines `db` service using `mysql:8` image with volume for data persistence | US-1, US-4 |
| FR-13 | Database schema initialization on container first start | High | `init.sql` (or equivalent) mounted into MySQL container; schema created automatically | US-1, US-4 |
| FR-14 | Preserve all existing Redux game logic in ported code | Medium | Combat, item, guild, NPC/shop handler classes present and compile cleanly under .NET 8; not tested end-to-end in M1 | US-2 |
| FR-15 | Configuration via environment variables or config file | Medium | DB host, port, credentials, server listen port are externally configurable without code changes | US-1, US-2, US-4 |
| FR-16 | Server binds and listens on configured port at startup | High | Log line confirms bind success; `netstat` (or equivalent) shows port in LISTEN state | US-1, US-2 |
| FR-17 | Docker Compose MySQL 8 service must configure `mysql_native_password` authentication plugin | High | (a) `docker compose exec db mysql -u root --password=<root_pass> -e "SHOW VARIABLES LIKE 'authentication_policy';"` returns a row containing `mysql_native_password`; (b) game server container log shows a successful DB connection on startup (log line containing `Database connected` or equivalent) | US-1, US-4 |
| FR-18 | Write README with Getting Started section | Low | README file contains a section titled exactly `Getting Started` with verbatim `git clone` and `docker compose up` commands; verifiable via `grep -q '## Getting Started' README.md && echo PASS` | US-1 |

---

## Non-Functional Requirements

| ID | Requirement | Metric | Target |
|----|-------------|--------|--------|
| NFR-1 | .NET 8 SDK | Build toolchain | .NET 8.0 SDK required; no .NET Framework or .NET 6/7 |
| NFR-2 | Runtime platform | CPU architecture | AnyCPU output; runs on x64 Linux and x64 Windows without recompile |
| NFR-3 | MySQL compatibility | DB engine version | MySQL 8.0.x; no features requiring 8.1+ |
| NFR-4 | Build success | CI gate | `dotnet build` exits 0 with zero errors; warnings are acceptable but must not block build |
| NFR-5 | Docker Compose bring-up | Ops gate | `docker compose up` on x64 Linux reaches healthy state (server listening) within 60 seconds on first run |
| NFR-6 | No Windows-only binaries | Portability | Zero P/Invoke calls to Windows-native DLLs in the critical path; `ManagedOpenSsl.dll` and `TinyMap.dll` (native) absent |
| NFR-7 | Connection handling | Stability | Server does not crash on a single client disconnect or malformed packet; exception is caught and logged |
| NFR-8 | Auth security baseline | Security | Passwords never stored in plaintext in DB; SHA1 hash comparison matches existing Redux schema convention |
| NFR-9 | Log verbosity | Observability | Server emits structured or plaintext logs for: startup, client connect, auth success/failure, client disconnect |
| NFR-10 | Container image size | Ops | Final Docker image uses `mcr.microsoft.com/dotnet/runtime:8.0` (not SDK); image under 500 MB |

---

## Glossary

| Term | Definition |
|------|------------|
| **Redux** | COServer Redux — the open-source C# CO 5065 server being forked. Targets .NET Framework 4.0 / x86 / NHibernate. |
| **CO / Conquer Online** | Conquer Online — a 2D MMORPG by TQ Digital. Patch 5065 is the client version targeted by this server. |
| **5065 patch** | Specific client protocol version (build 5065). Defines packet formats, crypto keys, and handshake sequence. |
| **TQCipher** | XOR-based stream cipher used to encrypt all game packets after the auth handshake. Keys are derived from the session token. |
| **RC5** | Symmetric block cipher used to decrypt the login password in MsgAccount (packet 1051). |
| **DMAP** | Binary map file format used by CO to define zone layout and tile passability. |
| **TinyMap.dll** | Native Windows DLL in Redux that parses DMAP files. Must be replaced with managed .NET 8 code for Linux compatibility. |
| **NHibernate** | .NET ORM used by Redux for all DB access. Being replaced with Dapper. |
| **Dapper** | Lightweight .NET micro-ORM. Wraps ADO.NET with extension methods for explicit SQL queries. Replacement for NHibernate. |
| **Monolith** | Single-process architecture — auth and game logic run in the same process, no microservice split. |
| **ManagedOpenSsl.dll** | Windows-only managed wrapper around OpenSSL used by Redux for crypto. Incompatible with Linux; removed in M1. |
| **MsgAccount** | Packet 1051 — client login packet carrying account name and RC5-encrypted password. |
| **MsgConnectEx** | Packet 1055 — server response to successful auth; carries session token for game server connection. |
| **MsgConnect** | Packet 1052 — client packet sent to game port; carries token to activate TQCipher session. |
| **M1** | Milestone 1 — the scope defined in this spec: server boots, client authenticates. Game logic present but not verified. |
| **Comet** | A separate open-source CO project (targeting patch 5017) with a clean .NET 6 async architecture. Used as an implementation reference for FR-6/FR-7/FR-8/FR-9; not forked. |
| **mysql_native_password** | MySQL authentication plugin required for compatibility with MySqlConnector / Dapper on .NET 8. Must be explicitly configured in MySQL 8 (no longer the default from 8.0.4+). |
| **CIDLoader** | Third-party launcher tool that redirects the CO client's server IP and port to a custom server without patching the client binary. |

---

## Out of Scope for M1

- New game features, content, or gameplay mechanics not present in Redux
- Auth/Game server split into separate processes or services
- Full async rewrite of handler bodies (only the accept loop is async in M1)
- Automated test suite (unit or integration tests)
- CI/CD pipeline (GitHub Actions or equivalent)
- Monster AI implementation (known TODO in Redux; deferred)
- HTTPS / TLS for any connection (CO 5065 uses application-layer crypto only)
- Anti-cheat or packet validation beyond what Redux already implements
- Admin tools, web panel, or remote management interface
- Horizontal scaling, load balancing, or multi-instance deployment
- Player-facing content updates (maps, items, quests beyond what Redux ships with)
- Performance benchmarking or load testing
- Database migrations tooling (Flyway, EF Core migrations, etc.) — init script is sufficient for M1
- End-to-end verification of combat, item, guild, or NPC systems (code present; correctness not gate-checked in M1)

---

## Dependencies

| Dependency | Notes |
|------------|-------|
| COServer Redux source code | Must be cloned from `https://github.com/conquer-online/redux` before implementation begins |
| .NET 8 SDK | Required on developer machine (Windows 11) and in CI/build environment |
| Docker Desktop (Windows dev) | Required for local `docker compose up` during development |
| Docker Engine + Docker Compose (Linux VPS) | Required on the Brazilian VPS for production deployment |
| MySQL 8.0 | Provided via Docker Compose `db` service; no separate install needed when using Docker |
| CO 5065 client | Community-distributed; see cooldown.dev for 5065 client download guide. Requires CIDLoader to redirect client to custom server IP:port. |
| Comet source (reference only) | `https://github.com/conquer-online/comet` — specific files used as implementation reference for FR-6/FR-7/FR-8/FR-9 (TQCipher.cs, RC5.cs, MsgAccount.cs). Code will be adapted (not copied verbatim); Comet license (non-commercial/academic) applies to any adapted code — acceptable for private/educational use. |
| CO developer wiki | `https://conquer-online.github.io/wiki/` — packet format and crypto reference |

---

## Success Criteria

1. `dotnet build` exits 0 on Windows 11 with .NET 8 SDK — zero errors
2. `docker compose up` on x64 Linux brings server and MySQL 8 to healthy state within 60 seconds
3. CO 5065 client successfully completes the three-message auth handshake (MsgAccount → MsgConnectEx → MsgConnect) against the running server
4. Character selection screen is reachable from the client (character list returned by server)
5. No native Windows-only DLLs (ManagedOpenSsl.dll, TinyMap.dll native) present in the build output
6. All Redux game-logic handler classes (combat, items, guilds, NPCs) compile cleanly under .NET 8

---

## Unresolved Questions

- **SHA1 storage format**: Does Redux store passwords as hex-encoded SHA1, base64, or raw bytes? Needs verification against the Redux `account` table schema before implementing AC-3.2.
- **TQCipher key derivation details**: Exact byte layout of the session token passed in MsgConnectEx / used to seed TQCipher keys — confirm against Redux source or CO wiki before implementing FR-6/FR-9.
- **MySQL 5.6 → 8 schema breaks**: Are there any `utf8mb3`, `ZEROFILL`, or implicit defaults in the Redux DDL that MySQL 8 strict mode rejects? Must audit `init.sql` during implementation.
- **Docker base image**: `mcr.microsoft.com/dotnet/runtime:8.0` vs `aspnet:8.0` — game server is not ASP.NET, so `runtime` is correct, but confirm no ASP.NET middleware is used by Redux.
- **Port layout**: Redux uses separate auth and game ports (typically 9958 / 5816). Confirm exact ports and whether both must be exposed in Docker Compose, or if auth and game share a single port in the monolith.

## Next Steps

1. Audit Redux source for exact port layout, SHA1 storage format, and MySQL 5.6 DDL to answer unresolved questions
2. Generate `design.md` — component breakdown, class mapping from Redux to .NET 8 equivalents, Dapper query strategy, Docker Compose topology
3. Generate `tasks.md` — ordered implementation tasks with verification steps, starting with build scaffolding and ending with M1 auth acceptance test
