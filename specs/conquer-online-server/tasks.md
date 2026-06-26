# Tasks: Conquer Online Server (5065 Modernization)

**Total tasks: 60** (Phase 1: 31, Phase 2: 9, Phase 3: 5, Phase 4: 15 including VE/V tasks)

**Workflow**: POC-first (GREENFIELD — fresh .NET 8 solution forked from Redux)
**Quality gate command**: `dotnet build --no-incremental 2>&1 | tail -5`
**Working directory**: `C:/Users/Windows/conquer-server/src/`

---

## Phase 1: Make It Work (POC)

Focus: Fork Redux, retarget to .NET 8, strip native DLLs, fix compilation, add Dapper + crypto + TinyMap infrastructure, implement auth flow, wire Docker Compose. Skip tests. Accept shortcuts. Ship working auth.

---

- [x] 1.1 Clone Redux source into `src/`
  - **Do**:
    1. `git clone https://github.com/conquer-online/redux C:/Users/Windows/conquer-server/src`
    2. `cd C:/Users/Windows/conquer-server/src && git checkout -b modernize/m1`
    3. Verify `C:/Users/Windows/conquer-server/src/ConquerServer.csproj` exists
  - **Files**: `C:/Users/Windows/conquer-server/src/` (entire cloned repo)
  - **Done when**: `ConquerServer.csproj` present at `src/` root; branch `modernize/m1` checked out
  - **Verify**: `Test-Path C:/Users/Windows/conquer-server/src/ConquerServer.csproj && echo PASS`
  - **Commit**: `chore(repo): fork redux into src/ on modernize/m1 branch`
  - _Requirements: FR-2_

- [x] 1.2 Retarget `.csproj` to `net8.0` / AnyCPU
  - **Do**:
    1. Open `src/ConquerServer.csproj`
    2. Replace `<TargetFrameworkVersion>v4.0</TargetFrameworkVersion>` with `<TargetFramework>net8.0</TargetFramework>` (and remove the old `TargetFrameworkVersion` node if separate)
    3. Remove `<PlatformTarget>x86</PlatformTarget>` and any `<Platform>x86</Platform>` condition blocks
    4. Replace the outer `<Project ToolsVersion="...">` SDK-style open tag with `<Project Sdk="Microsoft.NET.Sdk">` if Redux uses old-style format; ensure `<PropertyGroup>` contains `<OutputType>Exe</OutputType>` and `<TargetFramework>net8.0</TargetFramework>`
    5. Run `dotnet build 2>&1 | head -40` to capture first error wave (do not fix yet — just record)
  - **Files**: `C:/Users/Windows/conquer-server/src/ConquerServer.csproj`
  - **Done when**: `<TargetFramework>net8.0</TargetFramework>` present; `x86` absent; `dotnet build` runs (even if errors)
  - **Verify**: `Select-String -Path C:/Users/Windows/conquer-server/src/ConquerServer.csproj -Pattern 'net8.0' | Select-Object -First 1`
  - **Commit**: `build(csproj): retarget to net8.0 AnyCPU`
  - _Requirements: FR-2, NFR-1, NFR-2_

- [x] 1.3 Audit and remove native DLL references from `.csproj`
  - **Do**:
    1. Search `ConquerServer.csproj` for `ManagedOpenSsl`, `TinyMap`, `<Reference>`, `<Content>`, `<None>` nodes referencing these DLLs
    2. Delete those `<Reference>` and `<Content>` XML nodes
    3. Delete the physical files: `src/lib/ManagedOpenSsl.dll`, `src/lib/TinyMap.dll` (and `src/TinyMap.dll` if at root) — use `Remove-Item -Force` if they exist
    4. Search for `[DllImport]` attributes in `.cs` files referencing either DLL name; note the file paths (fix in 1.4)
  - **Files**: `C:/Users/Windows/conquer-server/src/ConquerServer.csproj`, any `lib/` DLL files
  - **Done when**: No `ManagedOpenSsl` or `TinyMap.dll` in `.csproj`; physical DLL files absent
  - **Verify**: `Select-String -Path C:/Users/Windows/conquer-server/src/ConquerServer.csproj -Pattern 'ManagedOpenSsl|TinyMap\.dll' | Measure-Object | Select-Object -ExpandProperty Count`
  - **Commit**: `build(csproj): remove ManagedOpenSsl.dll and TinyMap.dll native references`
  - _Requirements: FR-3, FR-4, NFR-6_

- [x] 1.4 Remove NHibernate packages and `.hbm.xml` mapping files
  - **Do**:
    1. In `ConquerServer.csproj`, delete all `<PackageReference>` nodes where `Include` contains `NHibernate`, `FluentNHibernate`, or `Iesi.Collections`
    2. Also delete any `<Reference>` nodes for NHibernate DLLs
    3. Find and delete all `.hbm.xml` files: `Get-ChildItem -Recurse -Filter '*.hbm.xml' C:/Users/Windows/conquer-server/src | Remove-Item -Force`
    4. Find and delete `hibernate.cfg.xml` or `NHibernate.cfg.xml` if present
    5. Note any `.cs` files that reference `NHibernate`, `ISession`, `SessionFactory` (fix in 1.5)
  - **Files**: `C:/Users/Windows/conquer-server/src/ConquerServer.csproj`, all `*.hbm.xml` files
  - **Done when**: Zero `.hbm.xml` files in `src/`; zero NHibernate `<PackageReference>` entries in `.csproj`
  - **Verify**: `(Get-ChildItem -Recurse -Filter '*.hbm.xml' C:/Users/Windows/conquer-server/src | Measure-Object).Count`
  - **Commit**: `build(csproj): remove NHibernate packages and mapping XML files`
  - _Requirements: FR-1_

- [x] 1.5 Add Dapper, MySqlConnector, and Microsoft.Extensions.Configuration NuGet packages
  - **Do**:
    1. `cd C:/Users/Windows/conquer-server/src && dotnet add package Dapper --version "2.*"`
    2. `dotnet add package MySqlConnector --version "2.*"`
    3. `dotnet add package Microsoft.Extensions.Configuration --version "8.*"`
    4. `dotnet add package Microsoft.Extensions.Configuration.Json --version "8.*"`
    5. `dotnet add package Microsoft.Extensions.Configuration.EnvironmentVariables --version "8.*"`
  - **Files**: `C:/Users/Windows/conquer-server/src/ConquerServer.csproj`
  - **Done when**: Five `<PackageReference>` entries present; `dotnet restore` exits 0
  - **Verify**: `cd C:/Users/Windows/conquer-server/src; dotnet restore 2>&1 | Select-String 'error' | Measure-Object | Select-Object -ExpandProperty Count`
  - **Commit**: `build(deps): add Dapper MySqlConnector and Microsoft.Extensions.Configuration`
  - _Requirements: FR-1, FR-15_

- [x] V1 [VERIFY] Quality checkpoint: `.csproj` structure clean, restore succeeds
  - **Do**: Run `dotnet restore` and verify `.csproj` has no NHibernate refs and has the 5 new packages
  - **Verify**: `cd C:/Users/Windows/conquer-server/src; dotnet restore 2>&1 | tail -3`
  - **Done when**: `dotnet restore` exits 0 with no error lines
  - **Commit**: `chore(build): fix restore errors if any`
  - _Requirements: FR-1, FR-2_

- [x] V1.1 [FIX V1] Fix: Remove legacy MySql.Data DLL reference conflicting with MySqlConnector
  - **Do**: Address the error: Legacy `<Reference Include="MySql.Data">` with net452 HintPath coexists with MySqlConnector PackageReference in Redux.csproj
    1. Remove the entire `<ItemGroup>` containing the `MySql.Data` Reference (lines with `<Reference Include="MySql.Data">` and its `<HintPath>`)
    2. Remove the stale comment `<!-- Legacy NuGet DLL references — to be replaced with PackageReferences in task 1.3 -->`
    3. Run `dotnet restore` to confirm exit 0 and no MySql.Data hint path remains
  - **Files**: `C:/Users/Windows/conquer-server/src/Redux/Redux.csproj`
  - **Done when**: No `MySql.Data` reference in Redux.csproj; `dotnet restore` exits 0
  - **Verify**: `Select-String -Path C:/Users/Windows/conquer-server/src/Redux/Redux.csproj -Pattern 'MySql\.Data' | Measure-Object | Select-Object -ExpandProperty Count`
  - **Commit**: `fix(csproj): remove legacy MySql.Data DLL reference conflicting with MySqlConnector`

---

- [x] 1.6 Stub out / remove NHibernate usages in Redux `.cs` files to unblock build
  - **Do**:
    1. Run `dotnet build 2>&1 | Select-String 'error CS' | Select-Object -First 50` from `src/` to get the full error list
    2. For every file referencing `NHibernate`, `ISession`, `SessionFactory`, `IQuery`: delete NHibernate `using` directives and comment out or stub the usages with `// TODO-M1: NHibernate removed` so the file compiles
    3. For files with `[DllImport("ManagedOpenSsl")]` or `[DllImport("TinyMap")]`: comment out the `[DllImport]` block and replace the method body with `throw new NotImplementedException("native DLL removed");`
    4. Do NOT fix game logic at this stage — only make the file parse/compile
  - **Files**: Any `.cs` in `C:/Users/Windows/conquer-server/src/` that reference NHibernate or removed DLLs
  - **Done when**: No `error CS0246` for NHibernate types; no `error CS0246` for removed DLL imports
  - **Verify**: `cd C:/Users/Windows/conquer-server/src; dotnet build 2>&1 | Select-String 'NHibernate' | Measure-Object | Select-Object -ExpandProperty Count`
  - **Commit**: `fix(compat): stub out NHibernate and native DLL usages`
  - _Requirements: FR-1, FR-3, FR-14_

