# Missing systems: map and work plan

Status: **Plan, accepted by the owner as the work order** (2026-11-10, answers in section 9), work item ARC-RES-099. Docs-only: no source
change, no checks run. Each slice is still handed out as its own task: it starts when the coordinator gives it a thread and
takes its task code from the coordinator (the labels `E1-a`, `E4-c` and so on are planning labels, not task codes).

It continues the [architecture audit](../research/architecture-audit-plan.md) (which settled the engine model) and the two bug triages
([2026-11-08](../research/bug-reports-triage-2026-11-08.md), [2026-11-10](../research/bug-reports-triage-2026-11-10.md)). Those fix
individual bugs; this document lists the **large parts of the game that are not built or only stubbed**, checks each against the code,
and orders the work. It does not repeat bug fixes that are running (section 7).

Main requirement groups the plan touches (each epic names its own; reference only, no coverage row changes): FR-MAGIC-007…011, FR-AI-004, FR-TRAVEL-003…005,
FR-ITEM-016, FR-ITEM-019, FR-LIGHT-011, FR-SCRIPT-001…004, FR-DIALOG-003, FR-DIALOG-004, FR-SOCIAL-009, FR-MEDIA-001, FR-MEDIA-002,
FR-UI-001, FR-UI-002, FR-UI-006, FR-UI-007, FR-UI-008, FR-PRODUCT-002, FR-PRODUCT-005, FR-CONTENT-003, FR-CONTENT-005,
FR-CONTENT-008, FR-NET-001…008, FR-SOCIAL-006 (the [requirements register](../requirements/README.md)).

## 1. Answer

- **Ten epics** (section 4), sized S/M/L with slices that can go to separate threads (section 5). Rough total: about 50 slices,
  of which 7 are research reads or censuses on the owner's Mac (section 6).
- **Epic 1, "Summon and objects created at run time", comes first** (the coordinator's and the B47 report's need). It is the one
  missing foundation under three other epics: no code path creates a critter or a scenery object on a map, and the Summon spell
  component is not built. Seven slices (one a read), size L overall (section 4, E1).
- **Three epics depend on E1:** population (monster generators, E2), the rest of the spell components (E4: some
  components need NPC magic state from E1) and placed traps (E6: a placed trap is a created object). The AI's summoned allies are E1's last slice.
- **Independent of E1 and can start at once:** the documentation re-baseline (E0), sleep and the NPC day (E3), script and dialog coverage
  (E5, its first slice is a census), the leftover player windows (E8), media (E7, after a decoder decision).
- **Deferred by earlier owner decisions** (listed, not planned): the network game, a second module, mods (E10).
- **The documents are behind the code.** The coverage matrix is dated 2026-10-20 and still says there is no world map screen, no
  trap code, no logbook tabs; section 2 is the code-checked state, and slice E0-a refreshes the matrix.
- **Questions for the owner on priorities** are in section 8.

## 2. Method and its limits

