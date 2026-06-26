# Tasks: client-patcher

Cross-platform `net8.0` console CLI that repoints a CO 5065 client's **auth endpoint only** (default `127.0.0.1:9958`) by ASCII byte-rewriting an operator-supplied `--find` string in a **copy** of `Conquer.exe` / `server.dat`. Pure byte-rewriter — no injection, no process launch, never sets game IP, never extends file length, never mutates source. New isolated `src/ClientPatcher/` + `src/ClientPatcher.Tests/` with dedicated `src/ClientPatcher.sln` and **no refs to server projects**.

**Workflow**: GREENFIELD → POC-first (5 phases). **Granularity**: fine. **Total tasks: 59** (Phase 1: 30, Phase 2: 8, Phase 3: 11, Phase 4: 7, Phase 5: 3).

**Commands** (from research.md): build `scripts/dotnet build src/ClientPatcher.sln` · test `scripts/dotnet test src/ClientPatcher.sln`. Repo has no lint/typecheck targets; C# compiler enforces types via build. No real TQ assets — synthetic in-memory `FixtureFactory` fixtures only (NFR-5).

## Toolchain (dockerized — no local .NET SDK)

There is **no local `dotnet` SDK** on the dev machine (macOS). All `.NET` commands run inside `mcr.microsoft.com/dotnet/sdk:8.0` (the same image `src/Dockerfile` uses) via the **`scripts/dotnet`** wrapper, which mounts the repo at `/repo` and a persistent NuGet cache volume. **Always invoke `scripts/dotnet …`, never bare `dotnet`.** Requires Docker running (it is). Because the wrapper only mounts the repo tree, any runtime E2E fixture dir must live under the repo — tasks use gitignored `./.e2e-tmp/`.

## Execution Policy (remote-driven)

This session is driven remotely; the operator runs the real client on a **separate Windows machine**.

- **Branch**: all work lands on the `feat/client-patcher` feature branch — never commit to the default branch (`modernize/m1`).
- **Commit + push every checkpoint**: each task commits per its **Commit** line; at **every `[VERIFY]` checkpoint** (and after each phase), `git push` all commits since the last push to `origin`. The remote must always reflect the latest green checkpoint so the operator can pull.
- **Manual test handoff**: the patcher's true end-to-end test (patch a real 5065 `Conquer.exe`, launch it, confirm it reaches auth on `127.0.0.1:9958`) runs on the operator's **Windows** box, not here. When the automated work is green and ready (notably after **VE1** passes, and again after **Phase 4** completes), **STOP and notify the operator** with exact run instructions instead of attempting the Windows-side test locally. Treat VE-manual as that handoff checklist.

---

## Phase 1: Make It Work (POC)

Focus: prove the core works end-to-end fast. POC milestone = PatchEngine does correct length-preserving null-padded ASCII search/replace on an in-memory fixture, and a minimal `Program` patches a fixture file from the CLI. Hardcoded shortcuts OK; tests come in Phase 3.

- [x] 1.1 Scaffold ClientPatcher.csproj
  - **Do**: Create `src/ClientPatcher/ClientPatcher.csproj`: `net8.0`, `OutputType=Exe`, `RootNamespace`/`AssemblyName=ClientPatcher`, `Nullable=enable`, `ImplicitUsings=enable`, no NuGet, no ProjectReference.
  - **Files**: src/ClientPatcher/ClientPatcher.csproj
  - **Done when**: csproj has all properties; no package or project refs.
  - **Verify**: `grep -q 'net8.0' src/ClientPatcher/ClientPatcher.csproj && grep -q 'Nullable>enable' src/ClientPatcher/ClientPatcher.csproj && echo PASS`
  - **Commit**: `chore(client-patcher): scaffold ClientPatcher.csproj`
  - _Requirements: NFR-1 · AC-6.1_ _Design: File Structure, Existing Patterns_

- [x] 1.2 Create ClientPatcher.sln + add app project
  - **Do**: Create `src/ClientPatcher.sln` (independent solution) and add `ClientPatcher.csproj`. Use `scripts/dotnet new sln` + `scripts/dotnet sln add` OR hand-author a valid sln referencing the csproj.
  - **Files**: src/ClientPatcher.sln
  - **Done when**: Solution references ClientPatcher.csproj only; no server projects.
  - **Verify**: `grep -q 'ClientPatcher.csproj' src/ClientPatcher.sln && ! grep -qi 'Redux\|Crypto\|Database\|Network\|Packets' src/ClientPatcher.sln && echo PASS`
  - **Commit**: `chore(client-patcher): add ClientPatcher.sln`
  - _Requirements: NFR-1_ _Design: Technical Decisions (dedicated sln)_

- [ ] 1.3 Minimal Program.cs + build green
  - **Do**: Create `src/ClientPatcher/Program.cs` with a stub `Main` that prints a banner and returns 0. Just enough to compile.
  - **Files**: src/ClientPatcher/Program.cs
  - **Done when**: Solution builds with the executable producing.
  - **Verify**: `scripts/dotnet build src/ClientPatcher.sln && echo PASS`
  - **Commit**: `chore(client-patcher): minimal Program entrypoint`
  - _Requirements: NFR-1_ _Design: §8 Program_

- [ ] 1.4 [VERIFY] Quality checkpoint: scaffold builds
  - **Do**: Run `scripts/dotnet build src/ClientPatcher.sln`.
  - **Verify**: `scripts/dotnet build src/ClientPatcher.sln && echo PASS`
  - **Done when**: Build succeeds, no errors.
  - **Commit**: `chore(client-patcher): pass quality checkpoint` (only if fixes needed)