- [x] 1.7 Fix .NET 8 API breaks in Redux game logic files
  - **Do**:
    1. Run `dotnet build 2>&1 | Select-String 'error CS'` to enumerate remaining errors
    2. Fix each error category in order:
       - `System.Windows.Forms` references: comment out with `// TODO-M1: Win-Forms removed`
       - `Thread.Abort()` calls: replace with `cts.Cancel()` or `return`
       - `System.Web` references: comment out
       - Obsolete `Encoding.Default` usage if it causes errors: replace with `Encoding.Latin1`
       - Missing `using` directives for moved APIs: add the correct `using`
    3. Repeat `dotnet build` until zero `error CS` lines
  - **Files**: Any `.cs` file producing compilation errors in `C:/Users/Windows/conquer-server/src/`
  - **Done when**: `dotnet build --no-incremental` exits 0 with zero `error CS` lines
  - **Verify**: `cd C:/Users/Windows/conquer-server/src; dotnet build --no-incremental 2>&1 | Select-String '^.*error CS' | Measure-Object | Select-Object -ExpandProperty Count`
  - **Commit**: `fix(compat): fix .NET 8 API breaks in Redux game logic`
  - _Requirements: FR-2, FR-14_

- [x] V2 [VERIFY] Quality checkpoint: clean build with zero errors
  - **Do**: Run `dotnet build --no-incremental` from `src/` and verify exit 0 and "Build succeeded"
  - **Verify**: `cd C:/Users/Windows/conquer-server/src; dotnet build --no-incremental 2>&1 | tail -5`
  - **Done when**: Output contains "Build succeeded" and zero `error` lines
  - **Commit**: `chore(build): resolve remaining build errors`
  - _Requirements: FR-2, FR-14_

---

- [x] 1.8 [P] Read Comet@5017 `TQCipher.cs` and `RC5.cs` references via WebFetch
  - **Do**:
    1. Fetch `https://raw.githubusercontent.com/conquer-online/comet/5017/src/Comet.Network/Security/TQCipher.cs` and read the full file
    2. Fetch `https://raw.githubusercontent.com/conquer-online/comet/5017/src/Comet.Network/Security/RC5.cs` and read the full file
    3. Note: (a) K1 static seed bytes, (b) K2 derivation algorithm from ulong token, (c) XOR decrypt/encrypt per-byte formula, (d) RC5 hardcoded key bytes, (e) RC5 key schedule and decrypt loop structure
    4. Record these values in comments at the top of the files you will create in 1.9 and 1.10
    5. Write research findings to `C:/Users/Windows/conquer-server/specs/conquer-online-server/crypto-notes.md` with the K1 seed bytes, K2 derivation algorithm, XOR formula, and RC5 key bytes
  - **Files**: `C:/Users/Windows/conquer-server/specs/conquer-online-server/crypto-notes.md`
  - **Done when**: K1 seed, K2 derivation, XOR formula, RC5 key, and RC5 schedule all noted; `crypto-notes.md` written with findings
  - **Verify**: `Select-String -Path C:/Users/Windows/conquer-server/specs/conquer-online-server/crypto-notes.md -Pattern 'K1|K2|RC5' | Measure-Object | Select-Object -ExpandProperty Count`
  - **Commit**: None (research task)
  - _Requirements: FR-6, FR-7_

- [x] 1.9 [P] Audit Redux DDL for SHA1 format and MySQL 5.6 syntax
  - **Do**:
    1. Find the Redux SQL dump file: `Get-ChildItem -Recurse -Filter '*.sql' C:/Users/Windows/conquer-server/src | Select-Object FullName`
    2. Read the `account` table `CREATE TABLE` definition — note the `Password` column type (VARCHAR(40)=hex SHA1, VARCHAR(64)=base64, BINARY(20)=raw)
    3. Note any: `CHARACTER SET utf8` (→ replace with `utf8mb4`), `ZEROFILL`, missing `DEFAULT` on `NOT NULL`, `ENGINE=MyISAM` (→ replace with InnoDB), MySQL 5.6-only syntax
    4. Document the SHA1 storage format in a comment block at top of `init.sql` (created in 1.18)
    5. Write DDL audit findings to `C:/Users/Windows/conquer-server/specs/conquer-online-server/ddl-audit.md` with the SHA1 format, MySQL 8 incompatibilities, and salt format
  - **Files**: `C:/Users/Windows/conquer-server/specs/conquer-online-server/ddl-audit.md`
  - **Done when**: SHA1 storage format confirmed; MySQL 8 incompatibilities listed; `ddl-audit.md` written with findings
  - **Verify**: `Select-String -Path C:/Users/Windows/conquer-server/specs/conquer-online-server/ddl-audit.md -Pattern 'SHA1|account|Password' | Measure-Object | Select-Object -ExpandProperty Count`
  - **Commit**: None (audit task)
  - _Requirements: FR-10, FR-17, AC-3.2_

- [x] V3 [VERIFY] Quality checkpoint: build still green after research tasks
  - **Do**: Confirm no accidental edits during research broke the build
  - **Verify**: `cd C:/Users/Windows/conquer-server/src; dotnet build --no-incremental 2>&1 | tail -3`
  - **Done when**: "Build succeeded" with zero errors
  - **Commit**: None (unless build regressed)
  - _Requirements: FR-6, FR-7, FR-10_

---

- [x] 1.10 Create `Crypto/RC5.cs` — RC5-32/12/16 password decryption
  - **Do**:
    1. Create directory `C:/Users/Windows/conquer-server/src/Crypto/` if absent
    2. Create `RC5.cs` with namespace `Conquer.Crypto`
    3. Implement `public sealed class RC5` with hardcoded 16-byte key (from Comet@5017 research in 1.8), `ExpandKey()` key schedule, and `Decrypt(byte[] ciphertext)` returning `byte[]` (ciphertext.Length must be multiple of 8)
    4. Adapt the algorithm from Comet@5017 `RC5.cs` — do not copy verbatim; re-type with understanding; keep the same key bytes and round count (12 rounds)
    5. Add a `// Adapted from Comet@5017 (non-commercial/academic license)` header comment
  - **Files**: `C:/Users/Windows/conquer-server/src/Crypto/RC5.cs`
  - **Done when**: File exists; `public byte[] Decrypt(byte[] ciphertext)` method present; `dotnet build` still passes
  - **Verify**: `cd C:/Users/Windows/conquer-server/src; dotnet build --no-incremental 2>&1 | Select-String 'Crypto\\RC5' | Measure-Object | Select-Object -ExpandProperty Count; dotnet build --no-incremental 2>&1 | tail -3`
  - **Commit**: `feat(crypto): add RC5-32/12/16 password decryption adapted from Comet`
  - _Requirements: FR-7, AC-3.2_

- [x] 1.11 Create `Crypto/TQCipher.cs` — XOR stream cipher
  - **Do**:
    1. Create `C:/Users/Windows/conquer-server/src/Crypto/TQCipher.cs` with namespace `Conquer.Crypto`
    2. Implement `public sealed class TQCipher` with fields `byte[] _K1 = new byte[512]`, `byte[] _K2 = new byte[512]`, `int _encryptPos`, `int _decryptPos`
    3. Constructor: call `InitK1()` to expand the static seed into `_K1`
    4. `InitK1()`: expand the 8-byte static seed `{0x9D,0x0F,0xFA,0x13,0x62,0x79,0x5C,0x6D}` into 512 bytes using the algorithm from Comet@5017 `TQCipher.cs`
    5. `GenerateKeys(object[] seeds)`: extract `ulong token = (ulong)seeds[0]`; derive `_K2` from token bytes using Comet's expansion
    6. `Decrypt(Span<byte> data)`: XOR each byte using the per-byte formula from design.md (pre-auth uses K1, post-auth uses K2 — track via a bool flag `_keysGenerated`)
    7. `Encrypt(Span<byte> data)`: XOR each byte with K1 (always K1 for encrypt per design)
    8. Add `// Adapted from Comet@5017 (non-commercial/academic license)` header
  - **Files**: `C:/Users/Windows/conquer-server/src/Crypto/TQCipher.cs`
  - **Done when**: File exists with all 6 members; `dotnet build` passes
  - **Verify**: `cd C:/Users/Windows/conquer-server/src; dotnet build --no-incremental 2>&1 | tail -3`
  - **Commit**: `feat(crypto): add TQCipher XOR stream cipher adapted from Comet`
  - _Requirements: FR-6, AC-3.4_

- [x] V4 [VERIFY] Quality checkpoint: Crypto/ compiles, build green
  - **Do**: Verify both crypto files compile without errors
  - **Verify**: `cd C:/Users/Windows/conquer-server/src; dotnet build --no-incremental 2>&1 | tail -5`
  - **Done when**: "Build succeeded", zero errors
  - **Commit**: `chore(crypto): fix compilation if needed`
  - _Requirements: FR-6, FR-7_

---

