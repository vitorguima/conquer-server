---
spec: client-patcher
phase: research
created: 2026-06-26
---

# Research: client-patcher

## Executive Summary

For a CO **patch 5065** client, the only address the client needs repointed is the **auth/login server** (host + port). The game-server IP/port is delivered at runtime by the auth server in its login reply (`reply.Info = GAME_IP`, `reply.ServerPort = GAME_PORT` in `LoginServer.cs`), so the patcher does **not** need to touch the game IP. The community-standard mechanism for 5065 is **runtime DLL injection via ConquerLoader (`CLHook.dll`)**, not file editing — because the 5065 `server.dat` is binary/obfuscated and the exe rejects LAN/loopback IPs without an additional hex patch. Recommended approach: ship a small .NET launcher that injects an IP/host hook at process start (ConquerLoader-style), with a binary-patch fallback for the exe's hardcoded auth IP. Final confirmation of exact offsets/format **requires inspecting the operator's actual 5065 `Conquer.exe` + `server.dat`** — these vary per client build.

## External Research

### How the 5065 client reads its server address (three mechanisms)

| Mechanism | Applies to 5065? | Notes |
|---|---|---|
| `server.dat` (binary server list) | Yes, present in client | Stores **login** host/port + server name. Obfuscated/encoded, NOT plain ini for 5065. The client rejects internal/LAN IPs unless the exe is hex-patched ("Server.dat is damaged" error otherwise). |
| Hardcoded IP string in `Conquer.exe` | Yes | TQ's retail auth host is baked into the binary; some private servers hex-patch this string directly. Subject to length/null-padding constraints. |
| Command-line arg `blacknull` | Yes | `Conquer.exe blacknull` opens an in-client dialog to type an IP, bypassing some server.dat handling. Common manual trick. |
| Runtime DLL injection (ConquerLoader) | Yes — **canonical for 5065** | Loader launches `Conquer.exe` suspended and injects `CLHook.dll` (for client version < 5717) which rewrites the server IP/name in memory. No file edits needed. |
| Windows registry | Not the primary path for 5065 | Not the repoint vector used by the community for this patch. |

Key fact: the auth server (port 9958) is the **only** endpoint the client must be pointed at. After auth succeeds, the server returns the game-server IP+port to the client. Confirmed in this repo at `src/Redux/Network/LoginServer.cs:91-92,108-109`.

### Prior Art — ConquerLoader (darkfoxdeveloper / OpenConquer)

- Repo: `github.com/darkfoxdeveloper/ConquerLoader` (formerly OpenConquerOrg). **C# (~89%)** + C++/C hook DLLs. Solution: `ConquerLoader.sln` (WPF launcher + `CLCore` shared lib + plugins). License: **not declared** (GitHub reports `license: null`) — treat as all-rights-reserved; do not vendor the code, reimplement the technique.
- Supported clients: **5065 → 6736** (English clients). Min OS Win7 SP1.
- Mechanism by client version (from its loader logic):

  | Version range | `UseDecryptedServerDat` | DLLs injected | How IP is set |
  |---|---|---|---|
  | **< 5717 (= 5065)** | No | **`CLHook.dll`** | In-memory IP/server-name patch ("legacy, limited") |
  | 5717–6186 | Yes | COFlashFixer.dll, ConquerCipherHook.dll, COHook.dll | Generated `server.dat` + cipher/flash hooks |
  | 6187–6736 | Yes | ConquerCipherHook.dll, COHook.dll | Generated `server.dat` |
  | > 6736 | No | CLHook.dll | In-memory patch |

  - `ConquerCipherHook.dll`: keeps password encryption correct during login.
  - `COHook.dll`: reads/applies a generated `server.dat`.
  - `CLHook.dll`: legacy in-memory IP/hostname redirect — **this is the 5065 path**.
- Config is per-server in the loader UI (LoginHost, LoginPort, ServerName, ServerVersion), persisted to a loader config; older docs reference a flat `LoaderSet.ini`:
  ```ini
  [Loader]
  IPAddress=your.host.or.ip
  LoginPort=9958
  GamePort=5816
  Website=NULL
  Force=TRUE
  ```
- Other tools in the ecosystem: `SmartConquerLoader`, server-list editors, and per-server "Auto Patcher" plugins (`CLAutoPatchPlugin`) — those handle game-asset patching, not IP repointing.

### `server.dat` format

- For **5717+** clients the loader builds `server.dat` from an XML/SQL-dump template (`ServersDatGenerator.cs`) with per-server fields:
  `id, ServerName, **ServerIP**, **ServerPort**, FlashName, FlashIcon, FlashHint, Child, PicServerIP, PicServerPort, BindServerIP, BindServerPort, Charges`, plus a group header row (`GroupAmount`/`ServerAmount` style).
  Critically: `ServerIP = LoginHost`, `ServerPort = LoginPort` — i.e. server.dat's "ServerIP" is the **auth/login** target, matching the runtime-handoff design.