- [ ] 1.5 [P] ExitCode enum
  - **Do**: Create `src/ClientPatcher/ExitCode.cs` with `enum ExitCode { Ok = 0, Validation = 2, NotFound = 3, IoError = 4 }`.
  - **Files**: src/ClientPatcher/ExitCode.cs
  - **Done when**: Enum compiles with exactly these four values.
  - **Verify**: `grep -q 'Ok = 0' src/ClientPatcher/ExitCode.cs && grep -q 'NotFound = 3' src/ClientPatcher/ExitCode.cs && echo PASS`
  - **Commit**: `feat(client-patcher): add ExitCode enum`
  - _Requirements: FR-12_ _Design: §8 exit-code table_

- [ ] 1.6 [P] PatchOptions model
  - **Do**: Create `src/ClientPatcher/PatchOptions.cs` per design §1: `ClientDir`, `Find`, `Ip="127.0.0.1"`, `Port=9958`, `OutDir`, `ShowHelp`.
  - **Files**: src/ClientPatcher/PatchOptions.cs
  - **Done when**: Defaults `127.0.0.1`/`9958` present (AC-1.2).
  - **Verify**: `grep -q '127.0.0.1' src/ClientPatcher/PatchOptions.cs && grep -q '9958' src/ClientPatcher/PatchOptions.cs && echo PASS`
  - **Commit**: `feat(client-patcher): add PatchOptions model`
  - _Requirements: FR-4 · AC-1.2_ _Design: §1 PatchOptions_

- [ ] 1.7 PatchEngine — core search/replace (POC heart)
  - **Do**: Create `src/ClientPatcher/PatchEngine.cs` per design §5: `MatchEdit`, `PatchResult`, `PatchError`, pure `Apply(byte[] source, byte[] find, byte[] replacement)`. Rules: `len(replacement)>len(find)`→`NewLongerThanOld` no output; ASCII byte-search collect ALL offsets; zero→`FindNotFound`; per offset write replacement then null-pad `0x00` to `len(find)`; output length == source length always.
  - **Files**: src/ClientPatcher/PatchEngine.cs
  - **Done when**: Compiles; length-preserving in-place mutation, replace-all, length-guard, null-pad implemented.
  - **Verify**: `scripts/dotnet build src/ClientPatcher.sln && grep -q 'NewLongerThanOld' src/ClientPatcher/PatchEngine.cs && echo PASS`
  - **Commit**: `feat(client-patcher): implement PatchEngine core search/replace`
  - _Requirements: FR-1, FR-6, FR-11 · AC-3.1/3.3/3.4 · NFR-2/3/6_ _Design: §5 PatchEngine_

- [ ] 1.8 [VERIFY] Quality checkpoint: engine builds
  - **Do**: Run `scripts/dotnet build src/ClientPatcher.sln`.
  - **Verify**: `scripts/dotnet build src/ClientPatcher.sln && echo PASS`
  - **Done when**: Build succeeds.
  - **Commit**: `chore(client-patcher): pass quality checkpoint` (only if fixes needed)

- [ ] 1.9 Inline POC harness: prove engine on in-memory fixture
  - **Do**: In `Program.cs`, behind a temporary `--selftest` flag, build an in-memory byte array containing `192.168.0.10\0` among filler, call `PatchEngine.Apply(buf, "192.168.0.10", "127.0.0.1")`, assert output length == input length and matched region == `127.0.0.1` + null pad. Print result, exit 0/1.
  - **Files**: src/ClientPatcher/Program.cs
  - **Done when**: `--selftest` runs and reports the round-trip patch correct.
  - **Verify**: `scripts/dotnet run --project src/ClientPatcher/ClientPatcher.csproj -- --selftest && echo PASS`
  - **Commit**: `feat(client-patcher): inline POC self-test for PatchEngine`
  - _Requirements: FR-1, FR-6 · AC-3.1 · NFR-3_ _Design: Test Strategy (round-trip)_

- [ ] 1.10 EndpointBuilder — replacement bytes + port plan
  - **Do**: Create `src/ClientPatcher/EndpointBuilder.cs` per design §4: `EndpointPlan{HostBytes,Port,PortApplied}`, `Build(PatchOptions)`. Default replacement = ASCII bytes of `--ip` (host only), `PortApplied=false`. Apply port ONLY when `--find` carries a `:port` suffix → replacement `<ip>:<port>`, `PortApplied=true`. Never construct game IP/port.
  - **Files**: src/ClientPatcher/EndpointBuilder.cs
  - **Done when**: Host-only default + co-located `:port` path implemented.
  - **Verify**: `scripts/dotnet build src/ClientPatcher.sln && grep -q 'PortApplied' src/ClientPatcher/EndpointBuilder.cs && echo PASS`
  - **Commit**: `feat(client-patcher): implement EndpointBuilder (host bytes + port plan)`
  - _Requirements: FR-1, FR-2, FR-6 · AC-1.1/1.3_ _Design: §4 EndpointBuilder (port caveat)_

- [ ] 1.11 [VERIFY] Quality checkpoint: endpoint builds
  - **Do**: Run `scripts/dotnet build src/ClientPatcher.sln`.
  - **Verify**: `scripts/dotnet build src/ClientPatcher.sln && echo PASS`
  - **Done when**: Build succeeds.
  - **Commit**: `chore(client-patcher): pass quality checkpoint` (only if fixes needed)

- [ ] 1.12 ArgumentParser + UsageText
  - **Do**: Create `src/ClientPatcher/ArgumentParser.cs` per design §1: hand-rolled `Parse(string[])`→`PatchOptions` recognizing `--client --find --ip --port --out --help/-h`. Unknown flag → throw `ArgParseException`. `UsageText()` lists all flags. POC: minimal, no exhaustive validation yet.
  - **Files**: src/ClientPatcher/ArgumentParser.cs
  - **Done when**: Parses all six flags, applies defaults, throws on unknown flag.
  - **Verify**: `scripts/dotnet build src/ClientPatcher.sln && grep -q 'UsageText' src/ClientPatcher/ArgumentParser.cs && echo PASS`
  - **Commit**: `feat(client-patcher): implement ArgumentParser + UsageText`
  - _Requirements: FR-3, FR-4 · AC-1.2_ _Design: §1 ArgumentParser_

