# ClientPatcher

Cross-platform (`net8.0`) console CLI that **repoints a Conquer Online 5065 client's auth/login
endpoint** (default `127.0.0.1:9958`) by ASCII byte-rewriting an operator-supplied search string
inside a **copy** of `Conquer.exe` and/or `server.dat`.

It is a **pure byte-rewriter**: it never injects, never launches the game, never sets the *game*
world IP (the auth server delivers that at runtime), and never extends file length. The operator's
original files are never opened for write — patched output and backups land in a separate output
directory.

> **No TQ assets are shipped with this tool.** The operator supplies their own 5065 client
> (`Conquer.exe` / `server.dat`). All automated tests run against synthetic, in-memory fixtures.

## Build & test (dockerized — no local .NET SDK)

This repo has **no local `dotnet` SDK**. All .NET commands run inside
`mcr.microsoft.com/dotnet/sdk:8.0` via the **`scripts/dotnet`** wrapper (run from the repo root,
with Docker running):

```bash
scripts/dotnet build src/ClientPatcher.sln
scripts/dotnet test  src/ClientPatcher.sln
scripts/dotnet run --project src/ClientPatcher/ClientPatcher.csproj -- --help
```

The wrapper mounts the repo at `/repo`, so anything the tool must read/write at runtime (e.g. E2E
fixture dirs) **must live under the repo tree** to be visible inside the container — use the
gitignored `./.e2e-tmp/`.

> **Project-style deviation:** unlike the server projects (`Nullable=disable`,
> `ImplicitUsings=disable`), this greenfield project enables `Nullable=enable` and
> `ImplicitUsings=enable`. It has no server-project references and no NuGet beyond xUnit (test
> project only).

## Usage

```
ClientPatcher --client <dir> --find <string> [--ip <host>] [--port <n>] [--out <dir>]
```

### Flags

| Flag | Required | Default | Description |
|------|----------|---------|-------------|
| `--client <dir>` | yes | — | Directory holding the client files (`Conquer.exe` / `server.dat`). |
| `--find <string>` | yes | — | ASCII search string to replace (the current/retail auth host). |
| `--ip <host>` | no | `127.0.0.1` | Replacement host (IPv4 or hostname). |
| `--port <n>` | no | `9958` | Auth port (`1..65535`). See the port co-location caveat below. |
| `--out <dir>` | no | `<client>/patched` | Output directory for patched copies + `.bak` backups. Must not equal `--client`. |
| `--help`, `-h` | no | — | Print usage and exit `0`. |

### Examples

Patch a client pointing at the default loopback auth endpoint (`127.0.0.1:9958`):

```bash
scripts/dotnet run --project src/ClientPatcher/ClientPatcher.csproj -- \
  --client ./client --find "192.168.0.10"
```

Patch with an explicit host and port, writing to a custom output directory:

```bash
scripts/dotnet run --project src/ClientPatcher/ClientPatcher.csproj -- \
  --client ./client --find "auth.example.com" --ip 127.0.0.1 --port 9958 --out ./client-patched
```

Co-located `host:port` replacement (only when the matched slot itself carries a `:port` — see
caveat below):

```bash
scripts/dotnet run --project src/ClientPatcher/ClientPatcher.csproj -- \
  --client ./client --find "192.168.0.10:9958" --ip 127.0.0.1 --port 9958
```

Print help:

```bash
scripts/dotnet run --project src/ClientPatcher/ClientPatcher.csproj -- --help
```

### Output

For each matched file the tool writes:

- `<out>/<file>` — the patched copy (length identical to the original).
- `<out>/<file>.bak` — a backup of the unpatched copy, written **before** the patched bytes land.

It then prints a plain-text report to stdout listing each file, the offset(s) changed (hex),
old → new bytes, match counts, totals, the backup paths, and whether the auth port was applied or
left unchanged.

## Exit codes

| Code | Meaning | Triggers |
|------|---------|----------|
| `0` | Success | At least one file patched; report written. (`--help` also exits `0`.) |
| `2` | Validation error | Bad/unknown flag, invalid IPv4/hostname, port out of `1..65535`, missing `--client` dir or no target file, non-ASCII `--find`, or `len(new) > len(old)`. |
| `3` | Search string not found | `--find` present in **neither** target file. Nothing is written. |
| `4` | IO error | Unreadable source, or output/backup write failure. |

