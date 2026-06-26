# Requirements: client-patcher

## Goal

CLI tool that repoints a CO 5065 client's **auth/login endpoint** (host + port) by statically rewriting the auth string in a **copy** of the operator's `Conquer.exe` / `server.dat`. The game-server IP is delivered at runtime by the auth server, so the patcher never sets a game IP.

## User Stories

### US-1: Repoint auth endpoint
**As an** operator running a private 5065 server
**I want to** point my client at my auth server with one command
**So that** players reach my login server instead of TQ's retail host.

**Acceptance Criteria:**
- [ ] AC-1.1: Given a client dir + valid `--ip`/`--port`/`--find`, When patcher runs, Then the auth host string in the patched output equals the new IP and the port reflects the new port.
- [ ] AC-1.2: Given no `--ip`/`--port`, When patcher runs, Then it defaults to `127.0.0.1:9958`.
- [ ] AC-1.3: Given the run completes, Then **no game IP** is written or modified anywhere.

### US-2: Safe, non-destructive patching
**As an** operator
**I want to** keep my original client intact
**So that** a bad patch never corrupts my only client copy.

**Acceptance Criteria:**
- [ ] AC-2.1: Given a source client file, When patched, Then the original bytes are unchanged on disk.
- [ ] AC-2.2: Given a patch run, Then a backup of each modified file is written before any output is produced.
- [ ] AC-2.3: Given the patch writes bytes, Then file length of the output equals the original (no extension/truncation).
- [ ] AC-2.4: Given a completed run, Then a summary report lists each file, offset(s), old bytes, and new bytes changed.

### US-3: Operator-supplied search target
**As an** operator with a build-specific retail string
**I want to** supply the old IP/hostname to find
**So that** the tool works against my exact client without guessing.

**Acceptance Criteria:**
- [ ] AC-3.1: Given `--find <string>`, When the string exists in `Conquer.exe` and/or `server.dat`, Then each occurrence is replaced.
- [ ] AC-3.2: Given `--find` not present in either target, When patcher runs, Then it exits non-zero with a clear "search string not found" error and writes nothing.
- [ ] AC-3.3: Given `len(new) > len(old)`, When validated, Then patcher refuses with a length error.
- [ ] AC-3.4: Given `len(new) < len(old)`, When patched, Then remaining bytes of the original slot are null-padded (`0x00`) and the terminator is preserved.

### US-4: Input validation
**As an** operator
**I want** bad inputs caught before any file is touched
**So that** I get actionable errors, not a corrupt client.

**Acceptance Criteria:**
- [ ] AC-4.1: Given an invalid IPv4 or hostname in `--ip`, When parsed, Then exit non-zero with validation error.
- [ ] AC-4.2: Given `--port` outside 1–65535, When parsed, Then exit non-zero with validation error.
- [ ] AC-4.3: Given `--client` dir missing or containing neither `Conquer.exe` nor `server.dat`, When checked, Then exit non-zero with a clear error.

### US-5: LAN/loopback guidance
**As an** operator
**I want to** be warned about LAN-IP pitfalls
**So that** I avoid the "Server.dat is damaged" failure.

**Acceptance Criteria:**
- [ ] AC-5.1: Given `--ip` is a private/LAN address (e.g. `10.*`, `192.168.*`, `172.16–31.*`), When patcher runs, Then it prints a warning that LAN IPs may trigger "Server.dat is damaged" and may need an additional exe hex patch.
- [ ] AC-5.2: The patcher does **not** auto-apply the LAN hex patch in v1 (documented as manual follow-up).

### US-6: Cross-platform + tested
**As a** maintainer
**I want** the tool to build and test in CI on any OS
**So that** patch correctness is regression-guarded without a real client.

**Acceptance Criteria:**
- [ ] AC-6.1: Given `net8.0`, When built/run on Linux, macOS, or Windows, Then it produces identical patch output.
- [ ] AC-6.2: Given the xUnit suite + synthetic fixtures, When run in CI, Then round-trip, length/null-pad, and missing-string cases pass without any real TQ client.

## Functional Requirements

| ID | Requirement | Priority | Acceptance Criteria |
|----|-------------|----------|---------------------|
| FR-1 | Patch auth host/IP string in a **copy** of `Conquer.exe` and/or `server.dat` | High | Patched output has new host; original untouched |
| FR-2 | Set auth endpoint **only**; never write a game IP | High | No game-IP bytes altered |
| FR-3 | CLI surface: `patcher --client <dir> --find <old> [--ip <ip>] [--port <port>]` | High | Args parsed; `--help` lists all |
| FR-4 | Defaults `--ip 127.0.0.1` and `--port 9958` | High | Omitted flags resolve to defaults |
| FR-5 | Validate IPv4/hostname, port 1–65535, client dir + at least one target file | High | Invalid input → non-zero exit, no writes |
| FR-6 | Replacement rule: `len(new) ≤ len(old)`, null-pad remainder, preserve terminator, never extend file | High | AC-3.3/3.4 hold |
| FR-7 | Resolve targets across `Conquer.exe` and `server.dat`; fail clearly if `--find` in neither | High | AC-3.2 |
| FR-8 | Write a backup of each modified file before output | High | Backup exists with original bytes |
| FR-9 | Emit a diff/summary report: file, offset(s), old→new bytes, count | High | AC-2.4 |
| FR-10 | Warn on LAN/private `--ip`; do not auto-hex-patch | Medium | AC-5.1/5.2 |
| FR-11 | Operate as a pure byte-rewriter (no injection, no process launch) in v1 | High | No DLL/injection code paths |
| FR-12 | Non-zero exit codes distinguish validation vs not-found vs IO errors | Medium | Exit codes documented |