- [ ] 1.13 TargetResolver
  - **Do**: Create `src/ClientPatcher/TargetResolver.cs` per design §3: `TargetFile(Name,SourcePath,OutputPath)`, `Resolve(PatchOptions)` returns 1-2 entries for `Conquer.exe`/`server.dat` that exist under `--client`, matched case-insensitively. OutputPath = `<OutDir>/<name>`.
  - **Files**: src/ClientPatcher/TargetResolver.cs
  - **Done when**: Resolves present targets case-insensitively; OutDir defaults to `<ClientDir>/patched`.
  - **Verify**: `scripts/dotnet build src/ClientPatcher.sln && grep -qi 'server.dat' src/ClientPatcher/TargetResolver.cs && echo PASS`
  - **Commit**: `feat(client-patcher): implement TargetResolver`
  - _Requirements: FR-7 · AC-3.2_ _Design: §3 TargetResolver_

- [ ] 1.14 [VERIFY] Quality checkpoint: parser + resolver build
  - **Do**: Run `scripts/dotnet build src/ClientPatcher.sln`.
  - **Verify**: `scripts/dotnet build src/ClientPatcher.sln && echo PASS`
  - **Done when**: Build succeeds.
  - **Commit**: `chore(client-patcher): pass quality checkpoint` (only if fixes needed)

- [ ] 1.15 BackupWriter
  - **Do**: Create `src/ClientPatcher/BackupWriter.cs` per design §6: `WriteBackup(outputPath, originalCopyBytes)` writes `<output>.bak` (original-copy bytes) BEFORE patched bytes land. Overwrite prior `.bak` on collision. Source under `--client` never opened for write.
  - **Files**: src/ClientPatcher/BackupWriter.cs
  - **Done when**: `<output>.bak` written in out dir from original-copy bytes.
  - **Verify**: `scripts/dotnet build src/ClientPatcher.sln && grep -q '.bak' src/ClientPatcher/BackupWriter.cs && echo PASS`
  - **Commit**: `feat(client-patcher): implement BackupWriter`
  - _Requirements: FR-8 · AC-2.1/2.2_ _Design: §6 BackupWriter_

- [ ] 1.16 ReportWriter
  - **Do**: Create `src/ClientPatcher/ReportWriter.cs` per design §7: `Write(TextWriter, results, warnings, endpoint)` plain-text stdout. Per file list offset (hex), old bytes, new bytes, match count; totals; backups; LAN warnings; port-applied/unchanged line. No JSON.
  - **Files**: src/ClientPatcher/ReportWriter.cs
  - **Done when**: Renders file/offset/old→new/count + totals + backups + port line per design sample.
  - **Verify**: `scripts/dotnet build src/ClientPatcher.sln && grep -q 'offset' src/ClientPatcher/ReportWriter.cs && echo PASS`
  - **Commit**: `feat(client-patcher): implement ReportWriter`
  - _Requirements: FR-9 · AC-2.4 · NFR-7_ _Design: §7 ReportWriter_

- [ ] 1.17 [VERIFY] Quality checkpoint: backup + report build
  - **Do**: Run `scripts/dotnet build src/ClientPatcher.sln`.
  - **Verify**: `scripts/dotnet build src/ClientPatcher.sln && echo PASS`
  - **Done when**: Build succeeds.
  - **Commit**: `chore(client-patcher): pass quality checkpoint` (only if fixes needed)

- [ ] 1.18 InputValidator (POC pass)
  - **Do**: Create `src/ClientPatcher/InputValidator.cs` per design §2: `ValidationResult{Ok,Errors,Warnings}`, `Validate(PatchOptions)`, `IsValidIpv4`, `IsValidHostname`, `IsPrivateLanIpv4`. Rules: valid IPv4/hostname; port 1..65535; client dir exists w/ ≥1 target; `--find` non-empty pure ASCII `0x20..0x7E`; LAN IP → warning (not error). POC: implement core rules, refine in Phase 2.
  - **Files**: src/ClientPatcher/InputValidator.cs
  - **Done when**: Collects errors + LAN warning; returns result without throwing.
  - **Verify**: `scripts/dotnet build src/ClientPatcher.sln && grep -q 'IsPrivateLanIpv4' src/ClientPatcher/InputValidator.cs && echo PASS`
  - **Commit**: `feat(client-patcher): implement InputValidator`
  - _Requirements: FR-5, FR-10 · AC-4.1/4.2/4.3 · AC-5.1_ _Design: §2 InputValidator_

- [ ] 1.19 LAN warning literal substring
  - **Do**: In `InputValidator` (or ReportWriter warning text), ensure the LAN warning string contains the literal substring `Server.dat is damaged` (pinned reviewer assertion AC-5.1).
  - **Files**: src/ClientPatcher/InputValidator.cs
  - **Done when**: LAN warning text includes exact substring `Server.dat is damaged`.
  - **Verify**: `grep -q 'Server.dat is damaged' src/ClientPatcher/InputValidator.cs && echo PASS`
  - **Commit**: `feat(client-patcher): add LAN warning with Server.dat-is-damaged substring`
  - _Requirements: FR-10 · AC-5.1_ _Design: §7 sample report, Test Strategy (pin)_

- [ ] 1.20 [VERIFY] Quality checkpoint: validator builds
  - **Do**: Run `scripts/dotnet build src/ClientPatcher.sln`.
  - **Verify**: `scripts/dotnet build src/ClientPatcher.sln && echo PASS`
  - **Done when**: Build succeeds.
  - **Commit**: `chore(client-patcher): pass quality checkpoint` (only if fixes needed)