On any non-zero path, **no patched output is written** for the failing condition.

## Operator notes

- **Delete the client's `tqantivirus` / anti-cheat folder** before launching a patched client.
  The 5065 anti-cheat will otherwise block or revert the patched executable.
- **Add an antivirus exclusion** for the client folder. False positives are common in this
  ecosystem; AV may quarantine the patched `Conquer.exe`.
- **Prefer loopback `127.0.0.1`.** It is the safest, best-tested auth host and the tool's default.
- **LAN/private IP caveat (`Server.dat is damaged`):** if `--ip` is a private/LAN address
  (`10.*`, `172.16–31.*`, `192.168.*`), the client may reject it with the error
  **"Server.dat is damaged"** and require an **additional manual exe hex patch**. The tool prints a
  warning in this case but **does NOT auto-apply** that LAN hex patch in v1 — it is a documented
  manual follow-up. Prefer loopback to avoid it.
- **ASCII-only `--find` (v1 limitation):** the search/replace is an ASCII byte match
  (`0x20..0x7E`). Non-ASCII `--find` is rejected at validation. Wide/UTF-16 host strings are not
  supported in v1 (deferred to v2).
- **Port co-location caveat:** the **host is always patched**. The auth **port** is applied
  **only** when the matched `--find` slot itself carries a co-located `:port` suffix (proving
  co-location for that specific build) — then the replacement becomes `<ip>:<port>`. Otherwise the
  port is **left unchanged** and the report says so explicitly. The port's storage location is
  per-build unknown and only resolvable against a real 5065 `server.dat`.
- **Per-build offsets:** the exact auth string offset/format is per-client-build. Confirm the
  `--find` string against the operator's real files.

## Security / legal

- No TQ client assets are shipped or vendored. The operator supplies their own 5065 client.
- No injection, no process launch, no native interop in v1 — the patcher itself presents no
  AV-injector surface.
- Source files under `--client` are never opened for write; backups are written before patched
  output.

## Manual operator E2E (out of CI)

The tool's automated tests and the in-memory E2E run against **synthetic fixtures only** — no real
TQ assets live in this repo (NFR-5). The true end-to-end test against an actual `Conquer.exe` is
**operator-verified on Windows and is NOT automated in CI**. Run this checklist on the operator's
Windows machine against a real 5065 client:

1. **Supply a real 5065 client.** Copy the operator's own `Conquer.exe` and/or `server.dat` into a
   working folder (e.g. `C:\co5065\client`). Nothing in this repo provides them.
2. **Delete the `tqantivirus` folder** inside the client directory (and add an AV exclusion for the
   folder). The patched client will not launch with the anti-cheat present.
3. **Determine the build-specific retail auth host string** stored in the client (the current
   hardcoded login host/IP). This is the value to pass as `--find` — it is per-build, so confirm it
   against the operator's actual files (e.g. with a hex/strings inspection).
4. **Run the patcher** against the client, repointing to your auth server. Example template
   (replace `<RETAIL_AUTH_HOST>` and the output path; use `127.0.0.1` for a local auth server or
   your server's IP otherwise):

   ```bash
   scripts/dotnet run --project src/ClientPatcher/ClientPatcher.csproj -- \
     --client C:\co5065\client --find "<RETAIL_AUTH_HOST>" --ip 127.0.0.1 --port 9958 \
     --out C:\co5065\client-patched
   ```

   (On a Windows box with a local .NET 8 SDK, the equivalent `dotnet run …` works without the
   `scripts/dotnet` Docker wrapper.)
5. **Copy the patched files back** from the output dir over the client (keep the `.bak` backups),
   then **launch the patched client** and confirm it **reaches auth on `127.0.0.1:9958`** (or the
   operator's server IP/port). A successful login screen / auth handshake confirms the repoint.

> This step is **operator-verified on Windows** and intentionally **out of CI** — the repo ships no
> real client and cannot run the actual game. If a LAN/private auth IP triggers
> "Server.dat is damaged", apply the manual LAN exe hex patch (not automated by v1).