- For **5065 specifically**: the on-disk `server.dat` is a packed/obfuscated binary the retail client decodes. The community does **not** reliably edit it by hand for 5065 (hence ConquerLoader uses in-memory `CLHook.dll` instead). The exe also blocks loopback/LAN IPs in server.dat until a hex patch is applied. **Exact 5065 byte layout must be confirmed against the operator's real file.**

### Binary patching of `Conquer.exe`

- Approach: locate the retail auth-host ASCII string (e.g. an old TQ hostname/IP) and overwrite in place.
- Constraints:
  - **Length**: replacement must be ≤ original string length; pad remainder with `0x00`. A loopback `127.0.0.1` (9 chars) or a short hostname usually fits the original retail string slot.
  - **Null-terminate**: ensure a trailing `\0`; do not run into adjacent strings.
  - **Checksum / integrity**: 5065-era retail clients are not known to ship strong self-integrity checks, but the original `TQ AntiCheat`/`tqantivirus` folder must be deleted/disabled (community guides require this). Any anti-tamper would break a static binary patch.
  - **Port**: the port is often stored separately (in server.dat or as an int near the IP) rather than in the same string — locating it requires inspecting the binary.

### Pitfalls to avoid (community-sourced)

- Internal/LAN IPs trigger "Server.dat is damaged" → use loopback (`127.0.0.1`) or a public IP/hostname, or apply the LAN hex patch.
- Antivirus flags injector DLLs as malware (false positive) — operators must add an exclusion.
- Must delete the client's `tqantivirus`/anti-cheat folder.
- `LOGIN_PORT` here defaults to **9959** in code (`Constants.cs:87`) but is overridden to **9958** via ini (`SettingsReader.cs`) and README. Patcher port default should be **9958**, and this discrepancy should be reconciled server-side.

## Codebase Analysis

### Existing patterns / relevant server facts

- `src/Redux/Network/LoginServer.cs:91-92,108-109` — auth reply sets `ServerPort = GAME_PORT`, `Info = GAME_IP`. **Proves the client gets the game IP at runtime; patcher only sets the auth endpoint.**
- `src/Redux/Database/Readers/SettingsReader.cs:19-21` — `GAME_IP`, `GAME_PORT` (5816), `LOGIN_PORT` (9958) read from ini at startup.
- `src/Redux/Constants.cs:87-93` — defaults: `LOGIN_PORT=9959`, `GAME_PORT=5816`, `GAME_IP=0.0.0.0`.
- Server stack: **.NET 8 / C# / WPF-incompatible (Linux Docker target)**. Server `Redux.csproj` is `net8.0`, `OutputType=Exe`, runs in Docker on Linux.

### Constraints

- The patcher targets a **Windows** client; the server runs in Linux Docker. A C#/.NET patcher keeps language parity with the repo but, if it does live DLL injection, it is Windows-only (`net8.0-windows` or a native injector). A pure **file/binary patcher** (read exe/server.dat, write bytes) can stay cross-platform `net8.0` and run anywhere, including in CI.
- The operator must supply the actual 5065 client; we cannot bundle it (copyright).

## Quality Commands

