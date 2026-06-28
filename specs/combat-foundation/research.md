---
spec: combat-foundation
phase: research
created: 2026-06-28T13:20:00Z
---

# Research: combat-foundation (EPIC 4/5 — PvE combat loop, Phase 0)

## Executive Summary

A working PvE melee loop is **feasible and additive** on top of the live World
layer. 0.1 already spawns monsters as 1014 entities in the same roster/grid players
ride (`MonsterEntity`, UID band 400000–500000, Pheasants verified live). The three
remaining slices each have a clean home: **0.2** adds a live-mutable combat-state
object on `ClientSession` seeded at login and flushed on disconnect — the exact
`CurrentX/Y` pattern (`ClientSession.cs:60-65`). **0.3** routes the plaintext melee
1022 (MsgInteract) in `PacketRouter.Dispatch`, computes a base-stats-only damage,
mutates `MonsterEntity.Life`, echoes a 1022 (Value=damage) to the screen via the
existing `Broadcast`, and on death reuses the existing despawn path (`Deregister` +
`BuildRemoveEntity` 132). **0.4** adds ONE hosted background tick (mirror Redux's
150 ms `MonsterThread`) iterating each `MapInstance`'s monsters for respawn + aggro/
chase/attack-back. The full 1022 wire layout + damage/XP/level formulas are
recoverable from the in-repo Redux original; the load-bearing risks are (a) the
`characters` table has **NO `Experience` column** (must add — confirmed below), (b)
`MapInstance.Move` currently hard-casts to `PlayerEntity` (`MapInstance.cs:63`) so
monster movement needs that generalized, and (c) live-client cooperation: does the
5065 client render a server-originated 1022 attack/damage + accept the death/respawn.

---

## 1. Existing patterns to reuse (the additive surface)

| Pattern | Where | Reuse for |
|---------|-------|-----------|
| **Monster entity in the shared roster/grid** | `World/MonsterEntity.cs` — `IWorldEntity`, mutable `Life`/`X`/`Y`, combat fields (`AttackMin/Max`, `Defence`, `ViewRange`, `Level`, `MaxLife`) already present | 0.3 damage target; 0.4 AI source-of-truth |
| **Spawn render (1014) for monsters** | `EntitySpawn.For` Monster branch (`Packets/EntitySpawn.cs:22`) → `SpawnEntity.Build(uid,mesh,0,level,(int)Life,x,y,name)` | already renders; re-send on HP change is just another 1014/1022 |
| **Visibility / screen send** | `ActionHandler.SyncScreen` (`ActionHandler.cs:113-161`) — iterates `QueryScreen`, VIEW-gate (18 tiles), dedup `Visible` set | combat broadcasts go through `MapInstance.Broadcast(center, packet, includeSelf)` (`MapInstance.cs:132`) — players-only fan-out |
| **Despawn = Deregister + 132** | `NetworkListener.cs:170-188` (disconnect) + `GeneralData.BuildRemoveEntity(uid)` (`GeneralData.cs:81`, Action=132) | 0.3 monster **death** despawn — same two calls |
| **Live-mutable state seeded@login, flushed@disconnect** | `ClientSession.CurrentX/Y/CurrentMap/PositionLoaded` (`ClientSession.cs:60-65`); seeded at 1052 (`MsgConnect.cs`), flushed in `NetworkListener.cs:155-159` (`_characters.UpdatePosition`) | **0.2 combat-state lives here** — same lifecycle |
| **Dapper read-once repos** | `MonsterTypeRepository`/`SpawnRepository`/`NpcRepository`/`CharacterRepository` (all `Conquer.Database`, `factory.Create()` + `conn.Query<T>`) | 0.2 may add an `Experience`/`UpdateExperience` column+method; no new infra |
| **World-injected guard-first handler** | `WalkHandler`/`NpcHandler`/`ChatHandler` — ctor takes `Conquer.World.World`, `Handle(session,payload)`, `if (payload.Length < N) return;`, resolve `session.WorldEntity is PlayerEntity` | 0.3 new `InteractHandler` is a byte-for-byte structural twin |
| **Per-session send-safety** | `ClientSession.SendGame` copies into a fresh buffer + `lock(_sendLock)` before encrypt/write (`ClientSession.cs:103-117`) | combat broadcast from a foreign thread (AI tick) is already safe — no new locking needed |
| **Send-once fan-out** | `Broadcast` hands the SAME `byte[]` to each player; `SendGame` copies before encrypt (AD-3/AD-4) | build the 1022 echo ONCE, fan to screen |