- [ ] 1.21 Wire Program pipeline (parse → validate → resolve)
  - **Do**: In `Program.cs`, replace stub `Main`: parse args (catch `ArgParseException`→exit 2), `--help`→print UsageText exit 0, validate (errors→exit 2), resolve targets (none→exit 2). Print warnings.
  - **Files**: src/ClientPatcher/Program.cs
  - **Done when**: Parse/validate/resolve wired with correct early exits.
  - **Verify**: `scripts/dotnet build src/ClientPatcher.sln && echo PASS`
  - **Commit**: `feat(client-patcher): wire parse/validate/resolve pipeline`
  - _Requirements: FR-3, FR-5, FR-7, FR-12 · AC-4.1/4.2/4.3_ _Design: §8, Data Flow_

- [ ] 1.22 Wire Program patch loop (read → engine → backup → write → report)
  - **Do**: In `Program.cs`, per resolved target: read source into in-memory copy (source NEVER opened for write), `EndpointBuilder.Build`, `PatchEngine.Apply` (NewLongerThanOld→exit 2). If `--find` matched in NO file→exit 3. Else per matched file write `<out>.bak` then patched bytes (length preserved), then `ReportWriter.Write` to stdout, exit 0. Wrap IO in try/catch→exit 4.
  - **Files**: src/ClientPatcher/Program.cs
  - **Done when**: Full pipeline writes backup + patched copy and emits report; exit-code mapping per §8.
  - **Verify**: `scripts/dotnet build src/ClientPatcher.sln && echo PASS`
  - **Commit**: `feat(client-patcher): wire patch loop + exit-code mapping`
  - _Requirements: FR-1, FR-2, FR-8, FR-9, FR-12 · AC-1.3/2.1/2.2/2.3/3.2_ _Design: §8, Data Flow_

- [ ] 1.23 [VERIFY] Quality checkpoint: full pipeline builds
  - **Do**: Run `scripts/dotnet build src/ClientPatcher.sln`.
  - **Verify**: `scripts/dotnet build src/ClientPatcher.sln && echo PASS`
  - **Done when**: Build succeeds.
  - **Commit**: `chore(client-patcher): pass quality checkpoint` (only if fixes needed)

- [ ] 1.24 POC end-to-end on real fixture files in temp dir
  - **Do**: Create a temp client dir **under the repo** at `./.e2e-tmp/poc` (gitignored; must be inside the repo tree so the dockerized `scripts/dotnet` container can see it) with stub `Conquer.exe` + `server.dat` byte files (each containing `192.168.0.10\0`). Run `scripts/dotnet run --project src/ClientPatcher/ClientPatcher.csproj -- --client ./.e2e-tmp/poc --find "192.168.0.10" --ip 127.0.0.1`. Assert: backups exist, patched files exist with length == originals, source temp files byte-unchanged, report printed, exit 0.
  - **Files**: (none — runtime verification; artifacts under gitignored `./.e2e-tmp/`)
  - **Done when**: CLI patches both fixture files end-to-end; source untouched; exit 0.
  - **Verify**: Script: build `./.e2e-tmp/poc`, run patcher, assert exit 0 + backup present + `cmp` original-vs-source equal + patched length equal. `echo POC_PASS` on success.
  - **Commit**: `feat(client-patcher): complete POC end-to-end patch`
  - _Requirements: FR-1, FR-8, FR-9 · AC-1.1/2.1/2.2/2.3_ _Design: Data Flow, Test Strategy (integration)_

- [ ] 1.25 [VERIFY] Quality checkpoint after POC
  - **Do**: Run `scripts/dotnet build src/ClientPatcher.sln`.
  - **Verify**: `scripts/dotnet build src/ClientPatcher.sln && echo PASS`
  - **Done when**: Build succeeds.
  - **Commit**: `chore(client-patcher): pass quality checkpoint` (only if fixes needed)

- [ ] 1.26 Remove POC self-test shortcut
  - **Do**: Remove the temporary `--selftest` branch/harness from `Program.cs` added in 1.9 (POC scaffolding no longer needed once E2E proven).
  - **Files**: src/ClientPatcher/Program.cs
  - **Done when**: `--selftest` flag gone; build green.
  - **Verify**: `! grep -q 'selftest' src/ClientPatcher/Program.cs && scripts/dotnet build src/ClientPatcher.sln && echo PASS`
  - **Commit**: `refactor(client-patcher): remove POC self-test harness`
  - _Design: §8 Program (clean orchestrator)_

- [ ] 1.27 [P] Confirm no server-project references
  - **Do**: Audit `ClientPatcher.csproj` + `ClientPatcher.sln` for any ProjectReference/PackageReference to Crypto/Database/Network/Packets/Redux. There must be none.
  - **Files**: src/ClientPatcher/ClientPatcher.csproj
  - **Done when**: Zero references to server projects or NuGet packages.
  - **Verify**: `! grep -qi 'Redux\|Crypto\|Database\|Network\|Packets\|PackageReference' src/ClientPatcher/ClientPatcher.csproj && echo PASS`
  - **Commit**: `chore(client-patcher): confirm isolation from server projects` (only if fixes needed)
  - _Requirements: NFR-1_ _Design: Overview, Technical Decisions_

- [ ] 1.28 [P] Confirm source-never-written invariant
  - **Do**: Audit `Program.cs`/`BackupWriter.cs` to confirm only `OutDir` paths are opened for write; source files under `--client` are read-only.
  - **Files**: src/ClientPatcher/Program.cs
  - **Done when**: No write/`File.Open(...Write)`/`WriteAllBytes` targets a source path under `--client`.
  - **Verify**: `! grep -nE 'File.WriteAllBytes\(.*Source|OpenWrite\(.*Source' src/ClientPatcher/Program.cs && echo PASS`
  - **Commit**: `chore(client-patcher): confirm source files never written` (only if fixes needed)
  - _Requirements: NFR-2 · AC-2.1_ _Design: §6, Data Flow_

- [ ] 1.29 [VERIFY] Quality checkpoint: post-cleanup build
  - **Do**: Run `scripts/dotnet build src/ClientPatcher.sln`.
  - **Verify**: `scripts/dotnet build src/ClientPatcher.sln && echo PASS`
  - **Done when**: Build succeeds.
  - **Commit**: `chore(client-patcher): pass quality checkpoint` (only if fixes needed)

