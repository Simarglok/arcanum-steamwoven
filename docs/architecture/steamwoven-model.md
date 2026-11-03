# Steamwoven model map: state owners and record sources

Status: **Research map** (2026-11-03), work item ARC-RES-079, slice M1 of the
[architecture audit plan](../research/architecture-audit-plan.md). Docs-only: no code
changes and no checks run.

Code read: `origin/main` 384152a4 (after PR #585). Scope: `src/Arcanum.Data`,
`src/Arcanum.Simulation`, `src/Arcanum.Scripting`, `src/Arcanum.Session` and the AREA
viewer that the alpha app runs (`tools/terrain-viewer/Core`, 139 files and about 27 k
lines without tests and smokes, and `tools/terrain-viewer/Godot`, 148 files and about 33 k). Out of scope: `Arcanum.Presentation` and
`Arcanum.Input` (stateless drawing and input mapping), `src/Arcanum.Godot` (the frame
and HD review app, not the game), launcher settings, render-only caches (textures,
frames, meshes) except where they hold game state.

This is a map, not a verdict. Each row is tagged with the plan's hypotheses
H1–H9 it **supports** (`+`) or **contradicts** (`−`); classing a gap as systemic,
narrow or intended is slice G1's job. "Saved" means captured in the snapshot at
schema 58 (`GameSessionSnapshot.CurrentSchemaVersion`); "run-local" means lost on a
quick load (a new session owner) and on a fresh run.

## Layer diagram

```
 original data (game folder, read-only)            player saves (JSON, schema 58)
        |                                                    ^
 [Arcanum.Data]  loaders -> immutable records                |
   AreaSnapshot per map (object rows), prototypes,          |
   step/terrain data, MES/DLG/SCR, tables                    |
        |                                                    |
        v                                                    |
 [viewer Core]  per-map hosts built for the SHOWN map  --facts per call-->  [Arcanum.Session]
   AreaInventoryMap, AreaCombatHost, AreaAiHost,            GameSessionOwner: the one mutable
   AreaScriptHost, AreaUseScriptHost, AreaSpellCastMap,      game state (clock, flags, map
   AreaCompanionGroup, AreaEventWorld/Pump ...               changes, ~25 domains, event queue)
   (rows by (map, entity); goal runs, movers: run-local)     |   uses rules from
        ^                                                    v
 [viewer Godot]  TerrainViewerApp._Process: one frame   [Arcanum.Simulation] stateless rules +
   order, hero position and hero action hosts             a few state holders (clock, queue,
   (AttackGoal, SpellCastGoal, click route), drawing       random, stats, spell runs, routes)
                                                         [Arcanum.Scripting] global flags,
                                                           dialog and SCR engines (per call)
```

Two facts shape everything below: the session never opens the game's data
(`Arcanum.Session` reads only save and settings files; base facts arrive as call
arguments from viewer hosts), and the viewer builds its hosts for the map it shows.

## 1. Identity and keys

| Key | Form | Where | Notes | Hyp. |
| --- | --- | --- | --- | --- |
| Map object entity | `oid:` + SHA-256 of the object ID | `AreaConversationState.EntityId`, `AreaInventoryHost` (`Core/AreaConversation.cs`, `Core/AreaInventoryHost.cs`) | Same on every map: identity-based | H1− |
| Row key | `AreaObjectKey(SourceSha256, RuntimeMap, Kind, Sector, SourceIndex, Offset, ObjectIdSha256, Length)` | `src/Arcanum.Data/Area/AreaModels.cs` | Positional (file, map, sector, offset); viewer drawing caches and companion facts key on it | H1+ |
| Session-made objects | `kit:n` (starting kit), `made:n` (made and script-created items), `junk:n`, `pool:n` | `GameSessionHero.cs`, `GameSessionInventory.cs`, `GameSessionJunkPiles.cs`, `GameSessionBloodPools.cs` | One numbering per kind, no object row | H2+ |
| Map | `SessionMapId(RuntimeMap)` | `GameSessionTypes.cs` | Runtime number from the map list | — |
| Event entity | `GameEventEntity(Map, Code)` | `src/Arcanum.Simulation/Events/GameEventTypes.cs` | Map is part of the reference; `Relocate` re-keys on a transfer | H1+, H7+ |
| Map change | `MapChangeKey(Scope Sector or Entity, Sector, Entity, Property)` under a `SessionMapId` | `GameSessionTypes.cs`, owner `_maps` | Project vocabulary: `dialog.lf`, `dialog.counters`, `dialog.script`, `follow.pc`, `use.lf`, `use.counters`, `use.script`, `obj.off`, `obj.destroyed`, `npc.faction`, `npc.cell`, `npc.map`, `lock.flags`, `lock.frame`, `lock.kind`, `trap.state`, `item.dropped`, `object.damage`, `object.condition` | H3+ |

## 2. Record sources (`Arcanum.Data`)

All loaders are pure: they read the game folder through `ContentStack`/`DatArchive`
and return immutable records. No loader keeps a mutable cache; static tables hold
only fixed pins (fingerprints, keyword maps).

| # | Source | Holds | Keyed by | Read by | Hyp. |
| --- | --- | --- | --- | --- | --- |
| D1 | `Area/` (`AreaSnapshot`, `AreaObjectRecord`) | A map's object rows at T0: type, prototype, fields, scope | `AreaObjectKey`, per map | Viewer load for the shown map (Crash Site: exterior and cave; map mode: the map or a 3×3 window of the main map) | H1+, H7+ |
| D2 | `StaticObjects/`, `Content/ContentPrototypes.cs` | Prototypes | Prototype number | `AreaCombatHost` prototype fallback, `AreaPrototypeItems`, created items | — |
| D3 | `Traversal/` (`AreaStepSnapshot`), `Terrain/` | Passability, tiles, terrain | Map, cell | Grid session, routes, AI paths, line of fire | — |
| D4 | `Maps/` (`MapList`, `MapTransitions`, `WorldAreas`, `MapPatch`) | Runtime map numbers, transitions, world areas | Map number | Transfers, world map | — |
| D5 | `Messages/`, `Dialogue/`, `Scripts/`, `Written/` | MES, DLG, SCR, books | File, line, script number | Conversation, script and reading hosts | — |
| D6 | `Characters/`, `Combat/`, `Magic/`, `Technology/`, `Reputations/`, `Inventory/`, `Lighting/`, `Audio/` | Rule tables, premade heroes, spells, schematics, inventory sources, schemes | Table index | Viewer at load, handed to the session as catalogues or per-call facts | H6+ |
| D7 | `Hd/` | Local HD packs | Frame key | Drawing only | — |

## 3. Session state owners (`Arcanum.Session`, `GameSessionOwner`)

One caller-serialized owner per game (`_areaSession` in the viewer); a quick load
replaces it with a new owner. Every row below is in the snapshot unless it says
otherwise.

| # | Owner (file) | Holds | Keyed by | Base rows | Snapshot | Hyp. |
| --- | --- | --- | --- | --- | --- | --- |
| S1 | `_clock` `GameClockOwner` (`Simulation/Clock`) | Game time, pause | — | — | Saved | H5+ |
| S2 | `_flags` `GlobalFlagOwner` (`Scripting/GlobalFlags`) | Global flags | Flag id | — | Saved | — |
| S3 | `_maps` | Map changes (§1 vocabulary) | Map, then `MapChangeKey` | — | Saved | H3+ |
| S4 | `_actor` | Hero map, cell, facing | — | — | Saved, but written only at commit points (§6 G1) | H6+ |
| S5 | `_story` (`GameSessionOwner.cs` `Story`) | Quests, global quests, rumors, quelled, known areas, reputations, story state | Number | — | Saved | — |
| S6 | `_party` `PartyDomain` | Follower order and records (loyalty marks, waiting) | Entity | — | Saved | H1− |
| S7 | `_hero` `HeroDomain` + `CharacterStatsOwner` | Hero stats, background, name, portrait, fate, advancement (gold and kit are in S9) | — | — | Saved | — |
| S8 | `_inventory` `InventoryDomain.Seeds` | Each attached map's T0 items and holders | Map, then entity | Seed built by the viewer (`AreaInventoryMap.Build`) from D1 | **Not state**: re-attached after every load for the loaded maps only; accumulates within one owner | H1+, H9+ |
| S9 | `InventoryDomain` PC lists | Gold, kit, taken, made, grid, identified, weights, last discarded number | Entity | — | Saved | — |
| S10 | `InventoryDomain` per-kind stores | Placed items (with source and map), junk piles, blood pools, ammunition stacks, given items, gold piles, generated-once containers | Entity; placed, piles and pools also carry their map and cell | — | Saved | H2+ |
| S11 | `InventoryDomain.Npcs`, `NpcWorn`, `Containers` | Made items added to NPCs and containers, NPC worn items | Holder entity | — | Saved | H1−, H2+ |
| S12 | `InventoryDomain.ScriptSlots` | Item script slots runs changed | (Item entity, point) | — | Saved | H8− |
| S13 | `_equipment` `EquipmentDomain` | Hero worn items, item event points, item spells marks | Slot | — | Saved | — |
| S14 | `_durability` `DurabilityDomain` | Conditions, destroyed items, uses, charges | Item entity | — | Saved | H1− |
| S15 | `_effects` `EffectDomain` | Hero effects, blessings, curses | Hero only | Effect rules from D6 | Saved (timed ends in S24) | — |
| S16 | `_experience`, `_advancement` | NPC experience, NPC advancement sheets, missed level steps | Entity | Schemes from D6 | Saved | H1− |
| S17 | `_life` `LifeDomain` | Hero and changed NPCs' life | Entity | Life facts passed per call | Saved | H1− |
| S18 | `_reactions` `ReactionDomain` | NPC reaction toward the PC | Entity | — | Saved | H1− |
| S19 | `_combat` `CombatDomain` | Turn-based round, combat-mode critters, concealment | Entity | Combatant facts from the round host per call | Saved; the subturn count and stalled check are not | H1−, H9+ |
| S20 | `_danger` `DangerDomain` | Danger bits, focus, fleeing-from, last hitter, enemy list | Entity | — | Saved | H1− |
| S21 | `_random` `SeededRandom` | Gameplay generator seed and draws | — | — | Saved | — |
| S22 | `_sound` `SoundDomain`, `_lighting` | Music and ambient slots, applied lighting | — | Scheme catalogue attached by the host (not saved) | Saved | — |
| S23 | `_hotkeys`, `_foundSchematics` | Hotkey bar, found schematics | — | — | Saved | — |
| S24 | `_events` `GameEventQueue` (`Simulation/Events`) | Pending events: life timers, effect ends, locks, party wait, script calls, respawn, decay, restock, AI ticks, spell ticks | Kind; payload `GameEventEntity(map, code)`; scope Global, EntityMap or ActiveMapOnly | — | Saved except `magic.run` (not saved) and `ai.tick` (dropped and rebuilt on load under the Original rule) | H7+, H9+ |
| S25 | `_spellRuns` `SpellRunTable`, `_maintainBar`, `_spellLastCost` | Active spell runs, the maintain bar | Run slot | — | **Run-local** | H9+ |
| S26 | `_shots` (`GameSessionProjectiles.cs`) | Shots in flight | Shot id | — | **Run-local** | H9+ |
| S27 | `SessionSpellCaster` (`SessionSpellCaster*.cs`) | One cast host's log, released runs, missiles, item chains | Run | Map facts through `ISpellCastMap` (the shown map) | Run-local, rebuilt per map | H1+, H9+ |
| S28 | `_rules`, difficulty, `_editorFloor`, encumbrance mark | Settings and editor state | — | — | Not saved by design (settings) | — |
| S29 | `GameSaveLibrary`, `GameSessionSaveSlot` | Save files, quick-save slot (one per mode) | Slot | — | The store itself | — |

## 4. Other Godot-free state holders

| # | Owner | Holds | Keyed by | Held by | Snapshot | Hyp. |
| --- | --- | --- | --- | --- | --- | --- |
| R1 | `Simulation/Navigation/RouteFollower`, `RouteCorridor` | The hero's click route | Cell | Viewer `_worldRoute` | Run-local | H4+, H9+ |
| R2 | `Simulation/Movement/MovementState` via `AreaGridSession` (`Core/DiagnosticGridSession.cs`) and `DiagnosticGridActor` | The hero's live position and step | Cell | Viewer `_worldActor` | Run-local (copied to S4 at commits) | H6+ |
| R3 | `Simulation/Party/CompanionFollower` | One companion's follow walk | Entity | `AreaCompanionGroup` | Run-local; position written to `npc.cell` when the group is recorded | H9+ |
| R4 | `Simulation/Travel/WorldJourney` | World-map journey | — | `Core/AreaWorldMap.cs` | Run-local (as the original's) | — |
| R5 | `Simulation/Clock/GameTimeFlow` | Host-time to game-time admission | — | `AreaAmbientDriver` | Run-local | H5+ |
| R6 | `Simulation/Ai/AiListCache` | AI spell and item lists | — | `AreaAiHost` | Run-local, rebuilt with the host | — |
| R7 | `Scripting/Dialog` `DialogSession`, `DialogConversation` | One open conversation | Speaker | `AreaConversationHost` | Run-local (no save while a dialog is open) | — |

## 5. Viewer per-map hosts (`tools/terrain-viewer/Core`)

Each is built for the shown map (or the loaded set) and dropped on a map change or
a quick load. "Covered maps" are the maps whose rows this run loaded: Crash Site
mode the exterior and the cave, map mode the shown map or window only.

| # | Host (file) | Holds | Keyed by | Base rows read | Lifetime | Hyp. |
| --- | --- | --- | --- | --- | --- | --- |
| V1 | `AreaInventoryMap`, `AreaInventoryHost` (`AreaInventoryHost.cs`) | Each covered map's seed, rows by entity, substitute containers, restock seeds | Map, entity | D1 of each covered map | Rebuilt on every map load | H1+ |
| V2 | `AreaCombatHost` (`AreaCombat.cs`) | Combat field and build caches, worn-set caches | (Map, entity) | D1 of covered maps, D2 fallback; `HomeMap` searches covered maps only | Rebuilt on map change | H1+, H9+ |
| V3 | `AreaCombatWorld`, `AreaSectorEntries` (`AreaCombatRound.cs`) | Round scan of the shown map's critters | Entity on one runtime map | Shown map | Per map | H7+ |
| V4 | `AreaAiHost` (`AreaAiHost.cs`) | The NPC goal runs `_runs` (`AiGoalRun`, due time), pass choices, used items, casts, attack plays, traces, its own `_nowMs` | Entity, shown map | `AreaAiScene` from the shown map's seed holders; `Stranger` keeps a visitor out of the rules | Rebuilt when the session, map, loaded area or the non-group follower set changes | H1+, H4+, H5+, H7+, H9+ |
| V5 | `AreaAiMovers` (`AreaAiMovers.cs`) | Cells of NPCs the AI moved | Entity | — | Run-local: a load or map change stands every NPC on its T0 cell | H7+, H9+ |
| V6 | `AreaCompanionGroup`, `AreaVisitors` (`AreaCompanions.cs`) | Companions of the shown map, absent ones, walks | Entity, `AreaObjectKey` | Shown map's speakers + bodies (L1) | Per map | H1+ |
| V7 | `AreaConversationHost`, `AreaConversationState`, `AreaScriptHost` (`AreaConversation.cs`, `AreaScriptHost.cs`) | Speakers, dialog local flags and counters baselines, script output lists | Speaker; state keys on the row's own map (`dialog.*`) | Shown map's speakers; `ForVisitor` builds one over the home map | Per map | H1+, H8+ |
| V8 | `AreaUseScriptHost` (`AreaUseScripts.cs`) | Use-script runs, created objects, awards | Shown map rows by entity; state keys `use.*`, `obj.*`, `npc.faction` on the row's map | Shown map + covered seeds | Per map | H1+, H8+ |
| V9 | `AreaItemScriptHost`, `AreaRestockHost` (`AreaItemScriptHost.cs`, `AreaRestock.cs`) | Item script and restock runs | Item or holder entity | Covered seeds | Per map | H1+ |
| V10 | `AreaSpellCastMap` (`AreaSpellCastMap.cs`) | Spell target rows and fields | Entity, `AreaObjectKey` | Shown map rows only (`IdOf`) | Per map | H1+ |
| V11 | `AreaEventPump`, `AreaEventWorld` (`AreaEvents.cs`) | The event pass and its world ports | — | Answers through V2, V4, V7 for the shown map | Owned by the ambient driver (C1) | H5+, H7+ |
| V12 | `AreaTransitionCoordinator`, `AreaMapTransfers` (`AreaTransition.cs`, `AreaMapTransfers.cs`) | Loaded maps, transition cells, tile and sector scripts | Map, cell | D4, D1 | Per load | — |
| V13 | `AreaShopSession` (`AreaShopMerchant.cs`) | A separate session owner for the opening shop | — | Shop map seed | Until handed to the game | — |
| V14 | `AreaSceneryAnimation`, `AreaHeroDeath`, `AreaWorldMap` | Scenery loops, death sequence, world-map journey | Object key | Shown map's scenery | Run-local | H9+ |

## 6. Goal and action machines

| # | Machine (file) | Actor | Clock | State | Snapshot | Hyp. |
| --- | --- | --- | --- | --- | --- | --- |
| G1 | Hero walk: `RouteFollower` + `DiagnosticGridActor` (`Godot/TerrainViewerWorld.cs`) | PC | C3 | Route, segment; "after route" intents for talk, use, pickup, pick pocket (`_talkAfterRoute`, `_areaUseAfterRoute`, `_inventoryAfterRoute`) | Run-local; the cell reaches S4 only at a save, transfer, attack or round | H4+, H6+, H9+ |
| G2 | `AttackGoal` (`Godot/TerrainViewerAttack.cs`) | PC | C2 | One swing; cancelled by a modal | Run-local | H4+, H6+ |
| G3 | `SpellCastGoal`, `_spellQueue` (`Godot/TerrainViewerSpellCast.cs`) | PC | C2 | Cast animation and release, queued casts | Run-local; a casting goal is not saved (its run would wait) | H4+, H6+, H9+ |
| G4 | Held throw (`Core/AreaHeldThrow.cs`, `Godot/TerrainViewerHeldThrow.cs`), viewer `_projectiles` (`Godot/TerrainViewerProjectiles.cs`) | PC | C2 | Throw, flying bodies beside S26 | Run-local | H4+, H9+ |
| G5 | `AiExecutorRules` (`Simulation/Ai/ExecutorRules.cs`, `ExecutorActionGoals.cs`) run by V4 | NPCs and companions on the shown map | C2 (V4 `_nowMs`) | `AiGoalRun` goal frames with the original's priorities | Run-local; the AI tick (S24) restarts passes | H4+, H5+, H7+, H9+ |
| G6 | Turn-based round (`CombatDomain`, `Godot/TerrainViewerTurnBased.cs`) | Both | Round order | Round, subturns | Round saved, subturn count not | H4− (one round for both) |

## 7. Clocks and frame pumps

| # | Clock | Source | Drives | Pauses | Hyp. |
| --- | --- | --- | --- | --- | --- |
| C1 | Session game time (S1) | `AreaAmbientDriver.Tick` (`Core/AreaAmbient.cs`) admits Godot `Time.GetTicksMsec` through `GameTimeFlow`; the event pump advances it; world-map steps and waits jump it | Events (S24), lighting hour | Menu pause while a modal is open | H5+ |
| C2 | `WorldTime` (`Godot/TerrainViewerWorld.cs`): Godot `Time.GetTicksUsec` (or a test time) | Read as `WorldMilliseconds()` | AI goal runs and movers (V4, V5), hero attack and cast, projectiles, missiles, eye candy, scenery | Not paused with C1: the frame chain runs while the world gate is closed; each host gates itself (the attack cancels under a modal, the AI pump has no gate) | H5+ |
| C3 | Movement `SimulationTime` | `_worldActor.Advance` | Hero steps | Held while modal, a transfer, a cast or lying down | H5+ |
| C4 | Godot frame `delta` | `_Process` | Death panel, light animation, zoom | Per consumer | H5+ |
| C5 | Turn-based round | `CombatDomain` | Subturns of both sides | Round state | — |

Frame order (`TerrainViewerApp._Process`, `ProcessWorldObserved`, `AdvanceWorldGrid`):
pacing, audio, C1 tick with the event pass, light animation, hero body, input
reduce, then transfer, walk-to-talk/use/pickup/pick pocket arrivals, hero attack,
NPC AI pump, projectiles, spells, spell missiles, eye candy, lying animations,
scenery, turn-based round, hero knockdown, hero step; then sheet, logbook, barter,
world-map and menu syncs, death, inventory, hover, cursor and bug-report syncs.

## 8. Run-local caches

| # | Cache (file) | Holds | Keyed by | Lost on | Hyp. |
| --- | --- | --- | --- | --- | --- |
| L1 | `_travellers` (`Godot/TerrainViewerCompanions.cs`) | A companion's body built from its home map's scene | Entity | Quick load or fresh run when the home map was not loaded (`AreaVisitors.Unbodied`) | H1+, H9+ |
| L2 | `_companionFacts` (`Godot/TerrainViewerCompanions.cs`) | Companion facts | `AreaObjectKey` | Map change | H1+ |
| L3 | `_visitorRefused` (`Godot/TerrainViewerConversation.cs`) | Visitors whose dialog was refused | Entity, per owner | Quick load | H9+ |
| L4 | V5 movers, V4 runs, S25 spell runs, S26 shots, G1–G4 | See above | — | Quick load, map change | H9+ |
| L5 | Drawing derivations: `_fallenBodies`, `_groundBodies`, `_bodyCycles`, `_portalBodies`, `_projectileBodies`, `_spellMissileBodies`, `_sceneryBodies`, `_eyeCandyBodies`, `_bloodPoolKeys`, `_areaRoofObjects` | Draw commands rebuilt from session state | `AreaObjectKey` | Rebuilt each change | H1+ (positional key) |
| L6 | `_aiCompanionOccupancy`, `_aiLineOfFire`, `_aiPcOccupancy` (`Godot/TerrainViewerAi.cs`) | Occupancy and line of fire with movers | Frame, version | Each frame or version | — |

## 9. What each handoff keeps

| Handoff | Session | Seeds (S8) | Hosts (V1–V14) | Positions | Runs |
| --- | --- | --- | --- | --- | --- |
| Map transfer | Carried | Loaded maps re-attached | Rebuilt for the new map | Hero placed on arrival; companions carried by `npc.cell`/`npc.map`; other NPCs at T0 | AI runs and movers dropped; NPC timers re-keyed (`Relocate`); `ActiveMapOnly` events removed |
| Stream window move (main map) | Carried | Re-attached | Rebuilt (a new loaded area) | Hero where it walked | AI runs and movers dropped |
| Quick load | New owner from the snapshot | Re-attached for the loaded maps only | Rebuilt | Hero from S4; NPCs at T0; companions from map changes, bodies from L1 if present | S25, S26, G1–G5 lost |
| Fresh run | New owner | Loaded maps | New | T0 | — |

## 10. Hypothesis index

| H | Supported by | Contradicted by |
| --- | --- | --- |
| H1 rows by (shown map, entity) | §1 row key; D1; S8; V1, V2, V4, V6–V10; L1, L2, L5 | Entity ids are identity-based (§1); S6, S11, S14, S16–S20 key state by entity on any map |
| H2 no general dynamic object | §1 session-made ids; S10, S11 (one store per kind) | — |
| H3 state as a key vocabulary | §1 map change vocabulary; S3 | Many domains hold typed records instead (S6–S23) |
| H4 two goal machines | G1–G4 (viewer hosts) vs G5 (`AiExecutorRules`) | G6: one turn-based round for both |
| H5 more than one clock | C1–C5; V4's own `_nowMs`; C1 owned by the lighting driver | — |
| H6 logic in the viewer tool | Session reads no game data (§2 D6, S8, S27); hero position (R2, S4) and every action host in `Godot`; frame order in `_Process` | All mutable game state lives in the Godot-free session (§3) |
| H7 only the shown map is alive | D1, V3, V4, V5, V11; `EntityMap` events wait while their map is inactive and `ActiveMapOnly` ones are removed (S24) | Global events (hero timers, script calls) run on any map |
| H8 script state by home map | V7, V8 (`dialog.*`, `use.*`, `obj.*` on the row's own map) | S12: item script slots by item entity |
| H9 run-local state | S8 (seeds), S19 (subturns), S24 (`magic.run`), S25–S27, R1–R3, V4, V5, V14, G1–G5, L1, L3 | — |
