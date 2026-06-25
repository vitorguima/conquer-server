# Tasks: Conquer Online Private Server — Milestone 1

**Total tasks: 58** (Phase 1: 32 | Phase 2: 8 | Phase 3: 9 | Phase 4: 4 | Phase 5: 5)
**Workflow:** POC-first (GREENFIELD)
**Granularity:** fine
**Task format:** checkbox (`- [ ] X.Y`) — converted from heading format for executor compatibility

---

## Phase 1: Make It Work (POC)

Focus: Vertical slice running end-to-end. Auth handshake → login → token → GameServer → character load → movement. Skip error handling polish and partial-read edge cases.

---

- [x] 1.1 Solution scaffold

  - **Do**: Create `ConquerServer.sln` with three projects: `src/Common/Common.csproj` (.NET 8 classlib), `src/AccountServer/AccountServer.csproj` (.NET 8 console), `src/GameServer/GameServer.csproj` (.NET 8 console). Add project references: both server projects reference Common. Add `data/README.md` documenting where to source `.dmap` files and the community SQL dump. Add `.gitignore` with standard .NET entries plus `data/maps/`.
  - **Files**: `ConquerServer.sln`, `src/Common/Common.csproj`, `src/AccountServer/AccountServer.csproj`, `src/GameServer/GameServer.csproj`, `data/README.md`, `.gitignore`
  - **Done when**: `dotnet build ConquerServer.sln` exits 0 with three projects compiled.
  - **Verify**: `dotnet build ConquerServer.sln 2>&1 | tail -5`
  - **Commit**: `chore(scaffold): initialize solution with Common, AccountServer, GameServer projects`

---

- [ ] 1.2 NuGet dependencies

  - **Do**: Add NuGet packages to each project. `Common`: `Dapper`, `MySqlConnector`, `Microsoft.Extensions.Configuration.Json`, `Microsoft.Extensions.Logging.Abstractions`. `AccountServer` and `GameServer`: `Microsoft.Extensions.Hosting`, `Microsoft.Extensions.Logging.Console`, `Serilog.Extensions.Hosting`, `Serilog.Sinks.Console`. Run `dotnet restore`.
  - **Files**: `src/Common/Common.csproj`, `src/AccountServer/AccountServer.csproj`, `src/GameServer/GameServer.csproj`
  - **Done when**: `dotnet restore` exits 0, no missing packages.
  - **Verify**: `dotnet restore ConquerServer.sln && echo RESTORE_OK`
  - **Commit**: `chore(deps): add Dapper, MySqlConnector, Hosting, Serilog NuGet packages`

---

- [ ] 1.3 [P] ICipher interface + NullCipher

  - **Do**: Create `src/Common/Crypto/ICipher.cs` defining `public interface ICipher { void Encrypt(Span<byte> data); void Decrypt(Span<byte> data); }`. Create `src/Common/Crypto/NullCipher.cs` implementing both methods as no-ops (sealed class).
  - **Files**: `src/Common/Crypto/ICipher.cs`, `src/Common/Crypto/NullCipher.cs`
  - **Done when**: `dotnet build src/Common` exits 0, both types exist in `ConquerServer.Common.Crypto` namespace.
  - **Verify**: `dotnet build src/Common/Common.csproj && grep -r "NullCipher" src/Common/Crypto/`
  - **Commit**: `feat(common/crypto): add ICipher interface and NullCipher no-op implementation`

---

- [ ] 1.4 [P] PacketFramer (basic, no partial-read)

  - **Do**: Create `src/Common/Packets/PacketFramer.cs`. Constructor takes `NetworkStream stream, ICipher cipher, ILogger logger, int maxPacketSize = 4096`. `ReadNextAsync(CancellationToken)` reads 4-byte header, parses `UInt16 LE length` and `UInt16 LE type`, reads remaining `length - 4` payload bytes, calls `cipher.Decrypt` on the full buffer, returns `byte[]`. `WriteAsync(byte[], CancellationToken)` calls `cipher.Encrypt` then writes to stream. For POC: assume packets arrive complete (no partial-read buffering yet).
  - **Files**: `src/Common/Packets/PacketFramer.cs`
  - **Done when**: `dotnet build src/Common` exits 0, `PacketFramer` compiles with both methods.
  - **Verify**: `dotnet build src/Common/Common.csproj && grep -r "ReadNextAsync" src/Common/Packets/`
  - **Commit**: `feat(common/packets): add PacketFramer with basic length-prefixed read/write`

---

- [ ] 1.5 [VERIFY] Quality checkpoint — Common base

  - **Do**: Build Common, verify cipher and framer types compile cleanly.
  - **Verify**: `dotnet build src/Common/Common.csproj 2>&1 | grep -E "error|warning" | head -20; dotnet build src/Common/Common.csproj 2>&1 | tail -3`
  - **Done when**: Zero errors, build succeeds.
  - **Commit**: `chore(common): pass quality checkpoint after crypto+framer scaffold`

---

- [ ] 1.6 DhKeyExchange (CO 5017 DH parameters)

  - **Do**: Create `src/Common/Crypto/DhKeyExchange.cs`. Implement CO's custom DH variant: hardcode the 512-bit prime `P` and generator `G` used by CO 5017 (well-known community constants). Method `GenerateKeyPair()` returns `(BigInteger privateKey, BigInteger publicKey)`. Method `DeriveSharedSecret(BigInteger serverPrivate, BigInteger clientPublic)` returns `BigInteger`. Method `DeriveRc5Key(BigInteger sharedSecret)` extracts 16 bytes from the shared secret (take first 16 bytes of the secret's byte array, little-endian, zero-padded if shorter) and returns `byte[16]`.
  - **Files**: `src/Common/Crypto/DhKeyExchange.cs`
  - **Done when**: `dotnet build src/Common` exits 0; `DhKeyExchange` has `GenerateKeyPair`, `DeriveSharedSecret`, `DeriveRc5Key` methods.
  - **Verify**: `dotnet build src/Common/Common.csproj && grep -c "DeriveRc5Key\|DeriveSharedSecret\|GenerateKeyPair" src/Common/Crypto/DhKeyExchange.cs`
  - **Commit**: `feat(common/crypto): implement CO 5017 DH key exchange with RC5 key derivation`

---