- [ ] 1.30 POC Checkpoint
  - **Do**: Demonstrate the patcher works end-to-end via automated run (temp fixture dir, real CLI invocation) and confirm exit 0, backups, length-preserved patched copies, untouched source, report substrings.
  - **Done when**: Feature demonstrably patches a synthetic client folder from the CLI.
  - **Verify**: Re-run the 1.24 E2E script → `echo POC_PASS`.
  - **Commit**: `feat(client-patcher): complete POC`
  - _Requirements: FR-1/FR-2/FR-8/FR-9 · US-1/US-2/US-3_ _Design: Data Flow_

---

## Phase 2: Refactoring

Clean up structure, harden error handling. No new features. Type/build must pass.

- [ ] 2.1 Harden InputValidator rules
  - **Do**: Tighten `IsValidIpv4` (octet 0..255), `IsValidHostname` (RFC-1123 labels), port `1..65535`, non-ASCII `--find` error message (v1 ASCII-only note), empty `--find` rejection. Collect ALL errors (not fail-fast on first).
  - **Files**: src/ClientPatcher/InputValidator.cs
  - **Done when**: Each invalid-input path yields a clear distinct error string; build green.
  - **Verify**: `scripts/dotnet build src/ClientPatcher.sln && echo PASS`
  - **Commit**: `refactor(client-patcher): harden input validation rules`
  - _Requirements: FR-5 · AC-4.1/4.2/4.3_ _Design: §2 rules, Error Handling_

- [ ] 2.2 Centralize error messages + exit-code mapping
  - **Do**: Extract user-facing error strings (per Error Handling table) so each maps cleanly to ExitCode in `Program.cs`. Ensure non-zero paths write NO patched output for the failing condition.
  - **Files**: src/ClientPatcher/Program.cs
  - **Done when**: Validation→2, not-found→3, IO→4 messages match Error Handling table; no partial writes on failure.
  - **Verify**: `scripts/dotnet build src/ClientPatcher.sln && echo PASS`
  - **Commit**: `refactor(client-patcher): centralize error messages and exit mapping`
  - _Requirements: FR-12 · AC-3.2/3.3_ _Design: §8 table, Error Handling_

- [ ] 2.3 [VERIFY] Quality checkpoint: validation + errors
  - **Do**: Run `scripts/dotnet build src/ClientPatcher.sln`.
  - **Verify**: `scripts/dotnet build src/ClientPatcher.sln && echo PASS`
  - **Done when**: Build succeeds.
  - **Commit**: `chore(client-patcher): pass quality checkpoint` (only if fixes needed)

- [ ] 2.4 Robust IO error handling (exit 4)
  - **Do**: Wrap source read, backup write, and patched write in targeted try/catch emitting "could not read/write <file>: <reason>" → exit 4. Abort in-flight file before flushing patched bytes on write failure.
  - **Files**: src/ClientPatcher/Program.cs
  - **Done when**: Unreadable source / write failure → exit 4 with message; no corrupt partial output.
  - **Verify**: `scripts/dotnet build src/ClientPatcher.sln && echo PASS`
  - **Commit**: `refactor(client-patcher): add IO error handling (exit 4)`
  - _Requirements: FR-12_ _Design: Error Handling (IO rows)_

- [ ] 2.5 Report formatting polish (hex offsets, port line, totals)
  - **Do**: Align `ReportWriter` output to design §7 sample: hex offsets `0x%08X`, quoted old/new bytes, `(host)` tag, port-applied vs "left unchanged (not co-located...)" line, totals, backup paths.
  - **Files**: src/ClientPatcher/ReportWriter.cs
  - **Done when**: Output matches design sample substrings.
  - **Verify**: `scripts/dotnet build src/ClientPatcher.sln && echo PASS`
  - **Commit**: `refactor(client-patcher): polish report formatting`
  - _Requirements: FR-9 · AC-2.4 · NFR-7_ _Design: §7 sample_

- [ ] 2.6 [VERIFY] Quality checkpoint: IO + report
  - **Do**: Run `scripts/dotnet build src/ClientPatcher.sln`.
  - **Verify**: `scripts/dotnet build src/ClientPatcher.sln && echo PASS`
  - **Done when**: Build succeeds.
  - **Commit**: `chore(client-patcher): pass quality checkpoint` (only if fixes needed)

- [ ] 2.7 Edge-case handling pass
  - **Do**: Confirm/implement edge cases (design §Edge Cases): zero-length find→validation; find>file→not-found; only one target present; match in one file not the other = success; match bounded `[0,len-len(find)]`; output-dir exists → reuse + overwrite; `.bak` collision → overwrite.
  - **Files**: src/ClientPatcher/PatchEngine.cs, src/ClientPatcher/Program.cs
  - **Done when**: All listed edge cases handled deterministically.
  - **Verify**: `scripts/dotnet build src/ClientPatcher.sln && echo PASS`
  - **Commit**: `refactor(client-patcher): handle documented edge cases`
  - _Requirements: FR-6, FR-7 · AC-3.2_ _Design: Edge Cases_

- [ ] 2.8 [VERIFY] Quality checkpoint: refactor complete
  - **Do**: Run `scripts/dotnet build src/ClientPatcher.sln`.
  - **Verify**: `scripts/dotnet build src/ClientPatcher.sln && echo PASS`
  - **Done when**: Build succeeds, no warnings introduced.
  - **Commit**: `chore(client-patcher): pass quality checkpoint` (only if fixes needed)

---

## Phase 3: Testing

xUnit + synthetic in-memory fixtures (NFR-4/5, AC-6.2). No real TQ assets. All tests must pass.

