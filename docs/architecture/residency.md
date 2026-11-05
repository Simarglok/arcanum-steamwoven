# Residency (K1, MIG9)

Status: **concept written, nothing implemented** (ARC-DEV-101, slice S1 of MIG9, 2026-11-05). Docs-only: no code changes and no
checks run. Concept document K1 of the [architecture audit](../research/architecture-audit-plan.md) and design of MIG9
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

At map open every mobile object of the map (NPCs, containers, items, projectiles; not wall, portal, scenery or trap) is a **resident**: a
directory entry (TM1) built from the map's base row (through the record source of the [resolver](../world/record-resolver.md), the same
`IMapRecordSource`), the object-state fields of MIG7a (`npc.pos`, `follow.pc`, flags), the destroyed ids and the map's created objects.
The residents of the open map are always there, loaded sector or not (RL1, RL4: the find index keeps every located object).

- **Place.** An NPC's place is its entry's map and cell (`npc.pos` when recorded, else the base row's cell). The **sector** of a cell is
  `(x >> 6, y >> 6)`; a **sector index** answers "the residents in sector S" and "in this rectangle" for any sector, loaded or not (the original's
  mobile-types-only query, RL9). Both are derived data, rebuilt at open and kept in step by the one relocation path (MIG11 makes it the only one).
- **What is not a resident.** Statics belong to sectors: their base rows come from the window snapshot the viewer builds for drawing, and
  their changes are the MIG7b records keyed by positional identity. They are not read by the residency model; a sector's statics being "loaded"
  matters only to drawing and to the wading test, both of which stay presentation or tile questions.
- **Cost.** The whole main map's mobile rows are read once at open. Their count is not measured yet; S3 starts with a census and a limits object
  (`ResidencyLimits`: residents per map, default well above the census) and a typed `LimitExceeded` result, never a partial roster
  (the resolver's rule 3).
- **Save.** Nothing new: residents are derived from base rows and saved differences, which MIG7 already saves.

### 3.3 The sector model (slice S2, pure rules, no wiring)

A Godot-free immutable state machine in `Arcanum.Simulation` (so `Arcanum.Session` can hold its state value and snapshot it, and
`Arcanum.World` can drive it without a cycle): `SectorCacheState` and `SectorCacheRules`.

- **State.** `Slots` (sector id, age stamp), the age counter, the eviction **history** (sector id, game time of eviction; bounded to twice the
  slot count) and `SectorCacheLimits` (slots, default **16**, valid 8–128 as the original's init clamps; history = 2 × slots).
- **`Lock(state, sector, nowMs)`** returns the new state and an outcome: `Hit` (age refreshed), `Loaded` (miss, with the evicted sector if the
  cache was full and the **fold plan**: `Fold` = no history record for the sector or `nowMs − evictedAt >= 3000`), or a typed refusal
  (`NoMapOpen`, `OutOfBounds`, `NoFreeSlot`). The original also refuses re-entry and a walk over the cache; the session is single-threaded
  and a call completes before the next begins, so neither exists here (a deliberate difference, §4).
- **Locks do not outlive a call.** The original's lock count matters only while a query holds its answer (the draw holds the view sectors
  for the draw). The model touches (age bump, load on miss) and releases inside the same call, so "every slot referenced" cannot
  occur and there is no `NoFreeSlot` in practice; the refusal stays for limits objects with fewer slots than one call touches.
- **`EvictUnlocked`** (the passes that empty the cache: map change, teleport spell, world-map landing) returns the emptied state with
  every evicted sector in the history; **map close** clears the history (RL20).
- **Who touches** (the rules' own reads, RL11), as session methods the callers use: `TouchSectors(map, cells)` for the 3 × 3 arrival pre-cache
  at the hero's landing, for the view rectangle the presentation reports each frame (age bump only), for a relocation of a PC or NPC (the
  wading test: the sector of the new cell) and for a tile or sector query that reads a sector's data. Queries over mobile types only
  (the sector index) touch nothing.
- **No save hook.** The original saves a dirty sector before freeing it. Every difference of a static or mobile object already lives in
  the session's state (MIG7), so an eviction saves nothing and loses nothing.

### 3.4 The fold (slice S4)

When `Lock` returns `Loaded`, the session folds the sector, in the original's order (RL8):

1. **List** the sector's residents from the sector index (the "listed" state matters to the tile queries of §3.3 and to the presentation; the
   roster itself never changes).
2. **First tick** for every resident NPC that is not destroyed (dead NPCs too, RL8): `ScheduleAiTick` with the existing `WakeUp` start (due
   1 ms later, the fold marker), replacing the NPC's pending tick. This replaces the host's delayed first tick for every NPC of the map.
3. **Waypoint or standpoint**, when the plan says `Fold`: the leaderless NPC outside combat is put on its day or night waypoint (RL17: a
   relocation when its own map is the open one, else a request that MIG11 owns), the standpoint step only when the waypoint step does
   nothing, then script point 10. The tick rules (`AiSchedulerRules.Tick`) already model the waypoint and standpoint steps through
   world callbacks; the host's `RunWaypoints` (`AreaAiHost`) is a stub that returns false today, so S4 first measures what waypoint and standpoint data the object rows carry and, if reading them is more than a small step, cuts a slice of its own (S4b) that the coordinator codes.
4. **Light fold**: day-night scenery and lights of the sector follow the hour (RL19), driven by the session clock. Presentation draws them;
   the session records only which sectors are loaded and the hour they were last folded at (derived, not saved).

### 3.5 The heartbeat (slice S5)

The `AiTick` event, its delay formula and the 30-tile test already live in `AiSchedulerRules` and `GameSessionAi`; what is missing is
the two answers the host stubs: whether the NPC's own sector is loaded (`SectorLoaded`) and the catch-up of a far follower. S5 gives both
to the session: the far branch reschedules only while the sector is loaded, and the chain ends otherwise (RL16); a chain starts only at
creation, resurrection, the fold and the handler's reschedule, so an NPC whose sector was never loaded in this visit has no heartbeat
(RL17, an inference of the original not yet exercised by a map with unreached sectors).

The **host** (`AreaAiHost`) stops being built per window: it is built per open map over the residents the session lists and serves the NPCs of
the loaded sectors. Because the viewer's window snapshot no longer decides who is alive, the host's chart (passability for movement)
comes from the **loaded sectors** through a sector source port beside the record source (`ISectorTerrain`, rows and passability of one sector by
id, read from the content stack, derived data); a goal that needs a tile of an unloaded sector touches it first, as the original's tile reads
do (RL11), so a long walk loads the sectors it crosses. A window move changes only the presentation scene and the view rectangle reported to the
cache; it does not retire the host.

### 3.6 Eviction history in the snapshot (slice S6)

Schema 68 adds one section `residency` for the open map: the map and the history (sector, eviction time), written by a game save with
every loaded sector that has no record added and stamped with the save's time (RL20, PS rows of the
[persistence model](original-persistence.md)); a load restores the history and folds the sectors the saved hero's position pre-caches, so
NPCs of sectors that stayed away over 3 s are re-placed the way the original's load does. The step from 67 leaves the history empty, which
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
| History is saved in schema 68 for the open map only. | The original keeps it with the game's other state and clears it at any other map close; same observable rule. |