- [ ] 1.7 Rc5Cipher (12-round RC5 with 16-byte key)

  - **Do**: Create `src/Common/Crypto/Rc5Cipher.cs` implementing `ICipher`. Use standard RC5-32/12/16 algorithm (32-bit word size, 12 rounds, 16-byte key). Implement `KeyExpand(byte[16] key)` to produce the subkey table `S[26]`. `Encrypt(Span<byte> data)` and `Decrypt(Span<byte> data)` operate in-place in 8-byte blocks (ECB mode, no IV — matches CO 5017 AccountServer convention). Constructor takes `byte[] key`.
  - **Files**: `src/Common/Crypto/Rc5Cipher.cs`
  - **Done when**: `dotnet build src/Common` exits 0; encrypting then decrypting a known 8-byte block returns the original bytes.
  - **Verify**: `dotnet build src/Common/Common.csproj && grep -c "KeyExpand\|Encrypt\|Decrypt" src/Common/Crypto/Rc5Cipher.cs`
  - **Commit**: `feat(common/crypto): implement RC5-32/12/16 cipher for CO AccountServer`

---

- [ ] 1.8 [VERIFY] Quality checkpoint — crypto stack

  - **Do**: Build Common after adding DhKeyExchange and Rc5Cipher.
  - **Verify**: `dotnet build src/Common/Common.csproj 2>&1 | grep -c "^.*error" || echo "CRYPTO_OK"`
  - **Done when**: Zero build errors.
  - **Commit**: `chore(common): pass quality checkpoint after DH+RC5 implementation`

---

- [ ] 1.9 Dapper DB helpers + config models

  - **Do**: Create `src/Common/Db/DapperConnectionFactory.cs` — static method `CreateConnection(string connectionString)` returns open `MySqlConnection`. Create `src/Common/Db/MigrationRunner.cs` — `RunAsync(string connectionString, IEnumerable<string> sqlStatements)` executes each SQL statement sequentially via Dapper `ExecuteAsync`. Create `src/Common/Config/ServerConfig.cs` with properties: `ConnectionStrings_Default`, `AccountServerPort` (int, default 9958), `GameServerPort` (int, default 5816), `GameServerPublicIp`, `TokenTtlSeconds` (int, default 30), `CharacterSaveIntervalSeconds` (int, default 60), `ScreenRangeTiles` (int, default 18), `MapFilesPath` (default `data/maps`), `MaxPacketSize` (int, default 4096).
  - **Files**: `src/Common/Db/DapperConnectionFactory.cs`, `src/Common/Db/MigrationRunner.cs`, `src/Common/Config/ServerConfig.cs`
  - **Done when**: `dotnet build src/Common` exits 0.
  - **Verify**: `dotnet build src/Common/Common.csproj && echo DB_CONFIG_OK`
  - **Commit**: `feat(common/db): add DapperConnectionFactory, MigrationRunner, ServerConfig`

---

- [ ] 1.10 TcpConnectionAcceptor

  - **Do**: Create `src/Common/Net/TcpConnectionAcceptor.cs`. Constructor takes `int port, IConnectionHandler handler, ILogger logger`. `StartAsync(CancellationToken)` starts a `TcpListener` on the given port, loops `AcceptTcpClientAsync`, spawns a `Task` per client calling `handler.HandleAsync(client, ct)` (fire-and-forget with try/catch that logs exceptions). Create `src/Common/Net/IConnectionHandler.cs` interface with `Task HandleAsync(TcpClient client, CancellationToken ct)`.
  - **Files**: `src/Common/Net/TcpConnectionAcceptor.cs`, `src/Common/Net/IConnectionHandler.cs`
  - **Done when**: `dotnet build src/Common` exits 0.
  - **Verify**: `dotnet build src/Common/Common.csproj && grep -r "AcceptTcpClientAsync" src/Common/Net/`
  - **Commit**: `feat(common/net): add TcpConnectionAcceptor and IConnectionHandler`

---

- [ ] 1.11 [VERIFY] Quality checkpoint — Common library complete

  - **Do**: Full build of Common, check no errors or unresolved references.
  - **Verify**: `dotnet build src/Common/Common.csproj 2>&1 | tail -5`
  - **Done when**: `Build succeeded` with 0 errors.
  - **Commit**: `chore(common): pass quality checkpoint — Common library complete`

---

- [ ] 1.12 AccountServer Program.cs + appsettings.json

  - **Do**: Create `src/AccountServer/Program.cs` using `IHostBuilder` / generic host. Load `appsettings.json` and environment variables into `ServerConfig`. Register `TcpConnectionAcceptor`, `AccountConnectionHandler`, `AccountRepository`, `TokenService` as singletons. Add `src/AccountServer/appsettings.json` with keys: `ConnectionStrings:Default` (pointing to `server=mysql;database=conquer;user=root;password=conquer`), `AccountServer:Port` (9958), `GameServer:PublicIp` (`127.0.0.1`), `GameServer:Port` (5816), `TokenTtlSeconds` (30). Wire `TcpConnectionAcceptor` startup via `IHostedService`.
  - **Files**: `src/AccountServer/Program.cs`, `src/AccountServer/appsettings.json`
  - **Done when**: `dotnet build src/AccountServer` exits 0.
  - **Verify**: `dotnet build src/AccountServer/AccountServer.csproj 2>&1 | tail -3`
  - **Commit**: `feat(account): AccountServer host builder with config and DI wiring`

---

- [ ] 1.13 DB schema migrations — accounts + transfer_tokens

  - **Do**: Create `src/AccountServer/Migrations/AccountMigrations.cs` with two static SQL string properties: `CreateAccounts` (`CREATE TABLE IF NOT EXISTS accounts (id INT AUTO_INCREMENT PRIMARY KEY, username VARCHAR(16) NOT NULL UNIQUE, password_hash VARCHAR(32) NOT NULL, created_at DATETIME DEFAULT NOW())`) and `CreateTransferTokens` (`CREATE TABLE IF NOT EXISTS transfer_tokens (token CHAR(32) PRIMARY KEY, account_id INT NOT NULL, expires_at DATETIME NOT NULL, consumed_at DATETIME NULL)`). Call `MigrationRunner.RunAsync` with these in `AccountServer` startup before the TCP acceptor starts.
  - **Files**: `src/AccountServer/Migrations/AccountMigrations.cs`, `src/AccountServer/Program.cs`
  - **Done when**: `dotnet build src/AccountServer` exits 0; migrations are called in the startup sequence.
  - **Verify**: `dotnet build src/AccountServer/AccountServer.csproj && grep -r "MigrationRunner" src/AccountServer/`
  - **Commit**: `feat(account/db): add accounts and transfer_tokens schema migrations`

---