- [ ] 3.1 Scaffold ClientPatcher.Tests.csproj + add to sln
  - **Do**: Create `src/ClientPatcher.Tests/ClientPatcher.Tests.csproj`: `net8.0`, xUnit (xunit + xunit.runner.visualstudio + Microsoft.NET.Test.Sdk), `ProjectReference` to ClientPatcher. Add project to `src/ClientPatcher.sln`.
  - **Files**: src/ClientPatcher.Tests/ClientPatcher.Tests.csproj, src/ClientPatcher.sln
  - **Done when**: Test project builds and is in the solution.
  - **Verify**: `scripts/dotnet build src/ClientPatcher.sln && grep -q 'ClientPatcher.Tests.csproj' src/ClientPatcher.sln && echo PASS`
  - **Commit**: `test(client-patcher): scaffold xUnit test project`
  - _Requirements: NFR-4 · AC-6.2_ _Design: File Structure_

- [ ] 3.2 FixtureFactory (synthetic in-memory fixtures)
  - **Do**: Create `src/ClientPatcher.Tests/Fixtures/FixtureFactory.cs`: builds byte arrays for a stub `Conquer.exe` (placeholder host `192.168.0.10\0` among filler) and a stub `server.dat` (host token), plus helper to write them to a temp dir. No real TQ assets.
  - **Files**: src/ClientPatcher.Tests/Fixtures/FixtureFactory.cs
  - **Done when**: Factory returns deterministic synthetic byte arrays + temp-dir writer.
  - **Verify**: `scripts/dotnet build src/ClientPatcher.sln && echo PASS`
  - **Commit**: `test(client-patcher): add synthetic FixtureFactory`
  - _Requirements: NFR-4/NFR-5 · AC-6.2_ _Design: Test Strategy, File Structure_

- [ ] 3.3 [VERIFY] Quality checkpoint: test scaffold builds
  - **Do**: Run `scripts/dotnet build src/ClientPatcher.sln`.
  - **Verify**: `scripts/dotnet build src/ClientPatcher.sln && echo PASS`
  - **Done when**: Build succeeds.
  - **Commit**: `chore(client-patcher): pass quality checkpoint` (only if fixes needed)

- [ ] 3.4 PatchEngineTests — round-trip, length, null-pad, not-found, determinism, multi-match
  - **Do**: Create `src/ClientPatcher.Tests/PatchEngineTests.cs`: round-trip replace + reverse-restore (AC-1.1/3.1); `len(new)>len(old)`→`NewLongerThanOld` no output (AC-3.3); shorter→tail null-padded + terminator at `offset+len(find)` unchanged (AC-3.4); find absent→`FindNotFound` no output (AC-3.2); `output.Length==source.Length` every case (NFR-3/AC-2.3); same inputs twice→byte-identical (NFR-6); two placeholders→two offsets both replaced (assumption d).
  - **Files**: src/ClientPatcher.Tests/PatchEngineTests.cs
  - **Done when**: All PatchEngine tests pass.
  - **Verify**: `scripts/dotnet test src/ClientPatcher.sln --filter FullyQualifiedName~PatchEngineTests && echo PASS`
  - **Commit**: `test(client-patcher): add PatchEngine unit tests`
  - _Requirements: FR-1/FR-6 · AC-2.3/3.1/3.2/3.3/3.4 · NFR-3/6_ _Design: Test Strategy (PatchEngineTests)_

- [ ] 3.5 PatchEngineTests — pinned AC-1.3 no-collateral assertion
  - **Do**: Add the pinned reviewer test: after patch, assert every byte OUTSIDE each matched `[offset, offset+len(find))` region is byte-identical to source (slice-by-slice `Assert.Equal` around edits). Concrete proof no game IP/unrelated bytes touched.
  - **Files**: src/ClientPatcher.Tests/PatchEngineTests.cs
  - **Done when**: No-collateral test passes.
  - **Verify**: `scripts/dotnet test src/ClientPatcher.sln --filter FullyQualifiedName~PatchEngineTests && echo PASS`
  - **Commit**: `test(client-patcher): pin AC-1.3 no-collateral assertion`
  - _Requirements: FR-2 · AC-1.3 · US-1_ _Design: Test Strategy (no-collateral, pinned)_

- [ ] 3.6 [VERIFY] Quality checkpoint: engine tests pass
  - **Do**: Run `scripts/dotnet test src/ClientPatcher.sln`.
  - **Verify**: `scripts/dotnet test src/ClientPatcher.sln && echo PASS`
  - **Done when**: All tests pass.
  - **Commit**: `chore(client-patcher): pass quality checkpoint` (only if fixes needed)

- [ ] 3.7 InputValidatorTests — incl. pinned AC-5.1 LAN substring
  - **Do**: Create `src/ClientPatcher.Tests/InputValidatorTests.cs`: valid/invalid IPv4 + hostname (AC-4.1); port boundaries `0,1,65535,65536` (AC-4.2); missing dir / empty dir (AC-4.3); non-ASCII `--find` rejection; **pinned** `--ip 192.168.1.5`→warnings contains literal substring `Server.dat is damaged` (AC-5.1); loopback `127.0.0.1`→no LAN warning.
  - **Files**: src/ClientPatcher.Tests/InputValidatorTests.cs
  - **Done when**: All validator tests pass incl. literal LAN substring assertion.
  - **Verify**: `scripts/dotnet test src/ClientPatcher.sln --filter FullyQualifiedName~InputValidatorTests && echo PASS`
  - **Commit**: `test(client-patcher): add InputValidator tests (incl. AC-5.1 LAN substring)`
  - _Requirements: FR-5/FR-10 · AC-4.1/4.2/4.3/5.1_ _Design: Test Strategy (Validator)_

