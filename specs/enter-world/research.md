---
spec: enter-world
phase: research
created: 2026-06-26
---

# Research: enter-world

## Executive Summary

After login the 5065 client receives `ANSWER_OK` + HeroInformation(1006), then sends a GeneralData(1010) whose `Action` subtype is **SetLocation(74)** and freezes awaiting world data. The original (pre-M1) server clears the freeze by **echoing that 1010 back with the player's MapID+X+Y, then sending MapStatusPacket(1110)** — no map files required (the client loads its own .cqmap; the net8 `TinyMapService.Valid` is a stub returning `true`). The player's own body renders from 1006 + that position; the SpawnEntity(1014) packet is sent only to *other* players (or back to self on InvisibleEntity/102 as a fallback). Minimal stand-still spawn = handle 1010/SetLocation → reply SetLocation echo + 1110.

## The Enter-World Sequence (ground truth: pre-M1 Redux code)

### What the client sends after 1006

GeneralData(1010) = `GeneralActionPacket`. Wire layout (`src/Redux/Packets/Game/[1010] GeneralData.cs:86-112`):

| Offset (body) | Field | Type |
|---|---|---|
| 0 | length / header | — |
| 2 | type = 1010 | u16 |
| 4 | Timestamp | u32 |
| 8 | UID | u32 |
| 12 | Data1 | u32 |
| 16 | Data2 | u32 |
| 20 | Data3 | u16 |
| **22** | **Action (DataAction)** | **u16** |

The carried log `1800 F203 <ts:4> 01000000 00000000` shows `18 00`=len 0x18 (24), `F2 03`=type 1010, then Timestamp + Data1=1 + Data2=0. **The Action field is at offset 22 — not visible in that 12-byte excerpt.** Re-capture the full frame live to read the exact subtype; on all known TQ 5065 servers the first post-1006 1010 is `SetLocation` (74).

### DataAction values (`src/Redux/Enum/DataAction.cs`)

```
SetLocation=74  Hotkeys=75  ConfirmFriends=76  ConfirmProficiencies=77
ConfirmSkills=78  ChangeAction=81  Teleport=86  InvisibleEntity=102
GetSurroundings=114  CompleteLogin=130  Jump=133  NewCoordinates=108
```

### What the original server does per case (`src/Redux/Network/GameServer.cs Process_GeneralActionPacket`)

| Case | Lines | Server response |
|---|---|---|
| **SetLocation (74)** | 429-437 | Echo packet back with `Data1=MapID, Data2Low=X, Data2High=Y`; then send `MapStatusPacket.Create(map)` (1110). **This is the world-entry trigger.** |
| GetSurroundings (114) | 440-443 | `UpdateSurroundings(true)` → spawns nearby entities (none needed for empty stand-still). |
| InvisibleEntity (102) | 603-609 | `user.Send(SpawnEntityPacket.Create(user))` — sends the player's OWN 1014 back to self. |
| Jump (133) | 446 | `HandleJump` — movement, out of scope. |
| CompleteLogin (130) | 582-600 | Later login step: lucky-time / heaven-bless timers; echo packet. Not required for spawn. |
| Hotkeys (75) | 401-407 | ServerTime + nobility + "logged on" broadcast. Not required. |

### Critical finding — the own-body spawn

`Populate()` (`src/Redux/Objects/Player.cs:746-831`) sends: 1006 + inventory items + equipment, sets `SpawnPacket`, then `MapManager.AddPlayer`. `MapManager.AddPlayer` (`src/Redux/Managers/MapManager.cs:32-45`) only inserts into the map and calls `UpdateSurroundings(true)` — **it never sends the player's own SpawnEntity(1014) to itself.** The client's body is rendered from **HeroInformation(1006) + the position delivered by the SetLocation reply**. The 1014 is sent to self only via InvisibleEntity(102) (`GameServer.cs:608`). → minimal spawn does NOT need 1014-to-self; keep it as a fallback.

## Packet Byte Layouts Needed

### Reply A — SetLocation echo (1010), clears loading

Re-emit the inbound `GeneralActionPacket` with position filled (`GameServer.cs:429-434`):
- `Action=74`, `UID=charUID`, `Data1=MapID`, `Data2Low=X`, `Data2High=Y` (Data2 = `(Y<<16)|X`).
- 28-byte body + 8 seal. Builder offsets per `[1010] GeneralData.cs:98-111`.

