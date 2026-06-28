# Requirements: combat-foundation (EPIC 4/5 — Phase 0 PvE combat loop)

## Goal

Deliver a working PvE melee loop on the active server: a player clicks a monster, deals
damage, kills it, gains XP and levels up; killed monsters respawn and aggro/chase/attack
back, and a dead player respawns at the map's revive point. Fully ADDITIVE on the live
World/Network/Packets layers (Redux is reference-only). 0.1 (monsters spawn + render via
1014) is already delivered; this spec covers 0.2 (live player combat-state), 0.3 (melee
1022 loop), 0.4 (respawn + AI tick + player death/respawn), plus the stat-seeding and
prerequisite fixes the research flagged.

## User Stories

### US-1: Live mutable player combat-state (0.2)
**As a** player in the world
**I want** my current HP/mana/attack to live in memory, seeded at login and saved at logout
**So that** combat can change my stats hundreds of times a fight without a DB hit per change

**Acceptance Criteria:**
- [ ] AC-1.1: GIVEN a player logs in (1052, MsgConnect.Handle where session.Character is set), WHEN combat-state seeds, THEN live mutable `CurrentHp`, `CurrentMana`, `Strength`, `Experience` (and a `CombatLoaded` flag) are copied from `DbCharacter` onto `ClientSession`, mirroring the `CurrentX/Y`/`PositionLoaded` lifecycle (ClientSession.cs:60-65).
- [ ] AC-1.2: GIVEN combat-state is seeded, WHEN any combat code reads live HP/Strength/Experience, THEN it reads the mutable session fields (authoritative), NOT the init-only `DbCharacter` row.
- [ ] AC-1.3: GIVEN HP/mana/experience/level change during play, WHEN they change, THEN they mutate IN MEMORY only — NO DB write per hit/kill (CLAUDE.md MMO rule).
- [ ] AC-1.4: GIVEN a player disconnects, WHEN teardown runs (NetworkListener finally, beside the existing `UpdatePosition` flush, ~NetworkListener.cs:155-159), THEN the live HP/mana/experience/level are flushed ONCE via an async/batched repo call (e.g. `UpdateExperienceLevel` + HP/mana), wrapped so teardown never throws.
- [ ] AC-1.5: GIVEN `session.CurrentHp` is the authoritative live HP, WHEN the World needs a player HP source (0.4 attack-back), THEN it uses `session.CurrentHp`; `PlayerEntity.Hp` may remain the spawn snapshot (single source of truth — no second authoritative HP).

### US-2: Real starting stats at character creation (stat seeding)
**As a** newly created player
**I want** sensible non-zero base attributes
**So that** my melee attacks deal meaningful damage instead of the floor of 1

**Acceptance Criteria:**
- [ ] AC-2.1: GIVEN a character is created (RegisterHandler), WHEN starting attributes are written, THEN Strength (and the other base attributes) are seeded to sensible class-appropriate non-zero values — NOT the Strength=0 placeholder (init.sql:42).
- [ ] AC-2.2: GIVEN the new defaults, WHEN a freshly created player attacks a Pheasant (Def=0), THEN damage = Strength (> 1) so kills take a sane number of hits, not 33.
- [ ] AC-2.3: GIVEN both the create path and the seed data, WHEN defaults change, THEN BOTH `RegisterHandler` and the `characters` seed (init.sql / seeded rows) reflect the new starting attributes (consistent create + seed).

### US-3: Experience persistence column (prerequisite)
**As a** server operator
**I want** the `characters` table to carry an Experience column
**So that** XP and level survive logout and reload

**Acceptance Criteria:**
- [ ] AC-3.1: GIVEN `characters` has no `Experience` column (init.sql:32-51, 0 matches), WHEN the schema is updated, THEN `init.sql` adds `Experience BIGINT NOT NULL DEFAULT 0` AND an idempotent `ALTER TABLE characters ADD COLUMN IF NOT EXISTS Experience …` so the existing db volume gets the column (CREATE TABLE IF NOT EXISTS will not add it).
- [ ] AC-3.2: GIVEN the column exists, WHEN `DbCharacter`/`CharacterRepository` are updated, THEN `DbCharacter` gains an `Experience` field and the repo SELECT and INSERT include it.
- [ ] AC-3.3: GIVEN XP/level must persist, WHEN a flush method is added, THEN an `UpdateExperienceLevel(charId, experience, level)` (mirroring `UpdatePosition`) writes them on disconnect.