- [ ] 3.8 ArgumentParserTests
  - **Do**: Create `src/ClientPatcher.Tests/ArgumentParserTests.cs`: defaults `127.0.0.1`/`9958` when flags omitted (AC-1.2); all flags parsed; `--help`/`-h` sets `ShowHelp`; unknown flag throws `ArgParseException`.
  - **Files**: src/ClientPatcher.Tests/ArgumentParserTests.cs
  - **Done when**: All parser tests pass.
  - **Verify**: `scripts/dotnet test src/ClientPatcher.sln --filter FullyQualifiedName~ArgumentParserTests && echo PASS`
  - **Commit**: `test(client-patcher): add ArgumentParser tests`
  - _Requirements: FR-3/FR-4 · AC-1.2_ _Design: Test Strategy (Parser)_

- [ ] 3.9 [VERIFY] Quality checkpoint: validator + parser tests
  - **Do**: Run `scripts/dotnet test src/ClientPatcher.sln`.
  - **Verify**: `scripts/dotnet test src/ClientPatcher.sln && echo PASS`
  - **Done when**: All tests pass.
  - **Commit**: `chore(client-patcher): pass quality checkpoint` (only if fixes needed)

- [ ] 3.10 EndpointBuilderTests
  - **Do**: Create `src/ClientPatcher.Tests/EndpointBuilderTests.cs`: host-only path → `PortApplied==false`, replacement==host bytes; co-located `--find` with `:port` → replacement==`<ip>:<port>`, `PortApplied==true`; output payload contains only `--ip`/`--port`-derived bytes (never a game IP, AC-1.3).
  - **Files**: src/ClientPatcher.Tests/EndpointBuilderTests.cs
  - **Done when**: All EndpointBuilder tests pass.
  - **Verify**: `scripts/dotnet test src/ClientPatcher.sln --filter FullyQualifiedName~EndpointBuilderTests && echo PASS`
  - **Commit**: `test(client-patcher): add EndpointBuilder tests`
  - _Requirements: FR-1/FR-2/FR-6 · AC-1.1/1.3_ _Design: Test Strategy (EndpointBuilder)_

- [ ] 3.11 In-memory integration test (full pipeline on temp fixture dir)
  - **Do**: Create `src/ClientPatcher.Tests/IntegrationTests.cs`: write FixtureFactory stubs to a temp dir, run the patch pipeline, assert backup written, patched length==original, source temp bytes unchanged (AC-2.1/2.2), report substrings present (AC-2.4: "File:", offset, "matches"). Clean up temp dir.
  - **Files**: src/ClientPatcher.Tests/IntegrationTests.cs
  - **Done when**: Integration test passes end-to-end on synthetic fixtures.
  - **Verify**: `scripts/dotnet test src/ClientPatcher.sln --filter FullyQualifiedName~IntegrationTests && echo PASS`
  - **Commit**: `test(client-patcher): add in-memory integration test`
  - _Requirements: FR-1/FR-8/FR-9 · AC-2.1/2.2/2.4_ _Design: Test Strategy (Integration)_

---

## Phase 4: Quality Gates

All local checks pass, README written, PR created, CI verified. Never push to default branch.

- [ ] 4.1 Write README (operator docs)
  - **Do**: Create `src/ClientPatcher/README.md`: usage + all flags + examples; exit codes; operator notes — delete client `tqantivirus`/anti-cheat folder, add AV exclusions, prefer loopback `127.0.0.1`, LAN "Server.dat is damaged" manual hex-patch caveat (v1 does not auto-apply), ASCII-only `--find` limitation, port co-location caveat; note Nullable/ImplicitUsings deviation from repo style; note no TQ assets shipped (operator supplies client).
  - **Files**: src/ClientPatcher/README.md
  - **Done when**: README covers usage, exit codes, and every operator note.
  - **Verify**: `grep -q 'tqantivirus' src/ClientPatcher/README.md && grep -q 'Server.dat is damaged' src/ClientPatcher/README.md && grep -q '127.0.0.1' src/ClientPatcher/README.md && echo PASS`
  - **Commit**: `docs(client-patcher): add operator README`
  - _Requirements: FR-10 · AC-5.2 · Operator Notes_ _Design: §Security/Legal, File Structure_

- [ ] 4.2 [VERIFY] Quality checkpoint: README + build
  - **Do**: Run `scripts/dotnet build src/ClientPatcher.sln`.
  - **Verify**: `scripts/dotnet build src/ClientPatcher.sln && echo PASS`
  - **Done when**: Build succeeds.
  - **Commit**: `chore(client-patcher): pass quality checkpoint` (only if fixes needed)

- [ ] V4 [VERIFY] Full local CI: build + test
  - **Do**: Run complete local suite for this solution: `scripts/dotnet build src/ClientPatcher.sln && scripts/dotnet test src/ClientPatcher.sln`. (Repo has no lint/typecheck targets; compiler enforces types via build.)
  - **Verify**: Both commands exit 0.
  - **Done when**: Build succeeds and all tests pass.
  - **Commit**: `chore(client-patcher): pass local CI` (if fixes needed)
  - _Requirements: NFR-1/NFR-4 · AC-6.1/6.2_

- [ ] V6 [VERIFY] AC checklist
  - **Do**: Read requirements.md; programmatically confirm each AC-* is satisfied by code/tests. Map: AC-1.1/1.2 (defaults + host patch) → ArgumentParserTests/EndpointBuilderTests; AC-1.3 → PatchEngineTests no-collateral; AC-2.1/2.2/2.3/2.4 → IntegrationTests; AC-3.1/3.2/3.3/3.4 → PatchEngineTests; AC-4.1/4.2/4.3 → InputValidatorTests; AC-5.1/5.2 → InputValidatorTests + README; AC-6.1/6.2 → net8.0 build + test run.
  - **Verify**: `scripts/dotnet test src/ClientPatcher.sln` green AND `grep -q 'Server.dat is damaged' src/ClientPatcher.Tests/InputValidatorTests.cs` AND README grep from 4.1 → all pass.
  - **Done when**: Every AC confirmed met via automated checks.
  - **Commit**: None