No switch is added now. Where an S4 or S5 check against the executable shows a behaviour that is a bug of the game rather than a rule (the dead
NPC's first tick and far-path chain, an encounter creature's destruction when its chain ends), it is classified by the
[original executable check](../research/original-executable-check.md) and put under the D8 "Original"/"Steamwoven" setting in that slice's PR.

## 5. Slices

| Slice | Code | Result | Tests and evidence |
| --- | --- | --- | --- |
| S1 | ARC-DEV-101 | This document; MIG9 row and K1 line updated. | none (docs-only) |
| S2 | ARC-DEV-102 | `SectorCacheState`, `SectorCacheRules`, `SectorCacheLimits` in `Arcanum.Simulation`; no caller. | Unit tests: hit, miss, eviction of the least age, fold decision at 2 999 ms and 3 000 ms and with no record, history bound, limits clamp, determinism, typed refusals. |
| S3 | ARC-DEV-103 | `ResidencyLimits`, the residents of the open map and the sector index in the session (`QueryResidents(map)`, `QueryResidentsIn(map, sector)`); census of the main map's mobile rows. | Session tests over synthetic maps; census recorded in `docs/world/limits-census.md`; no behaviour change in the viewer. |
| S4 | ARC-DEV-104 | The session owns the cache state, the touches, the fold: first tick `WakeUp`, waypoint/standpoint, light fold; the AI host reads `SectorLoaded` from it. | Session tests for each fold step; smoke `ai`, `ai-follower`, `town`; B06 and B10 first look. |
| S5 | ARC-DEV-105 | Heartbeat gating (far branch, chain end, no chain for an unreached sector); `ISectorTerrain`; the host built per open map; a window move no longer retires it. | Session and host tests; smoke `ai`, `ai-turn-based`, `ai-follower`, `town`, the altar run (it needs sector 1452,1291 loaded); B06, B08, B25 re-checked. |
| S6 | ARC-DEV-106 | Schema 68 `residency` section, fold on load, saved history, light fold for loaded sectors. | Snapshot round trip, refusals, the 67 → 68 step by hand, quick-load smoke. |
| S7 | ARC-DEV-107 | Re-check of B06, B10, B25 (and B08 where it depends), coverage and status rows, this document's status. | `./arcanum smoke pr` for the stages touched; daily full chain covers the rest. |

Each code slice is reviewed by an independent Opus subagent (model rule 2: session and threading core), merges its own PR and updates the
coverage rows of FR-WORLD-003, FR-WORLD-004, FR-AI-001 and FR-RUNTIME-001 it changes.

## 6. Risks and open questions

1. **Size of the main map's mobile rows.** Unmeasured; S3 measures it first and the limits object gives a typed refusal. If the whole map is
   too large for one read, the fallback is the original's own shape: rows read by sector range on demand, residents of absent sectors known only
   through the index (the model above already treats "resident" and "loaded" as different things).
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

Everything above; this page is a design. The residents, the sector model, the fold and the heartbeat gating are not implemented, and until S4
and S5 land the behaviour is the one described in §1.