- **Documents read:** [roadmap](roadmap.md), [status catalogue](status.md), [vision](vision.md), the [coverage matrix](../requirements/coverage.md)
  (every row's "what is missing" column), the [architecture audit plan](../research/architecture-audit-plan.md), the
  [target model](../architecture/target-model.md) migration list, the [decisions](../architecture/decisions.md), the
  [magic](../research/magic-implementation-plan.md), [skills](../research/skills-plan.md), [technology](../research/technology-evidence.md)
  and [NPC AI](../research/npc-combat-ai-plan.md) plans, the [lights](../research/lights-on-all-maps.md) plan, the
  [multiplayer research](../research/multiplayer-evidence.md) and both bug triages.
- **Code:** `origin/main` at `5321e8449`. Five read-only surveys by symbol search (magic and run-time objects; living world and time; UI,
  journal and media; items, skills and traps; scripts, AI and product), each item answered BUILT / PARTIAL / STUB / ABSENT with a
  file and symbol. I re-checked by hand the claims that decide the plan: the component dispatch of
  `SessionSpellCaster.Apply` (it runs Damage, Heal, Destroy for lying items, eye-candy log, knockback and hover goals, four object
  flags (entangled, stoned, paralysed, spoken-with-dead) and the hero's Effect; every other type falls to `ComponentNotReady`), `AreaAiHost.Spawn(npc) => null`, and the game bar's
  "not built" notices (skills, fate points, sleep). Other rows are the surveys' reading, marked "survey" where I did not re-read them.
- **Not done:** no viewer run, no build, no original-data run, no executable read. "Present" means a code path exists, not that it
  matches the original; parity claims stay with the [ledger](../research/original-verification-ledger.md).
- **Sizes:** S one PR of about 200 lines; M one PR with a smoke stage; L more than one slice. "Mac read" is a slice that reads the
  retail executable on the owner's Mac ([original executable check](../research/original-executable-check.md)); "cloud" is docs-only.

## 3. Map of the systems

### 3.1 Not built or stubbed (the plan's subject)

| System | State in code | Evidence | Original read? | Epic |
| --- | --- | --- | --- | --- |
| Critter or scenery created at run time | **Partial (ARC-DEV-347, E1-a):** created kinds are items, junk piles, blood pools, missiles, projectiles and, new, scenery and eye-candy objects by prototype (`SessionCreatedObject`, schema 85); no critter; script action 52 creates items only | `SessionObjectOrigin` (`GameSessionObjectDirectory.cs`), `CreateRefusal` rejects other types, `ObjectLinkVocabulary`: summoner "not modelled yet" | Yes: object creation and destruction rows [OM](../architecture/original-object-model.md) | E1 |
| Summon component | **Partial (ARC-DEV-352, E1-b):** scenery by prototype (Fireflash, Wall of Fire and the other scenery Summons), the summoned list, Destroy and the eye-candy callbacks; a critter Summon still falls to `ComponentNotReady` | `SessionSpellCaster.Apply`; B47: spells 21, 22, 57 | Critter summon read (0x450C40, 0x452830, 0x452900, [MG8–MG9](../research/ledger/magic.md)); what a flash or a wall creates is read ([ARC-RES-101](../research/summon-objects-res-101.md), SUM1–SUM16) | E1 |
| NPC magic state (effects, generic spell flags) | **Partial:** flags kept per critter only for the hold flags; effect list is the hero's alone | `GameSessionObjectFlags.cs`, `GameSessionEffects.cs` | Effect functions read (EF1–EF10) | E1, E4 |
| Monster generators, respawn, encounter creatures | **Stub:** the scheduler calls `world.Spawn` and handles its outcomes; the host's `Spawn` answers null | `AreaAiHost.cs:1021-1023`, `SchedulerRules.cs:115` | Generator branch read ([AI core loop](../research/npc-ai-core-loop.md)); spawner body 0x4BA790 not yet | E2 |
| Critter body decay | **Absent** (items and blood pools decay; critter bodies do not) | `GameSessionDecay.cs` | Not read here | E2 |
| Random encounters on the world map | **Absent** | `WorldJourney.cs` has no roll | Not read | E2 |
| Sleep and rest (PC) | **Absent in the viewer** (rules for the rest heal exist; the game bar says "sleep: the window is not built") | `LifeRules.RestHeal`, `TerrainViewerAreaGameBar.cs:130` | Partly (hour and heal rules; the window is not) | E3 |
| NPC hourly day (beds, sleep, walking between day and night standpoints) | **Partial:** waypoints and standpoints are placed on the fold and wake-up tick only | `NpcRouteRules.cs`, `SchedulerRules.cs:92-101` | Waypoint rules read; schedule consumers not | E3 |
| HUD clock or date | **Absent** (only the load screen has clock art) | survey | UI ledger | E3 |
| Spell components Movement (teleport), Cast, Polymorph, Dispel, Interrupt, Terminate, branches, Identify, Recharge, Use, EnvFlag, AIRedirect | **Stub** (17 of 25 component types parse but never run) | `SessionSpellCaster.Apply` | Most read ([magic plan](../research/magic-implementation-plan.md), MT15–MT18) | E4 |
| Remaining ObjFlag flags (charm, mind control, invisible, blind, fleeing, summoned, shrunk, water walking…) | **Stub**; only entangled, stoned, paralysed, spoken-with-dead are written | `SessionSpellCasterObjectFlags.cs` | Read (MT13, MT14) | E4 |
| Item recharge | **Absent** | survey | Charges read ([technology](../research/technology-evidence.md)) | E4 |
| Spell-run endings on rest and world-map travel; runs of other maps; callback runs | **Absent / not saved** | survey: the snapshot saves the goal runs and spell runs of the active map only (`GameSessionTypes.cs`, `CaptureSpells`) | Read (MT11, MT9b) | E3, E4 |
| Teleport bans by area | **Stub** (`TeleportAllowed` is never given false) | `SpellInvocationRules.cs:50,181` | Address read (0x4BDD30) | E4 |
| Script actions | **Partial: 76 of 142 admitted** (survey, `ScrAdmissionSet.S30`); conditions 16 of 53 | `ScrAdmission.cs`, `ScrCatalogue.cs` | [SCR census](../research/scr-census.md) | E5 |
| Dialog markers `c r t z` (trainers and special replies), `r:`, positive `IN` | **Stub** (`UnsupportedInteraction` blocks) | `DialogConversation.cs:1459-1466` | Trainers read (S16 of the skills plan) | E5 |
| Spot-trap roll, disarm action, placed trap devices, trap script 30002 | **Partial:** traps on maps fire and hurt; the spot roll logs "not ready", disarm has no viewer action, placing a device is "NOT READY" | `TerrainViewerTraps.cs:74`, `TerrainViewerAreaItemUse.cs:170` | Read ([ARC-TECHNOLOGY-010](../research/technology-evidence.md), TP1–TP8) | E6 |
| Movies (Bink), credits, intro, script-queued slides, endings | **Stub / absent** (menu buttons are "Unavailable"; only the death slide exists) | `AreaMainMenu.cs`, `AreaHeroDeath.cs`; no Bink code | Slide table read | E7 |
| Fate window and Fate Points button; Skill pane; combat action-point readout | **Stub / absent** ("not built" notices) | `TerrainViewerAreaGameBar.cs:127-129` | UI ledger partly | E8 |
| Companions screen and follower menu | **Absent** | survey | Not read | E8 |
| Gambling (FR-ITEM-019), identify window, heal another critter | **Stub / absent** | survey | Gambling read (S14) | E8 |
| Misc sound callers (scenery loops, busting, NPC footsteps, item drop, non-death AI barks) | **Absent** (hero footsteps, pickups, doors, chests, attack and death barks are wired) | no caller of the misc, fidget and drop cue kinds | Sound ledger | E8 |
| Options: module select, violence filter, brightness, auto-switch weapons, autosave timer | **Hidden** with reasons | `AreaGameOptions.cs` | UI ledger | E8 |
| Gamepad in the game UI, UI scale, Windows and Linux validation | **Lab only / partial / not run** | survey; `export_presets.cfg` has a Windows package; the coverage matrix (FR-PRODUCT-005, not re-checked) says it was never run on Windows | — | E9 |
| Network game, second module, mods, original-save import | **Absent** (requirements and research only) | no socket code; `ContentMountPlan` takes a module name but nothing selects one | Network read (NT1–NT16) | E10 |

### 3.2 Checked and present (so nobody surveys it again)

Logbook with seven tabs and quest, rumor and reputation writing from scripts; world map screen and town maps with notes; new-character
window, creation editor, opening shop; barter with haggle, quantity window, shop restock; schematics window and assembly, tech degrees
and point spending on the sheet; kill log; traps on maps (step firing, damage); AI awareness, line of fire, NPC door opening;
world-map travel with discovery; change-of-place effects (fades, clock steps, transition screen); main-map residency with the sector
cache; clock and calendar; pick locks (the viewer wires doors and windows only; the session also takes containers), repair, stealth, pick pocket, throwing and bow, item
spells with charges, wield and unwield scripts, armour art on the hero body; hotkey bar, spell pane, maintain bar; save schema 84
(readable back to 56); HD graphics and HD interface switches. The original has no automap, so none is planned.

## 4. Epics

Ten epics, E1 to E10, plus the documentation slice group E0 (listed last).

Each epic names what exists, what is missing, dependencies and slices. "FR" lists the groups the epic's tasks must name when created.

### E1 Summon and objects created at run time (priority)

**Goal.** The session can create an object (scenery, eye-candy object, critter) from a prototype on the open map, remove it, save it,
and the Summon spell component uses it. Closes B47 (Fireflash explosion, Wall of Fire, Summon Undead), the remainder of B25 ("no host
path creates a critter") and gives E2 and E6 their base. Plan slices **P2** and **MT17** of the [magic plan](../research/magic-implementation-plan.md),
K4 and K5 of the [audit plan](../research/architecture-audit-plan.md). FR: FR-MAGIC-008, FR-MAGIC-007, FR-AI-004 (part), FR-SCRIPT-002.

**What exists.** The [object directory](../architecture/object-directory.md) (one id space, one create and destroy path, per-kind stores),
made items, junk piles, blood pools, missiles, the record resolver by identity, the sector cache and fold, goal runs and saved runs.
Script action 52 creates items. The scheduler already handles `Spawned` and `SpawnRefused`.

**What is missing.** Any create path for a non-item; the eye-candy object kind (the caster only logs `EyeCandyAdded`); NPC effect list and
generic spell flags; the Summon component; the summoned flag, faction and palette rules; the run's end when the summon dies; a host
row for a critter created after the map opened.

**Risk to check first.** The residency concept ([residency](../architecture/residency.md), section 3.5) says the AI host takes its NPC rows
from the windows the viewer has shown; a critter created at run time must be hosted wherever it stands. Slice E1-c starts by checking
this in the code and, if true, fixes the record source before creating anything.

| Slice | Content | Size | Needs | Original |
| --- | --- | --- | --- | --- |
| E1-r | **Read** what object Fireflash's explosion and Wall of Fire's five pieces create (type, prototype, flags, life, how the run's END removes them) and what Summon Undead's second component does; list the spells whose Summon components create non-critters. Ledger rows. **Done (ARC-RES-101, [note](../research/summon-objects-res-101.md), ledger SUM1–SUM16):** all non-critter Summons make a scenery (seven prototypes, 25 components, no items or eye-candy objects); Wall of Fire is one component on five tiles whose pieces burn with the caster as source; Fireflash's flash needs an empty strike tile; nothing but the records' Destroy lines removes a summoned object; the hand-off for E1-a and E1-b is in the note | S | — | Mac read |
| E1-a | **Created scenery and eye-candy objects (ARC-DEV-347, built):** new origin kinds in the directory, create and destroy by prototype on the open map, saved as map-local state (schema step), drawn and picked through the existing object paths, fold of created objects with their sector, a synthetic smoke stage | M–L | — (E1-r for the exact kind) | OM rows read |
| E1-b | **Summon component for objects (ARC-DEV-352, built):** the flash and the wall; the records' CALLBACK and END_CALLBACK Destroy lines remove them (the eye-candy callbacks, EC11); the wall's pieces burn with the summoner as the source; stages `summon-flash` and `summon-wall`. Closes B47 for two of three reports. Not done: a Dispel on a summoned object, the blocking pieces of Wall of Stone and Force in the path queries | M | E1-a, E1-r | read in E1-r |
| E1-c | **Created critters:** a critter from a prototype on a tile, built by identity through the record resolver, directory, AI host row wherever it stands, saved; script action 52 extended to critters; closes the B25 remainder | L | E1-a | OM rows read |
| E1-d | **NPC magic state (P1):** effect list and generic spell flags for every critter from map and prototype fields, saved; Effect and the other flags reach NPCs | M | — | EF1–EF10 read |
| E1-e | **Summon component for creatures:** prototype or list pick (MG9 as a technical bug under the owner rule), summoned flags, palette, faction from `Clear_Faction:` (MG8), alignment, tracking in the run, END and dispel remove, run ends when the summon dies (0x459740), summoned follower join, Summon Undead's ObjFlag; stage for Summon Undead and one list spell | L | E1-c, E1-d | read (MG8, MG9) |
| E1-f | **AI summoning:** the 16 spells of the AI's summon list used by NPC casters, summoned allies in combat | M | E1-e | AI I8 read |

The slices of one lane touch the same spell-cast files: E1-b, E1-e, E1-f and E4's component slices run one at a time; E1-a, E1-c and E1-d
touch the directory and the session and can run beside E1-r.

### E2 Population: generators, respawn, decay, encounters

**Goal.** The original's world refills and ages: monster generators spawn, emptied areas repopulate, critter bodies rot away, the world
journey rolls encounters. FR: FR-AI-004, FR-TRAVEL-003, FR-RUNTIME-001. Audit slice K4.

**Exists.** The scheduler's generator branch and its outcome handling; the on-screen test for generators already decided (the out-of-sight rules
decision of 2026-11-07); item decay and blood pools; scenery respawn.

| Slice | Content | Size | Needs |
| --- | --- | --- | --- |
| E2-r | **Read** the spawner (0x4BA790), encounter creatures and the world-map encounter roll; body removal timers. Ledger rows | M | Mac read |
| E2-a | Spawner for map generators on top of created critters; the host's `Spawn` stops answering null | M–L | E1-c, E2-r |
| E2-b | Critter body decay and removal, repopulation of emptied areas | M | E1-c, E2-r |
| E2-c | Random encounters on the world journey: roll, encounter map, creatures, the "encounter creature destroyed" scheduler branches | L | E2-a, E2-r |

### E3 Sleep, rest and the NPC day

**Goal.** Time matters to the player: the PC sleeps and waits, the clock is visible, NPCs have a day. FR: FR-TRAVEL-004, FR-LIGHT-001,
FR-LIGHT-002, FR-UI-008, FR-AI-001, FR-AI-003. Audit slices K3, K7.

**Exists.** Rest heal rules (`LifeRules.RestHeal`, `RestingHours`), the NPC resting event, the sleeping condition in the awareness and speech
rules, clock and calendar, change-of-place clock steps, waypoint and standpoint placement on the fold.

| Slice | Content | Size | Needs |
| --- | --- | --- | --- |
| E3-r | **Read** the sleep and wait window, its conditions (no sleep in combat, network rules aside), bed use, the hourly NPC consumers | M | Mac read |
| E3-a | Sleep window and the PC rest action, time skip, interrupted rest; the game bar's Sleep button | M | E3-r |
| E3-b | HUD clock and date button (the original's clock button) | S | — |
| E3-c | Spell-run endings on rest and on world-map travel (MT11 remainder); callback runs saved | S–M | E3-a |
| E3-d | NPC day: beds and sleep, hourly walking between the day and night standpoints, not only on the fold; heartbeat budget check | L | E3-r |

### E4 The rest of the spell components

**Goal.** Every component of the 223 spell records runs. Slices **MT13 rest, MT14, MT15, MT16, MT18, MT21** of the
[magic plan](../research/magic-implementation-plan.md); done parts are listed there. FR: FR-MAGIC-007, FR-MAGIC-009, FR-MAGIC-010,
FR-MAGIC-011, FR-TRAVEL-005, FR-SCRIPT-002.

**Audit (ARC-RES-103, [magic audit](../research/magic-audit-res-103.md), 2026-11-11).** The 80 college spells use 12 of the 25 component types; of E4's slices the colleges need Dispel (E4-c narrowed), the ObjFlag writer (E4-a, E4-b), Movement (E4-e), Trait (E4-f) and Identify (E4-d), and the other control components belong to the technology records. The audit adds one slice the plan lacks (resistance and skill changes of effects are inert in the session) and orders the work by college and wave; it does not change a slice's scope here.

| Slice | Content | Size | Needs |
| --- | --- | --- | --- |
| E4-a | Remaining ObjFlag flags with render and movement effects (invisible, shrunk, water walking, "don't draw", blind), Effect on NPCs | M | E1-d |
| E4-b | Social and AI flags: charm (+30/−30), mind control, undead, fleeing, blinded as AI notifications; reaction adjustments; AIRedirect | M–L | E4-a |
| E4-c | Control components: Dispel, Interrupt, Terminate, TestNBranch, ChargeNBranch, Noop, EnvFlag, TraitIdx, Trait64, Use; Destroy for critters and containers | L (two slices) | E1-d |
| E4-d | Item recharge and Identify components | S–M | E4-c |
| E4-e | Movement: teleport to a tile, a random tile, the world map; no-teleport area data (FR-TRAVEL-005); what a teleport does to followers; nested Cast | M–L | E4-c |
| E4-f | Polymorph (Trait): drop what the new art cannot wield, monster art and palette, restore (MG10 as a technical bug) | M | E4-a |
| E4-g | Script hooks: conditions `knows_spell`, `mastered_spell_college`, `obj_is_influenced_by_spell`, `obj_has_body_spell`; the PC as a script caster; give spell mastery; paid spellcasting services | M | E5-c |
| E4-h | Saved runs of maps that are not active | M | E4-c |

### E5 Script and dialog coverage

**Goal.** Installed content stops hitting "action not admitted": 66 action types and about 37 condition types are refused today
(survey of `ScrAdmissionSet.S30`). The biggest groups: NPC state (`attack`, follow and disband, reaction, standpoints, change script,
change art), world objects (create item, transfer item, lock state, open and close toggle, blocked sector), story and time (global quest
state, hours, newspapers, give XP, fate point), media (movie, slide, fade) and dialog markers (`c r t z`, `r:`). FR: FR-SCRIPT-001…004,
FR-DIALOG-003, FR-DIALOG-004, FR-SOCIAL-009, FR-CHAR-018.

| Slice | Content | Size | Needs |
| --- | --- | --- | --- |
| E5-r | **Census:** which refused actions and conditions occur in the installed scripts, on which maps, in which quests, ranked by use. Decides the order of the slices below (docs-only output, real-data run on the Mac) | S–M | Mac data run |
| E5-a | NPC-state actions: attack, follow and disband, reaction adjust and set, standpoints, change script, change art | M–L | E5-r |
| E5-b | World-object actions: create item, transfer item, lock state, open or close toggle, sector blocked, transform | M | E5-r |
| E5-c | Story and time actions and conditions: global quest state, rumors quelled, hours, time, give XP, give fate point, blessing and curse | M | E5-r |
| E5-d | Dialog trainers and special replies (`t:`, `c`, `r`, `z`, `r:`, positive `IN`), skill training window, Educator | M–L | — |
| E5-e | Loops and calls (`loop break`, `call script at`) | S–M | E5-r |

### E6 Traps and placed devices

**Goal.** Traps are a full system: spotted by a roll, disarmed, placed by the player, fired by script 30002. FR: FR-ITEM-016, FR-NAV-006,
FR-CHAR-017, FR-CHAR-019. Skills plan slice **S10**, audit slice K6.

**Exists.** Trap objects on maps with step firing and damage; disarm in the session but without a viewer action.

| Slice | Content | Size | Needs |
| --- | --- | --- | --- |
| E6-a | Spot-trap roll on every path step and use, the master second roll; disarm action and the skill-bar button; the expert's device | M | — |
| E6-b | Placed devices: use an item with the trap-device flag on an object or tile, the device is spent, the marker object, the NPC alarm for record 176 | M | E1-a |
| E6-c | The native trap script 30002 (the shot) | S | E6-a |

### E7 Media, endings and credits

**Goal.** Movies, intro, credits, script-queued slides and the endings. FR: FR-MEDIA-001, FR-MEDIA-002, FR-UI-001. Audit slice K12 (proposed
for after the alpha). **Decision first:** how to decode Bink video and audio under the rules (free tools with free licences, MIT
project); the audit proposed no decoder.

| Slice | Content | Size | Needs |
| --- | --- | --- | --- |
| E7-r | **Research:** decoder options and licences (ffmpeg's Bink support is LGPL; or ship no video and show stills), the movie list and where scripts and maps call it | S–M | owner decision on scope |
| E7-a | Movie player and the change-of-place movie steps; menu "View Intro" | M–L | E7-r |
| E7-b | Credits screen and slide types 0 and 2; script-queued slides (`queue_slide`), fades; ending selection | M | E5-c (media actions) |

### E8 Player windows and economy leftovers

**Goal.** The windows and small systems the surveys found stubbed. FR: FR-CHAR-024, FR-UI-002, FR-UI-004, FR-UI-005, FR-UI-006, FR-ITEM-019,
FR-ITEM-008, FR-MEDIA-003, FR-COMBAT-002, FR-COMBAT-003.

| Slice | Content | Size | Needs |
| --- | --- | --- | --- |
| E8-a | Fate window and the Fate Points button (rules and the grant exist) | S–M | — |
| E8-b | Skill pane of the game bar; PickPocket and DisarmTraps hotkeys | S | E6-a for disarm |
| E8-c | Companions screen and follower menu; barter follower switch | M | Mac read |
| E8-d | Combat action-point readout and the turn-order list (owner decision 2026-10-28) | M | — (UI-124 runs beside) |
| E8-e | Options windows hidden today: violence filter, brightness, auto-switch weapons, follower skills, combat taunts, autosave timer | M | Mac read for each |
| E8-f | Sound callers: scenery loops, busting, AI barks, NPC footsteps, item drop | S–M | — |
| E8-g | Identify window, heal another critter, lockpicks on chests (viewer wiring; the session takes containers), junk-pile partial take, poison sources | M | — |
| E8-h | Gambling and newspapers queue (K10; proposed for after the alpha) | M | E5-c |

### E9 Platform, controller and presentation

**Goal.** FR: FR-PRODUCT-001, FR-PRODUCT-002, FR-PRODUCT-005, FR-UI-007, FR-LIFECYCLE-001, FR-LIFECYCLE-004.

| Slice | Content | Size | Needs |
| --- | --- | --- | --- |
| E9-a | Gamepad in the game UI (inventory, logbook, dialogs, map), on the lab's input framework | L | owner's device check later |
| E9-b | Adaptive UI layout and UI scale in game windows (`UiScalePercent` is stored, not applied) | M–L | — |
| E9-c | Windows validation of the packaged build on a Windows machine; Linux and handheld presets | M | owner's hardware |
| E9-d | One settings file as the original's `arcanum.cfg` | S | — |

### E10 Deferred by owner decision (not planned here)

| Part | Where it stands | Prerequisite if the owner wants it |
| --- | --- | --- |
| Network game FR-NET-001…008, FR-SOCIAL-006 | Read in the executable ([NT1–NT16](../research/ledger/multiplayer.md)); no code. Owner: "not alpha, no date" | The typed request boundary exists (target model GQ5 A); a concept K11, then the protocol |
| Second module (`Vormantown`), module select, campaign bootstrap | `ContentMountPlan` takes a module name; no selection. Owner: after alpha | Module picker, content-stack check |
| Mods, versioned compatibility profiles (FR-CONTENT-008) | Absent | Profile design |
| Importing original saves (FR-CONTENT-005) | [Save layout read](../architecture/original-persistence.md); no importer | Decision whether it is wanted |

### E0 Documentation re-baseline

| Slice | Content | Size |
| --- | --- | --- |
| E0-a | Refresh the [coverage matrix](../requirements/coverage.md) and the "still missing for a playable game" section of the [status catalogue](status.md) against the code of this document (cloud, docs-only, no checks) | M |
| E0-b | Write the concept documents K2–K12 that the audit proposed and nobody wrote, for the epics the owner chooses (cloud) | S each |

## 5. Order and dependencies

```
E1-r ─┐                         E5-r ──► E5-a, E5-b, E5-c ──► E7-b, E8-h
E1-a ─┴► E1-b                   E3-r ──► E3-a ──► E3-c
E1-a ──► E1-c ──► E1-e ──► E1-f   E3-r ──► E3-d
E1-d ──┬──────────┘              E2-r ──► E2-a, E2-b;  E2-a ──► E2-c
       ├► E4-a ──► E4-b, E4-f     E1-c ──► E2-a, E2-b
       └► E4-c ──► E4-d, E4-e, E4-h   E1-a ──► E6-b
E5-c ──► E4-g                    E6-a ──► E6-c, E8-b
```

**Wave 0, no dependency, can start in parallel** (limit by the Mac): E1-r, E1-a, E1-d, E0-a, E5-r, E3-r, E2-r, E3-b, E8-a, E8-d, E8-f, E6-a, E5-d.
**Wave 1:** E1-b (closes two B47 reports), E1-c, E3-a, E5-a…E5-c, E5-e, E4-a, E4-c, E8-b, E6-c.
**Wave 2:** E1-e (closes the third B47 report), E1-f, E2-a, E2-b, E3-c, E3-d, E4-b, E4-d…E4-h, E6-b, E8-g.
**Wave 3:** E2-c, E7 (after the decoder decision), E8-c, E8-e, E8-h, E9, E0-b (any time, cloud).

Conflict rules for threads: one writer per file set. The spell-cast files (`SessionSpellCaster*`) are one lane (E1-b, E1-e, E1-f, E4-a to E4-h); the object directory and session create paths are another (E1-a, E1-c, E2, E6-b); script admission files are a third (E5).
Threads of different lanes run beside each other.

## 6. Reads of the original that the plan needs

Mac reads, each a docs-only research slice that ends in ledger rows: E1-r, E2-r, E3-r, E5-r (a data census), E7-r (a research on decoders),
E8-c and E8-e (UI rows). Everything else uses reads that already exist in the [ledger](../research/original-verification-ledger.md); each
implementing slice still reads the F0 facts of the functions it changes, as the owner decided on 2026-10-20.

## 7. Not in this plan: work already running or already triaged

Running or proposed when this was written: the bug fixes F2 (room check for takes), F3 (quit box), F5 (a taken note), F6 (follower portrait
bars) and F7 (the ghost of Brego) of the [2026-11-10 triage](../research/bug-reports-triage-2026-11-10.md), and F8 (the inventory's
arrange button, added by that triage's correction); F4, the carried lights V7d (B50, DEV-335); DEV-331 (ai-cast leaves the hero entangled);
DEV-330 (roof over house containers); DEV-299 (town containers); DEV-332 (main menu music); UI-124 (combat panel). F1 of that triage is E1 of this plan.

One gap this plan leaves until V7d lands: **FR-LIGHT-011**, the attack and skill rolls' darkness penalty (the rules take a light value; the
survey found only the attacker's emission fed in; the wiring was not traced). It becomes a small slice after V7d lands.

## 8. Questions for the owner

1. **Priority order.** Recommended: E1 first (the foundation and B47), beside it E0-a, E5-r, E3-r and E2-r (reads and a census, cheap), then
   E3 and E5 (the player notices sleep and quest scripts first), then E2 and E4. Is that the order, or is the player-visible content
   (E3, E5) ahead of the magic lane?
2. **Alpha scope.** Are movies, credits and endings (E7), gambling and newspapers (E8-h) after the alpha, as the audit proposed?
3. **Video.** May E7-r look at an ffmpeg-based Bink decoder (LGPL, an external dependency) or must video be written from scratch or skipped?
4. **Deferred parts.** Do the network game, the second module, mods and original-save import stay deferred (E10)?
5. **Parallelism.** How many Mac threads may run reads at once? Wave 0 has four reads (E1-r, E2-r, E3-r, E5-r); E7-r and the UI reads E8-c and E8-e come later.
6. **Platforms.** When should Windows validation (E9-c) and the in-game gamepad (E9-a) start: before or after the content epics?
7. **Original behaviour decisions** expected inside slices (each will come as its own decision card): MG9's summon weight roll, the
   spawner's behaviour when the player is in sight (already decided by the out-of-sight rule), sleep rules when enemies are near.

## 9. Owner answers (Aleksandr, 2026-11-10)

1. The plan is the work order, in the order of section 5; it is kept in project memory.
2. Movies, credits, endings (E7), gambling and newspapers (E8-h) may be taken, with low priority.
3. E7-r may look at an ffmpeg-based Bink decoder (LGPL, external dependency).
4. The network game, the second module, mods and original-save import stay deferred (E10).
5. Mac concurrency: one Godot slot (`window`, `headless`, `export` share it, first in, first out) and two `dotnet` slots across all
   threads ([launch queue](../workflow/toolchain.md#launch-queue-arc-dev-033)); executable reads and docs-only slices are not queued.
6. Windows validation and the in-game gamepad (E9-a, E9-c) come after the content epics.
