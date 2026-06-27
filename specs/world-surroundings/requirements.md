# Requirements: world-surroundings (EPIC 1)

## Goal

Build the shared-world layer so players on the same map SEE EACH OTHER and see each
other spawn, walk, jump, and leave in real time — replacing today's per-session
isolation. PLAYERS ONLY for v1; the registry/grid/broadcast/send-safety it builds is
the foundation every later epic (chat, NPCs, monsters, combat, items) reuses unchanged.

## User Stories

### US-1: See who's already on my screen
**As a** player entering a map
**I want to** see every other player already standing within my screen range
**So that** the world feels populated, not empty

**Acceptance Criteria:**
- [ ] AC-1.1: GIVEN players A and B on the same map within screen range, WHEN B sends GetSurroundings(114) after entering, THEN B receives one SpawnEntity(1014) for A (and for every other on-screen player), excluding B's own.
- [ ] AC-1.2: GIVEN the 114 reply, WHEN A's 1014 is built, THEN it carries A's LIVE CurrentX/CurrentY (not A's DbCharacter row).
- [ ] AC-1.3: GIVEN B requests surroundings and sees A, WHEN the reply is sent, THEN A also receives B's 1014 (mutual visibility), and both visible-sets are seeded.
- [ ] AC-1.4: GIVEN no other players on B's screen, WHEN B sends 114, THEN B receives zero other-player 1014s and no error occurs.

### US-2: See other players spawn into my screen
**As a** player standing on a map
**I want to** see a new player appear when they enter my screen
**So that** arrivals are visible in real time without re-polling

**Acceptance Criteria:**
- [ ] AC-2.1: GIVEN A is on the map, WHEN B enters the map (HandleSetLocation completes), THEN B is registered into the map registry and grid at B's live position.
- [ ] AC-2.2: GIVEN A and B become mutually on-screen for the first time (via 114 or a move), WHEN the enter-screen is detected, THEN A receives B's 1014 and B receives A's 1014 (mutual), each added to the other's visible-set.
- [ ] AC-2.3: GIVEN a 1014 is sent before the entity's first move packet, WHEN ordering is enforced, THEN the spawn (1014) always precedes any forwarded walk/jump for that UID.

### US-3: See other players walk and jump
**As a** player watching another player
**I want to** see them walk and jump live as they do
**So that** movement is shared, not private to each session

**Acceptance Criteria:**
- [ ] AC-3.1: GIVEN A and B on each other's screen, WHEN A walks (WalkHandler), THEN after A's own-position update, a built outbound MsgWalk(1005) carrying A's UID@4/Dir@8/Mode@9 is broadcast to the players in A's 3×3 cell block (includeSelf=true).
- [ ] AC-3.2: GIVEN A walks, WHEN the broadcast is forwarded, THEN it is a BUILT outbound 1005 (Walk.BuildBroadcast), NOT the prefix-stripped inbound buffer.
- [ ] AC-3.3: GIVEN A and B on each other's screen, WHEN A jumps (ActionHandler 133), THEN after A's own-position update, the existing BuildJump packet is broadcast to A's 3×3 cell block (includeSelf=true), in addition to the existing self-send.
- [ ] AC-3.4: GIVEN A moves, WHEN the new position stays inside A's current cell, THEN no grid cell mutation occurs (cell move only on boundary cross); WHEN it crosses a cell boundary, THEN A is atomically removed from the old cell and added to the new.
- [ ] AC-3.5: GIVEN k players on A's screen, WHEN A moves, THEN the broadcast packet is built ONCE and fanned out to those k players (O(k), never O(N) full-scan, never O(N²)).

### US-4: See players leave my screen and disconnect
**As a** player watching another player
**I want to** see them disappear when they walk out of my screen or log off
**So that** stale ghosts do not linger

**Acceptance Criteria:**
- [ ] AC-4.1: GIVEN A is on B's screen, WHEN A moves out of B's 3×3 block (leave-screen), THEN B receives a RemoveEntity despawn (GeneralData 1010, Action=132, UID@8=A) and A is removed from B's visible-set (and reciprocally).
- [ ] AC-4.2: GIVEN A walked out of B's screen, WHEN A walks back into it, THEN A reappears for B via a fresh mutual 1014.
- [ ] AC-4.3: GIVEN A is registered, WHEN A's connection tears down (ServeGameAsync finally), THEN a RemoveEntity(132) for A is broadcast to A's screen AND A is deregistered from the registry and grid.
- [ ] AC-4.4: GIVEN the disconnect teardown, WHEN the deregister/broadcast runs, THEN it is wrapped in try/catch (like the existing position flush) so teardown never throws, and the existing position flush still runs.

### US-5: Broadcasting never corrupts another player's connection
**As a** server operator running hundreds of connections
**I want** sends from foreign threads to be serialized per-session
**So that** broadcasting does not corrupt the cipher keystream or interleave socket writes

**Acceptance Criteria:**
- [ ] AC-5.1: GIVEN ClientSession.SendGame is called concurrently from the owning loop and one or more broadcaster threads, WHEN sends run, THEN a per-session send lock serializes the encrypt+write pair atomically (no keystream corruption, no interleaved bytes).
- [ ] AC-5.2: GIVEN a broadcast packet built once and handed to N recipients' SendGame, WHEN each recipient encrypts, THEN SendGame copies into its own buffer before encrypting so the shared build-once buffer is not mutated.
- [ ] AC-5.3: GIVEN the send-lock change, WHEN applied, THEN it is purely additive (wraps the existing encrypt+write) and changes no auth/handshake/own-position behavior.

### US-6: A reusable shared-world layer for later epics
**As a** developer building later epics (chat, NPCs, monsters, combat, items)
**I want** the registry/grid/broadcast designed for reuse
**So that** later entity kinds plug in without reworking this layer

**Acceptance Criteria:**
- [ ] AC-6.1: GIVEN the new src/World project, WHEN wired, THEN World is injected via PacketRouter like CharacterRepository (no static singleton), and World references Network+Database only.
- [ ] AC-6.2: GIVEN ClientSession (Network) cannot reference World, WHEN handlers reach the world, THEN a Network-side interface or handler-injected World is used (no Network→World reference).
- [ ] AC-6.3: GIVEN the registry/grid/broadcast structures, WHEN documented, THEN the design notes how later epics register their entities into the SAME structures without rework.

## Functional Requirements

| ID | Requirement | Priority | Acceptance Criteria |
|----|-------------|----------|---------------------|
| FR-1 | Per-session send lock around encrypt+write in ClientSession.SendGame (prerequisite — broadcast is unsafe without it) | High | Concurrent foreign-thread sends produce uncorrupted, non-interleaved output (AC-5.1) |
| FR-2 | New src/World project: World → ConcurrentDictionary<int,MapInstance>; MapInstance holds Roster (UID→PlayerEntity) + Cells (cellKey→occupants); injected via PacketRouter | High | World/MapInstance constructible + injected; no static singleton (AC-6.1) |
| FR-3 | Fixed-cell spatial grid: 18-tile cells; cellX=X/18; cellKey=((long)cellX<<32)\|cellY; screen query = 3×3 cell block around the entity | High | 3×3 block enumerated; query cost independent of map population (AC-3.5) |
| FR-4 | PlayerEntity: UID, MapId, live X/Y, cached CellX/CellY, ClientSession handle, mesh/appearance, Visible set; BuildSpawn() from LIVE X/Y | High | Entity built and queryable; 1014 uses live coords (AC-1.2) |
| FR-5 | Generalize SpawnEntity.BuildSelf(DbCharacter) → Build(uid,mesh,...,x,y,name) taking live coords; BuildSelf delegates | High | BuildSelf unchanged in behavior; Build produces other-player 1014 (AC-1.1) |
| FR-6 | GetSurroundings(114): un-gate ActionHandler case 114 → reply one 1014 per OTHER on-screen player AND send requester's 1014 to each (mutual); seed visible-sets | High | 114 → mutual 1014 per on-screen player (AC-1.1, AC-1.3) |
| FR-7 | Register player into registry+grid in ActionHandler.HandleSetLocation (after spawn echo) | High | Player registered on map entry (AC-2.1) |
| FR-8 | Walk broadcast: add Walk.BuildBroadcast(uid,dir,mode) (framed 20-byte 1005); after own-position update + grid Move, broadcast once to 3×3 block (includeSelf) | High | Built 1005 broadcast, not echoed inbound (AC-3.1, AC-3.2) |
| FR-9 | Jump broadcast: after own-position update + grid Move, broadcast existing BuildJump to 3×3 block (includeSelf), in addition to existing self-send | High | Jump visible to screen (AC-3.3) |
| FR-10 | Grid Move on position change: cell-delta only on boundary cross (atomic TryRemove old + TryAdd new); no-op within a cell | High | No mutation within cell; atomic on cross (AC-3.4) |
| FR-11 | Enter/leave-screen diff after each move: new entrants → mutual 1014; leavers → RemoveEntity(132) | High | Spawn on enter, despawn on leave (AC-2.2, AC-4.1, AC-4.2) |
| FR-12 | Add GeneralData.BuildRemoveEntity(uid): 1010, UID@8=uid, Action@22=132, no coords | High | RemoveEntity packet built correctly (AC-4.1) |
| FR-13 | Deregister + broadcast RemoveEntity(132) in NetworkListener.ServeGameAsync finally, beside existing flush, try/caught | High | Despawn-on-disconnect; teardown never throws (AC-4.3, AC-4.4) |
| FR-14 | Build each broadcast packet ONCE, fan out to the 3×3 block only | High | One build per broadcast, O(k) fan-out (AC-3.5) |
| FR-15 | Enforce spawn-before-move ordering: send 1014 before any forwarded walk/jump for a UID | Medium | 1014 precedes first move packet (AC-2.3) |

## Non-Functional Requirements (scalability is the explicit priority — first-class, target HUNDREDS of concurrent connections)

| ID | Requirement | Metric | Target |
|----|-------------|--------|--------|
| NFR-1 | Broadcast fan-out cost | Per-move complexity | O(N·k), k=players on screen; NEVER O(N²); packet built once per broadcast |
| NFR-2 | Screen query cost | Per-query complexity | O(cells)≈O(1) via 3×3 cell block; NO per-query LINQ full-scan of all players |
| NFR-3 | Registry/grid read concurrency | Locking | Lock-free reads (ConcurrentDictionary); no global per-map lock |
| NFR-4 | Grid mutation concurrency | Locking | Atomic per-cell TryRemove+TryAdd; cell move only on boundary cross; no global map lock |
| NFR-5 | Per-session send correctness | Concurrency safety | Per-session send lock guarantees no keystream corruption / no interleaved writes under concurrent foreign-thread sends |
| NFR-6 | Hot-path allocation | Allocations per broadcast | Build-once broadcast buffer reused across recipients (SendGame copies before encrypt → shared buffer safe); ArrayPool only after measuring |
| NFR-7 | Build strictness | Warnings | New code nullable-clean; passes strict gate (Nullable=enable, TreatWarningsAsErrors, AnalysisLevel=latest); 0 warnings |
| NFR-8 | Forward-looking reuse | Design property | Registry/grid/broadcast designed so later epics (NPCs, monsters, items, chat, combat) register entities into the SAME structures without rework |

### Integration constraints (additive NFRs)

| ID | Requirement | Target |
|----|-------------|--------|
| NFR-9 | New src/World project references Network+Database; injected via PacketRouter like CharacterRepository (Program.cs); NO static singleton | World is a constructible injected service |
| NFR-10 | ClientSession (Network) MUST NOT reference World — use a Network-side interface or inject World into handlers | No Network→World project reference |
| NFR-11 | MUST NOT change auth, crypto/handshake, GameConnection, enter-world spawn ECHO, character creation, or movement's own-position logic — only ADD register/broadcast/grid-move after existing updates; send-lock is additive | Diff is purely additive at named hooks |
| NFR-12 | Build/test dockerized: scripts/dotnet build\|test src/Conquer.sln (NO local SDK); new src/World added to src/Conquer.sln | Green dockerized build+test |
| NFR-13 | Unit tests (new src/World.Tests or src/Packets.Tests) cover pure spatial math: cell-key packing + X/18 cell math, 3×3 screen-block enumeration, cell-transition (only-on-boundary), screen-area calc, visible-set diff, packet byte layouts (Walk.BuildBroadcast, BuildRemoveEntity, Build live-coords 1014) | xUnit covering all pure math |

## Glossary

- **Entity registry**: per-map `ConcurrentDictionary<uint,PlayerEntity>` (Roster) keyed by UID — the authoritative who-is-on-this-map structure.
- **Spatial grid / cell**: each map partitioned into fixed 18-tile cells; cellX=X/18, cellKey=((long)cellX<<32)|cellY; `ConcurrentDictionary<long,ConcurrentDictionary<uint,PlayerEntity>>`.
- **Screen 3×3 block**: the 9 cells around an entity's cell; equals the 36×36-tile screen (18-tile cell × 3). The screen query enumerates these 9 cells only.
- **GetSurroundings (114)**: GeneralData(1010) Action subtype 114; the client's request for who is on its screen. Server replies with one 1014 per on-screen player.
- **SpawnEntity (1014)**: the packet that makes an entity render on a client's screen; same builder for self and others; other-player variant uses LIVE X/Y.
- **RemoveEntity (132)**: GeneralData(1010) Action subtype 132; despawns the entity whose UID is at offset 8 from a client's screen.
- **Broadcast / fan-out**: build one outbound packet, send it to every PlayerEntity in the mover's 3×3 block (includeSelf as needed). Cost = O(players in screen).
- **Per-session send lock**: a lock around the encrypt+write pair in ClientSession.SendGame so concurrent sends from foreign threads cannot corrupt the stateful cipher or interleave socket writes.
- **PlayerEntity**: a player's presence in the world — UID, MapId, live X/Y, cached cell, ClientSession handle, appearance, and visible-set; produces its own spawn bytes via BuildSpawn().

## Out of Scope (explicit)

- NPCs, monsters, ground items, combat, chat — later epics; they REUSE this layer but build nothing here.
- Cross-map visibility (players on different maps never see each other).
- Movement range validation / anti-cheat (movement already trusts the client).
- Collision detection.
- The drained-send-queue upgrade — the per-session send lock is sufficient for v1; a queue is a future upgrade only if profiling shows send contention.
- Equipment/appearance fidelity on the 1014 beyond the existing fields (naked-but-visible avatar is acceptable; equipment appearance is a later epic).
- Persistence changes — surroundings is pure in-memory; position still flushes once on disconnect, unchanged.

## Dependencies

- Movement's in-memory own-position (ClientSession.CurrentMap/CurrentX/CurrentY) — the live source for entity X/Y; surroundings reads it, does not change it.
- SpawnEntity.BuildSelf 1014 builder (src/Packets/SpawnEntity.cs) — generalized to Build(...) with live coords.
- ActionHandler hooks: case 114 (un-gate), HandleSetLocation (register), HandleJump (broadcast).
- WalkHandler hook (after own-position update) — grid Move + walk broadcast + re-diff.
- NetworkListener.ServeGameAsync teardown (finally) — deregister + RemoveEntity broadcast, beside the existing position flush.
- PacketRouter + Program.cs manual-DI wiring — to inject World into handlers (mirrors CharacterRepository).
- src/Conquer.sln — add src/World and the test project.

## Success Criteria

- Operator E2E (the real acceptance): two clients (vitor=hawkk, account1=hawkk2) log in on the same map → SEE each other on screen → see each other WALK and JUMP in real time → when one logs off, it disappears for the other; when one walks out of screen it vanishes and reappears on return.
- xUnit suite for the spatial math (cell math, 3×3 query, cell-transition, screen-area, visible-set diff, packet layouts) passes under `scripts/dotnet test src/Conquer.sln`.
- `scripts/dotnet build src/Conquer.sln` is green (0 warnings, strict gate) with src/World added.
- No measured keystream corruption / interleaved writes under concurrent broadcast.

## Unresolved Questions

- Other-player 1014 spawn pose: is Direction/Action 0 (stand) acceptable on spawn, or must it carry the mover's current facing? (Low risk; verify on enter-screen during live E2E.)
- Visible pop-in at the 36×36 screen edge: the 18-tile 3×3 block slightly over-covers the exact box — confirm no visible artifact live; tighten to an exact-box filter only if needed.
- Client tolerance of a forwarded foreign-UID 1005/1010-133 arriving before its 1014 if ordering races — FR-15 mandates spawn-before-move; confirm live that ordering holds under load.

## Next Steps

1. After requirements approved, proceed to design phase (`/design`).
2. Design the src/World project surface (World, MapInstance, PlayerEntity, grid, broadcast) + the Network-side interface/injection seam + the send-lock change.
3. Design the pure-math unit-test surface (cell math, 3×3 query, transition, diff, packet layouts) for src/World.Tests / Packets.Tests.
4. Sequence implementation by the research milestones: (1) registry+grid+register+114 reply + send-lock → (2) walk/jump broadcast → (3) enter/leave-screen + disconnect despawn.