- [x] V3.1 [FIX 1.12] Fix: Scaffold multi-project structure — Crypto/Maps/Database/Network/Packets .csproj files
  - **Do**:
    1. Create `src/Crypto/Crypto.csproj` (SDK class library, net8.0, RootNamespace=Conquer.Crypto) — no extra PackageReferences needed
    2. Move (copy+delete) `src/Redux/Crypto/RC5.cs` → `src/Crypto/RC5.cs`; change namespace `Redux.Crypto` → `Conquer.Crypto`
    3. Move (copy+delete) `src/Redux/Crypto/TQCipher.cs` → `src/Crypto/TQCipher.cs`; change namespace `Redux.Crypto` → `Conquer.Crypto`
    4. Remove the now-empty `src/Redux/Crypto/` directory
    5. Verify `src/Maps/TinyMap.cs` has namespace `Conquer.Maps` (it should — no change needed)
    6. Create `src/Maps/Maps.csproj` (SDK class library, net8.0, RootNamespace=Conquer.Maps)
    7. Create `src/Database/Database.csproj` (SDK class library, net8.0, RootNamespace=Conquer.Database) with PackageReferences: Dapper 2.*, MySqlConnector 2.*, Microsoft.Extensions.Configuration 8.*
    8. Create `src/Network/Network.csproj` (SDK class library, net8.0, RootNamespace=Conquer.Network) with `<ProjectReference Include="../Crypto/Crypto.csproj" />`
    9. Create `src/Packets/Packets.csproj` (SDK class library, net8.0, RootNamespace=Conquer.Packets) with ProjectReferences to Crypto.csproj and Network.csproj
    10. Update `src/Redux/Redux.csproj`: add `<ProjectReference>` for Crypto, Maps, Database, Network, Packets
    11. Create `src/Conquer.sln` including Redux.csproj, Crypto.csproj, Maps.csproj, Database.csproj, Network.csproj, Packets.csproj using `dotnet new sln -o src/ -n Conquer` + `dotnet sln add` commands
    12. Verify `cd C:/Users/Windows/conquer-server/src/Redux; dotnet build --no-incremental` exits 0 (0 error CS)
  - **Files**: `src/Crypto/Crypto.csproj`, `src/Crypto/RC5.cs`, `src/Crypto/TQCipher.cs`, `src/Maps/Maps.csproj`, `src/Database/Database.csproj`, `src/Network/Network.csproj`, `src/Packets/Packets.csproj`, `src/Redux/Redux.csproj` (updated), `src/Conquer.sln`
  - **Done when**: `dotnet build C:/Users/Windows/conquer-server/src/Redux/Redux.csproj --no-incremental` exits 0; all .csproj files exist; `src/Crypto/RC5.cs` has namespace `Conquer.Crypto`
  - **Verify**: `cd C:/Users/Windows/conquer-server/src/Redux; dotnet build --no-incremental 2>&1 | Select-String 'error CS' | Measure-Object | Select-Object -ExpandProperty Count`
  - **Commit**: `build(project): scaffold multi-project structure with Crypto/Maps/Database/Network/Packets class libraries`

- [x] 1.12 [P] Create `Maps/TinyMap.cs` — managed DMAP binary parser
  - **Do**:
    1. Create directory `C:/Users/Windows/conquer-server/src/Maps/`
    2. Create `TinyMap.cs` with namespace `Conquer.Maps`
    3. Implement `public sealed class TinyMap` exactly per design.md TinyMap Class API:
       - Private fields: `int _width`, `int _height`, `ushort[] _tiles`
       - `public static TinyMap Load(string filePath)`: BinaryReader reads int32 Width, int32 Height, then `Width*Height` ushort values
       - Private constructor stores fields
       - `public bool IsPassable(int x, int y)`: bounds check then `_tiles[y * _width + x] == 0`
       - Properties `Width` and `Height`
  - **Files**: `C:/Users/Windows/conquer-server/src/Maps/TinyMap.cs`
  - **Done when**: File exists; `TinyMap.Load()` and `IsPassable()` present
  - **Verify**: `Test-Path C:/Users/Windows/conquer-server/src/Maps/TinyMap.cs && echo PASS`
  - **Commit**: `feat(maps): add managed TinyMap DMAP binary parser`
  - _Requirements: FR-4, NFR-6_

- [x] 1.13 [P] Create `Maps/MapRegistry.cs` — static map loading registry
  - **Do**:
    1. Create `MapRegistry.cs` with namespace `Conquer.Maps`
    2. Implement `public static class MapRegistry` exactly per design.md:
       - `private static readonly Dictionary<int, TinyMap> _maps = new()`
       - `public static void LoadAll(string mapsDir)`: enumerate `*.dmap`, parse filename as mapId, call `TinyMap.Load()`, store
       - `Console.WriteLine($"[Startup] Loaded {_maps.Count} maps")`
       - `public static TinyMap? Get(int mapId)` using `GetValueOrDefault`
  - **Files**: `C:/Users/Windows/conquer-server/src/Maps/MapRegistry.cs`
  - **Done when**: File exists; `LoadAll` and `Get` methods present
  - **Verify**: `Test-Path C:/Users/Windows/conquer-server/src/Maps/MapRegistry.cs && echo PASS`
  - **Commit**: `feat(maps): add MapRegistry static map loader`
  - _Requirements: FR-4_

- [x] V5 [VERIFY] Quality checkpoint: Maps/ compiles, build green
  - **Do**: Build after adding Maps/ files
  - **Verify**: `cd C:/Users/Windows/conquer-server/src; dotnet build --no-incremental 2>&1 | tail -3`
  - **Done when**: "Build succeeded", zero errors
  - **Commit**: `chore(maps): fix compilation if needed`
  - _Requirements: FR-4_

---

- [x] 1.14 Create `Database/ConnectionFactory.cs` — MySqlConnection factory
  - **Do**:
    1. Create directory `C:/Users/Windows/conquer-server/src/Database/`
    2. Create `ConnectionFactory.cs` with namespace `Conquer.Database`
    3. Implement exactly per design.md `ConnectionFactory` snippet:
       - Constructor takes `IConfiguration config`; reads `ConnectionStrings:Default`; throws `InvalidOperationException` if null
       - `public MySqlConnection Create()`: `new MySqlConnection(_connectionString); conn.Open(); return conn;`
    4. Add `using MySqlConnector;` and `using Microsoft.Extensions.Configuration;`
  - **Files**: `C:/Users/Windows/conquer-server/src/Database/ConnectionFactory.cs`
  - **Done when**: File compiles; `Create()` returns `MySqlConnection`
  - **Verify**: `cd C:/Users/Windows/conquer-server/src; dotnet build --no-incremental 2>&1 | tail -3`
  - **Commit**: `feat(db): add ConnectionFactory for MySqlConnection per-operation`
  - _Requirements: FR-1_

- [x] 1.15 [P] Create `Database/AccountRepository.cs` — account table queries
  - **Do**:
    1. Create `AccountRepository.cs` with namespace `Conquer.Database`
    2. Implement exactly per design.md:
       - `DbAccount` POCO: `AccountId` (int), `Username` (string), `Password` (string), `Salt` (string)
       - `AccountRepository(ConnectionFactory factory)`
       - `public DbAccount? FindByUsername(string username)`: Dapper `QueryFirstOrDefault<DbAccount>` with `SELECT AccountID, Username, Password, Salt FROM account WHERE Username = @username LIMIT 1`
    3. Note: column name `AccountID` in SQL must map to `AccountId` in C# — use `[Column("AccountID")]` attribute or Dapper's `SetTypeMap` if needed (check if Dapper auto-matches case-insensitively — it does, so no attribute needed for most setups)
  - **Files**: `C:/Users/Windows/conquer-server/src/Database/AccountRepository.cs`
  - **Done when**: File compiles; `FindByUsername` uses Dapper
  - **Verify**: `Test-Path C:/Users/Windows/conquer-server/src/Database/AccountRepository.cs && echo PASS`
  - **Commit**: `feat(db): add AccountRepository with Dapper FindByUsername`
  - _Requirements: FR-1, AC-3.2_

- [x] 1.16 [P] Create `Database/CharacterRepository.cs` — character table queries
  - **Do**:
    1. Create `CharacterRepository.cs` with namespace `Conquer.Database`
    2. Implement exactly per design.md:
       - `DbCharacter` POCO with all fields: `CharacterID`, `AccountID`, `Name`, `Mesh`, `Avatar`, `Level`, `MapID`, `X`, `Y`, `Silver`, `Strength`, `Agility`, `Vitality`, `Spirit`, `HealthPoints`, `ManaPoints`
       - Default values in POCO initializer: `Level=1`, `MapID=1010`, `X=61`, `Y=109`, `Silver=1000`, etc.
       - `FindByAccountId(int accountId)`: Dapper `QueryFirstOrDefault<DbCharacter>` with the full SELECT from design.md
       - `Insert(DbCharacter character)`: Dapper `Execute` with the INSERT from design.md
  - **Files**: `C:/Users/Windows/conquer-server/src/Database/CharacterRepository.cs`
  - **Done when**: File compiles; both methods present
  - **Verify**: `Test-Path C:/Users/Windows/conquer-server/src/Database/CharacterRepository.cs && echo PASS`
  - **Commit**: `feat(db): add CharacterRepository with FindByAccountId and Insert`
  - _Requirements: FR-1, AC-3.5_

- [x] V6 [VERIFY] Quality checkpoint: Database/ compiles, build green
  - **Do**: Build after adding Database/ files
  - **Verify**: `cd C:/Users/Windows/conquer-server/src; dotnet build --no-incremental 2>&1 | tail -3`
  - **Done when**: "Build succeeded", zero errors
  - **Commit**: `chore(db): fix compilation if needed`
  - _Requirements: FR-1_

---