- [ ] 1.14 AccountRepository (Dapper)

  - **Do**: Create `src/AccountServer/Data/AccountRepository.cs`. Constructor takes `string connectionString`. Method `FindByUsernameAsync(string username)` returns `AccountRecord?` (id, username, password_hash). Create `src/AccountServer/Data/AccountRecord.cs` as a simple record/struct. For POC: MD5 hex comparison done in the handler, not the repository.
  - **Files**: `src/AccountServer/Data/AccountRepository.cs`, `src/AccountServer/Data/AccountRecord.cs`
  - **Done when**: `dotnet build src/AccountServer` exits 0.
  - **Verify**: `dotnet build src/AccountServer/AccountServer.csproj && grep -r "FindByUsernameAsync" src/AccountServer/Data/`
  - **Commit**: `feat(account/data): add AccountRepository with FindByUsernameAsync`

---

- [ ] 1.15 TokenService (issue + atomic consume)

  - **Do**: Create `src/AccountServer/Services/TokenService.cs`. Constructor takes `string connectionString, int ttlSeconds`. `IssueAsync(int accountId)` generates a 128-bit token (32 hex chars via `Guid.NewGuid().ToString("N")`), inserts into `transfer_tokens` with `expires_at = NOW() + ttlSeconds`, returns the token string. `ConsumeAsync(string token)` runs `UPDATE transfer_tokens SET consumed_at=NOW() WHERE token=@token AND consumed_at IS NULL AND expires_at>NOW()` via Dapper, returns the `account_id` if `rowsAffected == 1`, else null.
  - **Files**: `src/AccountServer/Services/TokenService.cs`
  - **Done when**: `dotnet build src/AccountServer` exits 0.
  - **Verify**: `dotnet build src/AccountServer/AccountServer.csproj && grep -r "consumed_at IS NULL" src/AccountServer/Services/`
  - **Commit**: `feat(account/services): add TokenService with atomic issue and consume`

---

- [ ] 1.16 [VERIFY] Quality checkpoint — AccountServer data layer

  - **Do**: Build AccountServer, verify data layer compiles.
  - **Verify**: `dotnet build src/AccountServer/AccountServer.csproj 2>&1 | tail -5`
  - **Done when**: Build succeeded, 0 errors.
  - **Commit**: `chore(account): pass quality checkpoint — data layer`

---

- [ ] 1.17 AccountConnectionHandler — DH handshake phase

  - **Do**: Create `src/AccountServer/Handlers/AccountConnectionHandler.cs` implementing `IConnectionHandler`. In `HandleAsync`: create `PacketFramer` with `NullCipher` (DH phase uses no encryption). Call `DhKeyExchange.GenerateKeyPair()`. Send the server's DH public key to the client as packet type `0x0AA5` (CO DH challenge packet). Await the client's public key reply (packet type `0x0EF6`). Parse client public key bytes from the packet payload. Derive shared secret, then RC5 key via `DhKeyExchange`. Swap the framer's cipher to a new `Rc5Cipher(derivedKey)` for all subsequent reads/writes. Store cipher instance on the handler for the session.
  - **Files**: `src/AccountServer/Handlers/AccountConnectionHandler.cs`
  - **Done when**: `dotnet build src/AccountServer` exits 0; handler performs DH exchange and switches cipher.
  - **Verify**: `dotnet build src/AccountServer/AccountServer.csproj && grep -r "DhKeyExchange\|Rc5Cipher" src/AccountServer/Handlers/`
  - **Commit**: `feat(account/handler): implement DH handshake phase in AccountConnectionHandler`

---

- [ ] 1.18 AccountConnectionHandler — login + auth

  - **Do**: Extend `AccountConnectionHandler.HandleAsync`. After DH, await next packet (type `0x03CF` — login packet). Parse username (bytes 4–19, ASCII null-terminated) and password (bytes 20–35, ASCII null-terminated). Compute `MD5(password).ToHexString().ToLower()`. Call `AccountRepository.FindByUsernameAsync(username)`. Compare hashes. On mismatch: send login-error packet (type `0x03CF`, error code 2) and close connection. On success: proceed to token issuance.
  - **Files**: `src/AccountServer/Handlers/AccountConnectionHandler.cs`
  - **Done when**: Handler parses login packet and performs credential check; build exits 0.
  - **Verify**: `dotnet build src/AccountServer/AccountServer.csproj && grep -r "FindByUsernameAsync\|password_hash" src/AccountServer/Handlers/`
  - **Commit**: `feat(account/handler): implement login packet parsing and MD5 credential check`

---

- [ ] 1.19 AccountConnectionHandler — token issuance + redirect packet

  - **Do**: Extend `AccountConnectionHandler.HandleAsync`. After successful auth: call `TokenService.IssueAsync(account.Id)`. Build a redirect packet (type `0x0021`): write `GameServer.PublicIp` as ASCII (16 bytes, null-padded), `GameServer.Port` as `UInt16 LE`, token string as ASCII (32 bytes). Send via framer. Close connection after send. Log `"[AccountServer] Login OK user={username} token={token}"`.
  - **Files**: `src/AccountServer/Handlers/AccountConnectionHandler.cs`
  - **Done when**: Handler issues token and sends redirect; build exits 0.
  - **Verify**: `dotnet build src/AccountServer/AccountServer.csproj && grep -r "IssueAsync\|redirect" src/AccountServer/Handlers/`
  - **Commit**: `feat(account/handler): issue token and send GameServer redirect packet`

---

- [ ] 1.20 [VERIFY] Quality checkpoint — AccountServer complete

  - **Do**: Build AccountServer end-to-end.
  - **Verify**: `dotnet build src/AccountServer/AccountServer.csproj 2>&1 | tail -5`
  - **Done when**: Build succeeded, 0 errors.
  - **Commit**: `chore(account): pass quality checkpoint — AccountServer auth flow complete`

---

- [ ] 1.21 GameServer Program.cs + appsettings.json

  - **Do**: Create `src/GameServer/Program.cs` using generic host. Load `appsettings.json` and env vars into `ServerConfig`. Register `TcpConnectionAcceptor`, `GameConnectionHandler`, `CharacterRepository`, `TokenService`, `GameSessionManager`, `MapRegistry`, `BroadcastService` as singletons. Add `src/GameServer/appsettings.json` mirroring AccountServer config plus `GameServer:Port` (5816), `MapFilesPath` (`data/maps`), `ScreenRangeTiles` (18), `CharacterSaveIntervalSeconds` (60).
  - **Files**: `src/GameServer/Program.cs`, `src/GameServer/appsettings.json`
  - **Done when**: `dotnet build src/GameServer` exits 0.
  - **Verify**: `dotnet build src/GameServer/GameServer.csproj 2>&1 | tail -3`
  - **Commit**: `feat(game): GameServer host builder with config and DI wiring`

