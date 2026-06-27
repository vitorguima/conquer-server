# CLAUDE.md

Guidance for AI agents (architect/executor) working in the conquer-server repo.

## Build & test (dockerized — no local SDK)

There is **no local .NET SDK**. Every build/test command MUST go through the
Docker wrapper from the repo root — never bare `dotnet`:

```bash
scripts/dotnet build src/Conquer.sln     # server solution (8 projects)
scripts/dotnet test  src/Conquer.sln     # 15 tests: 7 Crypto + 8 Packets
scripts/dotnet build src/ClientPatcher.sln
```

Do **not** stage or commit `src/docker-compose.yml` — it carries a local-only
`GameServer__Ip` override that must stay uncommitted.

## Engineering Principles

Adapted from Holzmann's "Power of 10" rules for safety-critical code, mapped to
this managed C#/.NET 8 MMO server. Keep the build green and these invariants
intact.

- **Rule 10 — the build is strict.** `src/Directory.Build.props` is the shared
  baseline for every project under `src/`: `Nullable=enable`,
  `TreatWarningsAsErrors=true`, `AnalysisLevel=latest`,
  `EnforceCodeStyleInBuild=true`, `LangVersion=latest`. The build enforces zero
  warnings — keep it green. Analyzers, code-style, and all non-nullable compiler
  warnings are errors. Do not silence per-file; fix properly.

  - **TODO: burn down remaining nullable warnings (CS86xx).** The legacy `Redux`
    game-server project has ~274 pre-existing nullable sites (CS8600/8601/8602/
    8603/8604/8618/8625). Fixing them all at once risks behavior changes, so they
    are currently deferred via `<NoWarn>` in `Directory.Build.props` (nullable
    analysis still runs — `Nullable=enable` — so new code is held to it). Burn
    these down incrementally: fix a cluster (e.g. one class), remove its codes
    from the `NoWarn` list, confirm `scripts/dotnet build src/Conquer.sln` stays
    0/0, repeat. Goal: delete the `NoWarn` line entirely.

- **Rule 7 — validate ALL untrusted network input.** Every packet handler
  bounds-checks every offset/length read from the wire *before* reading it. The
  canonical pattern is an early `payload.Length < N` guard, e.g.
  `ActionHandler.cs` (`if (payload.Length < 22) ...`) and `RegisterHandler.cs`
  (`if (payload.Length < 60) ...`). Never index past a bound you have not first
  validated. New handlers MUST follow this guard-first shape.

- **Rule 2 — bounded loops.** Loops over wire data or collections must have a
  statically reasoned upper bound; no unbounded `while(true)` over network input.

- **Rule 4 — small functions** (~≤60 lines). Split large handlers/methods.

- **Rule 6 — smallest possible scope.** Declare variables at point of use;
  prefer `private`/local over wider visibility. No dead locals (CS0219).

- **Rule 1 — simple control flow.** No `goto`, no deep nesting; prefer early
  returns / guard clauses.

- **Rule 5 — defensive guards / assertions.** Check arguments and invariants at
  function entry; fail fast on impossible state.

- **Rule 9 — no `unsafe`/pointers in new code.** Prefer `Span<T>` /
  `ReadOnlySpan<T>` + `System.Buffers.Binary.BinaryPrimitives` for parsing and
  serializing wire data instead of pointer arithmetic. (Some legacy `Redux`
  packet code still uses `unsafe` + `AllowUnsafeBlocks`; migrate it toward spans
  when touched, don't add new `unsafe`.)

- **Rule 3 (managed spirit) — minimize hot-path allocations.** Rules 3 and 8 are
  C-specific (no heap after init / no preprocessor) and N/A here; the managed
  spirit of Rule 3 is: avoid per-packet `new byte[]` in the game loop. When a
  buffer allocation is on a hot path (per-movement / per-surroundings packet),
  use `ArrayPool<byte>` rather than allocating per call. Measure first.

## MMO scalability rule

Live world state — player positions, surroundings/visibility, combat — is held
**in memory as the authoritative source**. Persistence is **async and batched**;
there is NEVER a database round-trip per movement or per surroundings packet.
A DB hit on the hot path would not scale to a populated world.

- Keep the auth (`:9958`) / game (`:5816`) seam clean, but do **not** pre-split
  into microservices — one process today.
- **Measure before optimizing.** No speculative N+1 elimination or premature
  sharding; profile the real game loop first.