- [x] 1.17 Create `Network/TokenStore.cs` — in-memory session token dictionary
  - **Do**:
    1. Create directory `C:/Users/Windows/conquer-server/src/Network/`
    2. Create `TokenStore.cs` with namespace `Conquer.Network`
    3. Implement exactly per design.md: `public static class TokenStore` with `ConcurrentDictionary<ulong, int> _tokens`; `Add(ulong token, int accountId)`; `TryConsume(ulong token, out int accountId)` using `TryRemove`
  - **Files**: `C:/Users/Windows/conquer-server/src/Network/TokenStore.cs`
  - **Done when**: File compiles; `TryConsume` uses `TryRemove` (one-time consumption)
  - **Verify**: `Test-Path C:/Users/Windows/conquer-server/src/Network/TokenStore.cs && echo PASS`
  - **Commit**: `feat(network): add in-memory TokenStore for session tokens`
  - _Requirements: FR-9, AC-3.3_

- [x] 1.18 Create `Network/ClientSession.cs` — per-connection state object
  - **Do**:
    1. Create `ClientSession.cs` with namespace `Conquer.Network`
    2. Implement `public sealed class ClientSession` per design.md:
       - Constructor: `TcpClient tcp` → assigns `TcpClient`, `Stream = tcp.GetStream()`, `Cipher = new TQCipher()`
       - Properties: `TcpClient`, `Stream`, `Cipher` (TQCipher), `SessionToken` (ulong), `AccountId` (int), `IsAuthenticated` (bool)
       - `Send(byte[] packet)`: encrypt with cipher if authenticated, then write length prefix + payload to stream
       - `Disconnect()`: `try { TcpClient.Close(); } catch { }`
    3. `using Conquer.Crypto;` for `TQCipher`
  - **Files**: `C:/Users/Windows/conquer-server/src/Network/ClientSession.cs`
  - **Done when**: File compiles; `Send` applies cipher; `Disconnect` is exception-safe
  - **Verify**: `cd C:/Users/Windows/conquer-server/src; dotnet build --no-incremental 2>&1 | tail -3`
  - **Commit**: `feat(network): add ClientSession with per-connection state and cipher`
  - _Requirements: FR-5, FR-6, AC-3.4_

- [x] V7 [VERIFY] Quality checkpoint: Network/ base types compile
  - **Do**: Build after TokenStore + ClientSession
  - **Verify**: `cd C:/Users/Windows/conquer-server/src; dotnet build --no-incremental 2>&1 | tail -3`
  - **Done when**: "Build succeeded", zero errors
  - **Commit**: `chore(network): fix if needed`
  - _Requirements: FR-9_

---

- [x] 1.19 Create `Packets/MsgConnectEx.cs` — MsgConnectEx (1055) response builder
  - **Do**:
    1. Create directory `C:/Users/Windows/conquer-server/src/Packets/` if not already present from Redux
    2. Create `MsgConnectEx.cs` with namespace `Conquer.Packets`
    3. Implement `public static class MsgConnectEx` with method `public static byte[] Build(ulong token, string gameServerIp, ushort gamePort)`:
       - Allocate buffer: type 1055 (ushort LE at offset 0), then token (8 bytes LE at offset 2), then IP as null-padded ASCII 16 bytes, then port as uint32 LE
       - Total length field (ushort LE) prepended as first 2 bytes of the on-wire frame (see design.md packet framing)
       - Return the complete framed packet bytes
    4. Use `BinaryPrimitives` for all multi-byte writes; `Encoding.ASCII` for IP string
  - **Files**: `C:/Users/Windows/conquer-server/src/Packets/MsgConnectEx.cs`
  - **Done when**: File compiles; `Build()` returns a `byte[]` with correct type ID `1055`
  - **Verify**: `cd C:/Users/Windows/conquer-server/src; dotnet build --no-incremental 2>&1 | tail -3`
  - **Commit**: `feat(packets): add MsgConnectEx 1055 response builder`
  - _Requirements: FR-9, AC-3.3_

- [x] 1.20 Create `Packets/MsgAccount.cs` — MsgAccount (1051) auth handler
  - **Do**:
    1. Create `MsgAccount.cs` with namespace `Conquer.Packets`
    2. Implement `public sealed class AuthHandler` with:
       - Constructor: `AccountRepository accounts, IConfiguration config`
       - `public void Handle(ClientSession session, byte[] payload)`:
         - Parse username: `Encoding.Latin1.GetString(payload, 4, 16).TrimEnd('\0')`
         - Parse encrypted password bytes: `payload[20..36]` (16 bytes)
         - `RC5.Decrypt(encPwd)` → rawPwd bytes → `Encoding.Latin1.GetString(rawPwd).TrimEnd('\0')`
         - `Console.WriteLine($"[Auth] username={username}")`
         - `accounts.FindByUsername(username)` — if null → send fail packet, disconnect, return
         - `ValidateSha1(password, account.Password, account.Salt)` — if false → log `[Auth] FAIL`, send fail, disconnect, return
         - Generate token: `(ulong)Random.Shared.NextInt64()` (simple for POC)
         - `TokenStore.Add(token, account.AccountId)`
         - `Console.WriteLine($"[Auth] OK username={username}")`
         - Build and send `MsgConnectEx.Build(token, gameServerIp, gamePort)`
         - `session.Disconnect()`
       - `private bool ValidateSha1(string password, string storedHash, string salt)`: compute `SHA1.HashData(Encoding.Latin1.GetBytes(password + salt))`; compare to stored hash using format determined from DDL audit (1.9) — default to hex `Convert.ToHexString(hash).ToLowerInvariant()`
       - `private void SendAuthFail(ClientSession session)`: send a minimal reject packet (type=1055, error code — check Comet reference for the reject format)
    3. Add `using System.Security.Cryptography;` and `using Conquer.Crypto;`
  - **Files**: `C:/Users/Windows/conquer-server/src/Packets/MsgAccount.cs`
  - **Done when**: File compiles; `Handle` method present with all auth steps
  - **Verify**: `cd C:/Users/Windows/conquer-server/src; dotnet build --no-incremental 2>&1 | tail -3`
  - **Commit**: `feat(auth): add MsgAccount 1051 handler with RC5 decrypt and SHA1 validate`
  - _Requirements: FR-7, FR-8, FR-9, AC-3.1, AC-3.2, AC-3.3, AC-3.6_

- [x] 1.21 Create `Packets/MsgConnect.cs` — MsgConnect (1052) game auth handler
  - **Do**:
    1. Create `MsgConnect.cs` with namespace `Conquer.Packets`
    2. Implement `public sealed class GameHandler` with:
       - Constructor: `CharacterRepository characters, IConfiguration config`
       - `public void Handle(ClientSession session, byte[] payload)`:
         - Extract token: `BinaryPrimitives.ReadUInt64LittleEndian(payload.AsSpan(4, 8))`
         - `TokenStore.TryConsume(token, out int accountId)` — if false → `session.Disconnect(); return`
         - `session.AccountId = accountId`
         - `session.Cipher.GenerateKeys(new object[] { token })` — activates K2
         - `session.IsAuthenticated = true`
         - `Console.WriteLine($"[Game] Connect accountId={accountId} token={token}")`
         - `characters.FindByAccountId(accountId)` — if found: `SendMsgUserInfo(session, character)` else send character creation prompt
       - `private void SendMsgUserInfo(ClientSession session, DbCharacter ch)`: build MsgUserInfo (type 1006) packet with character fields per CO 5065 protocol; for POC, write the minimum fields the client needs to reach character screen
    3. For `SendMsgUserInfo`, reference the CO wiki at `https://conquer-online.github.io/wiki/` for MsgUserInfo (1006) byte layout; also check if Redux has an existing `MsgUserInfo.cs` to reuse/adapt
  - **Files**: `C:/Users/Windows/conquer-server/src/Packets/MsgConnect.cs`
  - **Done when**: File compiles; `Handle` validates token, activates cipher, sends user info
  - **Verify**: `cd C:/Users/Windows/conquer-server/src; dotnet build --no-incremental 2>&1 | tail -3`
  - **Commit**: `feat(game): add MsgConnect 1052 handler with token validation and cipher activation`
  - _Requirements: FR-9, AC-3.4, AC-3.5_

- [x] 1.21b Locate Redux `MsgUserInfo.cs` and patch for .NET 8 compatibility
  - **Do**:
    1. Check if Redux already has a `Packets/MsgUserInfo.cs`: `Test-Path C:/Users/Windows/conquer-server/src/Packets/MsgUserInfo.cs`
    2. If found, read it: note the class/struct layout, field count, and any .NET 4.0-era APIs used
    3. Patch any .NET 8 API breaks in the file: `System.Windows.Forms` refs → comment out with `// TODO-M1`; `Thread.Abort()` → `return`; obsolete encoding usages → `Encoding.Latin1`; missing `using` directives → add the correct `using`
    4. If NOT found, create a minimal stub at `C:/Users/Windows/conquer-server/src/Packets/MsgUserInfo.cs` that compiles: `namespace Conquer.Packets; public static class MsgUserInfo { public static byte[] Build(object character) => throw new NotImplementedException("MsgUserInfo stub — full impl deferred to M2"); }`
    5. Run `dotnet build --no-incremental 2>&1 | tail -3` to confirm no new errors introduced
  - **Files**: `C:/Users/Windows/conquer-server/src/Packets/MsgUserInfo.cs`
  - **Done when**: `MsgUserInfo.cs` exists and compiles under .NET 8 with zero `error CS` lines; no Windows Forms or obsolete API references remain
  - **Verify**: `cd C:/Users/Windows/conquer-server/src; dotnet build --no-incremental 2>&1 | Select-String 'MsgUserInfo' | Select-String 'error' | Measure-Object | Select-Object -ExpandProperty Count`
  - **Commit**: `fix(compat): patch or stub MsgUserInfo.cs for .NET 8 compatibility`
  - _Requirements: FR-14, AC-3.5_