---

- [ ] 1.22 CharacterRepository + characters migration

  - **Do**: Create `src/GameServer/Data/CharacterRecord.cs` (record with: Id, AccountId, Name, MapId, X, Y, Body, Hair, Class, Level). Create `src/GameServer/Data/CharacterRepository.cs` with methods: `FindByAccountIdAsync(int accountId)` returns `CharacterRecord?`, `CreateAsync(CharacterRecord)` inserts and returns new id, `SavePositionAsync(int charId, int mapId, int x, int y)`. Create `src/GameServer/Migrations/GameMigrations.cs` with `CreateCharacters` SQL (`CREATE TABLE IF NOT EXISTS characters (id INT AUTO_INCREMENT PRIMARY KEY, account_id INT NOT NULL UNIQUE, name VARCHAR(16) NOT NULL UNIQUE, map_id INT NOT NULL DEFAULT 1002, x SMALLINT NOT NULL DEFAULT 429, y SMALLINT NOT NULL DEFAULT 378, body SMALLINT NOT NULL DEFAULT 1003, hair SMALLINT NOT NULL DEFAULT 135, class SMALLINT NOT NULL DEFAULT 100, level TINYINT NOT NULL DEFAULT 1, updated_at DATETIME DEFAULT NOW())`). Run migration in GameServer startup.
  - **Files**: `src/GameServer/Data/CharacterRecord.cs`, `src/GameServer/Data/CharacterRepository.cs`, `src/GameServer/Migrations/GameMigrations.cs`
  - **Done when**: `dotnet build src/GameServer` exits 0.
  - **Verify**: `dotnet build src/GameServer/GameServer.csproj && grep -r "FindByAccountIdAsync" src/GameServer/Data/`
  - **Commit**: `feat(game/data): add CharacterRepository and characters schema migration`

---

- [ ] 1.23 GameSessionManager

  - **Do**: Create `src/GameServer/Session/GameSession.cs` holding: `AccountId`, `CharacterId`, `CharacterRecord`, `PacketFramer Framer`, `CancellationTokenSource Cts`. Create `src/GameServer/Session/GameSessionManager.cs` using `ConcurrentDictionary<int, ConcurrentDictionary<int, GameSession>>` keyed by `(mapId, charId)`. Methods: `AddSession(GameSession)`, `RemoveSession(int mapId, int charId)`, `GetSessionsInMap(int mapId)` returns `IEnumerable<GameSession>`, `GetSession(int charId)` linear scan across all maps.
  - **Files**: `src/GameServer/Session/GameSession.cs`, `src/GameServer/Session/GameSessionManager.cs`
  - **Done when**: `dotnet build src/GameServer` exits 0.
  - **Verify**: `dotnet build src/GameServer/GameServer.csproj && grep -r "ConcurrentDictionary" src/GameServer/Session/`
  - **Commit**: `feat(game/session): add GameSession and GameSessionManager with map-partitioned dictionary`

---

- [ ] 1.24 DmapParser + MapRegistry

  - **Do**: Create `src/GameServer/Map/DmapParser.cs`. Method `Parse(string filePath)` opens the `.dmap` binary file, reads the 4-byte magic header, then reads per-cell data. Each cell is a fixed-size struct; passability = `(cell.Mask & 1) == 0`. Returns `DmapData` with `Width`, `Height`, and `bool[,] Passable` grid. Create `src/GameServer/Map/MapRegistry.cs`. Constructor takes `string mapFilesPath, ILogger logger`. `LoadAsync()` scans `mapFilesPath` for `*.dmap` files, parses each via `DmapParser`, stores in `Dictionary<int, DmapData>` keyed by filename-derived map ID. `IsPassable(int mapId, int x, int y)` returns bool (false if map not found).
  - **Files**: `src/GameServer/Map/DmapParser.cs`, `src/GameServer/Map/MapRegistry.cs`, `src/GameServer/Map/DmapData.cs`
  - **Done when**: `dotnet build src/GameServer` exits 0.
  - **Verify**: `dotnet build src/GameServer/GameServer.csproj && grep -r "IsPassable\|DmapParser" src/GameServer/Map/`
  - **Commit**: `feat(game/map): add DmapParser and MapRegistry with DMAP passability parsing`

---

- [ ] 1.25 [VERIFY] Quality checkpoint — GameServer data + session + map

  - **Do**: Build GameServer after adding CharacterRepository, GameSessionManager, DmapParser.
  - **Verify**: `dotnet build src/GameServer/GameServer.csproj 2>&1 | tail -5`
  - **Done when**: Build succeeded, 0 errors.
  - **Commit**: `chore(game): pass quality checkpoint — data/session/map layers`

---

- [ ] 1.26 BroadcastService

  - **Do**: Create `src/GameServer/Broadcast/BroadcastService.cs`. Constructor takes `GameSessionManager sessions, ILogger logger`. Method `BroadcastToScreenAsync(int mapId, int originX, int originY, byte[] packet, int rangeTiles = 18)` iterates `sessions.GetSessionsInMap(mapId)`, checks Chebyshev distance (`Math.Max(|x - originX|, |y - originY|) <= rangeTiles`), sends `packet` via `session.Framer.WriteAsync` (catch and log exceptions per-session, do not throw). Method `BroadcastSpawnAsync(GameSession newSession, byte[] spawnPacket)` broadcasts the spawn packet to nearby sessions and sends all nearby players' spawn packets back to the new session.
  - **Files**: `src/GameServer/Broadcast/BroadcastService.cs`
  - **Done when**: `dotnet build src/GameServer` exits 0.
  - **Verify**: `dotnet build src/GameServer/GameServer.csproj && grep -r "Chebyshev\|BroadcastToScreenAsync" src/GameServer/Broadcast/`
  - **Commit**: `feat(game/broadcast): add BroadcastService with Chebyshev screen-range scan`

---

- [ ] 1.27 MovementHandler

  - **Do**: Create `src/GameServer/Handlers/MovementHandler.cs`. Method `HandleMoveAsync(GameSession session, byte[] packet, MapRegistry maps, BroadcastService broadcast, int rangeTiles)`: parse direction (byte at offset 8, values 0-7) and current position from packet. Compute new `(x, y)` from direction using 8-directional deltas (N/NE/E/SE/S/SW/W/NW). Call `maps.IsPassable(session.Character.MapId, newX, newY)`. On fail: send position-correction packet back to client (resend current position). On pass: update `session.Character.X/Y`. Call `broadcast.BroadcastToScreenAsync` with the movement packet.
  - **Files**: `src/GameServer/Handlers/MovementHandler.cs`
  - **Done when**: `dotnet build src/GameServer` exits 0.
  - **Verify**: `dotnet build src/GameServer/GameServer.csproj && grep -r "IsPassable\|direction" src/GameServer/Handlers/MovementHandler.cs`
  - **Commit**: `feat(game/handler): add MovementHandler with 8-directional adjacency and passability check`