### Reply B — MapStatusPacket (1110)

`src/Redux/Packets/Game/[1110] MapStatus.cs`: 16+8 bytes. `UID@4`, `ID@8` (map id), `Type@12`. net8 has no DbMap loaded → send minimal `UID=mapid, ID=mapid, Type=0`.

### Fallback — SpawnEntity (1014), if body doesn't render

`src/Redux/Packets/Game/[1014] SpawnEntity.cs`: 100+name bytes. Key fields: `UID@4`, `Lookface@8` (=Mesh), `Life@48`, `Level@50`, `X@52`, `Y@54`, `Hair@56`, `Direction@58`, `Action@59` (0=stand), `RebornCount@60`, `Level@62`, name via NetStringPacker@90.

### HeroInformation (1006) — already sent, no map/X/Y

Neither the net8 port (`src/Packets/HeroInformation.cs`) nor the original (`[1006]HeroInformation.cs`) carries map/position. **Position is delivered ONLY by the SetLocation reply.** No 1006 change needed; it is reused as-is.

## Map Data Dependency

| Question | Answer | Source |
|---|---|---|
| Does the server need .cqmap/DMAP to send jump/spawn? | **No.** | client loads its own maps |
| Does net8 validate coords against a loaded map? | **No** — stubbed. | `src/Redux/TinyMapStub.cs` `TinyMapService.Valid(...) => true` |
| Original gate on map validity? | Yes (`ChangeMap`/`HandleJump` call `Common.MapService.Valid`), but stub makes all pass | `Player.cs:1191,1245` |
| Known-good spawn map+coords for 5065 | **Map 1002 / X 438 / Y 381 (Twin City)** | `Player.cs CreateDbCharacter:688-690`; `Nov_16_Backup.sql` (most chars on 1002) |
| Seeded "Vitor" current coords | net8 `DbCharacter` defaults **Map 1010 / X 61 / Y 109** | `src/Database/CharacterRepository.cs:14-16` |

**Recommendation:** for first bring-up, override the SetLocation reply (or re-seed the character) to **1002 / 438 / 381** — the canonical, known-rendering TC location. Map 1010 is unverified for the operator's client and may be empty/invalid.

## Net8 Integration

| Step | Where | Change |
|---|---|---|
| Persist character on session | `src/Network/ClientSession.cs` | Add `DbCharacter? Character`. `GameHandler.Handle` (`src/Packets/MsgConnect.cs:47`) currently looks up + discards — store it on the session. |
| Dispatch 1010 | `src/Redux/PacketRouter.cs:46-61` | Add `case 1010: _game.HandleAction(session, payload); break;` |
| Parse action | new handler | Payload is frame minus 2-byte length prefix (so `payload[0]=type`, `payload[2]=Timestamp`). **Action is at payload offset 20** (= original body offset 22 − 2). Read `u16`. |
| Build SetLocation reply | new packet builder (mirror `[1010] GeneralData.cs` byte layout) | type 1010, body 28, Action=74, UID, Data1=MapID, Data2=(Y<<16)\|X |
| Build MapStatus | new builder (mirror `[1110] MapStatus.cs`) | type 1110, body 16, UID/ID=mapid, Type=0 |
| Send | `ClientSession.SendGame` (`src/Network/ClientSession.cs:75`) | 8-byte "TQServer" seal + Blowfish, already proven |

Existing outbound (`SendGame`) and frame split/dispatch (`GameConnection.HandleFrames` → `PacketRouter.Dispatch`) are reused unchanged.

## Feasibility Assessment

| Aspect | Assessment | Notes |
|---|---|---|
| Technical Viability | High | All primitives exist; only a 1010 handler + 2 small builders + session state. |
| Effort Estimate | S | ~1 handler, 2 packet builders, 1 session field. |
| Risk Level | Medium | Exact 1010 subtype + whether 1110 alone clears loading are only confirmable live. |

## Recommendations for Requirements