- [x] 1.21c Locate Redux `GamePacketHandler.cs` and patch for .NET 8 compatibility
  - **Do**:
    1. Search for the file: `Get-ChildItem -Recurse -Filter 'GamePacketHandler.cs' C:/Users/Windows/conquer-server/src | Select-Object FullName`
    2. If found, read it and enumerate .NET 4.0 API usages: `System.Windows.Forms`, `Thread.Abort`, `System.Web`, obsolete `Encoding.Default`, removed LINQ overloads
    3. For each error category found, apply the same patches as task 1.7: comment out Win-Forms refs, replace `Thread.Abort()` with `return` or `cts.Cancel()`, fix encoding refs
    4. If NOT found, no action required — log `GamePacketHandler.cs not present in Redux fork; skipping`
    5. Run `dotnet build --no-incremental 2>&1 | tail -3` to confirm build still green
  - **Files**: `C:/Users/Windows/conquer-server/src/GamePacketHandler.cs` or wherever located (path determined in step 1)
  - **Done when**: If file exists — zero `error CS` lines referencing `GamePacketHandler`; if file absent — build still green with no regressions
  - **Verify**: `cd C:/Users/Windows/conquer-server/src; dotnet build --no-incremental 2>&1 | Select-String 'GamePacketHandler' | Select-String 'error' | Measure-Object | Select-Object -ExpandProperty Count`
  - **Commit**: `fix(compat): patch GamePacketHandler.cs for .NET 8 compatibility`
  - _Requirements: FR-14_

- [x] V8 [VERIFY] Quality checkpoint: Packets/ compiles, build green
  - **Do**: Build after adding all packet handlers
  - **Files**: None (verification only)
  - **Verify**: `cd C:/Users/Windows/conquer-server/src; dotnet build --no-incremental 2>&1 | tail -3`
  - **Done when**: "Build succeeded", zero errors
  - **Commit**: `chore(packets): fix compilation if needed`
  - _Requirements: FR-9, FR-14_

---

- [x] 1.22 Create `Network/PacketRouter.cs` — packet dispatch by type ID
  - **Do**:
    1. Check if Redux already has a `PacketRouter.cs` or equivalent dispatch file; if so, MODIFY it; if not, CREATE it
    2. Implement `public sealed class PacketRouter` per design.md:
       - Constructor: `AccountRepository accounts, CharacterRepository characters, IConfiguration config`
       - Internal: instantiate `AuthHandler _auth` and `GameHandler _game` in constructor
       - `public (ushort typeId, byte[] payload) ReadPacket(NetworkStream stream)`: read 2-byte LE length, read `length-2` bytes payload, return `(typeId, payload)` — include sanity check: if `totalLen < 4 || totalLen > 8192` throw `IOException`
       - `public void Dispatch(ClientSession session, ushort typeId, byte[] payload)`: switch on typeId: `1051` → `_auth.Handle(session, payload)`; `1052` → `_game.Handle(session, payload)`; `default` → `Console.WriteLine($"[Warn] Unknown typeId={typeId}")`
       - Apply cipher: if `session.IsAuthenticated`, call `session.Cipher.Decrypt(payload)` BEFORE parsing typeId (decrypt first, then read typeId from decrypted bytes)
  - **Files**: `C:/Users/Windows/conquer-server/src/Network/PacketRouter.cs`
  - **Done when**: File compiles; `ReadPacket` + `Dispatch` present with cipher integration
  - **Verify**: `cd C:/Users/Windows/conquer-server/src; dotnet build --no-incremental 2>&1 | tail -3`
  - **Commit**: `feat(network): add PacketRouter with read-decrypt-dispatch pipeline`
  - _Requirements: FR-5, FR-9_

- [x] 1.23 Create `Network/NetworkListener.cs` — async accept loops for auth and game ports
  - **Do**:
    1. Create `NetworkListener.cs` with namespace `Conquer.Network`
    2. Implement `public sealed class NetworkListener` per design.md:
       - Constructor: `IConfiguration config, PacketRouter router`
       - `public async Task RunAuthAsync(CancellationToken ct)`: bind `TcpListener` on `config.GetValue<int>("AuthPort")`; log `[Startup] Auth listening on :{port}`; `while (!ct.IsCancellationRequested)` → `AcceptTcpClientAsync(ct)` → `_ = Task.Run(() => ServeClientAsync(client, ct), ct)`
       - `public async Task RunGameAsync(CancellationToken ct)`: same pattern on `config.GetValue<int>("GamePort")`; log `[Startup] Game listening on :{port}`
       - `private async Task ServeClientAsync(TcpClient tcp, CancellationToken ct)`: wrap in `using var session = new ClientSession(tcp)`; log `[Connect]`; loop: `ReadPacket` → `Dispatch`; catch `EndOfStreamException` (clean disconnect), `IOException`, and `Exception`; finally `session.Disconnect()` and log `[Disconnect]`
       - Include the packet length sanity check from design.md Error Handling section
  - **Files**: `C:/Users/Windows/conquer-server/src/Network/NetworkListener.cs`
  - **Done when**: File compiles; both `RunAuthAsync` and `RunGameAsync` methods present; per-client handler uses try/catch/finally
  - **Verify**: `cd C:/Users/Windows/conquer-server/src; dotnet build --no-incremental 2>&1 | tail -3`
  - **Commit**: `feat(network): add NetworkListener async accept loops for auth:9958 and game:5816`
  - _Requirements: FR-5, FR-16, NFR-7, AC-2.2_

- [ ] V9 [VERIFY] Quality checkpoint: Network/ compiles, full build green
  - **Do**: Full build check after NetworkListener
  - **Verify**: `cd C:/Users/Windows/conquer-server/src; dotnet build --no-incremental 2>&1 | tail -5`
  - **Done when**: "Build succeeded", zero errors
  - **Commit**: `chore(network): fix if needed`
  - _Requirements: FR-5, FR-16_

---

- [ ] 1.24 Create `appsettings.json` — externalized configuration
  - **Do**:
    1. Create `C:/Users/Windows/conquer-server/src/appsettings.json` with exact content from design.md Configuration Design section:
       ```json
       {
         "ConnectionStrings": {
           "Default": "Server=localhost;Port=3306;Database=conquer;User=conquer;Password=secret;CharSet=utf8mb4;"
         },
         "AuthPort": 9958,
         "GamePort": 5816,
         "ServerName": "Conquer",
         "GameServerIP": "127.0.0.1",
         "MapsDirectory": "./maps"
       }
       ```
    2. In `ConquerServer.csproj`, add `<Content Include="appsettings.json"><CopyToOutputDirectory>Always</CopyToOutputDirectory></Content>` inside an `<ItemGroup>`
  - **Files**: `C:/Users/Windows/conquer-server/src/appsettings.json`, `C:/Users/Windows/conquer-server/src/ConquerServer.csproj`
  - **Done when**: File exists; `.csproj` copies it to output; `dotnet build` still passes
  - **Verify**: `cd C:/Users/Windows/conquer-server/src; dotnet build --no-incremental 2>&1 | tail -3; Test-Path C:/Users/Windows/conquer-server/src/bin/Debug/net8.0/appsettings.json && echo CONFIG_COPIED`
  - **Commit**: `feat(config): add appsettings.json with ports, DB string, maps directory`
  - _Requirements: FR-15, AC-1.1_

- [ ] 1.25 Rewrite `Program.cs` — IConfiguration wire-up and listener startup
  - **Do**:
    1. Replace the entire body of `Program.cs` with the wire-up from design.md Configuration Design / Program.cs Wire-Up section:
       - Build `IConfiguration` from `appsettings.json` + env vars
       - Instantiate `ConnectionFactory`, `AccountRepository`, `CharacterRepository`, `PacketRouter`, `NetworkListener` (manual DI — no container)
       - Call `MapRegistry.LoadAll(config["MapsDirectory"] ?? "./maps")`
       - `Console.WriteLine("[Startup] Database connected")` after factory init
       - Set up `Console.CancelKeyPress` → `cts.Cancel()`
       - `await Task.WhenAll(listener.RunAuthAsync(cts.Token), listener.RunGameAsync(cts.Token))`
    2. Keep the namespace/using block minimal: `using Microsoft.Extensions.Configuration;` and project namespaces
  - **Files**: `C:/Users/Windows/conquer-server/src/Program.cs`
  - **Done when**: `Program.cs` uses `IConfiguration`; `Task.WhenAll` starts both listeners; `dotnet build` passes
  - **Verify**: `cd C:/Users/Windows/conquer-server/src; dotnet build --no-incremental 2>&1 | tail -3`
  - **Commit**: `feat(startup): rewrite Program.cs with IConfiguration and async listener startup`
  - _Requirements: FR-15, FR-16, AC-2.1, AC-2.2_

- [ ] V10 [VERIFY] Quality checkpoint: full solution builds cleanly
  - **Do**: Run `dotnet build --no-incremental` and confirm zero errors; also do `dotnet run --no-build` with `--help` to confirm the binary starts (Ctrl+C to stop)
  - **Verify**: `cd C:/Users/Windows/conquer-server/src; dotnet build --no-incremental 2>&1 | grep -E '^Build (succeeded|FAILED)'`
  - **Done when**: "Build succeeded" present
  - **Commit**: `chore(build): final POC compilation fix`
  - _Requirements: FR-15, FR-16_