---

- [ ] 1.28 GameConnectionHandler — token validation + character load

  - **Do**: Create `src/GameServer/Handlers/GameConnectionHandler.cs` implementing `IConnectionHandler`. In `HandleAsync`: create `PacketFramer` with `NullCipher`. Await first packet (type `0x03CF` — auth transfer). Parse token string (32 chars) from payload. Call `TokenService.ConsumeAsync(token)` — on null, close connection. Load character via `CharacterRepository.FindByAccountIdAsync(accountId)`. If none, create a default character at Twin City spawn (map 1002, x 429, y 378). Build `GameSession`, add to `GameSessionManager`. Send character-info packet (type `0x01F4`) with character data. Send map-info packet (type `0x0210`). Call `BroadcastService.BroadcastSpawnAsync`.
  - **Files**: `src/GameServer/Handlers/GameConnectionHandler.cs`
  - **Done when**: `dotnet build src/GameServer` exits 0.
  - **Verify**: `dotnet build src/GameServer/GameServer.csproj && grep -r "ConsumeAsync\|BroadcastSpawnAsync" src/GameServer/Handlers/`
  - **Commit**: `feat(game/handler): implement token validation, character load, and spawn broadcast`

---

- [ ] 1.29 GameConnectionHandler — movement loop + graceful shutdown

  - **Do**: Extend `GameConnectionHandler.HandleAsync` with the main packet loop: loop `framer.ReadNextAsync(ct)`, switch on packet type — `0x02145` → `MovementHandler.HandleMoveAsync`, unknown → log and ignore. On loop exit (disconnect or cancellation): call `CharacterRepository.SavePositionAsync` for the session, call `GameSessionManager.RemoveSession`, call `BroadcastService.BroadcastToScreenAsync` with a despawn packet (type `0x0251`). Register a `CancellationToken` callback from `IHostApplicationLifetime` to trigger graceful shutdown of all active sessions (iterate `GameSessionManager`, save all positions).
  - **Files**: `src/GameServer/Handlers/GameConnectionHandler.cs`, `src/GameServer/Program.cs`
  - **Done when**: `dotnet build src/GameServer` exits 0; movement loop and shutdown callback compile.
  - **Verify**: `dotnet build src/GameServer/GameServer.csproj && grep -r "SavePositionAsync\|IHostApplicationLifetime" src/GameServer/`
  - **Commit**: `feat(game/handler): add movement packet loop and graceful shutdown position save`

---

- [ ] 1.30 [VERIFY] Quality checkpoint — GameServer handlers complete

  - **Do**: Build full GameServer.
  - **Verify**: `dotnet build src/GameServer/GameServer.csproj 2>&1 | tail -5`
  - **Done when**: Build succeeded, 0 errors.
  - **Commit**: `chore(game): pass quality checkpoint — all handlers complete`

---

- [ ] 1.31 Docker Compose + Dockerfiles

  - **Do**: Create `docker/account-server.Dockerfile` and `docker/game-server.Dockerfile` — both use `mcr.microsoft.com/dotnet/sdk:8.0` to build and `mcr.microsoft.com/dotnet/aspnet:8.0` to run. Create `docker/init/01-schema.sql` seeding one test account (`username=test, password_hash=<MD5 of "test">`) and one test character at Twin City spawn. Create `docker-compose.yml` with three services: `mysql` (image `mysql:8`, healthcheck `mysqladmin ping`), `account-server` (depends on mysql healthy, port 9958:9958), `game-server` (depends on mysql healthy, port 5816:5816). Pass `ConnectionStrings__Default` and `GameServer__PublicIp` via env.
  - **Files**: `docker/account-server.Dockerfile`, `docker/game-server.Dockerfile`, `docker/init/01-schema.sql`, `docker-compose.yml`
  - **Done when**: `docker-compose config` exits 0 (validates compose file syntax).
  - **Verify**: `docker-compose config 2>&1 | tail -5`
  - **Commit**: `feat(docker): add Dockerfiles, init SQL, and docker-compose with MySQL health check`

---

- [ ] 1.32 POC Checkpoint — full solution build

  - **Do**: Build the entire solution to confirm all three projects compile together with no cross-project reference errors.
  - **Verify**: `dotnet build ConquerServer.sln 2>&1 | tail -10`
  - **Done when**: `Build succeeded` for all three projects, 0 errors.
  - **Commit**: `chore(poc): POC vertical slice build passes — all projects compile`

---

## Phase 2: Refactor

Focus: Clean up POC code. Proper error handling, config externalization, structured logging, partial-read buffering, packet DTO structs, graceful shutdown polish.

---

- [ ] 2.1 PacketFramer — partial-read buffering

  - **Do**: Rewrite `PacketFramer.ReadNextAsync` to use a `byte[8192]` ring buffer. Loop: read available bytes into buffer tail, advance tail pointer, check if we have ≥4 bytes for the header, then check if we have the full `length` bytes. Return complete packet only when all bytes arrived. Guard: if parsed `length > MaxPacketSize`, throw / close connection with a log warning.
  - **Files**: `src/Common/Packets/PacketFramer.cs`
  - **Done when**: `dotnet build src/Common` exits 0; method handles multi-read scenarios.
  - **Verify**: `dotnet build src/Common/Common.csproj && grep -r "ringBuffer\|partial\|MaxPacketSize" src/Common/Packets/PacketFramer.cs`
  - **Commit**: `refactor(common/packets): add ring-buffer partial-read handling to PacketFramer`

---

- [ ] 2.2 Packet DTO structs (Span-based)

  - **Do**: Create `src/Common/Packets/Dto/` directory. Add struct DTOs for the key packets using `MemoryMarshal.Read<T>` / `Span<byte>` field accessors: `LoginPacket` (type, username, password), `DhClientKeyPacket` (public key bytes), `RedirectPacket` (ip, port, token), `CharacterInfoPacket` (id, name, mapId, x, y, body, hair, class, level), `MovePacket` (charId, direction, x, y). Each struct has a static `Parse(ReadOnlySpan<byte>)` factory and a `Write(Span<byte>)` method.
  - **Files**: `src/Common/Packets/Dto/LoginPacket.cs`, `src/Common/Packets/Dto/DhClientKeyPacket.cs`, `src/Common/Packets/Dto/RedirectPacket.cs`, `src/Common/Packets/Dto/CharacterInfoPacket.cs`, `src/Common/Packets/Dto/MovePacket.cs`
  - **Done when**: `dotnet build src/Common` exits 0; all five DTOs compile.
  - **Verify**: `dotnet build src/Common/Common.csproj && ls src/Common/Packets/Dto/ | wc -l`
  - **Commit**: `refactor(common/packets): add Span-based packet DTO structs`

