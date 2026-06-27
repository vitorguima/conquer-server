---
spec: movement
phase: research
created: 2026-06-27
---

# Research: movement (server-authoritative MsgWalk 1005)

## Executive Summary

Server ignores MsgWalk(1005) (`[Warn] Unknown typeId=1005` in `PacketRouter.Dispatch`), so position is frozen at the spawn tile and relog resets it. Fix is fully additive: add `case 1005` → new `WalkHandler` (src/Packets) that parses direction+mode, applies the original 8-direction `DeltaX/DeltaY` table to mutable per-session live coords, and flushes once on disconnect via a new `CharacterRepository.UpdatePosition`. Ground-truth wire layout, deltas, and handler logic all confirmed in the in-repo pre-M1 Redux code. No DB write per step (CLAUDE.md MMO rule). Client prediction already moves the avatar, so v1 needs NO echo to the moving client.

## Codebase Analysis — Ground Truth (pre-M1 Redux)

### 1. MsgWalk(1005) wire layout — CONFIRMED

Source: `src/Redux/Packets/Game/[1005] Walk.cs` (`WalkPacket`). Original frame: header written at 0 by `PacketBuilder.AppendHeader` (len@0, type@2), then UID@4, Direction@8, Mode@9, Unknown1@10. The `byte*` reader: `UID=*(uint*)(ptr+4)`, `Direction=*(ptr+8)`, `Mode=*(ptr+9)`, `Unknown1=*(ushort*)(ptr+10)`. Total frame 20 bytes (incl. 8-byte seal in net8 framing).

| Field | Type | Original **body** offset | Net8 **dispatch payload** offset (body − 2) |
|-------|------|--------------------------|---------------------------------------------|
| Length prefix | u16 | 0 | — (stripped by GameConnection) |
| Type (1005) | u16 | 2 | **0** |
| UID | u32 | 4 | **2** |
| Direction | u8 (0–7) | 8 | **6** |
| Mode | u8 | 9 | **7** |
| Unknown1 | u16 | 10 | **8** |

`payload[0..2)`=typeId, `payload[2..6)`=UID, `payload[6]`=Direction, `payload[7]`=Mode. Min dispatch-payload length guard: **`payload.Length < 8`** (must read Direction@6 + Mode@7; matches the payload = frame−2 contract from `GameConnection.HandleFrames`, line 123).

Payload-offset rule restated: net8 `payload offset = original body offset − 2` (the 2-byte length prefix is stripped — same contract `ActionHandler` uses: Action@20 = body 22 − 2; `Packets.Tests/ActionParseTests.cs`).

### 2. 8-direction (dx,dy) delta table — CONFIRMED LITERAL

Source: `src/Redux/Common.cs:21-22` (9-element, index 8 = no-move/stationary):

```
DeltaX = { 0, -1, -1, -1,  0,  1,  1,  1,  0 };  // index 0..8
DeltaY = { 1,  1,  0, -1, -1, -1,  0,  1,  0 };
```

Applied at `GameServer.cs:1440-1441`: `x = client.X + DeltaX[dir]; y = client.Y + DeltaY[dir];` with `dir = packet.Direction % 8` (line 1439). Direction 0..7 going **counter-clockwise from due-south (+Y)**:

| dir | (dx,dy) | compass (Y grows south) |
|-----|---------|--------------------------|
| 0 | (0, +1) | S |
| 1 | (−1, +1) | SW |
| 2 | (−1, 0) | W |
| 3 | (−1, −1) | NW |
| 4 | (0, −1) | N |
| 5 | (+1, −1) | NE |
| 6 | (+1, 0) | E |
| 7 | (+1, +1) | SE |

Design hardcodes these as `static readonly sbyte[8]` (drop the redundant index-8 entry; guard `dir in 0..7`). Exact compass labels are CO/TQ convention and not load-bearing for v1 — the literal deltas are what matter.

### 3. Original handler logic (`Process_WalkPacket`) — CONFIRMED