---

- [ ] 1.26 Create MySQL 8 compatible `init.sql` from audited Redux DDL
  - **Do**:
    1. Using the DDL audit findings from task 1.9, create `C:/Users/Windows/conquer-server/src/init.sql`
    2. Start with `CREATE DATABASE IF NOT EXISTS conquer CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;` and `USE conquer;`
    3. Include at minimum: `account` table (with `AccountID`, `Username`, `Password`, `Salt`, `Status` columns) and `character` table (with all columns referenced in `CharacterRepository.cs`)
    4. Apply MySQL 8 fixes from audit: `utf8` → `utf8mb4`, remove `ZEROFILL` from non-integer cols, add explicit `DEFAULT` on `NOT NULL` VARCHAR cols, `ENGINE=InnoDB` for all tables, remove `ROW_FORMAT=COMPACT` if present, fix any invalid default expressions
    5. Add a test account row at bottom: `INSERT IGNORE INTO account (Username, Password, Salt, Status) VALUES ('testplayer', '<sha1-of-password123-plus-salt>', '<salt>', 1);` — use the format confirmed in audit
  - **Files**: `C:/Users/Windows/conquer-server/src/init.sql`
  - **Done when**: File exists; contains `CREATE TABLE account` and `CREATE TABLE character`; no MySQL 5.6-only syntax
  - **Verify**: `Select-String -Path C:/Users/Windows/conquer-server/src/init.sql -Pattern 'CREATE TABLE account' | Measure-Object | Select-Object -ExpandProperty Count`
  - **Commit**: `feat(db): add MySQL 8 compatible init.sql with account and character tables`
  - _Requirements: FR-10, FR-13, FR-17, AC-1.1, AC-4.3_

- [ ] 1.27 Create `Dockerfile` — multi-stage build
  - **Do**:
    1. Create `C:/Users/Windows/conquer-server/src/Dockerfile` with exact content from design.md Docker Topology section:
       ```dockerfile
       FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
       WORKDIR /src
       COPY *.csproj .
       RUN dotnet restore
       COPY . .
       RUN dotnet publish -c Release -o /app/publish --no-restore
       
       FROM mcr.microsoft.com/dotnet/runtime:8.0 AS runtime
       WORKDIR /app
       COPY --from=build /app/publish .
       ENTRYPOINT ["dotnet", "ConquerServer.dll"]
       ```
    2. Create `C:/Users/Windows/conquer-server/src/.dockerignore` excluding: `bin/`, `obj/`, `maps/`, `*.md`, `.git/`
  - **Files**: `C:/Users/Windows/conquer-server/src/Dockerfile`, `C:/Users/Windows/conquer-server/src/.dockerignore`
  - **Done when**: Both files exist; `Dockerfile` uses `runtime:8.0` final stage (not sdk or aspnet)
  - **Verify**: `Select-String -Path C:/Users/Windows/conquer-server/src/Dockerfile -Pattern 'runtime:8.0' | Measure-Object | Select-Object -ExpandProperty Count`
  - **Commit**: `feat(docker): add multi-stage Dockerfile sdk:8.0 → runtime:8.0`
  - _Requirements: FR-11, NFR-10, AC-4.2_

- [ ] V10b [VERIFY] Docker image builds from Dockerfile
  - **Do**: Run `docker build` against the newly created Dockerfile and confirm a successful image tag
  - **Files**: None (verification only)
  - **Verify**: `docker build -t conquer-server:v10b-check C:/Users/Windows/conquer-server/src/ 2>&1 | Select-String 'Successfully built|Successfully tagged'`
  - **Done when**: Docker build exits 0 with "Successfully built" or "Successfully tagged" in output
  - **Commit**: `fix(docker): fix Dockerfile build errors if any`
  - _Requirements: NFR-10, AC-4.2_

- [ ] 1.28 Create `docker-compose.yml` — db + server services
  - **Do**:
    1. Create `C:/Users/Windows/conquer-server/src/docker-compose.yml` with exact content from design.md docker-compose.yml section
    2. Key points to verify in the file:
       - `db` service uses `mysql:8.0` image
       - `command` includes `--default-authentication-plugin=mysql_native_password`
       - `db` has `healthcheck` with `mysqladmin ping`
       - `server` has `depends_on.db.condition: service_healthy`
       - `server` has env vars for `ConnectionStrings__Default`, `AuthPort`, `GamePort`, `GameServerIP`
       - Ports `9958:9958` and `5816:5816` exposed
       - `volumes` maps `./init.sql:/docker-entrypoint-initdb.d/init.sql:ro` on db service
       - `volumes` maps `./maps:/app/maps:ro` on server service
       - `restart: unless-stopped` on both services
       - Named volume `db_data` at bottom
  - **Files**: `C:/Users/Windows/conquer-server/src/docker-compose.yml`
  - **Done when**: File exists; both services defined; healthcheck present; mysql_native_password in command
  - **Verify**: `Select-String -Path C:/Users/Windows/conquer-server/src/docker-compose.yml -Pattern 'mysql_native_password' | Measure-Object | Select-Object -ExpandProperty Count`
  - **Commit**: `feat(docker): add docker-compose.yml with db and server services`
  - _Requirements: FR-11, FR-12, FR-13, FR-17, AC-1.1, AC-4.1, AC-4.4, AC-4.5_

- [ ] 1.29 POC Checkpoint — verify local `dotnet run` binds ports
  - **Do**:
    1. Start server in background: `Start-Job { cd C:/Users/Windows/conquer-server/src; dotnet run } | Out-Null; Start-Sleep -Seconds 8`
    2. Check port 9958 listening: `netstat -ano | Select-String ':9958'`
    3. Check port 5816 listening: `netstat -ano | Select-String ':5816'`
    4. Stop server: `Get-Job | Stop-Job; Get-Job | Remove-Job`
  - **Files**: None (verification only)
  - **Done when**: Both ports appear in LISTEN state within 8 seconds of `dotnet run`
  - **Verify**: `$job = Start-Job { cd C:/Users/Windows/conquer-server/src; dotnet run }; Start-Sleep 8; $out = netstat -ano | Select-String ':9958|:5816'; Stop-Job $job; Remove-Job $job; if ($out) { echo "PORTS_LISTEN_PASS" } else { echo "PORTS_FAIL" }`
  - **Commit**: `chore(poc): verify local port binding - POC complete`
  - _Requirements: FR-16, AC-2.2_

---

## Phase 2: Refactor

Focus: Clean up POC shortcuts. Improve error handling. Externalize remaining hardcoded values. Fix packet encoding correctness. No new features.

---

- [ ] 2.1 Fix `ClientSession.Send()` — correct packet framing with length prefix
  - **Do**:
    1. Review the `Send(byte[] packet)` implementation in `ClientSession.cs`
    2. Ensure the 2-byte LE length prefix is prepended correctly: `totalLen = packet.Length + 2`; write `BinaryPrimitives.WriteUInt16LittleEndian` then `stream.Write(packet)`
    3. Ensure cipher encrypt is applied AFTER framing (encrypt the payload only, not the length prefix) — or confirm the correct ordering per CO protocol (check Comet reference `ClientSession` or equivalent for ordering)
    4. Add null-check: if stream is not writable, log and return without throw
  - **Files**: `C:/Users/Windows/conquer-server/src/Network/ClientSession.cs`
  - **Done when**: `Send` writes correct framing; encrypt applied in correct order
  - **Verify**: `cd C:/Users/Windows/conquer-server/src; dotnet build --no-incremental 2>&1 | tail -3`
  - **Commit**: `fix(network): correct packet framing and cipher ordering in ClientSession.Send`
  - _Requirements: FR-9_

- [ ] 2.2 Improve error handling — add per-packet typeId to error logs
  - **Do**:
    1. In `NetworkListener.ServeClientAsync`, store the last-seen `typeId` in a local variable before `Dispatch`
    2. In the `catch (Exception ex)` block, include `typeId` in the log: `[Error] {endpoint} typeId={typeId} ex={ex.GetType().Name} msg={ex.Message}` — matches design.md error log format
    3. In `PacketRouter.ReadPacket`, add the length sanity check explicitly if not already present: throw `IOException($"Invalid packet length {totalLen}")` if `totalLen < 4 || totalLen > 8192`
  - **Files**: `C:/Users/Windows/conquer-server/src/Network/NetworkListener.cs`, `C:/Users/Windows/conquer-server/src/Network/PacketRouter.cs`
  - **Done when**: Error log includes typeId; sanity check present in ReadPacket
  - **Verify**: `cd C:/Users/Windows/conquer-server/src; dotnet build --no-incremental 2>&1 | tail -3`
  - **Commit**: `fix(network): add typeId to error logs and packet length sanity check`
  - _Requirements: NFR-7, NFR-9_

- [ ] V11 [VERIFY] Quality checkpoint: refactored network layer builds clean
  - **Do**: Build check after network refactors
  - **Verify**: `cd C:/Users/Windows/conquer-server/src; dotnet build --no-incremental 2>&1 | tail -3`
  - **Done when**: "Build succeeded", zero errors
  - **Commit**: None (unless fixes needed)
  - _Requirements: NFR-7, NFR-9_

---