---

- [ ] 2.3 Wire DTO structs into handlers

  - **Do**: Update `AccountConnectionHandler` to use `LoginPacket.Parse` and `DhClientKeyPacket.Parse` instead of raw offset arithmetic. Update `GameConnectionHandler` to use `MovePacket.Parse`. Update packet write calls to use `CharacterInfoPacket.Write` and `RedirectPacket.Write`.
  - **Files**: `src/AccountServer/Handlers/AccountConnectionHandler.cs`, `src/GameServer/Handlers/GameConnectionHandler.cs`, `src/GameServer/Handlers/MovementHandler.cs`
  - **Done when**: `dotnet build ConquerServer.sln` exits 0; handlers no longer contain raw magic-number byte offsets.
  - **Verify**: `dotnet build ConquerServer.sln && grep -rn "bytes\[" src/AccountServer/Handlers/ src/GameServer/Handlers/ | wc -l`
  - **Commit**: `refactor(handlers): replace raw byte offsets with typed packet DTOs`

---

- [ ] 2.4 [VERIFY] Quality checkpoint — packet layer refactor

  - **Do**: Build full solution after packet DTO refactor.
  - **Verify**: `dotnet build ConquerServer.sln 2>&1 | tail -5`
  - **Done when**: Build succeeded, 0 errors.
  - **Commit**: `chore(refactor): pass quality checkpoint — packet DTO layer`

---

- [ ] 2.5 Structured logging + error handling

  - **Do**: Add `ILogger<T>` to all handlers, repositories, and services (replace bare `Console.WriteLine` used in POC). In `AccountConnectionHandler` and `GameConnectionHandler`, wrap the main loop in `try/catch(Exception ex)` — log `"Connection error: {ex.Message}"` at Warning, ensure connection closes. In `TcpConnectionAcceptor`, log `"Accepted connection from {endpoint}"` at Debug, `"Handler threw: {ex}"` at Error.
  - **Files**: `src/Common/Net/TcpConnectionAcceptor.cs`, `src/AccountServer/Handlers/AccountConnectionHandler.cs`, `src/GameServer/Handlers/GameConnectionHandler.cs`
  - **Done when**: `dotnet build ConquerServer.sln` exits 0; no bare `Console.Write` calls remain.
  - **Verify**: `dotnet build ConquerServer.sln && grep -rn "Console.Write" src/ | wc -l`
  - **Commit**: `refactor(logging): replace Console.Write with ILogger structured logging across all handlers`

---

- [ ] 2.6 Config externalization + env var overrides

  - **Do**: In both `Program.cs` files, bind `IConfiguration` to `ServerConfig` using `config.GetSection("AccountServer").Bind(serverConfig)` pattern. Ensure all hardcoded ports, IPs, TTLs, and paths are sourced exclusively from `ServerConfig`. Add XML doc comments to `ServerConfig` properties. Verify `docker-compose.yml` env var names match the double-underscore convention (e.g., `ConnectionStrings__Default`).
  - **Files**: `src/AccountServer/Program.cs`, `src/GameServer/Program.cs`, `src/Common/Config/ServerConfig.cs`, `docker-compose.yml`
  - **Done when**: `dotnet build ConquerServer.sln` exits 0; grep finds no magic-number port literals outside config.
  - **Verify**: `dotnet build ConquerServer.sln && grep -rn "9958\|5816" src/ | grep -v "appsettings\|\.xml" | wc -l`
  - **Commit**: `refactor(config): externalize all config values, document ServerConfig properties`

---

- [ ] 2.7 Graceful shutdown — periodic character save

  - **Do**: In `GameServer/Program.cs`, add a background `IHostedService` (`CharacterSaveService`) that on a timer (`CharacterSaveIntervalSeconds`) iterates all active sessions in `GameSessionManager` and calls `CharacterRepository.SavePositionAsync` for each. On `StopAsync`, do a final save pass for all sessions before returning.
  - **Files**: `src/GameServer/Services/CharacterSaveService.cs`, `src/GameServer/Program.cs`
  - **Done when**: `dotnet build src/GameServer` exits 0; `CharacterSaveService` implements `IHostedService`.
  - **Verify**: `dotnet build src/GameServer/GameServer.csproj && grep -r "CharacterSaveService\|IHostedService" src/GameServer/`
  - **Commit**: `refactor(game/shutdown): add CharacterSaveService for periodic and shutdown position persistence`

---

- [ ] 2.8 [VERIFY] Quality checkpoint — Phase 2 complete

  - **Do**: Build full solution after all refactor tasks.
  - **Verify**: `dotnet build ConquerServer.sln 2>&1 | tail -5`
  - **Done when**: Build succeeded, 0 errors.
  - **Commit**: `chore(refactor): pass quality checkpoint — Phase 2 complete`

---

## Phase 3: Testing

Focus: Unit tests for crypto and protocol logic; integration tests for TokenService and auth flow with Testcontainers MySQL.

---

- [ ] 3.1 Test projects scaffold

  - **Do**: Add `tests/Common.Tests/Common.Tests.csproj` (.NET 8, xUnit, `<IsPackable>false`). Add `tests/GameServer.Tests/GameServer.Tests.csproj` (xUnit). Add `tests/Integration.Tests/Integration.Tests.csproj` (xUnit + `Testcontainers.MySql`). Add all three to `ConquerServer.sln`. Reference `Common` from `Common.Tests` and `Integration.Tests`; reference `AccountServer` lib types from `Integration.Tests`.
  - **Files**: `tests/Common.Tests/Common.Tests.csproj`, `tests/GameServer.Tests/GameServer.Tests.csproj`, `tests/Integration.Tests/Integration.Tests.csproj`, `ConquerServer.sln`
  - **Done when**: `dotnet build ConquerServer.sln` exits 0; `dotnet test` finds three test assemblies (0 tests, no failures).
  - **Verify**: `dotnet build ConquerServer.sln && dotnet test --no-build 2>&1 | grep -E "passed|failed|Test run"`
  - **Commit**: `chore(tests): scaffold Common.Tests, GameServer.Tests, Integration.Tests projects`