Source: `src/Redux/Network/GameServer.cs:1437-1466`:
- `dir = Direction % 8`; compute candidate `(x,y)` from deltas.
- Validate: `client.Map.IsValidPlayerLocation(new Point(x,y))` (map collision — **out of scope v1**, no .cqmap loaded).
- On VALID: `client.SendToScreen(packet, true)` → re-broadcasts the SAME walk packet to surroundings **including self** (`Player.SendToScreen(byte[] _data, bool _self=false)`, `Player.cs:1361`); then sets `Direction/X/Y`, `OnMove()`, `UpdateSurroundings()`.
- On INVALID: sends a `GeneralActionPacket{ Action = NewCoordinates }` snap-back to current X/Y; logs; does NOT disconnect (`//client.Disconnect(true)` commented out).

**v1 recommendation — NO echo to the moving client.** Client-side prediction already moves the avatar locally; the original `SendToScreen(...,true)` echo exists to drive the surroundings/broadcast system (out of scope). Updating in-memory `(x,y)` silently is sufficient and proven correct by the live "relog = spawn at last position" E2E. Sending the walk back risks a double-step under prediction. Snap-back (`NewCoordinates`) is also deferred — without server collision there is nothing to reject in v1; just bound-check and accept.

**Jump:** the 1005 `WalkPacket` has no x/y fields → jump is NOT carried on 1005 here (it is a separate packet/action in CO). v1 ignores jump entirely (no 1005 carries absolute target). Mode (`payload[7]`) distinguishes walk vs run; v1 reads it for logging but applies the same single-tile delta for any mode (run = client animation speed, same per-packet step).

### 4. Authoritative live position (in-memory) — recommendation

`ClientSession.Character` is `DbCharacter?` with `init` setters → X/Y immutable (confirmed `CharacterRepository.cs:8-24`). Add mutable live fields to `src/Network/ClientSession.cs`:

```csharp
public int CurrentMap { get; set; }
public ushort CurrentX { get; set; }
public ushort CurrentY { get; set; }
public bool PositionLoaded { get; set; }  // guard the disconnect flush
```

**Seed point:** `GameHandler.Handle` (`src/Packets/MsgConnect.cs:47-49`) sets `session.Character = character` on the 1052 connect. Seed `CurrentMap/X/Y` from `character.MapID/X/Y` there, immediately after the null check, and set `PositionLoaded = true`. (`ActionHandler.HandleSetLocation`, `ActionHandler.cs:49-52`, reads `ch.MapID/X/Y` for the spawn echo — that read stays on `Character` and is unchanged; it is the *seed source*, not the live store.) `WalkHandler` then mutates `CurrentX/Y` only. Per CLAUDE.md: in-memory authoritative, **no DB write per step**.

### 5. Persistence on disconnect — recommendation

**Hook:** `NetworkListener.ServeGameAsync` `finally` block (`src/Network/...` → actually `src/Redux/NetworkListener.cs:145-149`) — the single per-connection teardown for game sockets. Flush there, before/around `session.Disconnect()`:

```csharp
finally
{
    if (session.PositionLoaded && session.Character != null)
        characters.UpdatePosition(session.Character.CharacterID,
                                  session.CurrentMap, session.CurrentX, session.CurrentY);
    session.Disconnect();
    ...
}
```

Wiring note: `NetworkListener` currently holds only `PacketRouter` (no repo). Inject `CharacterRepository` into `NetworkListener` (already constructed in `Program.cs:32-34` — pass `characters` to the listener ctor). One UPDATE per session.

**New repo method** (`CharacterRepository` currently has only `FindByAccountId` + `Insert` — CONFIRMED `CharacterRepository.cs:35-54`):

```csharp
public void UpdatePosition(int characterId, int mapId, int x, int y)
{
    using var conn = _factory.Create();
    conn.Execute(
        "UPDATE characters SET MapID=@mapId, X=@x, Y=@y WHERE CharacterID=@characterId",
        new { characterId, mapId, x, y });
}
```

`characters.X/Y/MapID` are `INT NOT NULL` (`src/init.sql:39-41`). **Relog path already reads saved X/Y:** `FindByAccountId` SELECTs `MapID, X, Y` → `GameHandler` sets `Character` → `ActionHandler.HandleSetLocation` spawns at `Character.X/Y`. So flush-on-disconnect is **sufficient** for "spawn where you logged off."