| Type | Command | Source |
|---|---|---|
| Build | `dotnet build` | `src/Redux.sln`, `src/Conquer.sln` |
| Build (release) | `dotnet build -c Release` | csproj |
| Publish | `dotnet publish -c Release -o ./publish` | README.md |
| Lint | Not found (no analyzer config) | — |
| TypeCheck | N/A (C#, compiler enforces) | — |
| Unit Test | Not found (no test project) | — |
| Integration/E2E | Not found | — |

**Local CI**: `dotnet build -c Release` (no test/lint targets exist in repo).
A new patcher project should add its own xUnit test project (round-trip patch verification on a fixture binary).

## Verification Tooling

No automated E2E tooling detected (no package.json, Makefile, CI workflows, Playwright/Cypress, Dockerfile health endpoints for the patcher).

**Project Type**: Standalone Windows utility (CLI/GUI) producing a patched client.
**Verification Strategy**: Build the patcher, run it against a **synthetic fixture** (a stand-in binary containing a known placeholder IP string + a stub `server.dat`), assert bytes were rewritten correctly and length/null-padding rules held. True end-to-end (launch real `Conquer.exe`, connect to auth on 9958) is **manual, operator-side**, gated on the operator supplying a real 5065 client. Mark live-client VE tasks as operator-verified / skipped in CI.

## Feasibility Assessment

| Aspect | Assessment | Notes |
|---|---|---|
| Technical Viability | **Medium-High** | File/binary patch is straightforward; in-memory injection is proven (ConquerLoader) but Windows-only and more complex. |
| Effort Estimate | **M** (binary/file patcher) / **L** (injection launcher) | Injection needs native interop + per-version hook logic. |
| Risk Level | **Medium** | Main risks: unknown exact 5065 `server.dat`/exe layout (must inspect real client); AV false positives if injecting; LAN-IP rejection without hex patch. |

Unknowns resolvable ONLY by inspecting the operator's real client:
- Exact offset/format of the auth-host string in `Conquer.exe` (and whether port is co-located).
- Exact 5065 `server.dat` binary layout and whether it's encoded.
- Whether this client build enforces any integrity/anti-tamper check.

## Recommendations for Requirements

1. **Scope the patcher to set the AUTH (login) endpoint only** (host + port, default `127.0.0.1:9958`). Do NOT attempt to set the game IP — the server delivers it at runtime. (Source: `LoginServer.cs`.)
2. **Primary mechanism = static patch of the supplied client** (operator points the tool at their client folder): rewrite the auth host/IP string in `Conquer.exe` and/or the `server.dat` server-list entry. This keeps the patcher a pure, cross-platform `net8.0` byte-rewriter with no injection/AV concerns. Trade-off: depends on locating the exact string in the operator's specific binary.
3. **Secondary/fallback mechanism = a ConquerLoader-style launcher** that injects an in-memory IP hook at process start (the proven 5065 path via `CLHook.dll`-equivalent). Trade-off: Windows-only, native interop, AV false-positive risk. Reimplement the technique; do **not** vendor ConquerLoader code (no license).
4. **Make the search string operator-configurable**: the tool should accept the "old IP/hostname to find" (or auto-scan candidate ASCII IP/host patterns) and the "new IP:port", because the retail string differs per client build.
5. **Enforce binary-patch safety rules**: new string length ≤ old, null-pad remainder, preserve null terminator, never extend file length, write to a **copy** (never the original), and emit a backup + a diff report.
6. **Default to loopback `127.0.0.1`** and warn that LAN/internal IPs may require the additional exe hex patch ("Server.dat is damaged" guard); document deleting the client's `tqantivirus`/anti-cheat folder.
7. **Add a test project** with a synthetic fixture binary to verify round-trip patching in CI (the repo has no tests today).
8. **Document legal boundary**: the patcher ships WITHOUT any TQ client; operator supplies their own 5065 client. No CO client assets in the repo.

## Open Questions

- Does the operator's specific 5065 client read the auth IP from `server.dat`, from a hardcoded string in `Conquer.exe`, or both? (Determines patch target — must inspect the real files.)
- Is the 5065 `server.dat` plaintext, packed, or encoded for this client build?
- Is the port co-located with the IP string or stored separately?
- Does this client build have any integrity check that would reject a static patch (forcing the injection approach)?
- Preferred UX: CLI (`patcher.exe --client <dir> --ip 127.0.0.1 --port 9958`) vs GUI? CLI is simpler to test and automate.

## Related Specs

| Spec | Relation | mayNeedUpdate |
|---|---|---|
| `conquer-online-server` | **High** — same project; defines auth port 9958 / game port 5816 the patcher must target. Operator-facing README currently says "manually point client to 127.0.0.1:9958"; this patcher automates that step. | Yes — README/operator docs should reference the patcher once built; reconcile `LOGIN_PORT` default 9959 vs 9958. |

## Sources

- `https://github.com/darkfoxdeveloper/ConquerLoader` (mechanism, version→DLL table, 5065 support, C# stack, no license)
- `https://raw.githubusercontent.com/darkfoxdeveloper/ConquerLoader/master/ConquerLoader/ConquerLoader/Models/ServersDatGenerator.cs` (server.dat fields: ServerName/ServerIP/ServerPort=Login host+port)
- `https://github.com/luckymouse0/Redux-Conquer-Online-Server` (Redux 5065 server; client config via ConquerLoader + LoaderSet.ini)
- `https://www.elitepvpers.com/forum/co2-pserver-discussions-questions/4140440-conquer-online-server-dat.html` (server.dat sections, `blacknull` arg, LAN-IP hex patch, "Server.dat is damaged")
- `https://cooldown.dev/topic/236-serverdat-on-client-5065/` (server.dat on 5065)
- `https://www.elitepvpers.com/forum/co2-pserver-guides-releases/1322828-guide-how-make-5065-conquer-online-private-server.html` (5065 setup guide)
- `src/Redux/Network/LoginServer.cs` (runtime game-IP handoff)
- `src/Redux/Database/Readers/SettingsReader.cs`, `src/Redux/Constants.cs` (ports/IP config)
- `README.md` (operator instructions, ports)