---

- [ ] 3.2 Rc5Cipher unit tests

  - **Do**: Create `tests/Common.Tests/Crypto/Rc5CipherTests.cs`. Tests: (1) encrypt then decrypt 8-byte block returns original; (2) encrypting with two different keys produces different output; (3) known plaintext/ciphertext pair (use a community-verified CO 5017 test vector if available, otherwise a standard RC5-32/12/16 test vector).
  - **Files**: `tests/Common.Tests/Crypto/Rc5CipherTests.cs`
  - **Done when**: `dotnet test tests/Common.Tests` passes all 3 tests.
  - **Verify**: `dotnet test tests/Common.Tests/Common.Tests.csproj --filter "Rc5Cipher" 2>&1 | tail -5`
  - **Commit**: `test(common/crypto): add RC5 cipher round-trip and known-vector unit tests`

---

- [ ] 3.3 DhKeyExchange unit tests

  - **Do**: Create `tests/Common.Tests/Crypto/DhKeyExchangeTests.cs`. Tests: (1) `GenerateKeyPair` returns private ≠ public; (2) two parties deriving shared secret from each other's public keys produce the same result (`serverShared == clientShared`); (3) `DeriveRc5Key` returns exactly 16 bytes.
  - **Files**: `tests/Common.Tests/Crypto/DhKeyExchangeTests.cs`
  - **Done when**: `dotnet test tests/Common.Tests` passes all 3 DH tests.
  - **Verify**: `dotnet test tests/Common.Tests/Common.Tests.csproj --filter "DhKeyExchange" 2>&1 | tail -5`
  - **Commit**: `test(common/crypto): add DH key exchange symmetry and RC5 key derivation tests`

---

- [ ] 3.4 PacketFramer unit tests

  - **Do**: Create `tests/Common.Tests/Packets/PacketFramerTests.cs`. Tests using a `MemoryStream` as the stream: (1) write a packet then read it back returns the same bytes; (2) reading two back-to-back packets in one stream returns both correctly; (3) packet exceeding `MaxPacketSize` causes `ReadNextAsync` to throw or return null; (4) partial write (split across two stream flushes) still returns the complete packet.
  - **Files**: `tests/Common.Tests/Packets/PacketFramerTests.cs`
  - **Done when**: `dotnet test tests/Common.Tests --filter PacketFramer` passes all 4 tests.
  - **Verify**: `dotnet test tests/Common.Tests/Common.Tests.csproj --filter "PacketFramer" 2>&1 | tail -5`
  - **Commit**: `test(common/packets): add PacketFramer round-trip, back-to-back, oversized, and partial-read tests`

---

- [ ] 3.5 [VERIFY] Quality checkpoint — Common.Tests passing

  - **Do**: Run all Common.Tests.
  - **Verify**: `dotnet test tests/Common.Tests/Common.Tests.csproj 2>&1 | tail -5`
  - **Done when**: All tests pass, 0 failures.
  - **Commit**: `chore(tests): pass quality checkpoint — Common.Tests all green`

---

- [ ] 3.6 DmapParser unit tests

  - **Do**: Create `tests/GameServer.Tests/Map/DmapParserTests.cs`. Create a minimal synthetic `.dmap` binary fixture (4-byte magic + 4 cells, alternating passable/blocked). Tests: (1) parsed width/height match fixture dimensions; (2) passable cell returns `IsPassable == true`; (3) blocked cell returns `IsPassable == false`; (4) file with wrong magic throws `InvalidDataException`.
  - **Files**: `tests/GameServer.Tests/Map/DmapParserTests.cs`, `tests/GameServer.Tests/Map/Fixtures/test.dmap` (binary)
  - **Done when**: `dotnet test tests/GameServer.Tests --filter DmapParser` passes all 4 tests.
  - **Verify**: `dotnet test tests/GameServer.Tests/GameServer.Tests.csproj --filter "DmapParser" 2>&1 | tail -5`
  - **Commit**: `test(game/map): add DmapParser unit tests with synthetic binary fixture`

---

- [ ] 3.7 MovementHandler unit tests

  - **Do**: Create `tests/GameServer.Tests/Handlers/MovementHandlerTests.cs`. Mock `MapRegistry` returning passable for most tiles. Tests: (1) each of the 8 directions advances position by exactly 1 tile in the correct direction; (2) moving into a blocked tile leaves position unchanged; (3) moving outside map bounds (x<0, y<0) is rejected; (4) valid move triggers broadcast call (use a mock `BroadcastService`).
  - **Files**: `tests/GameServer.Tests/Handlers/MovementHandlerTests.cs`
  - **Done when**: `dotnet test tests/GameServer.Tests --filter MovementHandler` passes all 4 tests.
  - **Verify**: `dotnet test tests/GameServer.Tests/GameServer.Tests.csproj --filter "MovementHandler" 2>&1 | tail -5`
  - **Commit**: `test(game/handler): add MovementHandler direction, passability, and boundary unit tests`

---

- [ ] 3.8 TokenService integration test (Testcontainers)

  - **Do**: Create `tests/Integration.Tests/TokenServiceTests.cs`. Use `Testcontainers.MySql` to spin up a real MySQL container. Run `AccountMigrations.CreateTransferTokens` via `MigrationRunner`. Tests: (1) `IssueAsync` inserts a row and returns a 32-char token; (2) `ConsumeAsync` with a valid fresh token returns the correct `accountId` and sets `consumed_at`; (3) second `ConsumeAsync` on the same token returns null (already consumed); (4) `ConsumeAsync` on an expired token returns null.
  - **Files**: `tests/Integration.Tests/TokenServiceTests.cs`
  - **Done when**: `dotnet test tests/Integration.Tests --filter TokenService` passes all 4 tests (requires Docker for Testcontainers).
  - **Verify**: `dotnet test tests/Integration.Tests/Integration.Tests.csproj --filter "TokenService" 2>&1 | tail -5`
  - **Commit**: `test(integration): add TokenService Testcontainers integration tests`

---

- [ ] 3.9 [VERIFY] Quality checkpoint — all tests passing

  - **Do**: Run the entire test suite.
  - **Verify**: `dotnet test ConquerServer.sln 2>&1 | tail -10`
  - **Done when**: All tests pass across Common.Tests, GameServer.Tests, Integration.Tests.
  - **Commit**: `chore(tests): pass quality checkpoint — full test suite green`

---

## Phase 4: Quality Gates

---