**Periodic flush:** NOT needed for v1 (a single player, manual E2E). Note as future hardening (crash/kill loses the session's unsaved walk — disconnect-only flush misses hard crashes). Recommend a periodic `UpdatePosition` (e.g. every 30–60s per active session) only when populated-world / crash-durability matters.

### 6. Validation (Power-of-10 Rule 7)

Guard-first shape, mirroring `ActionHandler.cs:19` / `RegisterHandler.cs:41`:
1. `if (payload.Length < 8) { log "[Game] short 1005"; return; }` — bounds before reading Direction@6/Mode@7.
2. `if (session.Character == null || !session.PositionLoaded) return;` — no live position to move.
3. `byte dir = payload[6]; if (dir > 7) { log; return; }` (or `dir % 8` per original — prefer explicit `> 7` reject + log so bad input is visible).
4. Compute `nx = CurrentX + dx[dir]`, `ny = CurrentY + dy[dir]` as `int`; reject if `nx < 0 || ny < 0 || nx > ushort.MaxValue || ny > ushort.MaxValue` (sane-coord / non-negative ushort bound). No .cqmap → no walkability check (out of scope).
5. On invalid → **log + ignore the packet, do NOT disconnect** (a bad walk must not kill the session). A *malformed-length* frame is already handled upstream by `GameConnection.HandleFrames` (oversized/garbled → disconnect); the in-handler short guard just returns.
6. On valid → `session.CurrentX = (ushort)nx; session.CurrentY = (ushort)ny;` log `[Game] walk dir=N -> (x,y)`. No outbound send (v1).

### 7. Net8 strict-build constraint

New code lives in nullable-ENFORCED, warnings-as-errors projects (`src/Directory.Build.props`: `Nullable=enable`, `TreatWarningsAsErrors=true`):
- `WalkHandler` → `src/Packets` (namespace `Conquer.Packets`), no Redux import (Packets.csproj refs Crypto+Network+Database only — CONFIRMED).
- `ClientSession` fields → `src/Network`.
- `UpdatePosition` → `src/Database`.
All must be nullable-clean: null-check `session.Character` before deref; value-type fields need no null handling. `WalkHandler` registered in `PacketRouter` ctor like `_action`/`_register` (no DI container — manual `new`). It needs NO repo (pure in-memory mutate); construct `new WalkHandler()` and add `case 1005: _walk.Handle(session, payload); break;`.

## Net8 Integration Map (additive — mirror existing pattern)

| Change | File | Pattern source |
|--------|------|----------------|
| `case 1005:` + `_walk` field/ctor | `src/Redux/PacketRouter.cs:23-25, 50-71` | `_action`/`_register` |
| `WalkHandler.Handle` + static delta arrays | NEW `src/Packets/WalkHandler.cs` | `ActionHandler` / `RegisterHandler` |
| `CurrentMap/X/Y`, `PositionLoaded` | `src/Network/ClientSession.cs:53` | new mutable props |
| Seed live pos on 1052 | `src/Packets/MsgConnect.cs:47-49` | after `session.Character = character` |
| `UpdatePosition` | `src/Database/CharacterRepository.cs:54` | mirror `Insert` |
| Disconnect flush + repo inject | `src/Redux/NetworkListener.cs:107-150`, `Program.cs:32-34` | `ServeGameAsync` finally |
| xUnit: delta math + 1005 parse | NEW `src/Packets.Tests/WalkParseTests.cs` | `ActionParseTests.cs` |

## Feasibility Assessment

| Aspect | Assessment | Notes |
|--------|------------|-------|
| Technical viability | High | Wire layout + deltas + handler all confirmed in-repo; pattern is a proven copy of ActionHandler/RegisterHandler |
| Effort | S | ~1 handler + 4 session fields + 1 repo method + 1 disconnect hook + 2 tests |
| Risk | Low–Medium | Risks are LIVE-only (below); code path is simple, additive, no hot-path DB |

## Recommendations for Requirements (milestone order)

1. **M-walk-1**: parse+validate 1005 → update in-memory `CurrentX/Y` → **log it** (prove server tracks movement live). No outbound, no DB.
2. **M-walk-2**: `UpdatePosition` + disconnect flush + relog reads saved X/Y → confirm relog spawns at last position (not spawn tile).
3. **M-walk-3 (future, separate specs)**: broadcast walk to other players (surroundings/entity system), server-side collision (.cqmap), anti-speedhack timing, periodic flush.

## Risks / Unknowns (confirmable only LIVE)

- Exact 1005 bytes vs a real 5065-client capture (offsets are from the in-repo `WalkPacket`; verify Direction lands at payload[6], Mode at payload[7] on the wire).
- Whether the moving client needs ANY server confirm under 5065 prediction (recommend none; revert to original `SendToScreen(...,true)` echo only if the avatar visibly desyncs).
- Direction→delta mapping vs what the 5065 client expects (the `{0,-1,-1,-1,0,1,1,1}` / `{1,1,0,-1,-1,-1,0,1}` table is the fork's own convention — should match, but confirm a few directions visually).
- Jump packet id/shape if jumping is later required (not on 1005 here).

## Out of Scope (explicit)

Server-side map collision/walkability (no .cqmap loaded; client enforces its own — trust the move, only bound-check coords); anti-speedhack timing; **broadcasting movement to other players** (needs the surroundings/entity system — separate spec); `GetSurroundings(114)`; the `NewCoordinates` snap-back correction; jump.

## Quality Commands

Dockerized only — no local SDK (CLAUDE.md):

| Type | Command | Source |
|------|---------|--------|
| Build | `scripts/dotnet build src/Conquer.sln` | CLAUDE.md |
| Test | `scripts/dotnet test src/Conquer.sln` | CLAUDE.md (15 tests: 7 Crypto + 8 Packets) |
| Build (patcher) | `scripts/dotnet build src/ClientPatcher.sln` | CLAUDE.md |
| Lint/TypeCheck | (folded into build) | strict build = warnings-as-errors |
| E2E (operator-manual) | walk in real 5065 client → relog → spawn at last position | server on 192.168.0.252 |

**Do NOT stage/commit `src/docker-compose.yml`** (local-only `GameServer__Ip` override).

## Verification Tooling

No automated browser/E2E harness — this is a TCP game server, not a web app. Verification = Dockerized build + xUnit + operator-manual live walk-and-relog.

- **Project type**: .NET 8 MMO TCP server (auth :9958 / game :5816), single process.
- **Strategy**: xUnit for pure delta-math + 1005 parse (no socket/DB — mirror `ActionParseTests` static-method tests); operator-manual live E2E on 192.168.0.252.
- **Health/port detection**: ports from `appsettings.json` (`AuthPort` 9958, `GamePort` 5816 defaults in `NetworkListener.cs:25,45`). No HTTP health endpoint. Docker via `src/Dockerfile` + `src/docker-compose.yml`.

## Related Specs

Single active spec (`movement`). No sibling specs to cross-check; prerequisites (auth/enter-world/char-creation) are merged M1 history, not concurrent specs.

## Sources

- `src/Redux/Packets/Game/[1005] Walk.cs` — wire layout (UID@4, Direction@8, Mode@9)
- `src/Redux/Common.cs:21-22` — DeltaX/DeltaY tables
- `src/Redux/Network/GameServer.cs:1437-1466` — Process_WalkPacket logic
- `src/Redux/Objects/Player.cs:1361` — SendToScreen(_data, _self) signature
- `src/Redux/PacketRouter.cs` — Dispatch switch + handler injection pattern
- `src/Redux/GameConnection.cs:99-130` — frame split, payload = frame−2 contract
- `src/Redux/NetworkListener.cs:107-150` — ServeGameAsync read loop + finally (disconnect hook)
- `src/Redux/Program.cs:28-34` — manual DI (repo construction)
- `src/Packets/ActionHandler.cs`, `src/Packets/RegisterHandler.cs`, `src/Packets/MsgConnect.cs` — additive handler + guard + seed pattern
- `src/Packets/PacketBuilder.cs` — header layout (len = size−8 @0, type @2)
- `src/Database/CharacterRepository.cs` — Insert + FindByAccountId only (needs UpdatePosition)
- `src/init.sql:32-52` — characters schema (MapID/X/Y INT NOT NULL)
- `src/Packets.Tests/ActionParseTests.cs` — xUnit parse-test pattern
- `CLAUDE.md` — dockerized build, strict build, MMO in-memory/async-batched rule