**Key invariant carried in:** monsters/NPCs have **no `ClientSession`**, so
`Broadcast` already skips them (`MapInstance.cs:141` `e is PlayerEntity p`). Combat
packets are therefore only ever *sent to* players; monsters are only ever *targets*.

---

## 2. Packet 1022 (MsgInteract) — protocol findings

**Source of truth:** `src/Redux/Packets/Game/[1022] InteractPacket.cs`,
`src/Redux/Enum/InteractAction.cs`, `MSG_INTERACT` = 1022.

### Body layout (40-byte frame, Redux `byte*` reader `InteractPacket.cs:41-54`)

| Body offset | Active payload offset (−2) | Size | Field |
|-------------|---------------------------|------|-------|
| 0 | — | 2 | length (stripped by PacketRouter.ReadPacket) |
| 2 | 0 | 2 | type = 1022 (`payload[0..1]`) |
| 4 | **2** | 4 | Timestamp (u32) |
| 8 | **6** | 4 | **attacker UID** (u32) |
| 12 | **10** | 4 | **Target** (u32) — the clicked monster UID |
| 16 | **14** | 2 | X (u16) |
| 18 | **16** | 2 | Y (u16) |
| 20 | **18** | 2→int | **Action** (`InteractAction`) — **Attack=2**, Shoot=25, MagicAttack=21 |
| 24 | **22** | 4 | **Value** (u32) — damage on the server→client echo |

> **Active payload offsets = Redux body offset − 2.** `PacketRouter.ReadPacket`
> (`PacketRouter.cs:40-53`) decrypts the whole frame and returns `payload` =
> everything **after the 2-byte length prefix**, so `payload[0..1]` is the typeId
> and every field shifts down by 2 — identical to how `WalkHandler` reads UID @2
> (`WalkHandler.cs:86`) and `NpcHandler` reads UID @2 / Action @10
> (`NpcHandler.cs:37-38`). The `.progress.md` slice-0.3 offsets
> (attackerUID@6, Target@10, X@14, Y@16, Action@18, Value@22) match this table.

> Redux declares `Action` as `int` at body @20 but `InteractAction` underlying type
> is the default `int`; on the wire it occupies a 4-byte slot (next field Value is
> at +4 = body @24). Read it as a **u16 at payload @18** (low 2 bytes; Attack=2 fits)
> or a u32 — both work; the low 16 bits carry the enum. Guard `payload.Length >= 24`
> (need through Value@22..25 for completeness; minimum to read Action is 20).

### Plaintext vs encrypted — CONFIRMED

Only **MagicAttack (21)** is bit-mangled. `InteractPacket.cs:52` only calls
`DecodeMagicAttack` when `Action == MagicAttack`; `EncodeMagicAttack`
(`:72-79`) scrambles MagicType/Target/X/Y/MagicLevel for magic only. **Plain melee
`Attack (2)` and `Shoot (25)` are plaintext** — Target/X/Y/Action are read directly.
Phase 0 is melee-only, so **no bit-decode needed** (skip the whole `unsafe` Redux
encode/decode block — CLAUDE.md Rule 9; read with `BinaryPrimitives`).

### Server reply — broadcast a 1022 with Value=damage

Redux `CombatManager.LaunchAttack` (`CombatManager.cs:764-794`): after
`target.ReceiveDamage(dmg, owner.UID)` it does
`owner.SendToScreen(InteractPacket.Create(owner.UID, target.UID, target.X, target.Y,
InteractAction.Attack, dmg), true)` — i.e. **rebuild a 1022 with the SAME attacker
UID + target + Action, Value=damage, fan to the attacker's screen incl self**. The
client renders the swing + floating damage from this echo. On death Redux additionally
sends `InteractAction.Kill` (`Entity.cs:222`) — but for Phase 0 the despawn (132) +
the damage echo is the minimal correct set; the Kill(14) variant is a live-verify
nicety.

### Builder to add (no `unsafe`)

`src/Packets/Interact.cs` (mirror `Walk.BuildBroadcast`/`GeneralData`): write a
40-byte frame, `PacketBuilder.AppendHeader(span, 40, 1022)` (length = size−8 contract,
`PacketBuilder.cs:21`), then Timestamp@4, UID@8, Target@12, X@16, Y@18, Action@20
(u16/u32), Value@24, all `BinaryPrimitives.WriteUInt*LittleEndian`. Timestamp can be
`Environment.TickCount` (the client tolerates it; Redux uses `Common.Clock`).

