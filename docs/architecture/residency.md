# Residency (K1, MIG9)

Status: **concept written; slices S2 and S3 implemented** (the sector model, ARC-DEV-102, 2026-11-05: pure rules with no caller, §3.3; the
residents of the open map and the sector index, ARC-DEV-103, 2026-11-05, [§3.2](#32-residents-the-mobile-objects-of-the-open-map-slice-s3)); the
rest is not implemented. The concept itself was slice S1 (ARC-DEV-101, docs-only, no checks run). Concept document K1 of the [architecture audit](../research/architecture-audit-plan.md) and design of MIG9
([target model](target-model.md#migration-order), element TM5, decision GQ2 chosen A). Behaviour of the original stays the rows
RL1–RL31 of the [original object model](original-object-model.md) (read from the executable); this page fixes how the session
represents them, in which assembly each part lives, the order of the code slices and what each one must show.

Requirements (reference only, no coverage row changes): [FR-WORLD-003](../requirements/world.md#fr-world-003),
[FR-WORLD-004](../requirements/world.md#fr-world-004), [FR-RUNTIME-001](../requirements/world-travel-runtime.md#fr-runtime-001),
and the AI routine row [FR-AI-001](../requirements/story-social-ai.md#fr-ai-001). Each code slice updates the coverage rows it changes in its own PR.

## 1. The problem in one paragraph

Today "alive" means "inside the snapshot the viewer last built": one whole small map, or a window of sectors around the hero on the
main map. A window move, a map change and a quick load build a new snapshot, and with it a new `AreaAiHost`
(`TerrainViewerAi.AreaAiHostNow` rebuilds on the stamp session, map, loaded area, followers, companions), so every NPC outside the window does
not exist for the rules, and every NPC inside it gets one delayed first tick as if it had just been created (GR8–GR10, GR13; bugs B06, B08,
B10, B25 of the [smoke plan](../research/open-bugs-migration-smoke-plan.md)). The session knows only the actor's map and, since MIG7c, where
each NPC was last left (`npc.pos`); it has no set of "the objects of the open map" and no notion of a loaded sector. `AreaAiHost.SectorLoaded`
returns true and `CatchUp` is a stub.

## 2. What the original does, in the rows MIG9 follows

| Row | Fact the design keeps |
| --- | --- |
| RL1 | Static objects (wall, portal, scenery, trap with OF_DYNAMIC clear) exist only inside a loaded sector; every other object belongs to the open map from map open to close. |
| RL2, RL7 | Cache of **16 slots**, lock bumps an age stamp, a miss on a full cache saves and frees the unreferenced slot of least age. Nothing but lock pressure evicts in play on one map; map change, a teleport spell and a world-map landing empty it. |
| RL3, RL9 | A mobile object is *listed* on its tile only while its sector is loaded; a tile query with a sector type loads the sector, a query with mobile types only does not (it walks the find index, so it sees an absent sector's mobiles). |
| RL8, RL17 | The fold (sector load, fold flag when no history record or at least 3 000 ms of game time since eviction): lists the sector's mobiles and gives each NPC that is not destroyed one immediate AI event with the fold flag, replacing its pending ones; with the flag a leaderless NPC outside combat is put on its waypoint or standpoint for the hour. |
| RL11 | What loads a sector: the 3 × 3 arrival pre-cache, relocation (the wading test), a tile or rectangle query with a sector type, the view rectangle drawn, saved-reference readers. |
| RL16 | Heartbeat: near branch within 30 tiles of the hero (point 19, the pass, next run `250 + 4750 × min(d, 30) / 30` ms), far branch otherwise (animations stopped, follower catch-up onto the leader's tile), rescheduled after the far branch **only while the NPC's own sector is loaded**. Four chain starts only: creation, resurrection, fold first tick, the handler's own reschedule. Map open starts none. |
| RL19 | Day-night scenery and lights follow the fold and the hourly event, for loaded sectors only. |
| RL20 | Nothing replays time away. History is twice the cache (32), saved by a game save, read by a load, cleared by a new game and by any other map close. |

## 3. Design

### 3.1 The open map in the session

The session already records the hero's map (`_actor.Map`), and every `AiTick` carries the event scope `ActiveMapOnly`. MIG9 adds no new saved
"open map" value: the open map **is** the hero's map, and the snapshot already holds the current map and hero id (TM8). What changes is that
the session owns a derived **residency state of the open map**, rebuilt when a map opens (a change of place that lands on another map, a
restore) and dropped when it closes. Other maps exist only as their saved section (GI2), as today.

### 3.2 Residents: the mobile objects of the open map (slice S3)

At map open every mobile object of the map (RL1: everything that is not *static*, that is every NPC, PC, container, item and projectile, and every wall, portal, scenery or trap with OF_DYNAMIC set, such as run-time blood pools and trap markers) is a **resident**: a
directory entry (TM1) built from the map's base row (read through a record source like the [resolver](../world/record-resolver.md)'s
`IMapRecordSource`, which lives in `Arcanum.World`, so **World reads the rows and pushes them into the session**, which cannot reference World), the object-state fields of MIG7a and MIG7c (`follow.pc`, flags, `npc.pos`), the destroyed ids and the map's created objects.
The residents of the open map are always there, loaded sector or not (RL1, RL4: the find index keeps every located object).

- **Place.** An NPC's place is its entry's map and cell (`npc.pos` when recorded, else the base row's cell). The **sector** of a cell is
  `(x >> 6, y >> 6)`; a **sector index** answers "the residents in sector S" and "in this rectangle" for any sector, loaded or not (the original's
  mobile-types-only query, RL9). Both are derived data, rebuilt at open and kept in step by the one relocation path (MIG11 makes it the only one).
- **What is not a resident.** Statics belong to sectors: their base rows come from the window snapshot the viewer builds for drawing, and
  their changes are the MIG7b records keyed by positional identity. They are not read by the residency model; a sector's statics being "loaded"
  matters only to drawing and to the wading test, both of which stay presentation or tile questions.
- **Cost.** The whole main map's mobile rows are read once at open. Their count is not measured yet; S3 starts with a census and a limits object
  (`ResidencyLimits`: residents per map, default well above the census) and a typed `LimitExceeded` result, never a partial roster
  (as the resolver's rule 4: a map over the limit is never partly held). Items held in an inventory (OF_INVENTORY) are residents of their holder, not of a tile: the sector index skips them (RL8–RL10 skip their stale locations).
- **Save.** Nothing new: residents are derived from base rows and saved differences, which MIG7 already saves.

**As built (S3, ARC-DEV-103).** `GameSessionOwner.OpenResidency(map, rows, limits)` holds the base rows (`SessionResidentRow`: entity, the
original's type, a cell or a holder, the data's OF_DYNAMIC, OF_OFF and OF_DESTROYED) of the hero's map only and refuses, keeping the previous
residency, a map that is not the hero's, more rows than `ResidencyLimits` (default 16,384; the main map's census is 7,399) and a row it cannot
hold (a static, a PC row, a counter's or a `sector:` entity, a duplicate, a held object that is no item, a holder that is no container or NPC
row). `QueryResidents`, `QueryResidentsIn(map, ResidentSector)` and `QueryResidentsInCells(map, min, max)` apply the session's changes at query
time, in the object directory's order of stores: a deleted item is gone; the hero's pack is held by the hero (`pc`, itself a resident); a placed
item lies on its cell (or in its junk pile) on its own map only; an item in a holder's list is held by it; then the base row, with an NPC on its
companion cell (`npc.cell`) or else its walked place (`npc.pos`; a party member's walked place is ignored, its place is its companion cell or its
T0 cell), an NPC whose `npc.map` names another map away with every item it holds, a
critical's drop and a spilled container's items loose, and a pile's items in the pile. Visitors (a companion cell recorded on the open map for an
entity no row lists, and a map object of another map the hero carries, placed here or gave here: origin `Visitor`), junk piles and undecayed
blood pools join them. Off and destroyed residents stay (`Excluded` is the game's mask). The overlay is computed from the changes at every query
(not cached, so a query inside an uncommitted mutation sees it), so its cost follows what the session changed, not the map's size; the residency is derived (no revision,
not saved), closed by the hero's map change and absent after a restore. `Arcanum.World.MapResidentRows` builds the rows from a loaded map's
snapshot and inventory seed (mobile-file rows only, holders from the seed, flags from field slot 18) and refuses a window (`WindowOnly`). Over the
installed game (opt-in `MapResidentRowsRealDataTests`), 74 of the 80 whole maps open (4,911 rows, at most 591 on one map, 2,205 held, 3
detached, no static rows in a mobile file); the other 6 have no inventory seed, because the inventory reader refuses them, and so no rows.

### 3.3 The sector model (slice S2, pure rules, no wiring)

A Godot-free immutable state machine in `Arcanum.Simulation` (so `Arcanum.Session` can hold its state value and snapshot it, and
`Arcanum.World` can drive it without a cycle): `SectorCacheState` and `SectorCacheRules`.

- **State.** `Slots` (sector id, age stamp), the age counter, the eviction **history** (sector id, game time of eviction; bounded to twice the
  slot count) and `SectorCacheLimits` (slots, default **16**, accepted range 8–128, the range the original's init clamps to: a value outside it is refused by the limits object, not clamped; history = 2 × slots).
- **`Lock(state, sector, nowMs)`** returns the new state and an outcome: `Hit` (age refreshed), `Loaded` (miss, with the evicted sector if the
  cache was full and the **fold plan**: `Fold` = no history record for the sector, an eviction on another game day, or `nowMs − evictedAt >= 3000`; see the S2 read below), or a typed refusal
  (`NoMapOpen`, `OutOfBounds`, `NoFreeSlot`; `InvalidTime` for a negative time). `LockAll` locks a list of sectors as one call that holds them until it returns (the draw's view sectors). The original also refuses re-entry and a walk over the cache; the session is single-threaded
  and a call completes before the next begins, so neither exists here (a deliberate difference, §4).
- **Locks do not outlive a call.** The original's lock count matters only while a query holds its answer (the draw holds the view sectors
  for the draw). The model touches (age bump, load on miss) and releases inside the same call, so "every slot referenced" cannot
  occur and there is no `NoFreeSlot` in practice; the refusal stays for limits objects with fewer slots than one call touches.
- **`EvictUnlocked`** (the passes that empty the cache: map change, teleport spell, world-map landing) returns the emptied state with
  every evicted sector in the history; **map close** then clears the history (the flush evicts first, then the close clears, RL25, RL26; a load of a game keeps what it restores, RL20).
- **`Open(state, extent)` and `Close(state, nowMs, reason)`** frame a map: `Open` refuses a state with a map open (`MapAlreadyOpen`) or an
  extent outside 1..2^26 sectors per axis (`InvalidExtent`) and keeps the history; `Close` evicts every slot into the history and clears it
  unless `reason` is `GameLoad`; a close with no map open does nothing (the original's closed flag).
- **Who touches** (the rules' own reads, RL11), as session methods the callers use: `TouchSectors(map, cells)` for the 3 × 3 arrival pre-cache
  at the hero's landing, for the view rectangle the presentation reports each frame (an age bump, or a load and fold of a sector it newly meets), for a relocation of a PC or NPC (the
  wading test: the sector of the new cell) and for a tile or sector query that reads a sector's data. Queries over mobile types only
  (the sector index) touch nothing.
- **No save hook.** The original saves a dirty sector before freeing it. Every difference of a static or mobile object already lives in
  the session's state (MIG7), so an eviction saves nothing and loses nothing.

#### The S2 read of the executable

ARC-DEV-102 read the lock (0x4D0540, which also computes the time away, 0x45A7D0), the slot free (0x4D1400), the two emptying passes
(0x4D0BC0, 0x4D0B40), the sector close hook (0x4CF320) and the fold compare of the load step (0x45A9B0 at 0x4D19E3 and 0x4D2274) before
writing the rules ([ledger CX1–CX6](../research/ledger/world.md#sector-cache-arc-dev-102),
[code](../../src/Arcanum.Simulation/Residency/SectorCacheRules.cs), [tests](../../tests/Arcanum.Simulation.Tests/Residency/SectorCacheRulesTests.cs)).
It settles what the rows above leave open:

- **History record.** A free appends `(sector, game time)` at the end of the history; when the history holds twice the slot count, the
  oldest record is dropped first. A load takes the sector's **first** (oldest) record **out** of the history and decides the fold from it,
  so a loaded sector has no record. A miss on a full cache frees its victim (appending the victim's record) **before** it looks up the new
  sector, so with a full history the new sector's own record can be the one dropped, and it folds as "no record".
- **Fold compare.** Game time is the pair {day, millisecond of the day}; the lock subtracts the record's pair from the clock word by word
  when it takes the record, and the load step compares the difference with {0, 3000}. The sector folds when the day words differ, or on the same day when the millisecond words differ by 3000 or
  more. A return across midnight therefore folds however short the absence (`SectorCacheRules.FoldDue`). This is followed as is; whether
  it is a rule or a bug of the game is classified in S4, where the fold becomes visible (§4).
- **Victim and order.** The victim is the unreferenced slot of least age, the first in sector-id order on a tie; the slots are kept in
  ascending id, and both passes evict in that order, so the history receives a pass's sectors in ascending id. The map-close evicts every
  slot into the history, then clears the history unless a game is being loaded.
- **Counter.** The lock counter is bumped after the map and bounds checks, also for a lock that then finds no free slot; the model bumps
  it only when it stamps an age, which keeps every age comparison the same.
- **For S6.** The state is built only by `SectorCacheState.Initial` and the rules; restoring a saved history needs a validating restore
  (slot count, history bound and order, typed refusals) and the save step's records for loaded sectors (RL20), both S6's.

### 3.4 The fold (slice S4)

When `Lock` returns `Loaded`, the session folds the sector, in the original's order (RL8):

1. **List** the sector's residents from the sector index (the "listed" state matters to the tile queries of §3.3 and to the presentation; the
   roster itself never changes).
2. **First tick** for every resident NPC that is not destroyed (dead NPCs too, RL8): `ScheduleAiTick` due 1 ms later, replacing the NPC's pending tick. The existing `WakeUp` start always carries the fold marker, and the tick rules run the waypoint, standpoint and point-10 steps on that marker, while the original sets the flag only when the sector had no history record or was away 3 s or more; S4 therefore adds a third start (immediate, no marker) or a flag on the request, and uses the marked one only when the plan says `Fold`. `ScheduleAiTick` refuses inside an event pass (`InvalidEventPass`), so a fold that a tick's relocation or a timer's tile read causes inside a pass queues its first ticks and schedules them when the pass ends. This replaces the host's delayed first tick for every NPC of the map.
3. **Waypoint or standpoint**, when the plan says `Fold`: the leaderless NPC outside combat is put on its day or night waypoint (RL17: a
   relocation when its own map is the open one, else a request that MIG11 owns), the standpoint step only when the waypoint step does
   nothing, then script point 10. The tick rules (`AiSchedulerRules.Tick`) already model the waypoint and standpoint steps through
   world callbacks; the host's `RunWaypoints` (`AreaAiHost`) is a stub that returns false today, so S4 first measures what waypoint and standpoint data the object rows carry and, if reading them is more than a small step, cuts a slice of its own (S4b) that the coordinator codes.
**As built (S4, ARC-DEV-104).** `GameSessionOwner.OpenSectors(map, extent, limits)` opens the sector cache over an open residency of the hero's
map (refused without one, `NoResidency`, or with an extent a resident lies outside, `InvalidExtent`); it is derived like the residency and dropped
with it (a new open, a close, the hero's map change, a restore). `TouchSectors(expected, map, cells)` locks the sectors of the cells as one batch
(cells outside the extent are skipped; a negative cell and more distinct sectors than slots are refused, `InvalidSectors`) and folds every loaded
one: the residents of the sector index now (step 1: the loaded state is the listing) and, for each resident NPC that is not destroyed, in entity
order, an immediate tick 1 ms ahead replacing its pending one (step 2), with the marker when the plan says `Fold` and the new start
`SessionAiTickStart.Immediate` (1 ms, no marker) otherwise; one revision when a tick was scheduled. Inside an event pass the ticks are queued and
scheduled when the pass ends (`DeferredFoldTicks` of the pass result). The read of the executable (ledger [CX7, CX8](../research/ledger/world.md#sector-fold-arc-dev-104))
settled the midnight case of §3.3: the elapsed time has no borrow, so it is a **bug of the game**; Original keeps it
(`LifecycleRuleProfile.FoldOnDayChange`), Steamwoven folds after 3 s of true elapsed time (`SectorCacheRules.FoldDueAfter`). `QuerySectorLoaded`
answers the AI host's loaded test (`AreaAiHost.SectorLoaded`); it is null for a map without a cache, which the host counts as loaded. Step 3 measured: the object rows the viewer reads carry
no waypoint or standpoint field (no reader of the NPC's waypoint list or day and night standpoints exists, and 0x4ACBB0, 0x4ACD90 and 0x4ACDB0 are unread), so
the host's `RunWaypoints` and `RunStandpoints` stay stubs and the step is slice **S4b**; the marked tick already calls them and runs check wield
and script point 10. The viewer (`AreaResidency`) opens the residency and the cache of a whole map when its AI host is built and the session has
no cache, pre-caches the 3 × 3 sectors around the hero and touches the hero's view square (16 cells each way, a stand-in for the drawn rectangle)
every frame; NPCs the fold did not reach keep the host's delayed first tick (`ScheduleTicks` skips those with a tick). A window of the main map
opens nothing, so the main map's whole mobile read (§6 risk 1) is not needed yet and stays open.

4. **Light fold** (S6, not S4): day-night scenery and lights of the sector follow the hour (RL19), driven by the session clock. Presentation draws them;
   the session records only which sectors are loaded and the hour they were last folded at (derived, not saved).

### 3.5 The heartbeat (slice S5)

The `AiTick` event, its delay formula and the 30-tile test already live in `AiSchedulerRules` and `GameSessionAi`; what is missing is
the two answers the host stubs: whether the NPC's own sector is loaded (`SectorLoaded`) and the catch-up of a far follower. S4 gives the first answer
to the session and S5 the second: the far branch reschedules only while the sector is loaded **or a follower was moved** (the catch-up), and the chain ends otherwise (RL16; the tick rules already end it when the sector is not loaded. S4 answers `SectorLoaded` from the session wherever the viewer opened a cache, which is a whole map, whose host already covers the whole map; a window of the main map keeps true until S5 builds the host per open map); a chain starts only at
creation, resurrection, the fold and the handler's reschedule, so an NPC whose sector was never loaded in this visit has no heartbeat
(RL17, an inference of the original not yet exercised by a map with unreached sectors).

**As built (S5, ARC-DEV-105).** The tick rules (`AiSchedulerRules.Tick`) already ended the chain of an NPC whose sector is not loaded; S5 closes the two
host sides. (1) The follower catch-up (ledger [CX9](../research/ledger/world.md#sector-fold-arc-dev-104), 0x4AD4D0 and the activity test 0x45D790):
`AreaAiHost.CatchUp` answers the original's tests (a PC leader, its distance, the waiting-here NPC flag or a waiting standing, the paralysed critter
flag, active) and `MoveToLeader` ends the follower's goal run and stands it on the PC's cell through the follower source
(`IAreaAiFollowers.Jump`, the companion group's jump); the move makes the far branch reschedule whatever the sector. The group's own follow rule jumps a
following companion at the same distance (`FollowDistanceRules.JumpDistance`, 30), once per frame, so the host's catch-up decides only for a companion that rule
does not drive (one the host holds in a round) or whose tick comes before the group's next drive; the two land on the same cell. (2) Gating of the first tick: when the session holds a sector cache for the map, `AreaAiHost.ScheduleTicks` starts
no chain for a map NPC whose sector is not loaded (RL16, RL17: a chain starts only at creation, resurrection, the fold and its own reschedule); the
companions the host runs are created with its build and keep their delayed first tick. A map without a cache (a window of the main map) keeps the
delayed first tick of every living NPC. The evictions the 16 slots cause are in the viewer's `ARC-DEV-104 fold ... evicted=` log lines (the observed
pressure risk 2 asks for): the arrival's 3 × 3 pre-cache fills 9 slots and the view square adds at most one more sector each way. **Not built here
(cut to S5b, ARC-DEV-118, and S5c, ARC-DEV-119, below):** the sector terrain port, the host built per open map and the main map's whole mobile read.

The **host** (`AreaAiHost`) stops being built per window: it is built per open map over the residents the session lists and serves the NPCs of
the loaded sectors. Because the viewer's window snapshot no longer decides who is alive, the host's chart (passability for movement)
comes from the **loaded sectors** through a sector source port beside the record source (`ISectorTerrain`, in `Arcanum.World` beside the record source; rows and passability of one sector by
id, read from the content stack, derived data); a goal that needs a tile of an unloaded sector touches it first, as the original's tile reads
do (RL11), so a long walk loads the sectors it crosses. A window move changes only the presentation scene and the view rectangle reported to the
cache; it does not retire the host.

### 3.6 Eviction history in the snapshot (slice S6)

Schema 68 adds one section `residency` for the open map: the map and the history (sector, eviction time), written by a game save with
every loaded sector that has no record added and stamped with the save's time (RL20, PS rows of the
[persistence model](original-persistence.md)); a load restores the history and the sectors the saved readers and the first draw touch (RL11 (3), (4); the arrival pre-cache runs only for a teleported PC, so a load does not pre-cache) are folded by the same rule. Pre-caching the 3 × 3 around the saved hero at a load is a deliberate difference (§4) that keeps the first frame's NPCs awake. The step from 67 leaves the history empty, which
makes every sector fold once at the first load (what a game saved by the original without history would do). A restore refuses a sector
outside the map's limits, history longer than 2 × slots, an unsorted or duplicated list and a time after the saved clock (typed failures
with paths, as the other sections).

### 3.7 What stays outside

- The change-of-place request, the move list and the homecoming of former followers (MIG11); S4 and S5 call relocation through the
  one existing session method and state in their notes which caller will be replaced.
- The three clocks and the frame step (MIG12): residency uses the session's game time (`nowMs`) and a frame hook that is today's per-frame
  pump; MIG12 moves the hook, not the model.
- The goal table (MIG13, MIG14): the host keeps its runs; saved runs are not part of the fold.
- Generated main-map sectors (K2) and spawners/encounters (K4): the residents of generated sectors come from the generator's output when
  K2 is written; the model has no special case.

## 4. Deliberate differences from the original

| Difference | Why |
| --- | --- |
| Locks do not outlive a call; no re-entry or cache-walk refusal. | Single-threaded owner; the original's refusals protect against its recursion. |
| An eviction saves nothing. | All differences live in the session (MIG7). |
| Tile data are read from the content stack on demand (derived), not kept in 0x8878-byte slots. | The slot holds data the session does not need; only "which ids are loaded" decides behaviour. |
| A load pre-caches the 3 × 3 around the saved hero (the original loads sectors only through its readers and the draw). | The session has no draw before the first frame, and a loaded NPC near the hero must wake. |
| The viewer reports the hero's view square (16 cells each way) as the view rectangle (S4). | The draw's own rectangle is a screen rectangle of an isometric view; the square covers the original's view around a centred hero. |
| A fold gives its NPCs their ticks in entity order, not in the find index's chain order (S4). | The session has no chain; ticks due at the same time fire in schedule order. |
| A fold inside an event pass queues its first ticks until the pass ends, so a tick of the same NPC already due in that pass still fires once before the fold's replaces it (S4). | The queue does not take schedules during its own pass; the original's immediate insert replaces at once. |
| History is saved in schema 68 for the open map only. | The original keeps it with the game's other state and clears it at any other map close; same observable rule. |

No switch is added now. Where an S4 or S5 check against the executable shows a behaviour that is a bug of the game rather than a rule (the dead
NPC's first tick and far-path chain, an encounter creature's destruction when its chain ends), it is classified by the
[original executable check](../research/original-executable-check.md) and put under the D8 "Original"/"Steamwoven" setting in that slice's PR.

## 5. Slices

| Slice | Code | Result | Tests and evidence |
| --- | --- | --- | --- |
| S1 | ARC-DEV-101 | This document; MIG9 row and K1 line updated. | none (docs-only) |
| S2 | ARC-DEV-102 (**done**) | `SectorCacheState`, `SectorCacheRules`, `SectorCacheLimits` in `Arcanum.Simulation`; no caller. | Unit tests: hit, miss, eviction of the least age, fold decision at 2 999 ms and 3 000 ms and with no record, history bound, limits validation, determinism, typed refusals. |
| S3 | ARC-DEV-103 (**done**) | `ResidencyLimits`, the residents of the open map and the sector index in the session (`QueryResidents(map)`, `QueryResidentsIn(map, sector)`); census of the main map's mobile rows. | Session tests over synthetic maps; census recorded in `docs/world/limits-census.md`; no behaviour change in the viewer. |
| S4 | ARC-DEV-104 (**done**) | The session owns the cache state, the touches, the fold: first tick (immediate, marker only on a fold), waypoint/standpoint; the AI host reads `SectorLoaded` from it for the touched sectors. | Session tests for each fold step; smoke `ai`, `ai-follower`, `town`; B06 and B10 first look. |
| S4b | (code from the coordinator) | The waypoint and standpoint steps of a marked first tick: read 0x4ACBB0, 0x4ACD90 and 0x4ACDB0 and the NPC rows' waypoint list and day and night standpoints, then the host's `RunWaypoints` and `RunStandpoints` relocate a leaderless NPC (a request when its map is another, MIG11). | Session and host tests; smoke `town` (B06). |
| S5 | ARC-DEV-105 (**done** for the gating and the catch-up; the rest is S5b and S5c) | Heartbeat gating (far branch, chain end, no chain for an unreached sector) and the follower catch-up in the host. | Session and host tests (`AreaAiFollowerTests`); smoke `ai`, `ai-follower`, `town`; the exe reading CX9. |
| S5b | ARC-DEV-118 | `ISectorTerrain` (rows and passability of one sector by id, beside the record source in `Arcanum.World`); the host's chart from the loaded sectors, a planner touching what it reads; the main map's whole mobile read (7,399 rows, §6 risk 1) so the main map opens a residency. | World and host tests over synthetic sectors; smoke `ai`, `town`; the altar run (it needs sector 1452,1291 loaded). |
| S5c | ARC-DEV-119 | The host built per open map: whether `AreaAiHost` moves into `Arcanum.World` (TM11), a window move no longer retires it (the stamp stops carrying the loaded area), `./arcanum map` main-map smoke. | Host tests; smoke `ai`, `ai-turn-based`, `ai-follower`, `town`; B06, B08, B25 re-checked. |
| S6 | ARC-DEV-106 | Schema 68 `residency` section, fold on load, saved history, light fold for loaded sectors. | Snapshot round trip, refusals, the 67 → 68 step by hand, a quick save and load by hand and the `ai` stage after it. |
| S7 | ARC-DEV-107 | Re-check of B06, B10, B25 (and B08 where it depends), coverage and status rows, this document's status. | `./arcanum smoke pr` for the stages touched; daily full chain covers the rest. |

Each code slice changes the session core and threading, so it takes the larger review model of [the review-model rule](../workflow/tasks.md#review-model), merges its own PR and updates the
coverage rows of FR-WORLD-003, FR-WORLD-004, FR-AI-001 and FR-RUNTIME-001 it changes.

## 6. Risks and open questions

1. **Size of the main map's mobile rows.** Measured by S3: 7,399 rows (1,556 NPCs, 463 containers, 5,380 items, no sector types) in the
   main map's game-generated aggregate, the largest of all maps ([census](../world/limits-census.md)): one read at open is small, so the
   sector-range fallback is not needed. What is missing is a World reader of that aggregate for the residency (the viewer loads the main map
   only as windows, and a window is refused); S4 or S5 adds it before the main map opens a residency.
2. **Chart supply.** The host's chart today is the window's; the sector source port moves it to loaded sectors. The risk is path planning that
   asks a far tile: the rule is that a planner touches (and so loads) what it reads, which can evict a sector the hero is near when many
   NPCs walk at once. 16 slots against a 3 × 3 pre-cache plus the view rectangle leaves a few; S5 reports the observed pressure.
3. **View rectangle.** The presentation must report the rectangle it draws each frame (age bump only). If the viewer's window is smaller than
   the original's view, drawing never keeps far sectors alive; accepted, it matches the original where the camera is the only reader.
4. **Heartbeat chain end.** An NPC whose chain ended while its sector was away is revived only by the next fold; the first tick of the fold is
   therefore the one place that can wake a "sleeping" NPC. Unit tests must pin it (S4).
5. **First look of an inference.** RL17's "an NPC of a never-loaded sector never gets a heartbeat" is an inference of the original; S5 does not
   rely on it for anything the player sees beyond the pre-cache, and a read of the executable settles it if a smoke ever shows the opposite.
6. **Threads.** The sector source may read on a worker; it returns data, and the session applies the cache transition on its own thread.
   No reader mutates the cache.

## 7. Not ready

Everything above except the sector model (S2), the residents (S3, §3.2), the fold (S4, §3.4) and the heartbeat gating with the follower catch-up (S5, §3.5) is a design. The sector terrain port, the host per
open map and the saved history are not implemented; on the main map, which the viewer loads as windows and which opens no residency, the behaviour is
still the one described in §1. The waypoint and standpoint steps of the fold are stubs (S4b). Until S6 saves the history, a restored session opens its cache empty, so the arrival
pre-cache after every load folds the 3 × 3 sectors around the hero with the marker and replaces their NPCs' saved ticks (check wield and script
point 10 run again; the original, with its saved history, folds them without the marker). The emptying passes (a teleport within the map, a
world-map landing: `EvictUnlocked`) and the relocation's wading touch have no caller yet (MIG11). On a whole map larger than the touched sectors, a
far NPC of an unloaded sector now ends its chain after the host's delayed first tick (RL16, RL17), which the fold of the next touch revives; the
main map's whole mobile read is not
built, the 6 whole maps whose inventory seed the reader refuses have no rows, a visitor's own items are not listed (they are rows of its home map),
the items the session added to a container that later spilled keep the container as their holder, an NPC dismissed from the party that stands on
its T0 cell is placed on a walked place (`npc.pos`) recorded before it joined (nothing clears it on joining yet), and projectiles in flight and the
dead are not marked on a resident.