- [ ] VE1 [VERIFY] E2E in-memory: build + patch synthetic fixtures in temp dir
  - **Do**:
    1. `scripts/dotnet build src/ClientPatcher.sln` (build artifact).
    2. Create a temp dir **under the repo** at `./.e2e-tmp/ve1` (gitignored; must be inside the repo tree so the dockerized container can see it); write synthetic stub `Conquer.exe` + `server.dat` (each containing `192.168.0.10\0` among filler) — same fixture bytes FixtureFactory uses.
    3. Run patcher: `scripts/dotnet run --project src/ClientPatcher/ClientPatcher.csproj -- --client ./.e2e-tmp/ve1 --find "192.168.0.10" --ip 127.0.0.1`.
    4. Assert: exit 0; backups `./.e2e-tmp/ve1/patched/Conquer.exe.bak` + `server.dat.bak` exist; each patched output length == original; source temp files byte-unchanged (`cmp` against a pristine copy); report stdout contains `File:`, `matches`, and a backup path.
  - **Verify**: Script asserting all conditions → `echo VE1_PASS`. (Real-client E2E against an actual `Conquer.exe` is operator-verified and OUT of CI — see VE-manual.)
  - **Done when**: Built patcher patches a temp dir of synthetic fixtures with backups, length-preserved output, untouched source, expected report, exit 0.
  - **Commit**: None
  - _Requirements: FR-1/FR-8/FR-9 · AC-2.1/2.2/2.3/2.4 · NFR-1_

- [ ] VE-cleanup [VERIFY] E2E cleanup: remove temp fixture dir
  - **Do**: Remove the temp fixture directory created in VE1 (`rm -rf ./.e2e-tmp`). No long-running process/port to free (CLI tool). Runs even if VE1 failed.
  - **Verify**: `[ ! -d "./.e2e-tmp" ] && echo VE_CLEANUP_PASS`
  - **Done when**: Temp fixture dir removed; no artifacts left in repo.
  - **Commit**: None

- [ ] VE-manual [VERIFY] Document operator E2E checklist (out of CI)
  - **Do**: Append a "Manual operator E2E (out of CI)" checklist to `src/ClientPatcher/README.md`: (1) operator supplies real 5065 `Conquer.exe`/`server.dat`; (2) delete `tqantivirus`; (3) run patcher with their build-specific `--find` retail host; (4) launch patched client; (5) confirm it reaches auth on `127.0.0.1:9958`. State this is operator-verified, NOT automated in CI (NFR-5, no TQ assets in repo).
  - **Files**: src/ClientPatcher/README.md
  - **Verify**: `grep -qi 'operator' src/ClientPatcher/README.md && grep -q '9958' src/ClientPatcher/README.md && echo PASS`
  - **Done when**: Manual operator E2E checklist documented in README.
  - **Commit**: `docs(client-patcher): document manual operator E2E checklist`
  - _Requirements: NFR-5 · Out of Scope (real-client E2E)_ _Design: §E2E (out of CI)_

---

## Phase 5: PR Lifecycle

Autonomous PR management until all completion criteria met. Work is already on the `feat/client-patcher` feature branch (pushed continuously per the Execution Policy); never push to the default branch `modernize/m1`.

- [ ] 5.1 Create PR
  - **Do**:
    1. Confirm on a feature branch: `git branch --show-current` (not the default branch; if it is, STOP and alert user).
    2. Push: `git push -u origin <branch>`.
    3. `gh pr create --title "feat(client-patcher): cross-platform auth-endpoint client patcher" --body "<summary: auth-only byte-rewriter, isolated ClientPatcher.sln, no server deps, xUnit synthetic fixtures; note Nullable/ImplicitUsings deviation>"`.
  - **Verify**: `gh pr view --json url -q .url && echo PASS`
  - **Done when**: PR exists.
  - **Commit**: None

- [ ] 5.2 Monitor CI and fix failures
  - **Do**: Watch CI: `gh pr checks --watch`. On failure: read details, fix locally (build/test), `git push`, re-watch. Repeat until green.
  - **Verify**: `gh pr checks` shows all green.
  - **Done when**: All CI checks pass.
  - **Commit**: `fix(client-patcher): resolve CI failures` (if fixes needed)

- [ ] 5.3 Resolve review comments + final validation
  - **Do**: Address any review comments; confirm zero test regressions (`scripts/dotnet test src/ClientPatcher.sln` green), code modular, no server-project refs, no TQ assets vendored. Push fixes.
  - **Verify**: `scripts/dotnet test src/ClientPatcher.sln && gh pr checks && echo PASS`
  - **Done when**: No unresolved comments; CI green; all completion criteria met.
  - **Commit**: `fix(client-patcher): address review feedback` (if changes needed)

---

## Notes

- **POC shortcuts (Phase 1)**: temporary `--selftest` harness (1.9) removed in 1.26; InputValidator implemented loosely in 1.18 then hardened in 2.1. Hardcoded fixture host `192.168.0.10` used only in tests/VE.
- **Production TODOs deferred to v2**: wide/UTF-16 `--find` matching (v1 ASCII-only, rejected at validator); auto-scan for candidate host strings; port-always behavior pending a real 5065 `server.dat` to confirm co-location; LAN auto-hex-patch.
- **Verification commands**: build `scripts/dotnet build src/ClientPatcher.sln` · test `scripts/dotnet test src/ClientPatcher.sln`. Repo has no lint/typecheck targets (C# compiler enforces types via build) and no pre-existing tests — this spec adds the first test target.
- **Isolation invariant**: `ClientPatcher.csproj`/`ClientPatcher.sln` must never reference Crypto/Database/Network/Packets/Redux or any NuGet beyond xUnit (test project only). Guarded in 1.27.
- **Pinned reviewer assertions**: AC-1.3 no-collateral (3.5), AC-5.1 literal substring `Server.dat is damaged` (1.19 + 3.7).
- **No real TQ assets** anywhere (NFR-5); all fixtures synthetic in-memory via FixtureFactory; real-client E2E is operator-verified, out of CI (VE-manual).
