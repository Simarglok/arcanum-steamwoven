# Followers across maps: the original's object move and the visitor model

Status: **Research**, work item ARC-RES-076 (2026-10-29). Docs-only: no code
changes and no checks run. It answers the owner's question after ARC-DEV-065
(PR #582): a companion carried onto another map (Virgil in the Crash Site cave)
follows there but neither fights nor is fought, because the shown map has no
combat data for it. Is the "visitor" model itself wrong? This note reads in the
retail executable how party followers move between maps and sectors, what a
save keeps and which AI and combat state travels, compares it with
Steamwoven's visitor model, and proposes a fix in PR slices. The slices are
proposals only; none is started.

Requirements: [FR-WORLD-010](../requirements/world.md#fr-world-010),
[FR-SOCIAL-005](../requirements/story-social-ai.md#fr-social-005).

Earlier results reused, not re-read here: the teleport walk and payload
([companion study](companion-follow-evidence.md#findings) C15, C16), followers'
links across maps ([OL31–OL33](companion-orders-loyalty-evidence.md#7-followers-across-maps-q7-companion-o7)),
NPC inventory across maps ([NI1–NI7](ledger/items.md#npc-inventory-changes-arc-world-055)),
the event queue's map scope ([P1–P7](game-event-queue.md)) and the sector cache
([SS1–SS4](main-map-streaming.md#how-the-game-keeps-sectors)). This reading
confirms them where it touches them and adds identity, sector eviction, the
same-map case and the one tie an NPC keeps to a map.

## Method

Behaviour comes from the retail executable (`Arcanum.exe` 1.0.7.4, SHA-256
`9bd80511…78d52`, re-hashed for this reading), read statically with
`llvm-objdump` following the [original executable check](original-executable-check.md),
by two readers that each saw only the questions and the starting addresses,
not the earlier answers (method step 6). Object fields, flags and time-event
types were named from the executable's own tables (fields 0x59BEA8, the
OF_/OCF_/ONF_ name tables, event types 0x5B2188). Every direct branch of the
functions read stays inside `.text` (1,835 and 1,182 branches checked); the
indirect calls go to the sector load and save hooks, event callbacks, jump
tables and the `CoCreateGuid` import, none to `.IIDKing`. Both readers agree on
every row below unless the row says otherwise; rows marked F1 were settled by
one reader only. Everything is read from the executable unless marked
**(inference)**. Steamwoven facts come from the code on `origin/main` at
a156e143.

## Answer

- **The original has no visitors.** A follower carried to another map is
  written whole, with its own unchanged ID, into the destination map's dynamic
  object file; its source copy is destroyed and the source map remembers its ID
  as deleted. From then on it is an ordinary object of the destination map, with
  every field it had: stats, hit points, inventory, scripts and their local
  data, reaction, AI flags, leader. Combat, AI and scripts treat it like any
  other object of that map. Back on its first map it is again a dynamic object
  of that map; its original data record never comes back.
- **What the move clears** is transient fight state (combat focus, last
  hitter, enemy list, fleeing, substitute inventory, combat mode, its pending AI
  events, animation). When the PC changes maps, its time events travel with it
  when all the objects they name travel (an NPC teleported alone only has its
  record moved); everything else stays on the object.
- **One tie remains:** the NPC's teleport map (field 243), set once to the
  first map it was teleported from. A *former* follower (dismissed or jilted)
  left on a foreign map walks home: the next forced waypoint or standpoint pass
  teleports it to its standpoint on that map. A follower with a leader is never
  sent back.
- **Steamwoven's gap is systemic, not narrow.** Identity and mutable state
  already follow the entity across maps (party, life, reaction, experience,
  advancement, danger, combat sets, made and worn items). But the NPC's *base
  record* (its object row with combat fields, AI row, inventory seed holder,
  body) is found only through the shown map's own rows, and a run-local cache
  keeps the body. Every system that resolves "the row of entity X on the shown
  map" fails for a carried companion, so each one has needed a special case
  (visitor dialog host, home-map fallbacks, the ARC-DEV-065 skip, the
  "unbodied" visitor after a quick load). ARC-DEV-065 removed a crash; it did
  not fix the model.
- **Fix:** one identity-based actor directory that every consumer asks for
  "where is X and what is its base record", reading the record from the
  entity's home map data on demand; then the special cases go away one by one.
  Plan: [slices V1–V6](#fix-plan).

## 1. The original mechanism

### Rows

| ID | Function (address) | Original 1.0.7.4 | Level |
| --- | --- | --- | --- |
| FM1 | teleport request and run (0x4D3380, 0x4D3330 → 0x4D3460), move list (0x4D3760, head 0x601840) | A teleport request is queued and run later. The move list holds the object, every item of its inventory (critter fields 237/238, container 59/60; a count that differs from the list stops the walk) and each follower (field 241) that does not wait (ONF_AI_WAIT_HERE), recursively, so followers of followers travel too. The order of the nodes was not re-settled here; C15's reading (nodes after their followers and items, so a mismatch can leave out the object itself, possibly the PC) stands. Every entry gets the same destination tile. The destination map is opened only when a PC is in the list; an NPC teleported alone to another map only has its record moved. | F2 |
| FM2 | 0x4D3460, 0x45D800, npc wait 0x4AA7A0 | Before the walk, unless request flag 0x100 is set, every follower for which 0x45D800 answers yes is ordered to wait, so it stays behind. The two readers here disagreed (dead or fatigue-unconscious; the earlier reading C15 said unconscious). **Settled** by [RL23](../architecture/original-object-model.md#9-map-change-close-the-whole-map-build-the-next-from-files): 0x45D800 reads fatigue and is true for a critter that is not undead and has no fatigue left, so the followers left waiting are the unconscious, not the dead (applied by ARC-RES-086). | F2 (RL23) |
| FM3 | cross-map payload 0x4D39A0, writer 0x409980, handles→IDs 0x4064B0, IDs→handles 0x406520 | For another map, every listed object is prepared (FM6), then all are converted from handles to IDs before any is written, so links inside the group (leader, follower list, inventory parents) stay as IDs. Each is marked dynamic and teleported, placed on the destination tile with zero offsets and appended whole, its own ID included, to `Save\Current\maps\<destination>\mobile.mdy`. The live source copy is marked destroyed and extinct. Confirms C16 and NI4. | F2 |
| FM4 | ID getter 0x407EF0, positional ID 0x4E62D0, GUID 0x4E62A0, object read 0x409AA0, ID registry 0x4E4FD0 | IDs are never reassigned by a move. A positional ID (tile, temp id, map) is given only to static walls, portals, scenery and traps; NPCs, PCs and items get a GUID. Reading the record on the destination re-registers the same ID, so the follower keeps one GUID on every map. That map-data NPCs already carry a GUID is an **(inference)** from the getter and the stored IDs, not checked in the data files. | F2 |
| FM5 | flush 0x410070, difference save 0x410830, dynamic save 0x410B20, map open 0x40F4E0, mobile load 0x410D10, free 0x405BF0, dynamic load 0x411450 | When the source map closes, the ID of an original (non-dynamic) object that is destroyed or extinct is appended to `mobile.des`; a destroyed dynamic object is simply not written to `mobile.mdy`. A map opens in this order: its data objects, `mobile.md` differences, every `mobile.des` ID freed, `map.patch`, then `mobile.mdy`. So a follower returning home comes back from `mobile.mdy` as a dynamic object and its original data record stays deleted. There is no duplicate check: registering an ID already present replaces it, and `mobile.des` is the only guard. | F2 |
| FM6 | preparation 0x4D3D60 (both cases), 0x4D3E20 (cross-map extras) | Both cases: the animation is interrupted; an NPC's teleport map is set if it was 0 (FM10); combat focus (283), who hit me last (284), the enemy list (304), the substitute inventory (294) and fleeing-from (230) are cleared; lights are updated. Cross-map only: combat mode is left (0x4B3F20), the object's AI events are removed (0x4AD7D0), it leaves its sector (0x43CF70), floating texts are removed and a UI callback runs. | F2 |
| FM7 | rebinding 0x406520 → 0x40BDB0, lookup 0x4E50E0, handle→ID 0x40BBF0, post-load 0x410C50 | A reference is kept as an ID while saved and turned back into a handle among the objects in memory when a map opens; a GUID not found becomes null, a positional ID is searched only on the current map at its tile. So a link from an object of map A to a follower now on map B is null after A reopens, and is lost at A's next save. The PC's follower array and the followers' leader rebind because the group arrives together (OL31). Each teleported object gets a "Teleported" event (type 32), whose callback runs the whole arrival notification in mode 2 (wading, jump points, tile scripts, sector entry; [RL6](../architecture/original-object-model.md#7-sectors-what-a-sector-owns-who-is-listed-how-objects-move) and RL27, corrected by ARC-RES-086: this row first read it as a drawing refresh only), then the flag is cleared. | F2 |
| FM8 | teleport notify 0x45C720 → 0x45C7B0 (filter 0x45C500, list test 0x4D3420), queue map close 0x45BCE0 → 0x45C2F0, map open 0x45C580 / 0x45C610 | Only when a PC changes maps: all animation and magic/tech events are dropped; every event whose object parameters all travel is written to the destination's `TimeEvent.dat` and loaded there when it opens; at the source's close the AI, combat, turn-based, world-map and teleported kinds are dropped, and an event naming any object left behind (a waiting follower's wait timer, for example) is parked in the source's `TimeEvent.dat`. Events with no object stay in the global queue. Confirms P3–P5. The AI tick, open here, is settled by [RL23 and RL28](../architecture/original-object-model.md#9-map-change-close-the-whole-map-build-the-next-from-files): the carry runs before the payload removes the object's AI events, so a travelling follower's own tick is carried, reloaded at the open and replaced by the fold's first tick when its sector loads; there is one tick, not two. An event whose object cannot be found at reopen gets a null handle and later fires with no object (RL28, TE9). (Applied by ARC-RES-086.) | F2 |
| FM9 | 0x45C720, spell end 0x44F7A0, animations 0x422B10 | An active spell is carried to the destination's `MT.dat` only when its caster, parent, target and both target lists all travel; otherwise it is ended. Animations of travelling objects go to the destination's `Anim.dat`. One reader; the other read only that a 40-slot table (0x5FF620, through 0x459F50 → 0x4CC4C0) is reset, at low confidence. | F1 |
| FM10 | teleport map setter 0x45F6D0 (field 243), waypoints 0x4ACBB0, standpoint 0x4ACDB0, standpoint 0x4AD060, origin test 0x45D320 | The one tie between an NPC and a map is field 243, the teleport map: written with the current map only while it is 0, by every NPC teleport (FM6) and by the AI's home check; a script setting the night standpoint and one magic/tech effect may overwrite it. Standpoints (289/290) are bare locations; the origin (291) is a group number compared between NPCs, with no map. In the waypoint and standpoint passes, when the teleport map is not the current one, an unforced pass does nothing; a forced pass of a leaderless NPC requests a teleport of that NPC alone to its waypoint or standpoint on its teleport map. | F2 |
| FM11 | sector lock 0x4D0540, save hook 0x4D2750, object list save 0x4F1A20, free 0x4D1400, detach 0x443770, load 0x4D1A30, fold 0x4F1CD0 (index 0x4E3B30), first tick 0x441FC0 → 0x4AD790, time test 0x45A9B0, AI event 0x4AD200 | On one map, an evicted sector saves and frees only its static objects. Mobile objects (NPCs, followers, items) are only detached from drawing and lights; they stay in memory with all their state and are re-attached from a per-sector index when the sector loads again. Nothing protects the PC's or followers' sectors except lock counts. Each re-attached living NPC gets an immediate AI event whose "returning" flag is set when the sector was away at least 3,000 game milliseconds (P7: now + 3 s, ARC-RES-042 H1) or is not in the eviction history (SS3); the AI acts on it only for a leaderless NPC out of combat (forced waypoints, else forced standpoint, FM10). Whether AI or time events run for an NPC while its sector is evicted was not read. | F2 |
| FM12 | same-map branch of 0x4D39A0, 0x4EDF20 → 0x43E770, clock 0x45C200; world-map landing (ledger WT12) | On the same map, every non-inventory object of the move list is prepared (FM6, both-cases part only) and moved to exactly the destination tile; combat mode, AI events, sector membership and time events are untouched. Request flag 8 advances the clock first. World-map travel lands through such a same-map teleport, so the group lands on one tile with the PC (one reader; consistent with WT12's landing teleport when the PC stays on its map). | F2 (world map F1) |
| FM13 | follower removal 0x45DFC0 → 0x4AA8C0 → 0x45E040, wait expiry 0x4AA990, order handler 0x4C31A0 | Leaving, a wait expiry (jilted, reaction lowered, leader cleared) and every order act on the current map only; none of them teleports. No caller of the teleport request is in follower code: the callers are map, script, magic/tech, the AI home return (FM10) and the interface. | F2 |
| FM14 | player save (0x40D760, NI5) | A save holds no follower state outside the per-map files: the PC and the followers with it are dynamic objects of the current map; waiting followers are objects of the map they wait on, with the leader as an ID (OL32). | F2 (from NI5, OL32) |

### What travels, in one view

| State | In the original | At a cross-map move |
| --- | --- | --- |
| Identity | GUID on the object | kept (FM4) |
| Stats, hit points, fatigue, effects, level, XP | object fields | travel |
| Inventory and worn items | items' own records, parent links | travel, each item a dynamic record of the destination (FM1, FM3) |
| Scripts with local flags and counters, dialog-related fields, reaction slots | object fields | travel (OL33) |
| Leader, follower list | handle fields | travel as IDs, rebind on arrival (FM7) |
| NPC flags, AI row, standpoints, waypoints, origin, teleport map | object fields | travel; teleport map set if 0 (FM10) |
| Combat focus, last hitter, enemy list, fleeing, substitute inventory | object fields | cleared (FM6) |
| Combat mode, the object's AI events, animation | global lists | left or dropped (FM6, FM8) |
| Its time events | global queue | moved when all named objects travel, else parked on the source (FM8) |
| Active spells on it | magic/tech runs | moved when all participants travel, else ended (FM9, F1) |

## 2. Steamwoven today: the visitor model

Identity: an NPC is the entity `oid:` + the SHA-256 of its object ID
(`AreaConversationState.EntityId`, `tools/terrain-viewer/Core/AreaConversation.cs`), the same on
every map, which matches FM4.

Session state by key:

- **By entity, on any map:** party standing, life, reaction, NPC experience and
  advancement, danger records, combat critters and concealment, NPC made items
  and worn items (`src/Arcanum.Session/GameSessionParty.cs`,
  `GameSessionLife.cs`, `GameSessionReaction.cs`, `GameSessionExperience.cs`,
  `GameSessionAdvancement.cs`, `GameSessionDanger.cs`, `GameSessionCombat.cs`,
  `GameSessionInventory.cs`). This part already behaves like the original's
  "state is on the object".
- **By home map:** dialog local flags, counters and script state, off,
  destroyed and faction changes are keyed on the record's own map
  (`AreaConversation.cs`, `AreaScriptHost.cs`, `AreaUseScripts.cs`). Since the
  key does not change when the NPC moves, this is one state per identity too.
- **By map:** the companion's place (`npc.cell` on the map it stands on, and
  `npc.map` on its home map naming that map, `AreaCompanionPlacement`,
  `tools/terrain-viewer/Core/AreaCompanions.cs`), the inventory seed holders
  (`InventoryDomain.Seeds`, one per map) and event payload entities
  (`GameEventEntity(Map, Code)`). Pending timers of travelling followers are
  re-keyed to the destination at a transfer (`GameEventQueue.Relocate`,
  [event queue runtime](../world/game-event-queue-runtime.md)), which matches FM8's
  first half.

The **base record** (object row, combat fields, AI parameter row, seed holder,
body commands and visuals) is where the model departs from the original:

- `AreaCombatHost` keeps one row index per loaded map and answers
  `Fields(map, entity)` and `Build(map, entity)` only for an entity among that
  map's own rows (`tools/terrain-viewer/Core/AreaCombat.cs`). The AI host asks for
  the shown map (`AreaAiHost.Fields`, `Build`, `Row`, `Find`), so a carried
  companion has no fields, no build and no AI row there.
- `AreaCombatHost.Holders(session, map)` and `Worn` read only the shown map's
  inventory seed, so a carried companion's seed items are not found there.
- `AreaSpellCastMap.IdOf` has no row for "a row of another map such as a
  visiting companion's".
- `AreaCompanionTemplate` holds the body (T0 commands, visuals, home cell) built
  from the home map's scene, kept in a run-local dictionary (`_travellers`,
  `tools/terrain-viewer/Godot/TerrainViewerCompanions.cs`) that a fresh run or a
  quick load does not refill.

Each system then needed its own special case:

| Special case | Where | Why |
| --- | --- | --- |
| ~~Visitor dialog host over the home map~~ (gone: ARC-DEV-081, MIG5) | `AreaConversationHost.ForVisitor`, `AreaTalkRouting`, `TerrainViewerConversation.VisitorHost` (ARC-WORLD-048) | the shown map's host does not list the speaker; the one host lists a carried companion now (`AdmitVisitor`) |
| Home-map fallback for critter facts and object existence | `AreaEventWorld.CritterFacts` / `ObjectExists` → `AreaCombatHost.HomeMap` | the shown map has no row |
| Life bars, turn-based facts, sheet and levels read the home map | `TerrainViewerAreaLifeBars.CompanionLife`, `TerrainViewerTurnBased`, `TerrainViewerAttack` | the same |
| Visitor kept out of the AI rules | `AreaAiHost.Stranger` (ARC-DEV-065): not told of attacks, in no square, not in the flee count | the rules refuse an object `Find` does not know |
| Visitor not targetable by spells | `AreaSpellCastMap.IdOf` | no row |
| Visitor without a body after a quick load | `AreaVisitors.Unbodied` | the body exists only in the run-local cache |
| ~~Negative `IN` refused unless the home seed is covered~~ (gone: ARC-DEV-081, MIG5) | `refuseUncoveredInventory` | the seed is per map; the session refuses an entity the hero's map seed does not list (a cross-map give waits for MIG6) |
| `HomeMap` searches only covered maps | `AreaCombatHost.HomeMap` | rows exist only for maps loaded in this run |

The transfer itself (`AreaCompanionGroup.Carry`, `Arrive`) writes three map
changes (clear the old cell, write the new cell, point `npc.map`) and relocates
timers; the session then drops every AI tick and the danger records that hold only check bits (ARC-WORLD-084,
`GameSessionOwner`), but not FM6's focus, last hitter, enemy list, fleeing or combat mode; it has no followers of followers,
no unconscious or dead follower rule (FM2) and no inventory move (none is needed
for the identity-keyed stores, but the seed holder is per map). *Since ARC-DEV-073 (MIG10, V5):* the transfer asks the session
for a move list and applies it with the placement, so unconscious followers wait, FM6's links are cleared, a travelling
follower's AI tick is carried and its combat mode is left on another map; the move-list walk handles followers of followers,
but the session has no NPC leaders to feed it ([companion map change](../world/companion-follow-runtime.md#map-change-arc-world-017)).

## 3. Gap: systemic, not narrow

The ARC-DEV-065 note "load his combat data from the home map" reads like one
missing lookup, but the same lookup is missing in every consumer that asks "the
row of X on the shown map", and new consumers (each AI slice, spells, barter,
pick pocket, healing, scripts run by the AI host) will hit it again. The root is
that the base record is addressed by **(shown map, entity)** while the original
addresses the object by **identity alone**, wherever it stands. The state side
is already identity-based, so the model is not wrong in every layer; the
record lookup layer is.

| Gap | Kind | Original row |
| --- | --- | --- |
| Base record found only among the shown map's rows (combat fields, AI row, build, seed holder, spell target, body) | **systemic** | FM3, FM4 |
| Body lost after a fresh run or quick load (run-local cache) | systemic (same root) | FM14 |
| Transient fight state not cleared by a move | narrow | FM6 |
| No followers of followers; dead/unconscious follower not left waiting | narrow | FM1, FM2 |
| Former follower on a foreign map never goes home | narrow, missing feature | FM10, FM11 |
| Active spells of travelling objects | narrow, later (NPC runs are not saved per map yet) | FM9 |
| Links from other maps' objects to a moved follower are nulled | not modelled; Steamwoven keeps identity links, which is safer (no switch proposed) | FM7 |

## 4. Fix design

Two ways close the systemic gap:

- **(A) Actor directory over home data (recommended).** One session-level
  query answers, for an entity: the map it stands on (from `npc.map`/`npc.cell`
  as today) and where its base record comes from (its home map and object ID).
  A viewer-side record resolver loads the home map's object rows on demand (the
  rows only, not the scene) and every consumer goes through it: combat fields
  and builds, the AI row, the seed holder and worn items, spell targets, life
  and sheet facts, the body template. State stays where it already is, keyed by
  entity. No snapshot change for the lookup itself; the original's behaviour
  ("the object is the destination's, with all its fields") holds because no rule
  reads which map the record came from.
- **(B) A dynamic object store like the original's files.** The session copies
  the moved NPC's whole base record into a per-map dynamic store and marks it
  deleted on the source (`mobile.mdy`/`mobile.des`). Closer to the file layout,
  but it duplicates the Data layer's object model in the snapshot (every field
  with a schema), grows saves and needs a large migration, while the mutable
  part it would carry is already identity-keyed. Not recommended.

Under (A), "visitor" stops being a mode: a companion is drawn, talked to,
fought, targeted and healed on any map through the same paths as a local NPC.

## Fix plan

Proposals, one PR each, in this order; codes come from the coordinator when a
slice starts. Each keeps Data, Simulation, Scripting and Session free of Godot,
cites the rows it implements and updates the coverage of FR-WORLD-010 (and
FR-SOCIAL-005 for V6).

| Slice | Result | Rows | Depends on |
| --- | --- | --- | --- |
| V1 Actor directory and record resolver (**done for held maps and maps a whole-map plan admits**: ARC-DEV-072, [contract](../world/record-resolver.md), as MIG1 in the world runtime; a home on the main world map, a window, is not read on demand) | A session query for an entity's current map and record source; a viewer record resolver that reads a home map's object rows on demand (any map of the module, loaded in this run or not), bounded by an explicit limits object (maps held, rows per map); `AreaCombatHost.Fields`, `Build`, `Holders`, `Worn` and `HomeMap` resolve by entity through it. Synthetic tests: an NPC of map A standing on map B has the same fields, build and seed items as on A; a map never loaded in the run is read on demand. | FM3, FM4 | — |
| V2 Carried companions in the AI (**done: ARC-DEV-075 = MIG2**; a companion with no record, a home on the main map's window, stays out of the rules by `AreaAiHost.Unresolved`) | Remove the ARC-DEV-065 `Stranger` skip: `Find`, `Fields`, `Build` and the AI parameter row (`AreaAiHost.Row`) return the companion through V1; it is told of attack events, stands in squares and the flee count, attacks and is attacked. The `transfer` smoke's visitor fight asserts that Virgil strikes in the Crash Site cave and that the cave NPC can target him. | FM3, FO rows | V1 |
| V3 Targets and panels (**done: ARC-DEV-076, MIG3**; [contract](../world/record-resolver.md#consumers-by-identity-arc-dev-076-mig3)) | Spell targets (`AreaSpellCastMap.IdOf`), hover attack, life bars, turn-based facts, the sheet and levels read the companion through V1; the home-map fallbacks are removed. | FM3 | V1 |
| V4 Body after a load (**done: ARC-DEV-079 = MIG4**, [contract](../world/record-resolver.md#bodies-and-window-homes-arc-dev-079-mig4); the body is read from the home map's own data, a window home around the stored cell `npc.home`, not from the resolver's rows alone) | The companion template is built from the resolver (home map rows, visuals and frames on demand) instead of the run-local `_travellers`; `AreaVisitors.Unbodied` and the "no body kept this run" path go away; a quick load on a foreign map shows the companion and it follows. Native smoke: F7 in the cave, a fresh run, F8, Virgil drawn and following. | FM14 | V1 |
| V4b One dialog host (**done: ARC-DEV-081 = MIG5**, [contract](../world/record-resolver.md#one-dialog-host-arc-dev-081-mig5)) | A companion's conversation runs through the shown map's host with its record from V1 (its dialog state stays keyed on its record's map, as ARC-WORLD-048 keeps it); `AreaConversationHost.ForVisitor`, the routing in `AreaTalkRouting`, the viewer's visitor host cache and `refuseUncoveredInventory` go away (a negative `IN` finds the seed holder through V1). | FM3, OL33 | V1, V4 |
| V5 Transfer parity (**done: ARC-DEV-073 = MIG10**) | The session transfer clears FM6's transient state for every travelling follower (focus, last hitter, enemy list, fleeing; combat mode on a cross-map move; the AI ticks are already dropped by ARC-WORLD-084); followers of followers travel (FM1); an unconscious follower is made to wait first (FM2, settled by RL23). | FM1, FM2, FM6 | — (independent of V1) |
| V6 Former followers go home (**done: ARC-DEV-116 = MIG11 S5**, [concept](../architecture/change-of-place.md#6-what-the-session-and-viewer-do-for-the-homecoming-of-former-followers-s5); the teleport map is the object field `npc.teleport`, schema 69) | The NPC's teleport map in the session (set once at its first teleport); when a map opens (or its sector re-folds after the 3-second rule), a leaderless NPC whose teleport map differs is moved to its standpoint on that map, as the original's forced pass does; a snapshot schema bump (main + 1) for the new field. | FM10, FM11, FM13 | V1, V5 |

Not planned: carrying active spells (FM9) until NPC spell runs are saved per
map (MT9b in the [magic plan](magic-implementation-plan.md)); nulling links from
other maps (FM7).

## Unknowns

- Settled since (ARC-RES-086): 0x45D800 tests fatigue, so unconscious followers
  wait (FM2, RL23); a time event whose object fails to rebind fires with no
  object (FM8, RL28); while its sector is evicted an NPC's own timers run and
  its heartbeat ends unless it is near a player (FM11, RL14, RL16).
- Script-held object references across maps (not read).
- Whether map-data NPCs always carry GUIDs (FM4, inference).