### Monster UID band (render/attackability)

`MonsterManager.cs:14-22`: monster UIDs **400000–499999** (mirrors Redux
`Map.MobCounter`). The client treats a 1014-spawned UID in this band as an
**attackable monster** (a UID outside it renders/behaves as a player and is NOT
attackable). NPCs use 1000000+, players use low CharacterIDs — **no collision** in
the shared UID-keyed roster. The 1022 Target the client sends back will be the
monster's 400000-band UID; resolve it via `MapInstance.Roster.TryGetValue(target)`.

---

## 3. Damage + leveling model recommendation (base-stats-only, simplified)

### 3.1 Redux reference (what we are simplifying away from)

`Entity.CalculatePhysicalDamage` (`Entity.cs:101-122`):
`rand(MinDmg,MaxDmg) * BonusAttackPct/100 ... − target.Defense * BonusDefensePct/100`,
floored at 1, with a level-bonus multiplier vs monsters (`GetLevelBonusDamageFactor`,
`Entity.cs:38-47`: +bonus when attacker ≥3 levels above target) and a dodge roll.
For a **player**, `Recalculate` (`Player.cs:627-630`) seeds
`CombatStats.MinimumDamage = MaximumDamage = Strength` and adds equipment on top
(`CombatStatistics.AddItemStats`). Monster combat-stats come from the template:
`CombatStatistics.Create(DbMonstertype)` → `MinDmg=AttackMin, MaxDmg=AttackMax,
Defense=Defence` (`CombatStatistics.cs:12-18`). **Equipment is out of scope** (later
epic), so the player's whole attack reduces to **Strength**.

### 3.2 Phase-0 player→monster melee formula (RECOMMENDED)

```
attack  = player.Strength                     // base-stat only; no weapon/gear yet
damage  = max(1, attack − monster.Defence)    // Pheasant Def=0 → damage = Strength
monster.Life = (Life > damage) ? Life − damage : 0
```

- No RNG range, no dodge, no level-bonus for v1 (deterministic = trivially testable;
  add `rand(min,max)` later once gear introduces a real min/max). Floor at 1 (CLAUDE
  Rule 5 invariant — an attack always does ≥1).
- Pheasant (`monstertype` seed, `init.sql:121`): **Life 33, Atk 5–6, Def 0, Lvl 1,
  Mesh 104, AttackRange 1, ViewRange 8, BonusExp 100.** With Def 0, a player with
  Strength ≥ 33 one-shots; a low-Strength newbie takes several hits — good tuning
  baseline. The `MonsterEntity` already carries `Defence`, `MaxLife`, `Level`,
  `AttackMin/Max` (`MonsterEntity.cs:33-39`).
- **Where Strength comes from:** `DbCharacter.Strength` (init-only,
  `CharacterRepository.cs:18`) → seed into the 0.2 live combat-state at login. Note
  the default seed strength is 0 (`init.sql:42`), so **character creation must give a
  non-zero Strength** for combat to deal >1; otherwise every hit is the floor of 1
  (still kills a 33-HP Pheasant in 33 hits — works, but tune the seed). Flag for
  requirements.

### 3.3 Leveling — minimal Phase-0 approach

Redux `Player.GainExperience` (`Player.cs:326-355`): `Character.Experience += exp`,
loop `while Experience >= LevelExp.GetById(Level).Experience { Experience -= req;
Level++; }`, then `SetLevel` re-rolls stats from a profession/level stat table and
sends `UpdatePacket`. XP per kill = `CalculateExperienceGain` (`Entity.cs:49-75`):
`min(targetLife, dmg)` scaled by level delta — **for the killing blow this is
effectively the monster's remaining Life**. NOTE Redux scales by *damage dealt*, not
a flat `BonusExp`; the `monstertype.BonusExp` column (=100 for Pheasant) is the
designer's intended per-kill award and is the simpler Phase-0 source.

**Phase-0 recommendation:**
```
on monster death (Life hit 0):
    player.Experience += monster.BonusExp        // flat, from monstertype (Pheasant=100)
    while player.Experience >= ExpForLevel(player.Level):
        player.Experience -= ExpForLevel(player.Level)
        player.Level++
        (optionally bump Strength / MaxLife so leveling is felt)
    send UpdatePacket(Level/Experience) so the client UI updates
```

- **`BonusExp` is the Phase-0 XP source** (already loaded into `MonsterEntity`? —
  it is read by `MonsterTypeRepository` (`DbMonsterType.BonusExp`) but is **NOT
  currently a field on `MonsterEntity`** — `MonsterManager.cs:47-50` drops it. **0.3
  must add `BonusExp` to `MonsterEntity`** so the kill can award it.)