### US-4: Melee attack a monster (0.3)
**As a** player
**I want** to click a monster and see it take damage
**So that** combat is real and visible on screen

**Acceptance Criteria:**
- [ ] AC-4.1: GIVEN a melee 1022 (MsgInteract) arrives, WHEN `PacketRouter.Dispatch` routes it, THEN `case 1022` dispatches to a new guard-first `InteractHandler` (structural twin of `NpcHandler`).
- [ ] AC-4.2: GIVEN the 1022 payload, WHEN `InteractHandler` reads it, THEN it BOUNDS-CHECKS first (`if (payload.Length < 24) return;`) before reading attackerUID@6, Target@10, X@14, Y@16, Action@18, Value@22 (active offsets = Redux body offset − 2; CLAUDE.md Rule 7).
- [ ] AC-4.3: GIVEN melee, WHEN Action is read, THEN only Attack(2)/Shoot(25) are handled (plaintext — NO bit-decode, NO `unsafe`; only MagicAttack(21) is encoded and is out of scope); the handler reads with `BinaryPrimitives` over a `ReadOnlySpan<byte>`.
- [ ] AC-4.4: GIVEN a valid Target UID, WHEN it is resolved, THEN it is looked up via `MapInstance.Roster.TryGetValue(target)` and accepted only if it is a `MonsterEntity` in the 400000–499999 band (non-monster / out-of-band → ignored, no crash).
- [ ] AC-4.5: GIVEN attacker Strength and monster Defence, WHEN damage is computed, THEN `damage = max(1, Strength − MonsterEntity.Defence)` (deterministic; floored at 1; no RNG/dodge/level-bonus in v1) and `MonsterEntity.Life = Life > damage ? Life − damage : 0`.
- [ ] AC-4.6: GIVEN damage is applied, WHEN the server echoes, THEN a 1022 is rebuilt (same attacker UID, Target, X, Y, Action, Value=damage) via a new `Interact.Build` (Span/BinaryPrimitives, no `unsafe`) and broadcast to the attacker's screen including self (`Broadcast(includeSelf:true)`), built ONCE then fanned out.

### US-5: Kill a monster, gain XP, and level up (0.3)
**As a** player
**I want** a killed monster to despawn and reward XP toward leveling
**So that** combat has progression and payoff