- [ ] 4.1 README with setup instructions

  - **Do**: Create `README.md` at repo root. Include: project description (1 paragraph), prerequisites (Docker + .NET 8 SDK), 5-command setup (`git clone`, `cp data/README.md` note about dmap files, `docker-compose up -d`, `dotnet run --project src/AccountServer`, `dotnet run --project src/GameServer`), connection info (ports 9958/5816), how to run tests (`dotnet test`). Keep to ~60 lines.
  - **Files**: `README.md`
  - **Done when**: File exists with all five setup commands, under 80 lines.
  - **Verify**: `wc -l README.md && grep -c "docker-compose\|dotnet run\|dotnet test" README.md`
  - **Commit**: `docs(readme): add setup guide with prerequisites, 5-command quickstart, test instructions`

---

- [ ] 4.2 Full solution build + test

  - **Do**: Run complete build and test cycle.
  - **Verify**: `dotnet build ConquerServer.sln 2>&1 | tail -3 && dotnet test ConquerServer.sln 2>&1 | tail -5`
  - **Done when**: Build succeeded 0 errors, all tests pass.
  - **Commit**: `chore(quality): verify full solution build and test suite before PR`

---

- [ ] 4.3 V4 Full local CI

  - **Do**: Run build, all tests, verify Docker Compose config validates.
  - **Verify**: `dotnet build ConquerServer.sln && dotnet test ConquerServer.sln && docker-compose config && echo LOCAL_CI_PASS`
  - **Done when**: All three commands exit 0.
  - **Commit**: `chore(ci): local CI pass — build + tests + compose config valid`

---

- [ ] 4.4 Create PR

  - **Do**: Verify on feature branch. Push. Create PR: `gh pr create --title "feat(m1): Conquer Online Milestone 1 — auth, world entry, movement" --body "$(cat <<'EOF'\n## Summary\n- Implements full AccountServer DH/RC5 auth flow with MySQL token handoff\n- Implements GameServer character load, DMAP passability, 8-directional movement, screen-range broadcast\n- Docker Compose with MySQL health checks and init SQL seed data\n- Unit + integration tests for crypto, framing, movement, token service\n\n## Test plan\n- [ ] dotnet build ConquerServer.sln — 0 errors\n- [ ] dotnet test — all tests green\n- [ ] docker-compose up — all three containers healthy\n- [ ] AccountServer accepts TCP on 9958, GameServer on 5816\nEOF\n)"`.
  - **Verify**: `gh pr checks --watch 2>&1 | tail -10`
  - **Done when**: PR created; CI checks green.
  - **Commit**: None (PR creation step)

---

## Phase 5: E2E Verification

---

- [ ] VE1 [VERIFY] Docker Compose startup — all containers healthy

  - **Do**:
    1. Build images and start all services: `docker-compose up --build -d`
    2. Record compose process: `echo "compose started" > /tmp/ve-pids.txt`
    3. Wait up to 90 seconds for all services healthy: `for i in $(seq 1 90); do docker-compose ps | grep -E "healthy" | wc -l | grep -q "^3$" && break || sleep 1; done`
  - **Verify**: `docker-compose ps 2>&1 | grep "healthy" | wc -l | grep -q "^3$" && echo VE1_PASS`
  - **Done when**: All three containers (mysql, account-server, game-server) show `healthy` status.
  - **Commit**: None

---

- [ ] VE2 [VERIFY] AccountServer accepts TCP on port 9958

  - **Do**:
    1. Test TCP connection to AccountServer: attempt to connect and check port is open.
    2. Verify GameServer port: same check on 5816.
    3. Check container logs for startup confirmation lines.
  - **Verify**: `(echo "" | nc -w 2 localhost 9958 && echo AS_PORT_OK) && (echo "" | nc -w 2 localhost 5816 && echo GS_PORT_OK) && docker-compose logs account-server 2>&1 | grep -i "listen\|start\|port" | head -5 && echo VE2_PASS`
  - **Done when**: Both ports accept TCP connections; logs confirm server startup messages.
  - **Commit**: None

---

- [ ] VE3 [VERIFY] Server logs show clean startup (no exceptions)

  - **Do**:
    1. Read AccountServer logs for fatal errors.
    2. Read GameServer logs for fatal errors.
    3. Confirm migration ran (log line referencing `CREATE TABLE IF NOT EXISTS` or `Migration`).
  - **Verify**: `docker-compose logs account-server 2>&1 | grep -iE "exception|fatal|unhandled" | wc -l | grep -q "^0$" && docker-compose logs game-server 2>&1 | grep -iE "exception|fatal|unhandled" | wc -l | grep -q "^0$" && echo VE3_PASS`
  - **Done when**: Zero exception/fatal lines in either server log.
  - **Commit**: None

---

- [ ] VE4 [VERIFY] Full test suite passes in Docker context

  - **Do**: Run `dotnet test` against the running MySQL container to include integration tests.
  - **Verify**: `ConnectionStrings__Default="server=localhost;port=3306;database=conquer;user=root;password=conquer;" dotnet test ConquerServer.sln 2>&1 | tail -10`
  - **Done when**: All tests pass including Testcontainers integration tests.
  - **Commit**: `chore(e2e): all tests pass including integration tests against MySQL`

---

- [ ] VE5 [VERIFY] Docker Compose teardown

  - **Do**:
    1. Stop and remove containers and volumes: `docker-compose down -v`
    2. Verify no orphan containers.
    3. Remove PID file.
  - **Verify**: `docker-compose down -v && docker-compose ps 2>&1 | grep -c "Up" | grep -q "^0$" && rm -f /tmp/ve-pids.txt && echo VE5_PASS`
  - **Done when**: All containers stopped and removed, volumes purged.
  - **Commit**: None

---

## Notes

**POC shortcuts taken (Phase 1):**
- PacketFramer assumes complete packet arrives in one read (no partial-read buffering)
- Raw byte offsets used in handlers instead of typed DTO structs
- MD5 password comparison done inline in handler
- No input validation on packet sizes before parse
- `Guid.NewGuid().ToString("N")` used for token generation (not cryptographically sequenced)
- GameServer uses `NullCipher` — 5017 GS encryption not yet confirmed

**Production TODOs (post-M1):**
- Confirm 5017 GameServer cipher type from protocol captures; swap `NullCipher` → real cipher
- Add account registration flow (currently seed-only)
- Replace linear session scan with spatial index (R-tree or grid) for >10 concurrent players
- Add item system, combat, NPC/monster AI (Milestone 2+)
- Add rate limiting on login attempts
- Switch token generation to `RandomNumberGenerator.GetHexString(32)` for cryptographic quality