- [ ] 2.3 Externalize token generation — use `RandomNumberGenerator` instead of `Random.Shared`
  - **Do**:
    1. In `MsgAccount.cs` `AuthHandler.Handle`, replace `(ulong)Random.Shared.NextInt64()` with `BinaryPrimitives.ReadUInt64LittleEndian(RandomNumberGenerator.GetBytes(8))` for cryptographically random tokens
    2. Add `using System.Security.Cryptography;` if not already present
  - **Files**: `C:/Users/Windows/conquer-server/src/Packets/MsgAccount.cs`
  - **Done when**: `RandomNumberGenerator.GetBytes(8)` used for token generation
  - **Verify**: `Select-String -Path C:/Users/Windows/conquer-server/src/Packets/MsgAccount.cs -Pattern 'RandomNumberGenerator'`
  - **Commit**: `fix(auth): use RandomNumberGenerator for cryptographically random session tokens`
  - _Requirements: NFR-8_

- [ ] 2.4 Validate SHA1 comparison against DDL audit — fix if format differs from hex
  - **Do**:
    1. Review the DDL audit notes from task 1.9 and the `ValidateSha1` implementation in `MsgAccount.cs`
    2. If the stored format is hex (VARCHAR(40)): confirm `Convert.ToHexString(hash).ToLowerInvariant()` matches stored value — adjust case if needed
    3. If the format is base64 (VARCHAR(64)): change to `Convert.ToBase64String(hash)`
    4. If the format is raw bytes (BINARY(20)): change comparison to byte-by-byte
    5. Update the salt concatenation order if the DDL audit or Comet reference reveals a different ordering (e.g., `salt + password` instead of `password + salt`)
  - **Files**: `C:/Users/Windows/conquer-server/src/Packets/MsgAccount.cs`
  - **Done when**: SHA1 comparison matches the exact format stored in the Redux `account` table; build passes
  - **Verify**: `cd C:/Users/Windows/conquer-server/src; dotnet build --no-incremental 2>&1 | tail -3`
  - **Commit**: `fix(auth): correct SHA1 comparison format to match Redux account table`
  - _Requirements: FR-8, AC-3.2, NFR-8_

- [ ] 2.5 Config cleanup — ensure `GameServerIP` and `ServerName` flow from config into MsgConnectEx
  - **Do**:
    1. In `AuthHandler.Handle` (in `MsgAccount.cs`), read `gameServerIp = config["GameServerIP"] ?? "127.0.0.1"` and `gamePort = config.GetValue<ushort>("GamePort")` from `IConfiguration`
    2. Pass these to `MsgConnectEx.Build(token, gameServerIp, gamePort)`
    3. Confirm no hardcoded IPs or ports remain in auth handler
  - **Files**: `C:/Users/Windows/conquer-server/src/Packets/MsgAccount.cs`
  - **Done when**: No hardcoded `"127.0.0.1"` or port numbers in auth handler; config used
  - **Verify**: `Select-String -Path C:/Users/Windows/conquer-server/src/Packets/MsgAccount.cs -Pattern '127\.0\.0\.1|9958|5816' | Measure-Object | Select-Object -ExpandProperty Count`
  - **Commit**: `fix(config): route GameServerIP and GamePort through IConfiguration in auth handler`
  - _Requirements: FR-15, AC-4.1_

- [ ] 2.6 Add `maps/` placeholder and `.gitkeep` — ensure Docker volume mount point exists
  - **Do**:
    1. Create `C:/Users/Windows/conquer-server/src/maps/.gitkeep` (empty file)
    2. Create `C:/Users/Windows/conquer-server/src/.gitignore` (or update if exists) to include `maps/*.dmap` so operator-supplied maps are not committed
    3. Verify `docker-compose.yml` volume `./maps:/app/maps:ro` exists
  - **Files**: `C:/Users/Windows/conquer-server/src/maps/.gitkeep`, `C:/Users/Windows/conquer-server/src/.gitignore`
  - **Done when**: `maps/` directory exists with `.gitkeep`; `.dmap` excluded from git
  - **Verify**: `Test-Path C:/Users/Windows/conquer-server/src/maps/.gitkeep && echo PASS`
  - **Commit**: `chore(maps): add maps/ placeholder and gitignore for dmap files`
  - _Requirements: FR-4_

- [ ] V12 [VERIFY] Quality checkpoint: all Phase 2 changes build clean
  - **Do**: Full build after all Phase 2 changes
  - **Verify**: `cd C:/Users/Windows/conquer-server/src; dotnet build --no-incremental 2>&1 | tail -5`
  - **Done when**: "Build succeeded", zero errors
  - **Commit**: `chore(refactor): fix phase 2 compilation issues`
  - _Requirements: FR-15, NFR-8_

---

## Phase 3: Testing / Manual Verification

Focus: No automated unit tests in M1 (out of scope). Phase 3 = manual verification tasks that can be run as shell commands + build cleanliness confirmation.

---

