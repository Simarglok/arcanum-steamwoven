# Gap register: Steamwoven against the original's engine model

Status: **Research**, work item ARC-RES-086 (2026-11-04), slice G1 of the
[architecture audit](../research/architecture-audit-plan.md). Docs-only: no code changes
and no checks run. Reviewed after the audit by ARC-RES-088 in the [gap review](gap-review.md) (narrow, decision and
intended rows). This document compares; the model Steamwoven moves to, the owner's
choices and the migration order are in the [target model](target-model.md).

Requirements (reference only, no coverage row changes): the engine spines touch the groups
FR-WORLD ([003](../requirements/world.md#fr-world-003),
[004](../requirements/world.md#fr-world-004), [005](../requirements/world.md#fr-world-005),
[009](../requirements/world.md#fr-world-009), [010](../requirements/world.md#fr-world-010),
[011](../requirements/world.md#fr-world-011), [013](../requirements/world.md#fr-world-013),
[015](../requirements/world.md#fr-world-015), [016](../requirements/world.md#fr-world-016)),
FR-RUNTIME ([001](../requirements/world-travel-runtime.md#fr-runtime-001)–[003](../requirements/world-travel-runtime.md#fr-runtime-003)),
FR-LIFECYCLE ([002](../requirements/ui-media-lifecycle.md#fr-lifecycle-002)–[004](../requirements/ui-media-lifecycle.md#fr-lifecycle-004)),
FR-UI ([003](../requirements/ui-media-lifecycle.md#fr-ui-003),
[008](../requirements/ui-media-lifecycle.md#fr-ui-008)),
FR-SCRIPT ([001](../requirements/story-social-ai.md#fr-script-001)–[003](../requirements/story-social-ai.md#fr-script-003)),
FR-AI ([001](../requirements/story-social-ai.md#fr-ai-001),
[003](../requirements/story-social-ai.md#fr-ai-003),
[004](../requirements/story-social-ai.md#fr-ai-004)),
FR-SOCIAL ([004](../requirements/story-social-ai.md#fr-social-004),
[005](../requirements/story-social-ai.md#fr-social-005)), FR-LIGHT
([002](../requirements/lighting-time.md#fr-light-002)), FR-SCENE
([014](../requirements/scene.md#fr-scene-014)) and FR-NET
([002](../requirements/network.md#fr-net-002)). No status changes: G1 implements nothing.

## Method

- **Original side:** the reference rows of slices R1 to R6, read from the retail 1.0.7.4
  executable and reused by reference, not re-read: [object model and residency](original-object-model.md)
  (OM1–OM32, RL1–RL31), [persistence](original-persistence.md) (PS1–PS34),
  [frame and time](original-time-loop.md) (TL1–TL46), [goals](original-goals.md) (GS1–GS32),
  [scripts, change path and interface modes](original-scripts-authority.md) (SA1–SA55), and the
  [follower move](../research/follower-map-transfer.md) (FM1–FM14) with its fix slices V1–V6.
- **Steamwoven side:** the [model map](steamwoven-model.md) (M1, rows D, S, R, V, G, C, L), made
  on `origin/main` 384152a4. This register is written on `origin/main` 7c664257; between the two,
  the only code change is the new `Arcanum.Executable` assembly (ARC-DEV-067, the interpreter of
  the player's executable), which holds no game state, so M1's rows stand. Spot checks on
  7c664257: the snapshot schema is 58 (`GameSessionSnapshot.CurrentSchemaVersion`), the AI host
  still keeps a carried companion out of its rules (`AreaAiHost.Stranger`).
- **Classes**, as the plan defines them: **systemic** (a model or addressing difference that
  every consumer of a mechanism inherits), **narrow** (one rule or path), **intended** (a
  recorded owner decision, owner rule or rule-mode switch; this register also counts a requirement
  of the registry that states the behaviour, marked "by requirement" in §3). A difference whose intent is not
  recorded and that the owner must settle is marked **decision** and points at its question in
  the [target model](target-model.md#owner-decisions). No gap is classed from a reconstruction.

## Answer

- **All nine hypotheses hold**, three of them in a narrower form than the plan wrote them (H3 in
  part, H6 narrowed, H7 reformulated). H7 is
  not "only the shown map is alive" against "everything is alive": the original keeps every
  object and timer of the open map alive and gates by place exactly the NPC heartbeat, ambient
  lighting, tile data and drawing (RL13–RL20); Steamwoven's gap is that only the shown map or
  window has rows, hosts and positions. H6 holds for the rules, the record lookups, the hero
  and the frame, and for the run-local state they hold (AI runs, movers, the hero's live position
  and actions); all *saved* state is already in the Godot-free session.
- **37 gaps: 20 systemic, 12 narrow, 5 for the owner to decide.** Four roots carry the systemic ones: (1) Steamwoven has **no object**: identity, base
  record, location, flags, scripts, links and created things are spread over map rows, key
  overlays under the home map and one store per kind (GR1–GR5, GR7, GR11, GR17). (2) **Only the shown
  window lives**, and none of its moving state is saved or moved by one path (GR8–GR10, GR16,
  GR18). (3) **More than one clock and two goal machines**: four time bases, and the hero's
  actions are viewer hosts beside a partial NPC executor (GR20, GR21, GR24–GR26). (4) **The rules
  sit in the tool**: hosts, record access, the hero and the frame order live in
  `tools/terrain-viewer` (GR32, GR33).
- **What is already right.** State is keyed by identity in most domains (party, life,
  reactions, experience, danger, combat, durability, NPC items: M1 S6, S11, S14, S16–S20); the
  event queue has the original's scope semantics in one saved queue (owner decision OQ3), the
  admission rule of the clock (TL14) and the deadlines; the snapshot is atomic and validated.
  These are kept by the target model.
- **Intended differences** (8, list in [§3](#3-intended-differences)) are recorded owner
  decisions, requirements or technical-bug fixes: one atomic JSON save, a saved random
  generator, persisted AI ticks, FIFO ties, events at their own deadlines, a safe failed
  transfer and prototypes built from the player's executable.
- **Eight choices were the owner's** (GQ1–GQ8 in the [target model](target-model.md#owner-decisions),
  all decided on 2026-11-04 by the recommendation, GQ3, GQ7 and GQ8 revised the same day by the
  [gap review](gap-review.md#owner-answers)):
  the object state shape, what lives on the open map, pause with windows open, saving actions in
  flight, the change path, where the world logic lives, links to an object on another map, and
  the turn-based option in a save.

## 1. Hypotheses H1–H9

| H | Verdict | Class | Original | Steamwoven | Gaps |
| --- | --- | --- | --- | --- | --- |
| H1 Base records by (shown map, entity) | **Confirmed** | Systemic | One object, one persistent ID on every map; handles live, IDs saved (OM1–OM6, FM3, FM4) | Positional `AreaObjectKey`; rows only among the shown or covered maps (D1, V1–V10); bodies and facts in run-local caches (L1, L2); drawing keyed positionally (L5) | GR1, GR2 |
| H2 No general dynamic object | **Confirmed** | Systemic | One create path and one destroy path for every run-time object, from a blood pool to a summon; all saved alike in `mobile.mdy` (OM15–OM27, PS20) | One numbering and one store per kind (`kit:`, `made:`, `junk:`, `pool:`, placed items, ammunition, thrown items, shots: §1, S10, S11, S26) | GR3, GR4 |
| H3 State as a key vocabulary | **Confirmed in part** | Systemic for map-data objects | An instance stores the fields it wrote over its prototype; one difference record per changed object (OM9, OM10, PS21) | `MapChangeKey` properties under the home map for map-data objects (S3); typed entity-keyed domains elsewhere (S5–S23), which are an equivalent shape | GR5, GR6 |
| H4 Two goal machines | **Confirmed** | Systemic | One run table of 216 runs for the hero, NPCs, doors, projectiles, missiles, eye candy and burning scenery; 87 goal types, one priority rule (GS1–GS32) | Hero walk, attack, cast, throw as viewer hosts (G1–G4); NPCs on `AiExecutorRules` in the AI host (G5), 26 priorities and an interrupt that always succeeds (GS8, GS12) | GR24, GR25 |
| H5 More than one clock | **Confirmed** | Systemic | Real, game and animation clocks advanced once per ping from one stamp; three event lists (TL13–TL22) | Session game time (C1), viewer `WorldTime` (C2) not paused with C1, movement time (C3), frame delta (C4); the AI host's own `_nowMs` (V4) | GR20, GR21 |
| H6 Logic in the viewer tool | **Confirmed, narrowed** | Systemic | No split to copy: the rule functions apply changes; the network meets them inside (SA21–SA35) | All saved state is in `Arcanum.Session`, but the session reads no game data; hosts with their run-local state (V4 runs, V5 movers), record lookup, the hero's position and actions and the frame order are in `tools/terrain-viewer` (§2 D6, R2, S4, S8, V1–V14, §7) | GR32, GR33 |
| H7 Only the shown map is alive | **Confirmed, reformulated** | Systemic | Every object and timer of the open map is alive; place gates only the heartbeat (30 tiles), ambient lighting, tile data and drawing; other maps wait in files (RL13–RL20, RL28) | Only the shown map or window has rows and hosts; a window move or map change drops AI runs and movers; `EntityMap` events wait off their map, which matches the parking (S24, V3–V5) | GR8, GR9, GR10 |
| H8 Script state by home map | **Confirmed** | Systemic | Attachments with their local flags and counters are field 32 of the object, inherited from the prototype until first written, then owned and carried (SA1–SA7) | `dialog.*` and `use.*` keys under the record's home map; item slots by item entity (S12) | GR7 |
| H9 Run-local state | **Confirmed** | Systemic | The save holds goal runs with stacks, spell runs, NPC positions and the event queue; not the generator or AI ticks (PS11, PS15, PS20, PS32) | Goal runs, movers, spell runs, shots, missiles, the hero's route, inventory seeds and companion bodies are lost on a quick load (S8, S25–S27, R1–R3, V4, V5, G1–G5, L1) | GR2, GR10, GR11, GR18 |

## 2. Register

Each row names the original rows and Steamwoven rows it rests on, what goes wrong, the target
element of the [target model](target-model.md) that closes it, and where the work goes: a
migration slice (MIG) of the target model, a domain sweep (D1–D9) or a concept document
(K1–K16) of the [audit plan](../research/architecture-audit-plan.md).

### SP1 Object identity and fields

| ID | Gap | Class | Original | Steamwoven | Consequence | Target | Work |
| --- | --- | --- | --- | --- | --- | --- | --- |
| GR1 | A base record (combat fields, AI row, build, seed holder, worn set, spell target, body template) is found only among the shown map's or the run's covered maps' rows | Systemic (H1) | The object carries every field wherever it stands (OM9, FM3) | `AreaCombatHost.Fields`/`Build`/`HomeMap`, `AreaAiHost.Row`, `AreaInventoryMap` holders, `AreaSpellCastMap.IdOf` read the shown map (V1, V2, V4, V6–V10) | A carried companion is skipped by the AI (`Stranger`); spell targets, hover attack, life bars, turn facts and dialog need home-map fallbacks; eight special cases (ARC-RES-076), and every new consumer repeats them | TM1, TM3 | MIG1–MIG5 (V1–V4b, all done: dialog by ARC-DEV-081); D4, D5, D7 |
| GR2 | Run-local copies of what the original keeps on the object: ~~the companion body `_travellers`~~ (gone: ARC-DEV-079, MIG4), companion facts by row key, refused visitors | Systemic (H1, H9) | Body, art and fields are object fields, saved with it (FM14) | L2, L3 | ~~A quick load or fresh run on a foreign map leaves a companion without a body (`AreaVisitors.Unbodied`)~~ closed by MIG4: the body is read from its home map by identity ([bodies and window homes](../world/record-resolver.md#bodies-and-window-homes-arc-dev-079-mig4)); the visitor's refused dialog (L3) went with MIG5: one dialog host, the session's own refusal ([one dialog host](../world/record-resolver.md#one-dialog-host-arc-dev-081-mig5)) | TM3 | MIG4 and MIG5 done |
| GR3 | No common identity space: `oid:` hashes for map objects, one counter per kind for created ones, positional keys for drawing | Systemic (H2) | 24-byte IDs: GUID for every mobile and every run-time object, positional only for static walls, portals, scenery and traps; never reassigned by a move (OM3, OM4, FM4) | §1 keys; `AreaObjectKey` in L5 | Rules that reach map objects through their rows (scripts, spells, AI, picking) cannot reach a created one without a path of its kind; each kind needs its own drawing, picking and saving | TM1 | MIG6 (MIG6a done, ARC-DEV-084: [object directory](object-directory.md), origin by id, one locator, junk and pool ids never reused) |
| GR4 | One store and one path per kind of created object; no shared create and destroy | Systemic (H2) | One create tail (dynamic flag, sector listing or find index, NPC heartbeat, inventory generation, location notification) and one destroy (script point 2 may refuse; leaves leader and holder; drops invulnerable items; ends spells; drops events) (OM15–OM19) | S10, S11, S26, viewer `_projectiles` (G4) | Summons, generator copies, encounter creatures, traps and script-created critters (K4–K6) would each need a new store; destroy rules differ per kind. **MIG6 (ARC-DEV-086, -088, -090, -091): items, junk piles and pools have one create and one destroy path and records found by entity; the other kinds have no store yet.** | TM4 | MIG6; K4, K5, K6 |
| GR5 | Mutable state of map-data objects is a project key vocabulary under the home map (`obj.off`, `obj.destroyed`, `npc.cell`, `npc.map`, `npc.faction`, `follow.pc`, `lock.*`, `trap.state`, `item.dropped`, `object.damage`, `object.condition`) | Systemic (H3) | Changed fields of the object itself in one difference record format, destroyed IDs in a list, statics by sector order (OM10, PS20, PS21, PS24) | S3 | An object that leaves its home map keeps its state on the home map's keys; every new field is a new key or domain; the static and mobile classes are not told apart | TM2 | MIG6, MIG7 (MIG7a done, ARC-DEV-092: the fields of mobile map-data objects are kept by identity and saved as `objects.state`, schema 62, [object state](object-directory.md#mig7a-what-is-built); MIG7b done, ARC-DEV-093: the fields of static objects are kept by the map-qualified identity (map and `sector:S:I`) in the same store, schema 63, [static state](object-directory.md#mig7b-what-is-built); NPC positions MIG7c done, ARC-DEV-094: the place of a walking map NPC is the field `npc.pos` of its entry, schema 64, [NPC positions](object-directory.md#mig7c-what-is-built)) |
| GR6 | No prototype fallback at read time: a created object stores its own record; map rows are immutable T0 copies | Decision (H3; a difference of shape that consumers do not inherit) | An instance reads every unwritten field through its prototype; creation copies type, prototype, location and ID only (OM9, OM13) | D1, D2, S10, S11 | Equal behaviour as long as no prototype changes in play (for scripts none was found, R6 answer); a cost in created objects and saves, not a visible bug; closed only under GQ1 B | TM2 | Decision GQ1 |
| GR7 | Script attachments (number, 32 local flags, four counters) are keys under the record's home map, not on the object; no inheritance from the prototype; created objects have none | Systemic (H8) | Field 32 of the object, read through the prototype until the first write deep-copies the array; saved, copied and carried with the object by generic code (SA1–SA7, SA17) | `dialog.lf`, `dialog.counters`, `dialog.script`, `use.*` (S3, V7, V8); item slots by entity (S12) | A carried companion's dialog state was found only through `ForVisitor` (one host since ARC-DEV-081, which reads it on the record's map); a script-created or copied object starts with no attachments where the original inherits its prototype's | TM9 | MIG8; D8; slice 1 done, ARC-DEV-096: the attachments are fields of the object (`ObjectStateVocabulary.Attachment`, mobile by identity and travelling with it, static per map; `objects.state`, schema 66), read through the base record until a part differs; slice 2, ARC-DEV-097: a made item the session creates reads its prototype's array until its first write, which copies all points onto it (`AttachItemScripts`, `QueryItemScriptSlot`; no schema change); open: the other creators attach no prototype source yet |

### SP2 Residency and the map change

| ID | Gap | Class | Original | Steamwoven | Consequence | Target | Work |
| --- | --- | --- | --- | --- | --- | --- | --- |
| GR8 | Only the shown map or main-map window has rows, hosts and moving objects | Systemic (H7) | Every non-static object of the open map is in the pool from open to close, loaded sector or not; a global index finds it by sector; queries for mobile objects work on absent sectors (RL1–RL4, RL9) | D1, V3–V5; main map as a 3 × 3 window (FR-WORLD-003) | NPCs out of the window do not exist for the rules; a window move rebuilds hosts and drops runs; a long main-map walk loses every NPC position and run on the way (saved map changes survive) | TM5 | Decision GQ2; MIG9; K1, K2 |
| GR9 | No residency classes, sector cache, fold or eviction history | Systemic (H7) | Statics belong to a sector of a 16-slot cache; mobile objects are listed only while their sector is loaded; the fold lists them, gives each NPC a first tick and, after 3 s away, re-places leaderless NPCs on their waypoint (RL1–RL3, RL7, RL8, RL17, RL20) | No cache; the AI host gives every NPC of the shown map a delayed tick when built and never re-places one (S24, V4) | No waypoint or standpoint return, no first-tick timing, no day-night scenery switch on load (RL19) | TM5 | MIG9; K1, K3 |
| GR10 | NPC positions are not state: the AI host's movers are run-local and a load, map change or window move stands every NPC on its T0 cell | Systemic (H7, H9) | A position is field 2 of the object, kept in memory for the open map and saved as a difference (RL5, PS20) | V5; companions only through `npc.cell`/`npc.map` (S3) | Quick save and load teleports every moved NPC back; a fled or chasing NPC resets | TM2, TM5 | MIG7 (MIG7c done, ARC-DEV-094: the cell and facing the AI host last gave a map NPC are its object state `npc.pos`, saved in `objects.state` and restored when a host is built for the map; the goal runs, the heartbeat beyond the window and the fold's re-placement stay with MIG9 and MIG14) |
| GR11 | Inventory seeds are not state: rebuilt after every load for the loaded maps only | Systemic (H1, H9) | Items are objects with their holder as parent, in the map's files (OM23, NI rows) | S8, V1 | A rule that needs an NPC's or container's T0 items on an unloaded map has none; ~~negative `IN` needs `refuseUncoveredInventory`~~ (gone: ARC-DEV-081, MIG5: the session refuses an entity its hero-map seed does not list; a cross-map holder waits for MIG6) | TM3 | MIG1, MIG5 |
| GR12 | Location notification only for the hero and following companions: tile scripts, wading and sector-entry effects (corrected by [ARC-RES-088](gap-review.md#gr12-location-notification-tile-scripts-wading-sector-entry)) | Narrow | Every relocation of a PC or NPC runs the tile's scripts and the wading test; the local PC's sector change runs the sector script and point 27 for each follower; traps spring in the walk step only (RL5, RL6, RL18) | Tile and sector scripts in the transition coordinator for the hero (V12, SA18); a following companion of the shown map runs the tile scripts it steps into (ARC-SCRIPT-046); wading is drawn from the T0 flag of a map body (`AreaScenePlan`), the hero never wades, and no relocation sets it | Tile scripts entered by AI-host movers, visitors, jumps, teleports and created critters never fire; followers miss point 27 | TM5 | MIG9; D1, D8 |
| GR13 | The heartbeat is wired for the shown map only: no waypoint, standpoint or spawner step, and the far branch never ends a heartbeat (corrected by [ARC-RES-088](gap-review.md#gr13-the-heartbeats-world-side-routine-generators-far-branch): points 10 and 19 run) | Narrow (rules exist) | Near branch within 30 tiles runs point 19 and the pass; far branch stops animations and brings a follower to its leader; the heartbeat ends off a loaded sector; encounter creatures are destroyed (RL16, RL17) | `AiSchedulerRules` has the tick, the near test, the far branch and the catch-up; the host answers "loaded" for every NPC (`AreaAiHost.SectorLoaded`); the AI tick runs points 10 and 19 in a narrow admission set (ARC-WORLD-114, ARC-SCRIPT-047, ARC-SCRIPT-049) and no other AI point (V4, [AI host](../world/ai-host.md)) | No daily routine, no generators, no follower catch-up on unloaded ground | TM5, TM7 | MIG9; D7; K3, K4 |
| GR14 | Transfer parity: followers of followers do not travel, unconscious followers are not left waiting, FM6's transient fields are not cleared, a travelling follower's AI tick is dropped | Narrow | Move list walks followers recursively; 0x45D800 (fatigue: unconscious) makes followers wait; focus, last hitter, enemy list, fleeing and substitute inventory are cleared; combat mode left; the carried AI tick is replaced by the fold's first tick (FM1, FM2, FM6, RL22, RL23, RL28) | Session transfer by `npc.cell`/`npc.map`; ARC-WORLD-084 removes every tick | Wrong group on arrival; stale fight links after a move | TM12 | MIG10 (V5), **done: ARC-DEV-073** (followers of followers need NPC leaders in the session, none exist yet; the carried tick keeps its old deadline until MIG9) |
| GR15 | Former followers never go home | Narrow, missing feature | Field 243, the teleport map, set once; a forced waypoint or standpoint pass teleports a leaderless NPC to its teleport map (FM10, FM11, RL17) | No teleport map field | A dismissed companion stays on the foreign map forever | TM5, TM12 | MIG11 (V6); K3 |
| GR16 | No change-of-place request: each caller moves things itself, at once | Systemic (SP2) | One request slot processed by the teleport ping on the next frame: fades, movies, clock advance, wait orders, move list, payload, flush, close, open, pre-cache, Teleported events; callers are jump points, scripts, teleport spells, NPC waypoints, the world-map landing and the new game (RL21–RL24, RL29) | The transition coordinator from the frame chain (V12); world-map landing and scripts have their own paths | Teleport spells, script teleports, NPC homecoming and transition effects (K5, K8) each need their own path; the order of steps differs per caller | TM12 | MIG11; K5, K8 |

### SP3 Persistence

| ID | Gap | Class | Original | Steamwoven | Consequence | Target | Work |
| --- | --- | --- | --- | --- | --- | --- | --- |
| GR17 | Created objects are saved in one store per kind, not as one section of whole objects per map | Systemic (H2) | `mobile.mdy`: every dynamic object that is not destroyed, whole, the hero and carried followers and items among them (PS20, PS27) | S10, S11 per kind | Each new kind is a new snapshot section and schema step. **ARC-DEV-091: junk piles and pools are one section `objects` (schema 61); made and placed items are still in `inventory`, the section of whole objects per map is TM8.** | TM4, TM8 | MIG6 |
| GR18 | Actions in flight are lost on a load: NPC goal runs, the hero's route and action, spell runs, shots, missiles | Systemic (H9) | The Anim module saves every run with its goal stack; MagicTech saves spell runs and maintained marks (PS11, PS15, PS16) | S25–S27, V4, G1–G5 run-local | A maintained spell ends at a quick load; NPCs restart decisions; a save mid-fight is not the fight | TM8 | Decision GQ4; MIG14 |
| GR19 | The turn-based round is saved as is; the original restarts the order and the round number at load | Narrow | Combat blob: order rebuilt, round number restarts at 1 (PS13) | S19: round saved, subturns not; **built** by ARC-DEV-078 (owner's answer GR19 A): the subturn starts are saved with the round, the Original setting rebuilds at the load (`RoundRebuiltOnLoad`), the failed load fixed in both | A load in a round resumes where the original restarts; visible in round-numbered rules only | TM8 | D4 |
| GR37 | The turn-based setting is never in a save | Decision | The Combat blob keeps the module's cached copy of the turn-based setting, and a load writes it back, so a loaded game plays in the mode it was saved in until the option changes (PS34, consequence inferred) | A preference only: `AreaGameOptions.TurnBased` (`tools/terrain-viewer/Core/AreaGameOptions.cs`) is stored with the game settings, and the snapshot's root fields (`GameSessionSnapshotCodec`) have no turn-based field; a round running at the save is in the snapshot (S19) | A save made in turn-based mode outside a round loads in the player's current mode | TM8 | Decision GQ8; D9 |

### SP4 Time and the frame

| ID | Gap | Class | Original | Steamwoven | Consequence | Target | Work |
| --- | --- | --- | --- | --- | --- | --- | --- |
| GR20 | Four time bases instead of three clocks of one ping; goal steps, AI, projectiles and missiles run on Godot `WorldTime`, which the menu pause does not stop | Systemic (H5) | Real 1×, game and animation 8× the admitted interval; game and animation stop with the pause counter, game alone in a round; goal steps run on animation time (TL13–TL22, TL38) | C1–C4; the AI pump has no gate (C2); the AI host keeps its own `_nowMs` (V4) | NPC runs advance on Godot time while the session clock stands; timings depend on the host's time; no run can be saved consistently with the clock it waits on | TM6 | MIG12 |
| GR21 | No real-time and animation event domains in the session: UI timers, sleep and world-map steps, the turn-based step run from the frame delta | Systemic (same root as GR20) | Real-time list fires behind the pause (sleep and travel chains, UI timers, the TB event); animation list drives goals (TL24, TL26, TL28) | The queue core has the three domains, the 17 session kinds are all game-domain (TL19, TL24, TL32) | Steps tied to frames; a world-map step behind the Esc menu differs | TM6 | MIG12 |
| GR22 | Pause with any modal window | Decision | Only the Esc menu raises the pause counter; dialog, inventory, sheet, barter, logbook, world map and sleep run in real time (TL23, TL46) | A Menu pause for every modal window (`WorldModal`, TL11, TL24; noted in [schematic window](../world/schematic-window-runtime.md)) | Real-time danger while a window is open differs from the original | TM6 | Decision GQ3; D9 |
| GR23 | Sleep and random encounters missing | Narrow, missing feature | Sleep +1 h per real-time step, travel ⌊tiles / 64⌋ h per step, jumps fire nothing themselves (TL26–TL29) | No sleep window (FR-TRAVEL-004); encounters not started (FR-TRAVEL-003) | — | TM6 | K4, K7 |

### SP5 Actions and goals

| ID | Gap | Class | Original | Steamwoven | Consequence | Target | Work |
| --- | --- | --- | --- | --- | --- | --- | --- |
| GR24 | The hero's actions are viewer hosts beside the NPC executor | Systemic (H4) | Hero walk, attack, talk, use, pickup, throw, skill and cast are goals of the same table with the same nodes, priorities and interrupts; the click builds goals 3, 15, 18, 19, 21, 37, 38, 81 itself; Shift, Alt and Ctrl rules (GS1–GS3, GS25–GS28) | Click route plus "after route" intents, `AttackGoal`, `SpellCastGoal`, held throw (G1–G4) | A stun, knock-down or interrupt cannot stop the hero the way it stops an NPC; talk, use and loot are not interruptible goals; every hero action is a second implementation | TM7 | MIG13; D4, D5, D9 |
| GR25 | The NPC run model is partial: one run per NPC, priorities for 26 goal types, an interrupt that always succeeds, no levels, overlays, alias table or end nodes | Systemic (H4) | 85 of 87 goal types have a priority 1–5; five effective levels; 15 no-interrupt overlay goals; an interrupt ends the whole run with every end node (GS8–GS24) | `AiGoalRun`, `AreaAiHost.Priorities`, `InterruptLowerAnimations` (GS8, GS12, GS16) | A walk can replace an attack; a spell release can be cut | TM7 | MIG13; D7 |
| GR26 | Doors, projectiles, spell missiles, eye candy, burning scenery, dying, stun, knock-down and get-up are not runs | Systemic (H4), narrow effects | Each is a goal run: a door's own run opens, waits 1.5 s in goal 72 and closes (unless portal flag 0x100); damage starts stun 57 or knock-down 62; death starts dying 31 (GS29, GS31) | Viewer objects and animations; portal state without a close timer (GS29, GS31) | Doors never close by themselves (FR-SCENE-014, FR-AI-003); stun, knock-down and dying cannot interrupt a running action through the priority rule | TM7 | MIG13; D2, D4 |
| GR27 | Scripts start no goals | Narrow | Script actions perform animation, walk to, run to, use item, steal, get up and the casts start goals; stop attacking and become prone stop them (GS30) | Script hosts start no goal runs (V7, V8) | Scripted walks and animations are missing | TM7, TM9 | D8 |

### SP6 Scripts

| ID | Gap | Class | Original | Steamwoven | Consequence | Target | Work |
| --- | --- | --- | --- | --- | --- | --- | --- |
| GR28 | Writeback semantics differ: explicit writeback records applied by the host, one run per host call | Narrow | A run works on a copy and overwrites all twelve bytes once if anything changed; no nesting guard; the dialog action commits flags and counters mid-run (SA8–SA11) | `ScrDispatcher` returns `ScrWriteback` records (SA8, SA10, SA11) | A nested run on the same slot differs (the original keeps the inner run only if the outer changed nothing) | TM9 | D8 |
| GR29 | Admission refuses global variables, PC variables and PC flags | Narrow, missing feature | 2000 global variables, 3200 global flags, a story number; PC flags and variables are fields 272 and 273 of the PC (SA13, SA14) | Global flags and story state only (S2, S5; SA13) | Scripts that read or write them are refused (FR-SCRIPT-002) | TM9 | MIG8; D8; slices 3 and 4 done: ARC-DEV-098 stores and saves the global variables and the hero's flags and variables (`ScriptStores`, `scripts` section, schema 67); ARC-DEV-100 admits the value kinds 1, 5, 6 in set S21 and the viewer's dialog and use hosts read and write the stores (Dernholm's heartbeat 1015 is admitted); open: the item events and the DLG conditions and actions over PC flags and variables (0x4150D0, 0x415BA0) |

### SP7 Relations between objects

| ID | Gap | Class | Original | Steamwoven | Consequence | Target | Work |
| --- | --- | --- | --- | --- | --- | --- | --- |
| GR30 | Destroy semantics of links: a destroyed holder drops only invulnerable items, links to a destroyed target stay until the map is cleared, a destroyed follower leaves its leader | Narrow | OM17, OM18, OM29 | Each store removes its own records (OM17's Steamwoven column); a busted container already spills its items as the original's bust branch does (ARC-COMBAT-008, OD13), except that a never-opened container spills its T0 items instead of first generating its inventory; the script destroy (ARC-SCRIPT-052) records `obj.destroyed` with no item step (corrected by [ARC-RES-088](gap-review.md#gr30-destroy-a-holders-items-and-links-to-the-destroyed)) | Container destruction and leader death differ per store | TM4, TM10 | D3, D7 |
| GR31 | Summoner, substitute inventory and the scenery and projectile links have no home | Narrow, not built | The summoned list of the spell run, field 294, fields 70 and 80 (OM25, OM28) | A merchant's substitute container is a lookup of the viewer's inventory host per shown map (`AreaInventoryHost`, ARC-WORLD-090), not a link, not cleared by a move; the rest not modelled (corrected by [ARC-RES-088](gap-review.md#gr31-summoner-substitute-inventory-scenery-and-projectile-links)) | Needed by summons (K5) and pick pocket's substitute inventory | TM10 | K5; D3 |
| GR36 | A link to an object that left for another map keeps its identity | Decision | A link from an object of map A to one now on map B becomes null when A opens again and is lost at A's next save; the travelling group rebinds because it arrives together (FM7, OM31) | Entity references in the domains survive any move; ARC-RES-076 proposed keeping that ("not planned") without an owner decision | An NPC's last hitter, focus or enemy list naming a companion who left is kept where the original forgets it | TM10 | Decision GQ7 (B: a rule-mode switch); D7; MIG7d done, ARC-DEV-095: the NPC links (focus, fleeing from, who hit it last, enemy list) are fields of the entry with the map they were written on (`objects.links`, schema 65), and the Original setting erases a link to an object that left when the map opens (`LifecycleRuleProfile.OpeningMapNullsLinksToLeftObjects`), Steamwoven keeps it ([object directory](object-directory.md#mig7d-what-is-built)); the NPC leader, summoner and other TM10 links stay GR31 |

### SP8 Authority and placement of the logic

| ID | Gap | Class | Original | Steamwoven | Consequence | Target | Work |
| --- | --- | --- | --- | --- | --- | --- | --- |
| GR32 | The rules that read base records and drive the world are in the tool: `tools/terrain-viewer/Core` (about 27 k lines) and `Godot` (about 33 k) | Systemic (H6) | — (the original has one program; the comparison is with the owner's [simulation boundary](decisions.md)) | The session never opens game data; facts reach it as call arguments from viewer hosts (§2 D6, S8, S27, V1–V14) | Every rule that needs a base row lives in the viewer; the alpha app runs a diagnostic tool's hosts; tests of the rules need the viewer | TM11 | Decision GQ6; MIG15 |
| GR33 | The hero's live position, actions and the frame order belong to the Godot viewer; the session gets the position at commit points | Systemic (H6) | The PC is an ordinary object; every step writes its location (RL5); the frame's order is the module table (TL2, TL5) | R2, S4, G1–G4, §7 frame order | A rule that reads the hero's place from the session between commit points (an event handler, a script, an NPC decision) sees a stale cell; the frame order is a Godot method | TM11, TM14 | MIG13, MIG15 |
| GR34 | No named command boundary | Decision (not a gap against the original) | No single change path: rule functions branch single player, host and client themselves; client requests are sent from inside rule functions (SA21–SA35) | Viewer hosts call the session owner directly (SA31) | A future network game (FR-NET-002) and replays have no seam | TM11 | Decision GQ5; K11 |

### SP9 Input and interface modes

| ID | Gap | Class | Original | Steamwoven | Consequence | Target | Work |
| --- | --- | --- | --- | --- | --- | --- | --- |
| GR35 | No interface mode stack: cursor mode derived from which panel is open, each window its own flag | Narrow, structural | One stack of 21 modes; windows replace the top, pickers are pushed, target modes over an inventory are pushed and pop twice; the game view acts only in modes 0, 1, 2, 3, 16, 18 (SA36–SA45) | `AreaCursorMode`, per-panel flags `WorldModal`, `WorldUiFocus` (SA37, SA40, SA43) | Esc, target modes over inventory and window-to-window moves differ case by case | TM13 | MIG16; D9 |

## 3. Intended differences

Recorded decisions, requirements or applications of the owner's rule on technical bugs; the
target model keeps them.

| ID | Difference | Original | Steamwoven | Record |
| --- | --- | --- | --- | --- |
| GI1 | One atomic, validated JSON snapshot with stepwise schema upgrades instead of a folder archive and module blobs | PS1–PS4 | Write, read back, compare, replace; typed failures; ARC-UI-037 upgrades | By requirement: FR-LIFECYCLE-002, FR-LIFECYCLE-003 |
| GI2 | Parked and carried events in one queue with a scope per event instead of per-map files | PS26, RL28 | `Global`, `EntityMap`, `ActiveMapOnly` | Owner decision OQ3 ([event queue](../research/game-event-queue.md)) |
| GI3 | Ties fire first scheduled first | TE3 | Persisted sequence | Owner decision OQ1 |
| GI4 | AI ticks are saved; the Original rule rebuilds them on load | PS31 | `ai.tick` persisted | Owner decision OQ2 |
| GI5 | Events fire at their own deadlines and repeaters do not drift | TL20–TL22 | Pump steps to each deadline, up to 64 steps a frame | By requirement: FR-RUNTIME-001, ARC-WORLD-039 |
| GI6 | The random generator is saved | PS32 | Seed and draws (S21) | By design of session randomness, ARC-WORLD-019 (the seeded replays of the AI plan rely on it, as OQ2's reason says); owner decision 2026-11-04 (ARC-RES-088, [gap review](gap-review.md#owner-answers)): kept in Steamwoven, which turns off save-scumming; the Original setting reseeds at a load and keeps only a saved next skill roll (a switch, not built yet) |
| GI7 | A failed transfer leaves the session on its map | RL30 (no map, nothing rolled back) | Nothing half moved | Owner rule on technical bugs; FR-WORLD-016 |
| GI8 | Prototypes on a fresh installation are built from the player's executable | OM12 (the game writes the missing files on its first run) | ARC-RES-085 | Owner decision 2026-11-03 |

## 4. Earlier records corrected in this PR

R1 to R6 listed records they contradict and left them for G1. Each is now edited in place with
a pointer to the row that settles it.

| Record | Correction | Source |
| --- | --- | --- |
| [FR-LIFECYCLE-003](../requirements/ui-media-lifecycle.md#fr-lifecycle-003) observation | The library load compares the `data.sav` version with 25 and refuses another | PS4 |
| [combat-domain-load-map](../research/combat-domain-load-map.md) load row | Same | PS4 |
| MW22 ([UI and audio ledger](../research/ledger/ui-audio.md)) | The info file is `save\<slot><description>.gsi` | PS1, PS5 |
| [Main-map streaming](../research/main-map-streaming.md) "mobile objects" row | Portals are static unless dynamic | PS20, PS24, RL1 |
| OM10 | The full record is used in the module's `.sec` files and `mobile.mdy`; the game's sector difference files hold difference records | PS24 |
| FM2 (follower research and [social ledger](../research/ledger/social.md)) | Settled: 0x45D800 reads fatigue, so unconscious followers wait | RL23 |
| FM7 (follower research and social ledger) | The Teleported event runs the whole arrival notification in mode 2 | RL6, RL27 |
| FM8 (follower research and social ledger), P3, P7 ([event queue](../research/game-event-queue.md)) | The carry runs before the payload removes AI events: the follower's tick is carried, then replaced by the fold's first tick; one tick | RL23, RL28 |
| TL40 | 15 no-interrupt goals, eight of them eye candy; read on the current goal, ignored at level 6 | GS9, GS16 |
| TL42 | 0x4357B0 starts the stun (goal 57); the knock-down starter is 0x435A90 (goal 62) | GS13 |
| TL43 | The first frame of a new goal is queued at 5 ms (1 ms into the live list for a non-current owner in a round) | GS7, TL41 |
| PS15 | A goal frame has 21 slots in memory, the save writes slot 20 as −1 | GS1, GS5 |
| AG1, AG4, AG7 ([NPC AI ledger](../research/ledger/npc-ai.md)) | Step word bits 0x1000000 and 0x2000000; "frame flags 0x4 and 0x8" are run flags at +0xC | GS6 |
| EX22 | The back-off run starter calls the movement gate, so it is gated in a round | GS31 |
| TH9 | The fidget start accepts the hero as well | GS28 |
| [AI execution](../research/npc-ai-execution.md) starters | Second dead starter: skill conceal 0x436370 (goal 35) | GS32 |
| GS25 | The turn toward the cursor is on the right button's release | SA36, R6 Q8 |
| E11 ([SCR dispatcher](../research/scr-event-dispatcher.md)) | The dialog action also persists a run's flags and counters, mid-run | SA8 |
| [Multiplayer](../research/multiplayer-evidence.md) and NT6 of the [multiplayer ledger](../research/ledger/multiplayer.md) | 0x4B43A0 is inside the damage entry 0x4B4390; the client request list gains 5, 7, 8, 9, 19, 30, 31, 44/2, 48, 50, 80, 96, 117, 118, 122, 125, 126 and 100/12; 1 and 81 were not found as client sends; the dispatcher 0x49D690 is reached only as a registered callback | SA25, SA31–SA33 |

Not edited here: the [function registry](../verification/functions.csv) row of 0x434F80 (the
equal-goal test walks every run of the object, GS4), because the registry is test data read by
`FunctionRegistryTests` and this PR is docs-only; the D7 sweep corrects it. FM1 and FM9 were
re-read by R2 and R3 and stand. Answered open questions are marked in their
documents: R3 Q5's second half (RL28), R4 Q2's first half (GS29), R4 Q6 in part (R5 Q1 holds
the rest).

## 5. Open questions carried to the sweeps

The open questions of R1 to R6 that no G1 choice depends on, with the sweep or concept that
reads them when its domain is built (owner rule of 2026-10-20: facts are read when the domain
is implemented).

| Questions | Topic | Goes to |
| --- | --- | --- |
| R1 Q1, R1 Q4 (traps), R1 Q7 | Summoner as follower; trap device creation; mobile-only queries of absent sectors | K5, K6, K1 |
| R3 Q5 (first half) | Events and runs of an NPC or a group without the hero sent to another map | MIG11, K5 |
| R1 Q2, R1 Q10, R3 Q7 | Prototype file folder; type-2 map at new game; multiplayer save parts | ARC-RES-085 follow-ups, D9, K11 |
| R1 Q5, R3 Q3 | Items left in a destroyed holder | D3 |
| R1 Q6, R1 Q8, R1 Q9, R1 Q11, R1 Q13, R1 Q14, R3 Q1, R3 Q2, R3 Q10 | Sector locks, fold timing, line-of-sight loads, statics in links, failed sector loads | K1 (MIG9 reads what it needs) |
| R1 Q12, R3 Q8 | Interface map-change hooks; eye-candy events after a load | D9, D4 |
| R3 Q4, R3 Q6, R3 Q9 | Record formats (`.dif`, `Anim.dat`, `MT.dat`, the field kind table) | MIG7, MIG14 (only if a format must be matched; GI1 keeps JSON) |
| R4 Q1, R4 Q3, R4 Q4, R4 Q5 | Popup buttons, another participant's subturn, AI deciders in a round, item spell AP | D9, D4, D7, D5 |
| R4 Q7, R4 Q8 | DDrawCompat, loading screen, network clock | D9, K11 |
| R4 Q9, R6 Q1, R6 Q2, R6 Q3 | Script points 10, 15, 16, 19; dialog callback order; dialog gate; prototype operands | D8 |
| R5 Q1–Q8 | Carried runs at a new map; end nodes; frame flag 0x80; run flags; doors while walking; spell-named goals; mode 3 and Alt; full stacks | MIG13, MIG14, D2, D5, D9 |
| R6 Q4, R6 Q5 | Packets without a sender; host sender check | K11 |
| R6 Q6, R6 Q7 | Inventory modes 19 and 20; Esc in target modes | D9 |

## 6. What each sweep and concept takes from the register

| Work | Gaps it builds on or closes | Before it starts |
| --- | --- | --- |
| D1 World, maps, transitions, travel, lighting, time | GR8–GR10, GR12, GR14–GR16, GR21, GR23 | TM5, TM6, TM12 decided (GQ2, GQ3) |
| D2 Scene objects, doors, containers, roofs | GR5, GR26 (doors close), GR9 (static class) | TM2, TM7 |
| D3 Items, inventory, economy | GR3, GR4, GR11, GR17, GR30, GR31 | TM1–TM4 (GQ1) |
| D4 Combat | GR1, GR24, GR25, GR26, GR19 | TM1, TM7 |
| D5 Magic and technology | GR1, GR18 (spell runs), GR24 (cast) | TM7, TM8 (GQ4) |
| D6 Character, skills | GR24 (skills as goals), GR29 (PC variables) | TM7, TM9 |
| D7 NPC AI, followers, reputation | GR1, GR13–GR15, GR25, GR30, GR36 | TM3, TM5, TM7, TM10 (GQ7) |
| D8 Scripts, dialogs, story | GR7, GR12, GR27–GR29 | TM9 |
| D9 Interface, lifecycle, saves | GR18, GR22, GR33, GR35, GR37 | TM8, TM13 (GQ3, GQ4, GQ8) |
| K1 Living world beyond the window | GR8–GR10, GR13 | GQ2; written as MIG9's design: [residency](residency.md) (ARC-DEV-101) |
| K2 Generated main-map sectors in play | GR8, GR9 | K1 |
| K3 Daily routine, standpoints, doors for NPCs | GR9, GR26, GR13, GR15 | TM5, TM7 |
| K4 Generators, respawn, decay, encounters | GR4, GR17, GR13, GR23 | TM4, TM5 |
| K5 Summons and teleports | GR4, GR16, GR31 | TM4, TM12 |
| K6 Traps and placed devices | GR4, GR9 (traps are static) | TM4, TM5 |
| K7 Sleep and rest | GR21, GR23 | TM6 |
| K8 Transition effects and time | GR16 | TM12 |
| K9 Kill log, injuries, companions screen | GR5 (hero fields), GR31 | TM2 |
| K10 Newspapers and gambling | — | — |
| K11 Network readiness | GR34 | GQ5 |
| K12 Movies, slides, endings | GR20 (frozen-ping modes, TL46) | TM6 |
| K13 Object directory and field store (new) | GR1–GR7, GR17 | GQ1; written as MIG6's design: [object directory](object-directory.md) (MIG6a done, ARC-DEV-084; MIG6b, ARC-DEV-085; MIG6c, ARC-DEV-086; MIG6d-1, ARC-DEV-088: the NPC and container lists as entries; MIG6d-2, ARC-DEV-090: the hero's lists, placed items and given records as entries; MIG6d-3, ARC-DEV-091: the junk piles and pools of blood as records, one snapshot section) |
| K14 One goal table for every animated object (new) | GR24, GR25, GR26, GR27 | TM7; MIG13's design |
| K15 Clocks and the frame in the session (new) | GR20, GR21, GR22 | GQ3; MIG12's design |
| K16 Interface mode stack (new) | GR35 | TM13; MIG16's design: [interface modes](interface-modes.md) (S1, ARC-DEV-108: the stack and its tables in `Arcanum.World`; S2, ARC-DEV-109: the viewer's windows open through it and the cursor and the modal test read it; S3, ARC-DEV-110: its target modes and overlays are on it too) |

K13 to K16 are new concept documents: the target model changes these parts enough that each
needs its own design before its migration slice, as the plan's last paragraph of phase 4
allows.