- **Level thresholds:** the `levexp` table (id/level/exp/up_lev_time/unknown) exists
  in `src/Nov_16_Backup.sql:12634` (L1 needs 120 exp, L2 180, L3 240, …) but is **NOT
  in `src/init.sql`**. Two options: (a) **import a minimal `levexp` table into
  `init.sql`** (level + exp columns only; drop Redux's up_lev_time/unknown) and a
  `LevelExpRepository` that loads it once at startup into a `Dictionary<int,long>`;
  or (b) **a hard-coded threshold array/formula in code** for the first N levels
  (e.g. the backup's L1–L15 values, or `exp = 100 * level`) to avoid a new table for
  v1. **Recommend (b) — a small in-code threshold table seeded from the backup's
  early `levexp` rows** — fewest moving parts, no schema/DB dependency, and the table
  is tiny. Upgrade to (a) when the curve needs balancing past low levels.

### 3.4 Experience column — MUST ADD (confirmed)

- **`src/init.sql` characters table (`:32-51`) has NO `Experience` column** (verified:
  0 matches for "experience"). `DbCharacter` (`CharacterRepository.cs:6-24`) has NO
  `Experience` field either.
- **`src/Nov_16_Backup.sql:129` characters table HAS** `Experience int(22) NOT NULL
  DEFAULT '0'` — confirms the live schema convention.
- **Action:** add `Experience BIGINT NOT NULL DEFAULT 0` to `init.sql` characters,
  add `Experience` to `DbCharacter` + the `CharacterRepository` SELECT/INSERT, and an
  `UpdateExperienceLevel(charId, exp, level)` flush method (mirror `UpdatePosition`).
  Apply to the existing db volume via the idempotent one-off
  `docker compose -f src/docker-compose.yml exec -T db mysql … conquer < src/init.sql`
  (ALTER won't run via `CREATE TABLE IF NOT EXISTS`; the table already exists, so
  **0.2 also needs an `ALTER TABLE characters ADD COLUMN IF NOT EXISTS Experience …`**
  in init.sql, or a manual ALTER on the running db — flag for requirements).

---

## 4. 0.2 — live mutable player combat-state (where it lives, seed, flush)

**Pattern to mirror exactly:** `CurrentX/Y/CurrentMap/PositionLoaded` on
`ClientSession` (`ClientSession.cs:60-65`) — seeded from `DbCharacter` at 1052,
mutated in-memory, flushed once on disconnect (`NetworkListener.cs:155-159`).

**Recommendation:** add a small mutable combat-state to `ClientSession` (primitives,
so Network needs no new ref — the file already holds `int CurrentMap`, `uint Uid`):

```
public int  CurrentHp     { get; set; }   // live, mutable; seeded from DbCharacter.HealthPoints
public int  CurrentMana   { get; set; }   // seeded from DbCharacter.ManaPoints
public int  Strength      { get; set; }   // seeded from DbCharacter.Strength (derives attack)
public ulong Experience   { get; set; }   // live XP (after the Experience-column add)
public bool  CombatLoaded { get; set; }   // mirror PositionLoaded
```

- **Seed** alongside the position seed at 1052 (`MsgConnect.Handle`) where
  `session.Character` is first set — copy HealthPoints/ManaPoints/Strength/Experience
  into the live fields, set `CombatLoaded=true`. (Derived attack/defense for Phase 0 =
  just `Strength`; no need to precompute a CombatStatistics struct yet.)
- **Mirror to the World entity:** `PlayerEntity` is currently constructed with a
  one-shot `Hp` snapshot (`ActionHandler.cs:82-83`, `PlayerEntity.Hp` is get-only).
  For combat the **authoritative live HP should be the session field** (single-writer:
  the owning loop / the AI tick that damages it). `PlayerEntity.Hp` can stay the spawn
  snapshot; if monster→player damage (0.4) needs to mutate it, either make
  `PlayerEntity.Hp` settable or have the AI tick mutate `session.CurrentHp` and re-send
  the player's status. **Recommend: session.CurrentHp is authoritative; PlayerEntity
  keeps the spawn snapshot** (avoids a second source of truth). Flag the
  `PlayerEntity.Hp` mutability question for design.
- **Flush** in the same `NetworkListener` `finally` as the position flush
  (`:155-159`): extend `UpdatePosition` → an `UpdatePosition + UpdateCombat` (Hp/Mana/
  Experience/Level), or a sibling repo call. Same once-per-disconnect, async/batched
  spirit — **NEVER per-hit DB writes** (CLAUDE.md MMO rule; HP changes hundreds of
  times a fight, persistence is disconnect-only for v1).

---

## 5. 0.4 — monster respawn + AI tick design

### 5.1 Redux reference

`MonsterThread` (`Threading/MonsterThread.cs`): a **single background thread, 150 ms**
period, iterates `MapManager.ActiveMaps` → each map's spawns → `SpawnManager_Timer()`
(respawn) and `PetManager_Tick()`. `Monster.Monster_Timer` (`Objects/Monster.cs:276+`)
is a 3-state FSM: **Idle** (scan `ViewRange` for a valid target → `Walk`; else random
wander every `MoveSpeed*4`), **Walk** (step toward target; if `dist > ViewRange` →
Idle; if in range → Attack), **Attack** (`if dist > ViewRange → Idle`; else
`CombatEngine.ProcessInteractionPacket(InteractPacket.Create(UID, TargetID, x, y,
Attack, 0))` — i.e. the monster attacks via the SAME 1022 path). Aggro gate uses
`ViewRange` (Pheasant = 8) + `IsValidTarget`.

### 5.2 Recommended Phase-0 tick (honoring the in-memory rule)

- **One hosted background loop** (a `BackgroundService`/`Task` started in
  `Program.cs` beside the listeners), period ~150–250 ms, iterating
  `world` → each `MapInstance` → its monster entities. Pure in-memory; **no DB per
  tick** (CLAUDE.md). Bound the per-map monster scan (Rule 2) — it's already O(monsters
  on map); fine at POC scale.
- **Iterate monsters:** `MapInstance` has no public "monsters" enumerator yet —
  `Roster` is all kinds. Add a cheap filter (`Roster.Values.Where(e => e is
  MonsterEntity)`), or a dedicated monster list per map populated at register, or scan
  the grid. **Recommend a per-map monster collection** (registered alongside the
  roster at spawn) so the tick doesn't re-filter the whole roster every 150 ms.
- **Respawn:** track per-spawn-region `(template, box, AmountMax, deadCount,
  nextRespawnTick)`; when a monster dies it decrements the region's live count; the
  tick refills up to `AmountMax` after `Frequency` ticks (the `spawns` row already has
  `AmountPer`/`AmountMax`/`Frequency`, `SpawnRepository`/`DbSpawn`). `MonsterManager`
  currently spawns once at startup with no region bookkeeping — **0.4 generalizes
  `MonsterManager` into a live respawn manager** holding the regions + a UID allocator
  (reuse the 400000-band counter; recycle dead UIDs or keep allocating within the
  band — bound it, `MonsterUidCeiling=500_000`).
- **Aggro/chase/attack-back:** per monster, if a player is within `ViewRange` (tile
  distance, like `SyncScreen`'s VIEW gate), set target → step toward it (uses
  `MapInstance.Move`, see §6) → when adjacent, deal `rand(AttackMin,AttackMax) −`
  (player defense; Phase-0 = 0) to `session.CurrentHp`, broadcast a 1022 (monster UID
  as attacker, player as target, Value=damage) to the player's screen. Player death
  (CurrentHp→0) is **OUT of Phase-0 scope** unless the brief wants it — recommend
  v1 = monster CAN damage the player's HP bar (visible feedback) but player
  death/revive is deferred. Flag for requirements.
- **Movement broadcast:** when a monster steps, the screen must see it move. Players
  move via 1005 walk; a monster step needs an equivalent — Redux re-uses the walk/jump
  broadcast. For v1, **re-send the monster's spawn (1014) or a walk/jump packet** on
  each step to on-screen players (the existing `Broadcast`). Simplest correct: a
  1010-133 jump (`GeneralData.BuildJump`) per step, or a `Walk.BuildBroadcast` with the
  step direction. Flag the exact "monster move" packet for live verify (low risk — both
  exist).

---

## 6. `MapInstance.Move` generalization (the one required refactor)

`MapInstance.Move` (`MapInstance.cs:56-114`) hard-casts the moved entity to
`PlayerEntity`:

```csharp
((PlayerEntity)e).SetPosition(newX, newY);   // line 63 — comment: "Move is player-only"
```

`MonsterEntity` has its own `internal void SetPosition` (`MonsterEntity.cs:65-69`) but
**`Move` will throw `InvalidCastException` if handed a monster**. For 0.4 monster
movement, generalize the position write:

- **Option A (minimal):** branch on type —
  `if (e is PlayerEntity p) p.SetPosition(...); else if (e is MonsterEntity m)
  m.SetPosition(...);`. Quick; the cast comment becomes stale.
- **Option B (clean):** add `void SetPosition(ushort x, ushort y)` to the
  `IWorldEntity` interface (both entities already implement it `internal`; widen to a
  public interface method) so `Move` calls `e.SetPosition(...)` polymorphically — no
  cast, no branch. **Recommend B** — it matches the existing `CellX/CellY` settable
  pattern on the interface (`IWorldEntity.cs:35-38`) and keeps `Move` kind-agnostic.
  `SetPosition` is currently `internal` (same assembly = World), so it can be promoted
  to an interface member without leaking to Packets beyond the interface.

`Move`'s grid logic (cell-cross diff, atomic TryRemove/TryAdd, ScreenDiff) is already
kind-agnostic — only the position-write cast needs the fix. NPCs still never Move.

---

## 7. Open questions / risks

| Item | Source | Confidence |
|------|--------|-----------|
| 1022 body layout + offsets (UID@8, Target@12, X@16, Y@18, Action@20, Value@24) | Redux `[1022] InteractPacket.cs:41-54` | **High — from original** |
| Active payload offsets = body −2 (typeId@0) | `PacketRouter.ReadPacket` + `WalkHandler`/`NpcHandler` precedent | **High — from code** |
| Melee Attack(2)/Shoot(25) are PLAINTEXT; only MagicAttack(21) bit-encoded | `InteractPacket.cs:52,28` | **High — from original** |
| Server reply = rebuild 1022 (Value=dmg) to screen | `CombatManager.LaunchAttack:764-794` | **High — from original** |
| Monster UID band 400000–500000 = client-attackable | `MonsterManager.cs:14-22` | **High — from code; live-verified (0.1)** |
| **`characters` table missing `Experience` column** | `init.sql:32-51` (0 matches) vs `Nov_16_Backup.sql:129` | **High — MUST ADD (init.sql ALTER + DbCharacter + repo)** |
| **`MapInstance.Move` casts to `PlayerEntity`** | `MapInstance.cs:63` | **High — MUST generalize for 0.4** |
| `MonsterEntity` drops `BonusExp` (needed for XP award) | `MonsterManager.cs:47-50`, `MonsterEntity.cs` has no BonusExp field | **High — add BonusExp field for 0.3** |
| No `levexp` table in init.sql (only in backup) | `init.sql` (0 levexp) vs `Nov_16_Backup.sql:12634` | **High — use in-code threshold table or import** |
| Default seed Strength = 0 → every hit = floor 1 | `init.sql:42`, char-create | **Med — tune the stat seed for felt combat** |
| **Does the 5065 client render a server-originated 1022 attack + floating damage?** | client behavior | **LIVE CAPTURE** |
| **Death sequence the client accepts** (132 alone vs Kill(14)+132) | `Entity.cs:222` Kill vs despawn | **LIVE CAPTURE (low risk; ship 132, add Kill if needed)** |
| **Exact "monster move" packet** (1005 walk vs 1010-133 jump per step) for 0.4 | both builders exist | **LIVE CAPTURE (low risk)** |
| Player death/revive | out of brief | **DEFERRED — confirm scope** |
| Attack-range / line-of-sight validation (anti-cheat) | trust-client like movement | **DEFERRED (v1 trusts the client; bound-check only)** |

---

## 8. Feasibility verdict per slice

| Slice | Verdict | Effort | Notes |
|-------|---------|--------|-------|
| **0.1 monsters spawn+render** | **DONE** (da268f7) | — | Pheasants render live; UID band verified |
| **0.2 live player combat-state** | **High** | S | Add fields to `ClientSession` (primitives), seed@1052, flush@disconnect; add `Experience` column + `DbCharacter`/repo. Pure pattern-copy of `CurrentX/Y`. |
| **0.3 melee 1022 loop** | **High** | M | New `InteractHandler` (guard-first twin of `NpcHandler`) + `Interact.Build` (no `unsafe`) + route `case 1022` in `PacketRouter` + simplified `max(1, Str−Def)` damage + mutate `MonsterEntity.Life` + 1022 echo via `Broadcast` + death = `Deregister`+`132` + award `BonusExp` + level-up. All sub-pieces have an in-repo precedent. |
| **0.4 respawn + AI tick** | **High (most new code)** | M–L | One hosted 150 ms loop; generalize `MapInstance.Move` (§6); per-map monster list + per-region respawn bookkeeping; aggro/chase/attack-back mutating `session.CurrentHp`; monster-move broadcast. No DB on the tick (CLAUDE.md). |

**Overall: all three remaining slices are feasible and additive.** The only schema
change is the `Experience` column; the only required refactor is the `Move` cast; the
only true unknowns are live-client visual fidelity (does the 1022 echo render the swing/
damage, does the client accept the death/respawn) — answerable in one live session
during 0.3 E2E.

---

## Out of scope (explicit)

Equipment/weapon damage (later epic — Phase 0 attack = base Strength only), archer/bow
mechanics, magic/skills (the MagicAttack bit-encode path), dodge/accuracy/crit, status
effects, PK/team/guild combat rules, drops/loot (EPIC 7 — `Monster.GenerateDrops` is
reference only), player death/revive, attack-range & line-of-sight anti-cheat, profession
stat-table re-rolls on level-up (`Player.SetLevel` stat table — Phase 0 may just bump
Strength/MaxLife or nothing). Redux `Player`/`PlayerManager`/`CombatManager`/NHibernate
are **reference only** — do not reuse wholesale.

---

## Quality Commands

Dockerized — no local SDK (CLAUDE.md). No package.json/Makefile/CI in repo root.

| Type | Command | Source |
|------|---------|--------|
| Build | `scripts/dotnet build src/Conquer.sln` | CLAUDE.md |
| Test | `scripts/dotnet test src/Conquer.sln` | CLAUDE.md |
| Build (patcher) | `scripts/dotnet build src/ClientPatcher.sln` | CLAUDE.md |

**Local gate:** `scripts/dotnet build src/Conquer.sln && scripts/dotnet test
src/Conquer.sln`. Strict (`Nullable=enable`, `TreatWarningsAsErrors`,
`AnalysisLevel=latest` — `src/Directory.Build.props`); new code must be nullable-clean.

**Unit-testable (pure, no socket/DB):** `Interact.Build` byte layout; the
`max(1, Strength − Defence)` damage fn; the XP-add + level-up threshold loop; the
`MapInstance.Move` monster-position write (post-generalization); per-region respawn
count/refill math. Mirror `Packets.Tests`/`World.Tests`.

## Verification Tooling

| Tool | Command | Detected From |
|------|---------|---------------|
| Build/Test | `scripts/dotnet build\|test src/Conquer.sln` | CLAUDE.md |
| Compose (app+db) | `docker compose -f src/docker-compose.yml -f src/docker-compose.override.yml up` | `src/docker-compose*.yml` |
| DB schema apply | `docker compose -f src/docker-compose.yml exec -T db mysql … conquer < src/init.sql` | §3.4 |

**Project type:** TCP game server (.NET 8) — no HTTP/health endpoint, no browser.
**Verification strategy:** xUnit for the pure damage/XP/level/respawn/move math +
`Interact.Build` byte layout; **operator-manual E2E** = a real 5065 client on
the live host (LAN IP `192.168.0.155`, gitignored compose override; DHCP-volatile)
using the operator's test account: log in near spawn (61/109) → see a
Pheasant → click-attack it → see the swing + floating damage → kill it (despawn) →
see XP/level update → (0.4) see it respawn and aggro/attack back. Do NOT stage/commit
`src/docker-compose.yml` (CLAUDE.md — carries a local `GameServer__Ip` override).

---

## Related Specs

Sibling specs in `specs/`: `world-surroundings` (EPIC 1 — built the World/grid/
broadcast/send-safety this spec consumes unchanged), `static-npcs` (EPIC 3 — added the
`IWorldEntity` generalization + `EntitySpawn.For` branch this spec extends with the
Monster branch, already present), plus `screen-chat`. ROADMAP places this at
**EPIC 4 (monster-spawns, partly done as 0.1) → EPIC 5 (combat-core)** on the critical
path `surroundings → monster-spawns → combat-core → ground-drops` (ROADMAP §EPIC-5
`:226-238`).

| Spec | Relationship | mayNeedUpdate |
|------|-------------|---------------|
| `world-surroundings` | **High** — combat reuses grid/`Broadcast`/`Deregister`/132/send-lock | No (consumed unchanged) |
| `static-npcs` | **High** — extends `IWorldEntity`/`EntitySpawn.For`; §6 widens `IWorldEntity` with `SetPosition` | **Low-yes** — interface gains a member (additive; NpcEntity already implements internally) |
| `screen-chat` | Low | No |
| `ground-drops` (future) | Medium — depends on this spec's kill event | n/a (not yet a spec) |

---

## Recommendations for Requirements

1. **0.2:** add live `CurrentHp/CurrentMana/Strength/Experience/CombatLoaded` to
   `ClientSession` (primitives), seed from `DbCharacter` at 1052 alongside the position
   seed, flush once on disconnect alongside `UpdatePosition`. `session.CurrentHp` is the
   authoritative live HP; `PlayerEntity.Hp` stays the spawn snapshot.
2. **Schema:** add `Experience BIGINT NOT NULL DEFAULT 0` to `characters` (init.sql
   `ALTER TABLE … ADD COLUMN IF NOT EXISTS` + apply to the running volume), add
   `Experience` to `DbCharacter` + `CharacterRepository` SELECT/INSERT + an
   `UpdateExperienceLevel` flush method. Add a `BonusExp` field to `MonsterEntity`
   (carry it from `DbMonsterType` in `MonsterManager`).
3. **0.3:** route `case 1022` in `PacketRouter`; new `InteractHandler` (guard-first,
   `payload.Length >= 24`, melee Attack(2)/Shoot(25) only, plaintext — no bit-decode);
   `Interact.Build` (Span/BinaryPrimitives, no `unsafe`); damage = `max(1, Strength −
   MonsterEntity.Defence)`; mutate `MonsterEntity.Life`; echo a 1022 (Value=damage) via
   `Broadcast(includeSelf:true)`; on death `Deregister` + `BuildRemoveEntity(132)` to
   the screen + award `BonusExp` + level-up loop + `UpdatePacket` to the killer.
4. **Leveling:** in-code threshold table seeded from the backup's early `levexp` rows
   (L1=120, L2=180, L3=240, …) or `exp = 100*level`; loop-subtract on level-up. No new
   table for v1.
5. **§6 refactor:** add `SetPosition(ushort,ushort)` to `IWorldEntity` and replace the
   `(PlayerEntity)e` cast in `MapInstance.Move` with a polymorphic call (keeps `Move`
   kind-agnostic for monster movement). Regression-guard existing player walk/jump tests.
6. **0.4:** one hosted ~150 ms background loop in `Program.cs` iterating each
   `MapInstance`'s monsters; per-map monster list + per-region respawn bookkeeping
   (reuse `spawns` AmountMax/Frequency); aggro within `ViewRange` → chase via `Move` →
   attack-back mutating `session.CurrentHp` + 1022 broadcast. NO DB on the tick. Player
   death/revive deferred (confirm scope).
7. Tests: xUnit for `Interact.Build`, the damage fn, the XP/level loop, the respawn
   refill math, and the generalized monster `Move`. Operator E2E on `192.168.0.155`.
   New code nullable-clean.

## Sources

- `src/World/{MonsterEntity,MapInstance,IWorldEntity,PlayerEntity,World,Grid}.cs`
- `src/Packets/{EntitySpawn,ActionHandler,WalkHandler,NpcHandler,SpawnEntity,GeneralData,PacketBuilder}.cs`
- `src/Network/ClientSession.cs` · `src/Redux/PacketRouter.cs` · `src/Redux/NetworkListener.cs:150-194` · `src/Redux/Program.cs:35-56` · `src/Redux/MonsterManager.cs`
- `src/Database/{CharacterRepository,MonsterTypeRepository,SpawnRepository}.cs`
- `src/Redux/Packets/Game/[1022] InteractPacket.cs` · `src/Redux/Enum/InteractAction.cs`
- `src/Redux/Managers/CombatManager.cs` (LaunchAttack/ProcessAttack/ReceiveDamage path)
- `src/Redux/Objects/Entity.cs:38-122,154-225` (CalculatePhysicalDamage/ExperienceGain/ReceiveDamage/Kill)
- `src/Redux/Objects/Player.cs:294-355,624-666` (GainExperience/Recalculate/Strength)
- `src/Redux/Structures/CombatStatistics.cs:12-18,629` (monster stat seed; player attack=Strength)
- `src/Redux/Threading/MonsterThread.cs` · `src/Redux/Objects/Monster.cs:276-371` (150ms tick + FSM)
- `src/init.sql:32-160` (no Experience; monstertype/spawns seed; Pheasant stats) · `src/Nov_16_Backup.sql:119-129,12634-12649` (Experience column; levexp table)
- `specs/combat-foundation/.progress.md` · `specs/ROADMAP.md` §EPIC-4/5 · `CLAUDE.md`