1. Store the `DbCharacter` on `ClientSession` at MsgConnect(1052); stop discarding it.
2. Add a 1010 (MSG_ACTION) handler dispatched from `PacketRouter`; read `Action` at payload offset 20.
3. On `SetLocation(74)`: reply with the 1010 echo (MapID+X+Y from the session character) **then** MapStatus(1110), via `SendGame`.
4. Use spawn coords **Map 1002 / X 438 / Y 381** for first bring-up (override or re-seed); keep DB MapID/X/Y as the source once a map is confirmed.
5. Keep a 1014-to-self path ready (InvisibleEntity/102 style) as a fallback if the body fails to render.
6. Keep the existing `[Game]` diagnostic logging through this work; add a log line dumping the full 1010 frame (all 28 body bytes) to confirm the subtype live. Clean up logs at milestone end.

## Recommended Milestone Order

1. **M1 — Clear loading:** handle 1010/SetLocation → send SetLocation echo (map 1002/438/381) + MapStatus(1110). Verify: does the loading screen clear?
2. **M2 — Render standing:** if the body is invisible, add SpawnEntity(1014)-to-self. Verify: char visible standing at TC.
3. **M3 — Minimal surroundings (only if needed):** handle GetSurroundings(114) as a no-op/empty `UpdateSurroundings`; add CompleteLogin(130)/Hotkeys(75) echoes if the client still hangs on a follow-up 1010.

## Open / Unresolved Questions (confirm live vs the real client)

- Exact `Action` value in the client's first 1010 (offset 22) — expected 74, **must dump the full frame to confirm**.
- Whether SetLocation echo + 1110 alone clears the loading screen, or the client expects 1014-to-self / CompleteLogin first.
- Whether map 1010 is renderable in the operator's client (recommend 1002 to de-risk).
- Whether the client sends additional 1010 subtypes (GetSurroundings/CompleteLogin) after the first and blocks awaiting their echoes.

## Quality Commands

| Type | Command | Source |
|---|---|---|
| Build | `scripts/dotnet build src/Conquer.sln` | progress.md / repo convention (dockerized) |
| Test | `scripts/dotnet test src/Conquer.sln` | progress.md |
| Run | `docker compose -f src/docker-compose.yml up -d --build` | progress.md (server @192.168.0.252) |
| Logs | `docker compose -f src/docker-compose.yml logs -f server` | progress.md |
| Lint / TypeCheck / E2E | Not found | no automated client; verification is operator-manual |

## Verification Tooling

No automated E2E tooling for the game client. **Verification is operator-manual vs the real Windows 5065 client** (watch server logs + screen). Loop: rebuild via Docker → operator logs in → observe loading-clear and on-screen character → read `[Game]` logs for the 1010 subtype and reply flow.

**Project Type:** Game server (.NET 8) + external Windows client.
**Verification Strategy:** Build in Docker, run on 192.168.0.252, operator logs in with the real client; confirm (1) loading clears, (2) "Vitor" renders standing at TC, via screen + logs.

## Sources

- `src/Redux/Network/GameServer.cs` — `Process_MsgConnectPacket` (301), `Process_GeneralActionPacket` (332): SetLocation 429-437, GetSurroundings 440-443, InvisibleEntity 603-609, CompleteLogin 582-600, Hotkeys 401-407.
- `src/Redux/Objects/Player.cs` — `Populate` 746-831, `CreateDbCharacter` 670-690, `ChangeMap` 1184-1239, `HandleJump` 1243.
- `src/Redux/Managers/MapManager.cs` — `AddPlayer` 32-45, `SpawnByUID` 118-181.
- `src/Redux/Objects/Entity.cs` — `UpdateSurroundings` 251-274.
- `src/Redux/Enum/DataAction.cs` — full enum.
- `src/Redux/Packets/Game/[1010] GeneralData.cs`, `[1006]HeroInformation.cs`, `[1014] SpawnEntity.cs`, `[1110] MapStatus.cs`.
- `src/Redux/TinyMapStub.cs` — stubbed map validation (`Valid => true`).
- `src/Redux/PacketRouter.cs`, `src/Redux/GameConnection.cs`, `src/Network/ClientSession.cs`, `src/Packets/MsgConnect.cs`, `src/Packets/HeroInformation.cs` — net8 path.
- `src/Database/CharacterRepository.cs` — net8 DbCharacter (MapID/X/Y defaults).
- `src/init.sql`, `src/Nov_16_Backup.sql` — seed data / canonical TC coords.