**Acceptance Criteria:**
- [ ] AC-5.1: GIVEN a monster's Life reaches 0, WHEN it dies, THEN it is despawned via the existing path — `Deregister` + `GeneralData.BuildRemoveEntity(uid)` (Action=132) broadcast to the screen (same two calls as disconnect despawn).
- [ ] AC-5.2: GIVEN `MonsterEntity` currently drops `BonusExp` (MonsterManager.cs:47-50), WHEN it is fixed, THEN `MonsterEntity` gains a `BonusExp` field carried from `DbMonsterType` (Pheasant=100) so the kill can award it.
- [ ] AC-5.3: GIVEN a kill, WHEN XP is awarded, THEN `session.Experience += monster.BonusExp` (flat per-kill from the template), in memory only.
- [ ] AC-5.4: GIVEN XP crosses a level threshold, WHEN leveling runs, THEN a loop subtracts the per-level requirement and increments Level using an IN-CODE level-threshold table (seeded from the backup's early `levexp` rows, e.g. L1=120/L2=180/L3=240) — NO new DB table for v1.
- [ ] AC-5.5: GIVEN a level-up, WHEN it completes, THEN an `UpdatePacket` (Level/Experience, and optionally bumped Strength/MaxLife) is sent to the killer so the client UI updates.

### US-6: Monsters respawn and fight back (0.4)
**As a** player
**I want** killed monsters to respawn and nearby monsters to aggro, chase, and hit me
**So that** the world is alive and PvE is two-sided

**Acceptance Criteria:**
- [ ] AC-6.1: GIVEN the server runs, WHEN it starts, THEN ONE hosted background loop (started in Program.cs beside the listeners, period ~150–250 ms) iterates each `MapInstance`'s monsters; it does NO DB round-trip per tick (CLAUDE.md MMO rule) and has bounded per-map iteration (Rule 2).
- [ ] AC-6.2: GIVEN the tick must not re-filter the whole roster each pass, WHEN monsters are iterated, THEN each `MapInstance` exposes a per-map monster collection populated at register (not a per-tick `Roster` LINQ scan).
- [ ] AC-6.3: GIVEN a monster died, WHEN respawn bookkeeping runs, THEN per spawn-region `(template, box, AmountMax, Frequency)` state tracks the live/dead count and the tick refills up to `AmountMax` after `Frequency` (reusing `spawns` AmountMax/Frequency; `MonsterManager` generalized into a live respawn manager with a 400000-band UID allocator bounded by 500000).
- [ ] AC-6.4: GIVEN a player is within a monster's `ViewRange` (Pheasant=8, tile distance), WHEN the monster ticks, THEN it acquires that player as target and steps toward it via `MapInstance.Move`, broadcasting the monster's move to on-screen players (walk or jump per-step packet — exact packet confirmed at live verify).
- [ ] AC-6.5: GIVEN a monster is adjacent to its target (within AttackRange), WHEN it attacks back, THEN it deals `rand(AttackMin,AttackMax) − playerDefense` (Phase-0 playerDefense=0) to `session.CurrentHp` and broadcasts a 1022 (monster UID as attacker, player as target, Value=damage) to the player's screen.
- [ ] AC-6.6: GIVEN a monster loses its target (distance > ViewRange), WHEN it ticks, THEN it returns to idle (stops chasing) — bounded, no unbounded chase.

### US-7: Player death and respawn (0.4)
**As a** player
**I want** to respawn at the revive point with restored HP when I die
**So that** death is a setback, not a dead end

**Acceptance Criteria:**
- [ ] AC-7.1: GIVEN monster damage drives `session.CurrentHp` to 0, WHEN death is detected, THEN the player is respawned at the map's revive coords (BirthVillage ~60,108) with HP restored (e.g. to max).
- [ ] AC-7.2: GIVEN respawn, WHEN it completes, THEN the player's live position (CurrentX/Y/CurrentMap) and grid registration are updated to the revive point and the client is moved/teleported there (so screen + position stay consistent).
- [ ] AC-7.3: GIVEN Phase-0 scope, WHEN a player dies, THEN there is NO item loss, NO XP loss, and NO PK flag change (death penalties are explicitly out of scope — later epic).

### US-8: 0.1 baseline cleanups carried forward
**As a** developer building 0.3/0.4 on the 0.1 monster baseline
**I want** the research-flagged prerequisite gaps closed
**So that** monsters can take damage, move, and award XP without re-plumbing

**Acceptance Criteria:**
- [ ] AC-8.1: GIVEN `MapInstance.Move` hard-casts `(PlayerEntity)e` (MapInstance.cs:63), WHEN it is generalized, THEN `SetPosition(ushort,ushort)` is promoted to the `IWorldEntity` interface and `Move` calls it polymorphically (no cast, no throw on a monster), keeping `Move`'s grid logic kind-agnostic.
- [ ] AC-8.2: GIVEN the `Move` generalization, WHEN it lands, THEN existing player walk/jump/visibility behavior is unchanged (regression-guarded by the existing tests).
- [ ] AC-8.3: GIVEN `MonsterEntity` already carries `Life/MaxLife/AttackMin/AttackMax/Defence/ViewRange/Level`, WHEN 0.3/0.4 consume them, THEN only the missing `BonusExp` field is added (no other monster-stat plumbing required).

## Functional Requirements

| ID | Requirement | Priority | Acceptance Criteria |
|----|-------------|----------|---------------------|
| FR-1 | Add live mutable `CurrentHp/CurrentMana/Strength/Experience/CombatLoaded` (primitives) to `ClientSession`; seed at 1052 from `DbCharacter`; authoritative live HP = `session.CurrentHp` | High | AC-1.1, AC-1.2, AC-1.5 |
| FR-2 | Flush HP/mana/experience/level once on disconnect via async repo call beside `UpdatePosition`, try/caught | High | AC-1.3, AC-1.4 |
| FR-3 | Seed real non-zero starting attributes (Strength etc.) at character creation in `RegisterHandler` AND the `characters` seed | High | AC-2.1, AC-2.2, AC-2.3 |
| FR-4 | Add `Experience BIGINT NOT NULL DEFAULT 0` to `init.sql` (+ idempotent `ALTER TABLE … ADD COLUMN IF NOT EXISTS`); add `Experience` to `DbCharacter` + repo SELECT/INSERT + `UpdateExperienceLevel` flush | High | AC-3.1, AC-3.2, AC-3.3 |
| FR-5 | Route `case 1022` in `PacketRouter.Dispatch` to a new guard-first `InteractHandler` (twin of `NpcHandler`) | High | AC-4.1 |
| FR-6 | `InteractHandler` bounds-checks `payload.Length < 24` first, reads attackerUID@6/Target@10/X@14/Y@16/Action@18/Value@22 via `BinaryPrimitives` over a span; melee Attack(2)/Shoot(25) only, plaintext (no `unsafe`) | High | AC-4.2, AC-4.3 |
| FR-7 | Resolve Target via `MapInstance.Roster.TryGetValue`; accept only `MonsterEntity` in 400000–499999 band; ignore otherwise | High | AC-4.4 |
| FR-8 | Damage = `max(1, Strength − MonsterEntity.Defence)`; apply to `MonsterEntity.Life` (clamped at 0); deterministic, no RNG in v1 | High | AC-4.5 |
| FR-9 | `Interact.Build` (40-byte 1022, Span/BinaryPrimitives, no `unsafe`); echo 1022 Value=damage built once, `Broadcast(includeSelf:true)` to screen | High | AC-4.6 |
| FR-10 | On death: `Deregister` + `GeneralData.BuildRemoveEntity` (132) to screen | High | AC-5.1 |
| FR-11 | Add `BonusExp` field to `MonsterEntity`, carried from `DbMonsterType` in `MonsterManager` | High | AC-5.2, AC-8.3 |
| FR-12 | Award `session.Experience += monster.BonusExp` on kill (in memory) | High | AC-5.3 |
| FR-13 | In-code level-threshold table; loop-subtract on level-up; send `UpdatePacket(Level/Experience)` | High | AC-5.4, AC-5.5 |
| FR-14 | Generalize `MapInstance.Move`: add `SetPosition(ushort,ushort)` to `IWorldEntity`, replace the `(PlayerEntity)e` cast with a polymorphic call | High | AC-8.1, AC-8.2 |
| FR-15 | One hosted ~150–250 ms background tick (Program.cs) iterating each map's per-map monster collection; no DB per tick; bounded iteration | High | AC-6.1, AC-6.2 |
| FR-16 | Generalize `MonsterManager` into a live respawn manager: per-region `(template, box, AmountMax, Frequency)` count/refill + 400000-band UID allocator (≤500000) | High | AC-6.3 |
| FR-17 | Monster AI: aggro a player within `ViewRange` → chase via `Move` + broadcast move → attack-back into `session.CurrentHp` + 1022 broadcast when in `AttackRange`; return to idle when target out of `ViewRange` | High | AC-6.4, AC-6.5, AC-6.6 |
| FR-18 | Player death (CurrentHp→0) → respawn at map revive coords (~60,108) with HP restored; update live position + grid + move client; no penalties | High | AC-7.1, AC-7.2, AC-7.3 |

## Non-Functional Requirements

| ID | Requirement | Metric | Target |
|----|-------------|--------|--------|
| NFR-1 | Validate ALL untrusted wire input (CLAUDE.md Rule 7) | Guard-first | `InteractHandler` bounds-checks `payload.Length < 24` before any offset read |
| NFR-2 | In-memory authoritative state; async/batched persistence | DB round-trips on hot path | ZERO DB hits per packet AND per AI tick; HP/XP/level flush once on disconnect only |
| NFR-3 | No new `unsafe`; Span + BinaryPrimitives for wire parse/serialize (Rule 9) | `unsafe` blocks added | 0 — melee 1022 is plaintext; magic bit-decode skipped (out of scope) |
| NFR-4 | Small functions (Rule 4), guard clauses (Rule 1/5), smallest scope (Rule 6) | Function length | New handlers/tick methods ~≤60 lines; split as needed |
| NFR-5 | Bounded loops (Rule 2) | Loop bounds | AI tick per-map monster scan bounded; chase ends at ViewRange; UID allocator bounded ≤500000; level-up loop bounded by XP |
| NFR-6 | Strict build stays green (Directory.Build.props) | Warnings/errors | 0 warnings / 0 errors; new code nullable-clean (Nullable=enable, TreatWarningsAsErrors, AnalysisLevel=latest) |
| NFR-7 | Additive — do not regress existing systems | Regressions | Login, movement, visibility/surroundings, chat, NPC behavior unchanged; `Move` generalization regression-guarded |
| NFR-8 | Dockerized build/test (no local SDK) | Command | `scripts/dotnet build\|test src/Conquer.sln` green; never bare `dotnet`; do NOT stage/commit `src/docker-compose.yml` |
| NFR-9 | Send-safety from foreign (AI tick) thread | Concurrency | Reuse existing `ClientSession.SendGame` copy + `lock(_sendLock)`; no new locking needed |
| NFR-10 | Per-fight allocation discipline (Rule 3 managed spirit) | Hot-path allocs | Build combat broadcasts once per event; avoid per-hit `new byte[]`; ArrayPool only if measured |

## Glossary

- **0.1 / 0.2 / 0.3 / 0.4**: combat slices — 0.1 monster spawn+render (DONE), 0.2 live player combat-state, 0.3 melee 1022 loop, 0.4 respawn + AI + death/respawn.
- **1014 (SpawnEntity)**: packet that makes an entity render on a client's screen; monsters spawn via the Monster branch of `EntitySpawn.For`.
- **1022 (MsgInteract)**: the melee/attack packet; client→server is the attack request, server→client echo carries Value=damage. Active payload offsets = Redux body offset − 2 (typeId@0): attackerUID@6, Target@10, X@14, Y@16, Action@18, Value@22.
- **132 (RemoveEntity)**: GeneralData(1010) Action subtype 132; despawns the UID at offset 8 from a client's screen — reused for monster death.
- **InteractAction**: enum on the 1022 — Attack=2, Shoot=25 (melee, plaintext, in scope); MagicAttack=21 (bit-encoded, out of scope).
- **MonsterEntity**: a monster's presence in the shared roster/grid — UID (400000–499999), mutable `Life`/`X`/`Y`, plus `MaxLife/AttackMin/AttackMax/Defence/ViewRange/Level` and (added here) `BonusExp`.
- **BonusExp**: the per-kill XP award from `DbMonsterType` (Pheasant=100); must be added as a `MonsterEntity` field (currently dropped in `MonsterManager`).
- **ViewRange**: tile-distance aggro radius (Pheasant=8); a player inside it becomes the monster's target.
- **AttackRange**: tile distance at which a monster can hit its target (Pheasant=1).
- **Live combat-state**: mutable `CurrentHp/CurrentMana/Strength/Experience` on `ClientSession`, seeded at 1052, flushed on disconnect — mirrors `CurrentX/Y`/`PositionLoaded`.
- **Revive point / BirthVillage**: the map's respawn coords (~60,108) where a dead player reappears with restored HP.
- **Level-threshold table**: an in-code `level → exp-required` table (seeded from the backup's early `levexp` rows) used for level-up — no new DB table in v1.
- **AI tick**: one hosted ~150–250 ms background loop iterating each map's monsters for respawn + aggro/chase/attack-back; pure in-memory, no DB.
- **`MapInstance.Move` generalization**: promoting `SetPosition(ushort,ushort)` to `IWorldEntity` so `Move` no longer hard-casts to `PlayerEntity` and can move monsters.

## Out of Scope (explicit — later epics)

- Equipment/gear/weapon stats (Phase-0 attack = base Strength only; no min/max from gear).
- Skills/magic and the MagicAttack(21) bit-encode/decode path.
- Ranged/bow specifics beyond plain Shoot(25) (no projectile/range mechanics).
- Items, drops, loot (the kill event is the hook a later ground-drops epic consumes).
- PK/PvP and any player-vs-player combat rules.
- Party / team XP sharing.
- Death penalties (XP loss, item drop, PK flag) — death respawns clean in v1.
- Monster skills/special abilities; dodge/accuracy/crit; status effects.
- Pathfinding around obstacles (simple straight-line chase is acceptable).
- Multi-map combat balancing / profession stat-table re-rolls on level-up.
- Attack-range / line-of-sight anti-cheat (v1 trusts the client like movement; bounds-check only).
- Redux `Player`/`PlayerManager`/`CombatManager`/NHibernate — reference only, do not reuse.

## Dependencies

- **0.2 (US-1)** depends on the live `ClientSession` `CurrentX/Y`/`PositionLoaded` seed/flush lifecycle (the pattern it mirrors) and the **Experience column (US-3)**.
- **0.3 (US-4/US-5)** depends on **0.2** (live Strength/HP/Experience), the **Experience column (US-3)**, the **`BonusExp` field (AC-5.2/AC-8.3)**, and the existing despawn path (`Deregister` + 132).
- **0.4 (US-6/US-7)** depends on **0.3** (the 1022 echo + damage/death path it reuses for attack-back) and the **`MapInstance.Move` generalization (US-8/AC-8.1)** — monsters cannot move until the `(PlayerEntity)e` cast is removed.
- **Prerequisites (must land before/with the slice that needs them):** Experience column (init.sql ALTER + DbCharacter + repo); `MapInstance.Move` generalization; `BonusExp` on `MonsterEntity`; in-code level-threshold table.
- **Reuses unchanged:** World grid/`Broadcast`/`Deregister`/132/send-lock (world-surroundings), `IWorldEntity`/`EntitySpawn.For` Monster branch (static-npcs + 0.1).
- **Build/DI:** `PacketRouter` + `Program.cs` manual-DI to wire `InteractHandler` + the AI-tick hosted loop; `scripts/dotnet build|test src/Conquer.sln`.

## Success Criteria

- Operator E2E on the live host (LAN IP `192.168.0.155`, the gitignored docker-compose override; DHCP-volatile — confirm with the operator) using the operator's test account: log in near spawn (61/109) → see a Pheasant → click-attack → see the swing + floating damage → kill it (despawn) → see XP/level update → see it respawn → get aggroed, chased, and hit (HP bar drops) → die and respawn at the revive point with restored HP.
- xUnit suite (new `World.Tests`/`Packets.Tests`) covers the pure math: `Interact.Build` byte layout, `max(1, Strength − Defence)` damage, the XP-add + level-up threshold loop, per-region respawn count/refill, and the generalized monster `Move` position write.
- `scripts/dotnet build src/Conquer.sln` green (0 warnings, strict gate) and `scripts/dotnet test src/Conquer.sln` green.
- No DB round-trip per packet or per AI tick (measured/reviewed); existing login/movement/visibility/chat/NPC behavior unregressed.

## Unresolved Questions

- Does the 5065 client render a server-originated 1022 attack swing + floating damage, and accept the death/respawn sequence? (LIVE CAPTURE during 0.3 E2E — high impact on the echo design.)
- Death sequence the client accepts: is 132 despawn alone sufficient, or is an additional `InteractAction.Kill(14)` needed for the on-screen kill animation? (Ship 132; add Kill if live verify needs it — low risk.)
- Exact "monster move" packet per AI step: 1005 walk vs 1010-133 jump (both builders exist) — confirm which the client renders cleanly for a monster UID. (LIVE CAPTURE, low risk.)
- Level-up stat reward: bump Strength/MaxLife on level-up, or level-only for v1? (Either works; pick a simple felt reward in design — no profession stat-table re-roll.)
- Starting-attribute values: which concrete class-appropriate Strength/base stats to seed at creation (need a single agreed number per class so create + seed match).

## Next Steps

1. After requirements approved, proceed to design phase (`/design`).
2. Design the 0.2 `ClientSession` combat-state surface + the Experience-column schema/repo change + the disconnect flush.
3. Design the 0.3 surface: `InteractHandler` (guard-first), `Interact.Build` byte layout, damage/XP/level functions, death despawn, `MonsterEntity.BonusExp`.
4. Design the 0.4 surface: the `IWorldEntity.SetPosition` generalization, per-map monster collection + respawn manager, the hosted AI tick (aggro/chase/attack-back), and player death/respawn at the revive point.
5. Define the pure-math xUnit surface (Interact.Build, damage, XP/level loop, respawn refill, monster Move).
6. Sequence implementation by slice with prerequisites first: (Experience column + BonusExp + Move generalization) → 0.2 → 0.3 → 0.4.