## Non-Functional Requirements

| ID | Requirement | Metric | Target |
|----|-------------|--------|--------|
| NFR-1 | Portability | TFM | `net8.0`, runs on Linux/macOS/Windows + CI |
| NFR-2 | Safety | Original file mutation | Zero — source never written |
| NFR-3 | Integrity | Output file length delta vs original | 0 bytes |
| NFR-4 | Testability | Automated coverage of patch rules | Round-trip, length/null-pad, missing-string in xUnit |
| NFR-5 | Legality | TQ client assets in repo | None shipped or vendored |
| NFR-6 | Determinism | Same inputs → same output bytes | Byte-identical across OSes |
| NFR-7 | Observability | Report completeness | Every changed offset reported |

## Glossary

- **5065**: Conquer Online client patch/build version targeted by this tool.
- **Auth/login endpoint**: Host + port the client connects to for login (default `127.0.0.1:9958`). The only address the patcher sets.
- **Game IP/port**: Address of the game world server (default port 5816). Sent to the client at runtime by the auth server (`LoginServer.cs:91-92,108-109`). **Never patched.**
- **`Conquer.exe`**: Client executable; may hold the auth host as a hardcoded ASCII string.
- **`server.dat`**: Binary/packed client server-list file; may hold the auth host/port (packed for 5065).
- **`--find` / search string**: Operator-supplied old IP/hostname to locate and replace; build-specific.
- **Null-pad**: Filling the unused tail of the original string slot with `0x00` when the new value is shorter.
- **LAN hex patch**: Separate exe edit (out of scope v1) needed for some clients to accept private/loopback IPs.
- **ConquerLoader / `CLHook.dll`**: Community injection-based loader for 5065. The injection approach is **out of scope** for v1.
- **`tqantivirus`**: Client anti-cheat folder operators must delete for a patched client to run.

## Out of Scope

- **Setting the game IP/port** — auth server delivers it at runtime.
- **Injection launcher** (ConquerLoader / `CLHook.dll`-style in-memory redirect) — noted as future work (v2).
- **Auto-scan** for candidate IP/host strings — operator supplies `--find` in v1.
- **GUI** — CLI only.
- **Auto-applying the LAN "Server.dat is damaged" exe hex patch** — documented, not automated.
- **Shipping/vendoring any TQ client assets or ConquerLoader source** (no declared license) — operator supplies their own 5065 client; technique reimplemented if ever needed.
- **True end-to-end verification against a real `Conquer.exe`** — operator-verified, outside CI.

## Dependencies

- Operator-supplied 5065 client (`Conquer.exe` and/or `server.dat`); none in repo.
- Operator-supplied `--find` string (the build-specific old auth host/IP).
- .NET 8 SDK (`net8.0`); xUnit for tests. Repo currently has no test/CI scaffolding — this spec adds it.
- **Related spec `conquer-online-server`**: defines the auth port the patcher targets. Default auth port is **9958** (per ini/README/`SettingsReader.cs`), but `Constants.cs:87` defaults `LOGIN_PORT=9959`. Documentation note: patcher default is **9958**; the server-side 9959-vs-9958 discrepancy should be reconciled in that spec (out of scope here).

## Operator Notes (must document in deliverable)

- Delete the client's `tqantivirus`/anti-cheat folder before running the patched client.
- Add AV exclusions for the client folder where relevant (false positives are common in this ecosystem).
- Prefer loopback `127.0.0.1`; LAN/internal IPs may require the manual LAN hex patch.
- Exact offset/format of the auth string is per-client-build; confirm against the operator's real files.

## Unresolved Questions

- Backup naming/location convention: alongside source (`*.bak`) vs a dedicated output dir? (Assumed: write patched copy to an output dir, backup as `<file>.bak`.)
- Should `--find` matching be ASCII-only, or also match length-aware UTF-16/wide strings if a build stores the host wide? (Assumed: ASCII byte match in v1; flag if a real client needs wide.)
- Is the auth **port** co-located with the host string or stored separately in `server.dat`? Resolvable only against a real file — may constrain whether `--port` can always be applied. (Assumed: patch host always; apply port where co-located/known, else report port left unchanged.)
- Multiple occurrences of `--find`: replace all, or error on ambiguity? (Assumed: replace all, report each offset.)

## Next Steps

1. Operator/maintainer reviews and approves these requirements.
2. Proceed to design (`/design`): define CLI parser, validation, copy+backup flow, byte-search/replace engine with length/null-pad rules, report format, and the synthetic-fixture xUnit project.
3. Confirm `--find` matching mode (ASCII vs wide) and port co-location assumption against a real 5065 file before locking design.
4. Reconcile the 9959-vs-9958 `LOGIN_PORT` discrepancy in the `conquer-online-server` spec (separate task).