- [ ] 3.1 Verify `dotnet publish` produces AnyCPU output with no native DLLs
  - **Do**:
    1. `cd C:/Users/Windows/conquer-server/src && dotnet publish -c Release -o /tmp/conquer-publish`
    2. List the publish output: `Get-ChildItem /tmp/conquer-publish | Select-Object Name`
    3. Verify: `ManagedOpenSsl.dll` absent, `TinyMap.dll` absent
    4. Verify: `ConquerServer.dll` present, `appsettings.json` present
    5. Check the PE header of `ConquerServer.dll`: `$bytes = [System.IO.File]::ReadAllBytes('/tmp/conquer-publish/ConquerServer.dll'); [System.BitConverter]::ToString($bytes[0..1])` should be `4D-5A` (MZ header, confirms it's a PE binary)
  - **Files**: None (verification only)
  - **Done when**: No native DLLs in publish; `ConquerServer.dll` present
  - **Verify**: `!(Test-Path /tmp/conquer-publish/ManagedOpenSsl.dll) -and (Test-Path /tmp/conquer-publish/ConquerServer.dll) && echo PUBLISH_PASS`
  - **Commit**: None (verification task)
  - _Requirements: FR-3, FR-4, NFR-2, NFR-6, AC-2.3_

- [ ] 3.2 Verify `init.sql` syntax with MySQL Docker container (dry run)
  - **Do**:
    1. Start a one-shot MySQL 8 container: `docker run --rm -d --name mysql-test -e MYSQL_ROOT_PASSWORD=rootpass -e MYSQL_DATABASE=conquer mysql:8.0 --default-authentication-plugin=mysql_native_password`
    2. Wait for ready: `$ready = $false; for ($i=0; $i -lt 30; $i++) { Start-Sleep 2; $out = docker exec mysql-test mysqladmin ping -h localhost -uroot -prootpass 2>&1; if ($out -match 'alive') { $ready = $true; break } }`
    3. Copy and execute `init.sql`: `docker cp C:/Users/Windows/conquer-server/src/init.sql mysql-test:/init.sql; docker exec mysql-test mysql -uroot -prootpass conquer -e "source /init.sql" 2>&1`
    4. Check tables created: `docker exec mysql-test mysql -uroot -prootpass conquer -e "SHOW TABLES;" 2>&1`
    5. Stop container: `docker stop mysql-test`
  - **Files**: None (verification only)
  - **Done when**: `SHOW TABLES` returns `account` and `character` with no errors
  - **Verify**: `docker exec mysql-test mysql -uroot -prootpass conquer -e "SHOW TABLES;" 2>&1 | Select-String 'account|character' | Measure-Object | Select-Object -ExpandProperty Count`
  - **Commit**: `fix(db): fix init.sql syntax errors found during dry run` (if fixes needed)
  - _Requirements: FR-10, FR-13, AC-4.3_

- [ ] 3.3 Verify Docker image builds and is under 500 MB
  - **Do**:
    1. `docker build -t conquer-server:test C:/Users/Windows/conquer-server/src/`
    2. Check image size: `docker image inspect conquer-server:test --format '{{.Size}}' | % { [int]($_/1MB) }` — must be under 500
    3. Verify entrypoint: `docker image inspect conquer-server:test --format '{{.Config.Entrypoint}}'` — must show `[dotnet ConquerServer.dll]`
  - **Files**: None (verification only)
  - **Done when**: Image builds; size < 500 MB; entrypoint is dotnet
  - **Verify**: `$size = docker image inspect conquer-server:test --format '{{.Size}}'; [int]([long]$size / 1MB) -lt 500 && echo "SIZE_OK"`
  - **Commit**: None (verification task — fix Dockerfile if size exceeded)
  - _Requirements: NFR-10, AC-4.2_

- [ ] 3.4 Write `README.md` with Getting Started section
  - **Do**:
    1. Create `C:/Users/Windows/conquer-server/src/README.md` (or project root `C:/Users/Windows/conquer-server/README.md`)
    2. Include section `## Getting Started` with exactly:
       ```
       git clone https://github.com/conquer-online/redux
       docker compose up
       ```
    3. Also include sections: Project description, Requirements (Docker, .NET 8 SDK, CO 5065 client), Configuration (env var table), Port layout (9958 auth, 5816 game), Map files note (operator must supply)
  - **Files**: `C:/Users/Windows/conquer-server/README.md`
  - **Done when**: File exists; `## Getting Started` section present with the two commands
  - **Verify**: `Select-String -Path C:/Users/Windows/conquer-server/README.md -Pattern '## Getting Started' | Measure-Object | Select-Object -ExpandProperty Count`
  - **Commit**: `docs(readme): add README with Getting Started section`
  - _Requirements: FR-18, AC-1.3_

- [ ] V13 [VERIFY] Quality checkpoint: final build clean before quality gates
  - **Do**: Last build check before Phase 4
  - **Verify**: `cd C:/Users/Windows/conquer-server/src; dotnet build --no-incremental 2>&1 | tail -5`
  - **Done when**: "Build succeeded", zero errors
  - **Commit**: `chore(build): final pre-phase4 build gate`
  - _Requirements: NFR-4, NFR-6_

---

## Phase 4: Quality Gates

Focus: Full local build gate, Docker image check, AC checklist verification, E2E Docker Compose bring-up + port check (VE tasks).

---

- [ ] V14 [VERIFY] Full local CI: `dotnet build` clean, publish succeeds, no native DLLs
  - **Do**:
    1. `dotnet build --no-incremental 2>&1 | tail -5` — must show "Build succeeded"
    2. `dotnet publish -c Release -o /tmp/final-publish --no-restore 2>&1 | tail -3` — must show "published"
    3. `Test-Path /tmp/final-publish/ManagedOpenSsl.dll` — must return `False`
    4. `Test-Path /tmp/final-publish/ConquerServer.dll` — must return `True`
  - **Verify**: `cd C:/Users/Windows/conquer-server/src; dotnet build --no-incremental 2>&1 | Select-String 'Build succeeded'`
  - **Done when**: Build succeeded; publish succeeded; no native DLLs; main DLL present
  - **Commit**: `chore(quality): pass full local build gate`
  - _Requirements: NFR-4, NFR-6, AC-2.1, AC-2.3_

- [ ] V15 [VERIFY] AC checklist — verify each acceptance criterion programmatically
  - **Files**: None (verification only)
  - **Do**: Run the following checks and confirm each passes:
    1. **AC-1.3** (README Getting Started): `Select-String -Path C:/Users/Windows/conquer-server/README.md -Pattern '## Getting Started'`
    2. **AC-2.1** (dotnet build zero errors): `cd C:/Users/Windows/conquer-server/src; dotnet build --no-incremental 2>&1 | Select-String 'Build succeeded'`
    3. **AC-2.3** (no ManagedOpenSsl): `!(Test-Path /tmp/final-publish/ManagedOpenSsl.dll) && echo AC23_PASS`
    4. **AC-3.1** (Auth logs `[Auth]`): `Select-String -Path C:/Users/Windows/conquer-server/src/Packets/MsgAccount.cs -Pattern '\[Auth\]'`
    5. **AC-3.2** (RC5 decrypt + SHA1): `Select-String -Path C:/Users/Windows/conquer-server/src/Packets/MsgAccount.cs -Pattern 'RC5|SHA1'`
    6. **AC-3.3** (MsgConnectEx token): `Select-String -Path C:/Users/Windows/conquer-server/src/Packets/MsgConnectEx.cs -Pattern '1055'`
    7. **AC-3.4** (TQCipher GenerateKeys): `Select-String -Path C:/Users/Windows/conquer-server/src/Packets/MsgConnect.cs -Pattern 'GenerateKeys'`
    8. **AC-3.6** (invalid credentials handled): `Select-String -Path C:/Users/Windows/conquer-server/src/Packets/MsgAccount.cs -Pattern 'FAIL|SendAuthFail'`
    9. **FR-17** (mysql_native_password in compose): `Select-String -Path C:/Users/Windows/conquer-server/src/docker-compose.yml -Pattern 'mysql_native_password'`
    10. **FR-18** (README): `Select-String -Path C:/Users/Windows/conquer-server/README.md -Pattern 'docker compose up'`
  - **Verify**: All 10 checks return non-empty output (grep exits 0)
  - **Done when**: All acceptance criteria confirmed present via code/config grep
  - **Commit**: None
  - _Requirements: FR-1 through FR-18, all ACs_

---

- [ ] VE1 [VERIFY] E2E startup: `docker compose up -d` and wait for healthy
  - **Do**:
    1. From `C:/Users/Windows/conquer-server/src/`: `docker compose up -d`
    2. Wait for both containers to be running/healthy (up to 90 seconds): `for ($i=0; $i -lt 18; $i++) { Start-Sleep 5; $ps = docker compose ps 2>&1; if (($ps -match 'server') -and ($ps -match 'running|Up')) { break } }`
    3. Record container IDs: `docker compose ps -q | Out-File /tmp/ve-containers.txt`
    4. Check db is healthy: `docker compose ps db 2>&1 | Select-String 'healthy'`
    5. Check server is running: `docker compose ps server 2>&1 | Select-String 'running|Up'`
  - **Files**: None (verification only)
  - **Verify**: `cd C:/Users/Windows/conquer-server/src; docker compose ps 2>&1 | Select-String 'server' | Select-String 'running|Up'`
  - **Done when**: Both `server` and `db` containers running; db shows healthy
  - **Commit**: None
  - _Requirements: FR-11, FR-12, FR-13, AC-4.1_

- [ ] VE2 [VERIFY] E2E check: server listening on ports 9958 and 5816, DB connected in logs
  - **Do**:
    1. Check port 9958: `docker compose exec server sh -c "netstat -tlnp 2>/dev/null || ss -tlnp" 2>&1 | Select-String '9958'`
    2. Check port 5816: `docker compose exec server sh -c "netstat -tlnp 2>/dev/null || ss -tlnp" 2>&1 | Select-String '5816'`
    3. Check server logs for `[Startup] Database connected`: `docker compose logs server 2>&1 | Select-String 'Database connected'`
    4. Check server logs for `[Startup] Auth listening`: `docker compose logs server 2>&1 | Select-String 'Auth listening'`
    5. Check server logs for `[Startup] Game listening`: `docker compose logs server 2>&1 | Select-String 'Game listening'`
    6. Check `mysql_native_password` in db: `docker compose exec db mysql -u root -prootpass -e "SHOW VARIABLES LIKE 'default_authentication_plugin';" 2>&1 | Select-String 'native'`
  - **Files**: None (verification only)
  - **Verify**: `cd C:/Users/Windows/conquer-server/src; docker compose logs server 2>&1 | Select-String 'Database connected|Auth listening|Game listening'`
  - **Done when**: All 6 checks return non-empty matches; both ports visible in netstat; DB connected log present
  - **Commit**: None
  - _Requirements: FR-16, FR-17, AC-1.2, AC-2.2, NFR-9_

- [ ] VE3 [VERIFY] E2E cleanup: `docker compose down -v`, verify port free
  - **Do**:
    1. **IMPORTANT: Run this task regardless of VE2 outcome — cleanup is mandatory to free ports and remove containers.**
    2. `cd C:/Users/Windows/conquer-server/src; docker compose down -v`
    3. Verify containers stopped: `docker compose ps 2>&1 | Select-String 'server|db' | Measure-Object | Select-Object -ExpandProperty Count` — must be 0
    4. Verify port 9958 free: `netstat -ano | Select-String ':9958' | Measure-Object | Select-Object -ExpandProperty Count` — must be 0
    5. Verify port 5816 free: `netstat -ano | Select-String ':5816' | Measure-Object | Select-Object -ExpandProperty Count` — must be 0
    6. Remove temp files: `Remove-Item -Force /tmp/ve-containers.txt -ErrorAction SilentlyContinue`
  - **Files**: None (verification only)
  - **Verify**: `cd C:/Users/Windows/conquer-server/src; docker compose down -v 2>&1 | tail -3`
  - **Done when**: Both containers stopped and removed; volumes removed; ports free
  - **Commit**: None
  - _Requirements: AC-4.1, NFR-5_

---

- [ ] 4.1 Final review — commit spec artifacts and tag M1
  - **Do**:
    1. Stage all spec files: `git -C C:/Users/Windows/conquer-server add specs/`
    2. Stage all `src/` changes (confirm no secrets or .dmap files): `git -C C:/Users/Windows/conquer-server add src/`
    3. Verify `.gitignore` excludes `maps/*.dmap`, `bin/`, `obj/`, `.env`
    4. Final `dotnet build --no-incremental` check from `src/`
    5. Tag the working state: `git -C C:/Users/Windows/conquer-server tag m1-poc`
  - **Files**: All files in `src/` and `specs/conquer-online-server/`
  - **Done when**: All implementation files committed; `m1-poc` tag exists; build still green
  - **Verify**: `git -C C:/Users/Windows/conquer-server tag | Select-String 'm1-poc'`
  - **Commit**: `chore(m1): tag M1 POC complete — .NET 8 build clean, Docker Compose ready`
  - _Requirements: All FRs_

---

## Notes

**POC shortcuts taken (Phase 1):**
- Token generation uses `RandomNumberGenerator.GetBytes(8)` (secure enough but not the full CO token derivation)
- `ValidateSha1` SHA1 format assumed hex-lowercase — must be confirmed from DDL audit in 1.9 and corrected in 2.4 if wrong
- `MsgUserInfo` (1006) builder in `GameHandler` is minimal — sends only fields needed for character screen; full field set deferred to M2
- No retry logic on DB connection failures (first-start race handled by `depends_on: service_healthy` in Docker Compose)
- `MapRegistry.LoadAll` silently skips maps directory if empty — no maps required for auth flow to succeed
- Character encoding defaults to `Encoding.Latin1` — must be verified against actual CO 5065 client behavior

**Production TODOs (post-M1):**
- Replace `Console.WriteLine` with structured logging (`Microsoft.Extensions.Logging` or Serilog)
- Add connection pooling configuration to `ConnectionFactory`
- Port Redux game packet handlers (items, combat, guilds, NPCs) through the new `PacketRouter`
- Add NPC/monster AI stubs (known TODO in Redux)
- Full `MsgUserInfo` packet with all 50+ character fields
- Game loop / tick system for movement and combat updates
- CI/CD pipeline (GitHub Actions)
