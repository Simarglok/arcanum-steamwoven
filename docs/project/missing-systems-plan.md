# Missing systems: map and work plan

Status: **Plan, accepted by the owner as the work order** (2026-11-10, answers in section 9 and the [decisions](../architecture/decisions.md) row "Missing systems work order"), work item ARC-RES-099. Docs-only: no source
change, no checks run. Re-baselined again on 2026-11-11 against `origin/main` `4dc5162` (ARC-DEV-1110, issue #1110; sections 3.1, 1 and the slice rows it names). Each slice is still handed out as its own task: it starts when the coordinator gives it a thread and
takes its task code from the coordinator (the labels `E1-a`, `E4-c` and so on are planning labels, not task codes).

It continues the [architecture audit](../architecture/gap-register.md) (which settled the engine model) and the two bug triages
([the 2026-11-08 and 2026-11-10 triages](../research/open-bugs.md)). Those fix
individual bugs; this document lists the **large parts of the game that are not built or only stubbed**, checks each against the code,
and orders the work. It does not repeat bug fixes.

Since 2026-11-11 a slice merges without waiting for the owner's in-game comparison. It adds its comparison steps to the [owner re-check list](owner-recheck-list.md) instead.

## 1. Answer

- **Ten epics** (section 4), sized S/M/L with slices that can go to separate threads (section 5). Rough total: about 50 slices,
  of which 7 are research reads or censuses on the owner's Mac (section 6).
- **Epic 1, "Summon and objects created at run time", comes first** (the coordinator's and the B47 report's need): the one foundation
  under three other epics. Seven slices (one a read), size L overall (section 4, E1); its built part is in the section 3 table.
- **Three epics depend on E1:** population (monster generators, E2), the rest of the spell components (E4: some
  components need NPC magic state from E1) and placed traps (E6: a placed trap is a created object). The AI's summoned allies are E1's last slice.
- **Independent of E1 and can start at once:** the documentation re-baseline (E0), sleep and the NPC day (E3), script and dialog coverage
  (E5, its first slice is a census), the leftover player windows (E8), media (E7, after a decoder decision).
- **Deferred by earlier owner decisions** (listed, not planned): the network game, a second module, mods (E10).
- **Documents re-baselined:** slice E0-a is done (ARC-RES-107 re-checked the coverage matrix and the status catalogue against the code,
  2026-11-11) and ARC-DEV-1110 re-checked them again against `origin/main` `4dc5162` after the magic, technology and trap slices of the same day; the map in section 3 is the code-checked state of that commit. One gap was left, **FR-LIGHT-011** (the darkness penalty of rolls); ARC-WORLD-1149 closed its host part: the cell light level feeds the hero's attacks and
  throws, the combat panel, portal lockpicking and trap spotting (what still takes full light is listed in the [lighting area](../rendering/area-lighting.md#light-level-for-checks-arc-world-1149)).

## 3. Map of the systems

### 3.1 Not built or stubbed (the plan's subject)

| System | State in code | Evidence | Original read? | Epic |
| --- | --- | --- | --- | --- |
| Critter or scenery created at run time | **Partial (ARC-DEV-347, E1-a):** created kinds are items, junk piles, blood pools, missiles, projectiles and, new, scenery and eye-candy objects by prototype (`SessionCreatedObject`, schema 85); **E1-c1 (ARC-DEV-359):** critters by prototype (`oid:critter:n`, `SessionCreatedKind.Critter`), NPC residents hosted by the AI host; **E1-c2 (ARC-DEV-370):** drawn, animated, picked and laid fallen by the viewer; **E1-c3 (ARC-DEV-371):** script action 52 creates critters too (the installed "snapping twig" tile script 1629: eight critters next to the PC), each with the encounter flag 0x20000 and its tile notification, the AI tick destroys an encounter creature left without a next tick; the inventory of a created critter is generated at creation (ARC-DEV-386, E1-c4: a created holder in the map's seed, the restock body and the best-items wield, the loot of its body); **ARC-MAGIC-050 (TI12):** a created pet joins the party as a companion and is carried over a map change (`RehomeCreatedCritters`). **ARC-MAGIC-053 (E1-e):** critters made by a Summon spell (hidden until the animation's callback, summoned flags, the creation notice). Left: critters made by generators (E2) | `SessionObjectOrigin` (`GameSessionObjectDirectory.cs`), `CreateRefusal` rejects other types, `ObjectLinkVocabulary`: summoner "not modelled yet" | Yes: object creation and destruction rows [OM](../architecture/original-object-model.md) | E1 |
| Summon component | **Partial (ARC-DEV-352, E1-b; ARC-MAGIC-053, E1-e):** scenery by prototype (Fireflash, Wall of Fire and the other scenery Summons), critters by prototype or list pick (Summon Undead, Succour Beast, the elementals and the rest, SUM17–SUM22), the summoned list, Destroy and the eye-candy callbacks; the Dispel component ends a summoned created critter (ARC-MAGIC-034); not built: `Clear_Faction:`, `Palette:`, the concealed start, an NPC summoner's alignment and faction (E1-f) | `SessionSpellCaster.Apply`; B47: spells 21, 22, 57 | Critter summon read (0x450C40, 0x452830, 0x452900, [MG8–MG9](../research/ledger/magic.md)); what a flash or a wall creates is read ([ARC-RES-101](../research/ledger/spell-runs.md), SUM1–SUM16) | E1 |
| NPC magic state (effects, generic spell flags) | **Built:** the session keeps an effect list per NPC and any written flag bit (ARC-DEV-348, schema 87); the Effect component works on the hero and every NPC, and effect lists reach the combat resolver and the magic save (MA-2, ARC-MAGIC-027, -033); the cast host writes every spell, object and critter flag with the original's reference count (MA-1, ARC-DEV-358, schema 88) and the social and AI flags (MA-4, ARC-MAGIC-035). Left: flags on created creatures (E1-e) | `GameSessionObjectFlags.cs`, `GameSessionEffects.cs`, `SessionSpellCasterEffects.cs`, `SessionSpellCasterObjectFlags.cs` | Effect functions read (EF1–EF10), flag writer read (OB1–OB5, SO1–SO8) | E1-e |
| Monster generators, respawn, encounter creatures | **Stub:** the scheduler calls `world.Spawn` and handles its outcomes; the host's `Spawn` answers null | `AreaAiHost.cs:1021-1023`, `SchedulerRules.cs:115` | Generator branch read ([AI core loop](../research/npc-ai-core-loop.md)); spawner, placement, counters and release read (ARC-RES-1103, [PO1–PO8](../research/ledger/npc-ai.md#generators-encounter-creatures-and-body-decay-arc-res-1103)) | E2 |
| Critter body decay | **Built (ARC-WORLD-1158, E2-b):** the death of an NPC queues the Decay Dead Bodies event one game day on (not for the no-decay flag); when due the inventory is laid on the body's cell (junk-pile drop rule; summoned and no-display items destroyed, all others laid down, ARC-WORLD-1299) and the critter is destroyed ([BD1–BD7](../research/ledger/npc-ai.md#body-decay-built-arc-world-1158)). Not modelled: the hero's decay, another map's seed items | `GameSessionBodyDecay.cs`, `BodyDecayRules.cs` | Read: schedule, delays, handler, inventory drop (ARC-RES-1103, PO10; [BP6, BP7](../research/ledger/combat.md#pool-of-blood-arc-render-041)) | E2 |
| Random encounters on the world map | **Built (ARC-WORLD-1159, E2-c, schema 97):** the Random Encounters event (300 to 700 game minutes, re-armed first) rolls the chance, power, terrain and table of `rules/wmap_rnd.mes` and makes created critters on the ring spots; a fired roll closes the world map (a journey lands at the marker; the opener refuses 4 s) or the rest window ([PO13–PO19, EN1–EN5](../research/ledger/npc-ai.md#random-encounters-built-arc-world-1159)). Not modelled: the `-norandom` switch, the creatures' facing, the blocked test beyond the shown window | `EncounterRules.cs`, `EncounterTables.cs`, `GameSessionEncounters.cs`, `AreaEncounterHost.cs` | Yes (stage `encounters`) | E2 |
| Sleep and rest (PC) | **Built, with the hero's bed** (ARC-WORLD-1160: the window, the open decision, the step chain, the heal and the fade; the bar's Sleep button and the key S; ARC-WORLD-1162: a click on a bed, its script, occupancy, kind-2 heal; the random encounter closes it (ARC-WORLD-1159); no NPC beds, sleeper flag, nudge and bed frame (the picture unchecked); no hero flags) | `AreaRest.cs`, `TerrainViewerAreaSleep.cs`, `LifeRules.RestHeal` | Yes (bed, encounter) | E3 |
| NPC hourly day (beds, sleep, walking between day and night standpoints) | **Partial:** waypoints and standpoints are placed on the fold and wake-up tick only | `NpcRouteRules.cs`, `SchedulerRules.cs:92-101` | Waypoint rules read; schedule consumers not | E3 |
| HUD clock or date | **Absent** (only the load screen has clock art) | survey | UI ledger | E3 |
| Spell components AIRedirect, EnvFlag, Interrupt, Recharge, TraitIdx, Trait64 | **Stub: 19 of the 25 component types run** (Damage, Heal, Destroy, EyeCandy, Summon (scenery), AGoal, AGoalTerminate, ObjFlag, Identify, Dispel, Movement, Trait (art type), NoOp, Terminate, ChargeNBranch, TestNBranch, Use, Cast, Effect; since 2026-11-11 the table `ComponentHandlers`); the six listed have no handler and log `ComponentNotReady`. No installed record uses Interrupt; the records that still hit a missing handler are 103 (Recharge) and 183 (AIRedirect with a creature Summon) | `SessionSpellCasterComponents.cs` (`ComponentHandlers`), `SessionSpellCaster.Apply` | Most read ([magic plan](../research/magic-implementation-plan.md), MT15–MT18; [magic audit](../research/magic-audit-res-103.md)) | E4, E1-e |
| ObjFlag remainder | **Mostly built:** every spell, object and critter flag bit is written (MA-1, ARC-DEV-358), charm, mind control, fleeing, blindness and the undead act through the AI host (MA-4), locks and holds on doors and containers (MA-6, ARC-MAGIC-037), the polymorph's art, palette and flags (MA-8, ARC-MAGIC-040, -046, -047, -052). Left: the viewer's draw of hidden, shrunk and translucent critters and of don't-draw objects and the shielded hit eye candy (E4-a2), the permanent polymorph's ObjFlag part (record 222, MA-8), the removal calls whose reads are F0 (drowning, 0x435D70), the combat and life readers of a written undead bit ([#1323](https://github.com/Simarglok/arcanum-steamwoven/issues/1323)) and followers of a non-hero caster ([#1322](https://github.com/Simarglok/arcanum-steamwoven/issues/1322)); the NPC flag KOS_OVERRIDE of charm is built (ARC-MAGIC-1142) | `SessionSpellCasterObjectFlags.cs`, `SessionSpellCasterTrait.cs`, [technology audit](../research/technology-audit-res-125.md) (81 of 83 technology records run; 183 and 222 are PART) | Read (MT13, MT14, OB1–OB5, SO1–SO8, TR1–TR6) | E4 |
| Item recharge | **Absent** | survey | Charges read ([technology](../research/technology-evidence.md)) | E4 |
| Spell-run endings on rest and world-map travel; runs of other maps; callback runs | **Built:** the rest press (ARC-MAGIC-1161) and the world-map travel start (ARC-MAGIC-059) end the hero's spells; runs of other maps and callback runs done (ARC-MAGIC-058) | the player's change of map ends the runs that do not travel (`SpellRunTravelRules`); a run waiting for a callback stands in Begin or End and is saved | Read (MT11; MT9b done); the summon's death ending (MT17) open | E3, E4 |
| Teleport bans by area | **Built (MA-5, ARC-MAGIC-036, -038):** the table `Rules\AntiTeleport.mes` and the check 0x4BDD30 (`AntiTeleportTable`, `AntiTeleportRules`) are asked by the invocation check and the Movement component; Teleportation opens the world map in teleport mode. Left: the multiplayer host branch, the town map and the path by waypoints in teleport mode | `SessionSpellCasterTeleport.cs`, `AreaSpellTeleport.cs`, `SpellInvocationRules.cs` | Read ([TP1–TP5](../research/ledger/spell-runs.md#teleport-types-and-the-anti-teleport-test-arc-magic-038)) | E4 |
| Script actions | **Partial: 114 of 142 admitted** (the widest set, `ScrAdmissionSet.S37`: S35 adds fade 103 (ARC-SCRIPT-1241), S36 the nine NPC state actions (E5-a, ARC-SCRIPT-1163), S37 ten world-object actions (E5-b, ARC-SCRIPT-1164); S30 held 76 on the survey, S31 to S33 added give spell mastery, use ammunition units, transform, force follow and attack, S34 (E5-c, ARC-SCRIPT-1165) the 13 story and time actions); conditions 29 of 53 (S31 added the four spell conditions to the 16 of S30, S34 six more, S37 three). Each host admits its own subset | `ScrAdmission.cs`, `ScrCatalogue.cs` | [SCR census](../research/scr-census.md) | E5 |
| Dialog markers `c r t z` (trainers and special replies), `r:`, positive `IN` | **Partial: `t:` built** (ARC-SCRIPT-1150, issue #1150, [ledger DTR1–DTR9](../research/ledger/social.md#trainer-reply-arc-script-1150)); the Player replies `r:` (ARC-SCRIPT-1304, issue #1304, [ledger RMR1–RMR9](../research/ledger/social.md#rumor-reply-arc-script-1304)), `c:` (ARC-SCRIPT-1321), `h:` (ARC-SCRIPT-1732, issue #1732, [ledger HLR1–HLR12](../research/ledger/social.md#healer-reply-arc-script-1732)), `q:` (ARC-SCRIPT-1532), `d:` and `x:` (ARC-SCRIPT-1533), the NPC line `i:` (ARC-SCRIPT-1505) and positive `IN` (ARC-WORLD-1131) are built; `u:`, `z:` ([#1534](https://github.com/Simarglok/arcanum-steamwoven/issues/1534), blocked by the repair window) and the NPC lines `m:`, `r:` ([#1535](https://github.com/Simarglok/arcanum-steamwoven/issues/1535)) stay blocked or frontier | `DialogConversation.cs`, `DialogConversationTrainer.cs`, `DialogConversationRumor.cs` | The remaining slices of E5-d (issue #1150) | E5 |
| Spot-trap roll, disarm action, trap script 30002, the hero's step onto a placed marker | **Partial:** traps on maps fire and hurt; placed trap devices are built (E6-b ARC-WORLD-124: timer bombs 15154 and 15193; ARC-WORLD-125: script traps 15100 and 15120 on a portal, a container or a cell; stage `item-aim`); the spot roll logs "not ready", disarm has no viewer action, a device used on a target the rules refuse says "NOT READY" | `TerrainViewerTraps.cs`, `TerrainViewerAreaItemUse.cs` | Read ([ARC-TECHNOLOGY-010](../research/technology-evidence.md), TP1–TP8) | E6 |
| Movies (Bink), credits, intro, script-queued slides, endings | **Stub / absent** (menu buttons are "Unavailable"; only the death slide exists) | `AreaMainMenu.cs`, `AreaHeroDeath.cs`; no Bink code | Slide table read | E7 |
| ~~Fate window and Fate Points button~~ (built, ARC-UI-1112); ~~Skill pane~~ (built, ARC-UI-1113); ~~combat action-point readout~~ (built, ARC-UI-1114) | **Stub / absent** ("not built" notices) | `TerrainViewerAreaGameBar.cs` | UI ledger partly | E8 |
| Companions screen and follower menu | **Partly built** (the survey of 2026-11-10 said absent; re-checked 2026-11-12, ARC-RES-1107): the frames, bars and portraits (ARC-UI-020, ARC-UI-024), the order menu with six live rows and the barter follower form with its cycle buttons (ARC-WORLD-046, ARC-DEV-280, ARC-UI-1120); no show/hide button, no scroll arrows past six followers, no portrait click aiming a spell or skill, the menu's Inventory and Character Sheet rows disabled | `AreaFollowerPortraits.cs` | **Read** ([FP1–FP13](../research/ledger/ui-audio.md#follower-panel-and-menu-arc-res-1107)); the hover is built (ARC-UI-1281, #1281) | E8 |
| Gambling (FR-ITEM-019), identify window, heal another critter | **Stub / absent** | survey | Gambling read (S14) | E8 |
| Misc sound callers (scenery loops, busting, NPC footsteps, item drop, non-death AI barks) | **Absent** (hero footsteps, pickups, doors, chests, attack and death barks are wired) | no caller of the misc, fidget and drop cue kinds | Sound ledger | E8 |
| Options: module select, violence filter, brightness, auto-switch weapons, follower skills | **Hidden** with reasons (the original has **no autosave timer**, OH6: not a missing system) | `AreaGameOptions.cs` | **Read** ([OH1–OH6](../research/ledger/ui-audio.md#hidden-options-arc-res-1107)) | E8 |
| Gamepad in the game UI, UI scale, Windows and Linux validation | **Lab only / partial / not run** | survey; `export_presets.cfg` has a Windows package; the coverage matrix (FR-PRODUCT-005, not re-checked) says it was never run on Windows | — | E9 |
| Network game, second module, mods, original-save import | **Absent** (requirements and research only) | no socket code; `ContentMountPlan` takes a module name but nothing selects one | Network read (NT1–NT16) | E10 |

## 4. Epics

Ten epics, E1 to E10, plus the documentation slice group E0 (listed last).

Each epic names what exists, what is missing, dependencies and slices. "FR" lists the groups the epic's tasks must name when created.

### E1 Summon and objects created at run time (priority)

**Goal.** The session can create an object (scenery, eye-candy object, critter) from a prototype on the open map, remove it, save it,
and the Summon spell component uses it. Closes B47 (Fireflash explosion, Wall of Fire, Summon Undead), the remainder of B25 ("no host
path creates a critter") and gives E2 and E6 their base. Plan slices **P2** and **MT17** of the [magic plan](../research/magic-implementation-plan.md),
K4 and K5 of the [audit plan](../architecture/gap-register.md). FR: FR-MAGIC-008, FR-MAGIC-007, FR-AI-004 (part), FR-SCRIPT-002.

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
| E1-r | **Read** what object Fireflash's explosion and Wall of Fire's five pieces create (type, prototype, flags, life, how the run's END removes them) and what Summon Undead's second component does; list the spells whose Summon components create non-critters. Ledger rows. **Done (ARC-RES-101, [note](../research/ledger/spell-runs.md), ledger SUM1–SUM16):** all non-critter Summons make a scenery (seven prototypes, 25 components, no items or eye-candy objects); Wall of Fire is one component on five tiles whose pieces burn with the caster as source; Fireflash's flash needs an empty strike tile; nothing but the records' Destroy lines removes a summoned object; the hand-off for E1-a and E1-b is in the note | S | — | Mac read |
| E1-a | **Created scenery and eye-candy objects (ARC-DEV-347, built):** new origin kinds in the directory, create and destroy by prototype on the open map, saved as map-local state (schema step), drawn and picked through the existing object paths, fold of created objects with their sector, a synthetic smoke stage | M–L | — (E1-r for the exact kind) | OM rows read |
| E1-b | **Summon component for objects (ARC-DEV-352, built):** the flash and the wall; the records' CALLBACK and END_CALLBACK Destroy lines remove them (the eye-candy callbacks, EC11); the wall's pieces burn with the summoner as the source; stages `summon-flash` and `summon-wall`. Closes B47 for two of three reports. Not done: a Dispel on a summoned object, the blocking pieces of Wall of Stone and Force in the path queries | M | E1-a, E1-r | read in E1-r |
| E1-c | **Created critters:** a critter from a prototype on a tile, built by identity through the record resolver, directory, AI host row wherever it stands, saved; script action 52 extended to critters; closes the B25 remainder. **Cut in three (owner rule on big tasks): E1-c1 (ARC-DEV-359, built):** the row built from the prototype by the original's create rule (`CreatedObjectRows`), the session kind `Critter` (`oid:critter:n`, directory, NPC resident, saved), the record resolver, the combat host and the AI host (`SyncCreated`, first tick, kept through a window move, dropped at destroy); the AI risk above is confirmed and fixed. **E1-c2 (ARC-DEV-370, built):** the viewer draws, animates, picks and hovers a created critter (the body from the prototype's art id and the window's art archives, `AreaCreatedCritterArt`), the smoke stage `created-critter`. **E1-c3 (ARC-DEV-371, built):** script action 52 creates critters (`CreateCritterNear`, the tile search of PG12, the encounter flag 0x20000 of PG1 written in the same revision), the location notification of OM16 (the tile script runs for the created critter), the AI tick's destroy of an encounter creature (`IsEncounterCreature`), the heartbeat of a created critter, stage `script-critters` on the installed tile script 1629; closes the B25 remainder (a created critter's tile script). **E1-c4 (ARC-DEV-386, done):** the inventory generation of OM16 (restock body 0x463C60 at creation, then the best-items wield), with a created holder in the session's map seed and in the inventory host's maps first | L (4 slices) | E1-a | OM rows read |
| E1-d | **Built (ARC-DEV-348, schema 87).** **NPC magic state (P1):** effect list and generic spell flags for every critter from map and prototype fields, saved; Effect and the other flags reach NPCs | M | — | EF1–EF10 read |
| E1-e | **Summon component for creatures (ARC-MAGIC-053, built):** the NPC prototype or the list pick (the table at 0x5B0ED8, MG9 fixed in both modes as a technical bug, SUM17–SUM22), the effect's flags (summoned 0x4000, `OF_DONTDRAW`, `OCF2_NO_PICKPOCKET`), the experience cleared and the hero's alignment copied, the summoned list, the records' own ObjFlag, EyeCandy, CALLBACK and END_CALLBACK lines (mind control and the forced join, the animation, the reveal, the destroy), the run ending when the summon dies (0x459740 first loop), the party forgetting a destroyed follower, the creation notice queued (TG9, ARC-DEV-402); stage `summon-undead`. Closes B47's third report. Not built: `Clear_Faction:` and `Palette:` on a critter (no installed record has either), the faction copy from an NPC summoner and the alignment of an NPC summoner (E1-f), the concealed start (SUM19), the Familiar's own flags (`OSF_FAMILIAR`, `ONF_FAMILIAR`, MA-1) and its hiding on the world map (the familiar (74) hidden and shown with the party on the world map, XR14 (b): the original leaves it shown, an owner-decided bug fixed in both modes, [magic audit](../research/magic-audit-res-103.md#decisions-for-the-owner)) | L | E1-c, E1-d | read (MG8, MG9, SUM17–SUM22) |
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
| E2-r | **Read** the spawner (0x4BA790), encounter creatures and the world-map encounter roll; body removal timers. Ledger rows | M | Mac read. **Done** (ARC-RES-1103, issue #1103: ledger rows [PO1–PO12](../research/ledger/npc-ai.md#generators-encounter-creatures-and-body-decay-arc-res-1103), registry rows of the generator band at F1) |
| E2-a | **Built (ARC-WORLD-1157, issue #1157, schema 91; [ledger GS1–GS8](../research/ledger/npc-ai.md#generator-spawner-built-arc-world-1157)).** Spawner for map generators on top of created critters; the host's `Spawn` answers the rate's delay and makes the copies. Left: the generator's instance fields in the copy, script action 110 and condition 46 (E5-b) | M–L | E1-c, E2-r |
| E2-b | **Built (ARC-WORLD-1158, issue #1158, no schema step; [ledger BD1–BD7](../research/ledger/npc-ai.md#body-decay-built-arc-world-1158)).** Critter body decay and removal; repopulation of an emptied area is the generators' ticks only (PO11), so nothing else was to build | M | E1-c, E2-r |
| E2-c | **Built (ARC-WORLD-1159, issue #1159, schema 97; [ledger PO13–PO19, EN1–EN5](../research/ledger/npc-ai.md#random-encounters-built-arc-world-1159)).** Random encounters on the world journey: the Random Encounters event, the roll, the creatures; closing the world map and the rest window. No separate encounter map exists in the original's roll; the roll's creatures carry no encounter flag, so the "encounter creature destroyed" branches stay as ARC-DEV-371 built them | L | E2-a, E2-r |

### E3 Sleep, rest and the NPC day

**Goal.** Time matters to the player: the PC sleeps and waits, the clock is visible, NPCs have a day. FR: FR-TRAVEL-004, FR-LIGHT-001,
FR-LIGHT-002, FR-UI-008, FR-AI-001, FR-AI-003. Audit slices K3, K7.

**Exists.** Rest heal rules (`LifeRules.RestHeal`, `RestingHours`), the NPC resting event, the sleeping condition in the awareness and speech
rules, clock and calendar, change-of-place clock steps, waypoint and standpoint placement on the fold.

| Slice | Content | Size | Needs |
| --- | --- | --- | --- |
| E3-r | **Done** (ARC-RES-1102, issue #1102): the sleep and wait window, its conditions, bed use and the hourly NPC consumers are read in the [ledger rows SWB1–SWB22](../research/ledger/world.md#sleep-wait-and-beds-arc-res-1102). Answers: sleep and wait are refused in combat mode only when an NPC is engaged with the hero or his party (not for enemies merely near); a town refuses resting without the town-map wait marker, but beds work there; a bed is any scenery of art type 4, sub-type 0x16; there is no hourly NPC day timer, the day test runs at each AI step; a random encounter is the only interruption (SWB4, SWB3, SWB14, SWB20, SWB13) | M | Mac read |
| E3-a | **Done** (ARC-WORLD-1160, issue #1160): sleep and wait window, the PC rest action, the hourly time skip, the game bar's Sleep button and the key S ([contract](../world/sleep-window-runtime.md)); the random-encounter interruption is built (E2-c, ARC-WORLD-1159); the hero's bed is E3-d slice 1 (ARC-WORLD-1162) | M | E3-r |
| E3-b | **Done (ARC-UI-1111, issue #1111; [ledger GB8–GB12](../research/ledger/ui-audio.md#interface-bars-arc-ui-018), [viewer](../rendering/terrain-viewer.md#clock-and-date-arc-ui-1111)).** HUD clock and date button (the original's clock window and its button: the sky of the game time, the click writes the time and the date) | S | — |
| E3-c | **Done (ARC-MAGIC-1161, issue #1161):** the press of a rest length ends the sleeper's maintained spells. Spell-run ending on rest (MT11 remainder; the travel ending is done, ARC-MAGIC-059: the rest option click calls 0x457450, [UE6](../research/ledger/spell-runs.md#spell-run-endings-of-a-critter-arc-dev-301)) | S | E3-a |
| E3-d | **Slices 1 to 3 done** (ARC-WORLD-1162: the hero's bed, script, occupancy, kind-2 heal; the census of the installed beds; slice 2: NPC beds, the latch, the seat at the standpoint, the wakers). slice 3: the NPC sleeper's don't-draw flag, the nudge, the bed's occupied frame (picture on the owner re-check list). Open: the attack on a bed, the wander start, the hero's own flags; the heartbeat budget check is done (slice 4: unchanged, [numbers](../world/sleep-window-runtime.md#budget-check-of-the-ai-heartbeat-arc-world-1162-slice-4-2026-11-12)); the main-map bed census (slice (d), ARC-WORLD-1262) is done: 275 beds, 32 scripted, [numbers](../world/sleep-window-runtime.md) | L | E3-r |

### E4 The rest of the spell components

**Goal.** Every component of the 223 spell records runs. Slices **MT13 rest, MT14, MT15, MT16, MT18, MT21** of the
[magic plan](../research/magic-implementation-plan.md); done parts are listed there. FR: FR-MAGIC-007, FR-MAGIC-009, FR-MAGIC-010,
FR-MAGIC-011, FR-TRAVEL-005, FR-SCRIPT-002.

**Audit (ARC-RES-103, [magic audit](../research/magic-audit-res-103.md), 2026-11-11).** The 80 college spells use 12 of the 25 component types; of E4's slices the colleges need Dispel (E4-c narrowed), the ObjFlag writer (E4-a, E4-b), Movement (E4-e), Trait (E4-f) and Identify (E4-d), and the other control components belong to the technology records. The audit adds one slice the plan lacks (resistance and skill changes of effects are inert in the session) and orders the work by college and wave; it does not change a slice's scope here.

| Slice | Content | Size | Needs |
| --- | --- | --- | --- |
| E4-a | **Built (MA-1, ARC-DEV-358); the viewer draw is E4-a2 in the [technology audit](../research/technology-audit-res-125.md#6-queue).** Remaining ObjFlag flags with render and movement effects (invisible, shrunk, water walking, "don't draw", blind), Effect on NPCs | M | E1-d |
| E4-b | **Built except AIRedirect (MA-4, ARC-MAGIC-035; AIRedirect goes with E1-e).** Social and AI flags: charm (+30/−30), mind control, undead, fleeing, blinded as AI notifications; reaction adjustments; AIRedirect | M–L | E4-a |
| E4-c | **Built except Interrupt (a no-op in the original) and TraitIdx** (Dispel ARC-MAGIC-034, MA-3; Terminate, TestNBranch, ChargeNBranch, NoOp ARC-MAGIC-044; Use ARC-MAGIC-048; EnvFlag and Trait64 ARC-MAGIC-1146, with the spell stop on an object and the Combat-Focus Wipe event). Control components: Dispel, Interrupt, Terminate, TestNBranch, ChargeNBranch, Noop, EnvFlag, TraitIdx, Trait64, Use; Destroy for critters and containers | L (two slices) | E1-d |
| E4-d | **Identify built (MA-7, ARC-MAGIC-032); Recharge built (ARC-MAGIC-1146).** Item recharge and Identify components | S–M | E4-c |
| E4-e | **Built (MA-5, ARC-MAGIC-036, -038; nested Cast ARC-MAGIC-049).** Movement: teleport to a tile, a random tile, the world map; no-teleport area data (FR-TRAVEL-005); what a teleport does to followers; nested Cast | M–L | E4-c |
| E4-f | **Built except the permanent polymorph's ObjFlag part (MA-8, ARC-MAGIC-040, -046, -047, -052; record 222).** Polymorph (Trait): drop what the new art cannot wield, monster art and palette, restore (MG10 as a technical bug) | M | E4-a |
| E4-g | Script hooks: conditions `knows_spell`, `mastered_spell_college`, `obj_is_influenced_by_spell`, `obj_has_body_spell`; the PC as a script caster; give spell mastery; paid spellcasting services. **Part done (ARC-MAGIC-029, MT21a):** set S31 holds the four conditions and give mastery in the interpreter; the host side is ARC-MAGIC-030 (MT21b): the use and talk hosts answer them and write the mastery (the PC and an NPC of the shown map); the body-of-element spell-flag bits and the rest stay. **Stop spell on object (130) done (ARC-SCRIPT-1294, issue #1294):** set S42, the placed, heartbeat and dying sets; ledger RM5–RM6 | M | E5-c |
| E4-h | Saved runs of maps that are not active (done: ARC-MAGIC-058, the runs that do not travel end at the player's change of map) | M | E4-c |
| E4-i | **Tempus Fugit (spell 79, magic audit MA-9):** the flag's reader; the original changes speed only while the local player holds the spell (XR6), an owner-decided bug fixed in both modes: an NPC's cast takes effect without the player holding it; **done by ARC-MAGIC-056 (2026-11-12)**: holders gain 10, every other critter loses 10 while any critter holds the spell ([XR14 (a)](../research/magic-audit-res-103.md#decisions-for-the-owner)) | S–M | — (the audit needs only MA-R and MA-1, both done) |
| E4-j | **Raise script hook (magic audit MA-10):** Heal's resurrect script (point 0x12) through the SCR dispatcher (spells 58, 64) | S | the SCR dispatcher (E5) |

### E5 Script and dialog coverage

**Goal.** Installed content stops hitting "action not admitted": 66 action types and about 37 condition types were refused at the survey
(`ScrAdmissionSet.S30`; with S33 it is 61 and 33, section 3.1). The biggest groups: NPC state (`attack`, follow and disband, reaction, standpoints, change script,
change art), world objects (create item, transfer item, lock state, open and close toggle, blocked sector), story and time (global quest
state, hours, newspapers, give XP, fate point), media (movie, slide, fade) and dialog markers (`c r t z`, `r:`). FR: FR-SCRIPT-001…004,
FR-DIALOG-003, FR-DIALOG-004, FR-SOCIAL-009, FR-CHAR-018. **Census update (ARC-RES-1101):** follow and attack are in S33 now; the live refusals are 26 conditions and 53 actions ([census](../research/script-refusal-census.md)).

| Slice | Content | Size | Needs |
| --- | --- | --- | --- |
| E5-r | **Done (ARC-RES-1101, 2026-11-12; [census](../research/script-refusal-census.md)):** which refused actions and conditions occur in the installed scripts, on which maps, in which quests, ranked by use; the order of the slices below. Under S33, 447 of 1,809 attached scripts (2,529 of 7,766 placed sites) are blocked by 79 types | S–M | Mac data run |
| E5-a | NPC-state actions: disband, reaction adjust and set, standpoints (day, night, the two extended), change script, change art (follow and attack are in S33 already). Census: 166 blocked scripts, 550 sites freed alone. **Done in part (ARC-SCRIPT-1163, issue #1163):** set S36 holds all nine; the use, placed, heartbeat and dying hosts apply eight ([ledger NS1–NS8](../research/ledger/scripts.md#npc-state-commands-arc-script-1163-e5-a)); left: change art number needs a per-object art overlay in the viewer (51 installed scripts), and the dialog host does not hold the nine yet | M–L | E5-r (done) |
| E5-b | World-object actions: create item, lock state, open or close toggle and `obj_is_open`, sector blocked and `sector_is_blocked`, monster generator, destroy item named (transform is in S33; transfer item is one site). Census: 71 blocked scripts, 208 sites alone. **Done in part (ARC-SCRIPT-1164, issue #1164):** set S37 holds all thirteen commands; the use, placed, heartbeat and dying hosts apply three conditions and six actions ([ledger WO1–WO13](../research/ledger/scripts.md#world-object-commands-arc-script-1164-e5-b)); left: transfer item, set written start, destroy item named and toggle item inventory display need a per-item field write or removal the session does not offer (the follow-up issue holds them) | M | E5-r (done) |
| E5-c | Story and time actions and conditions: global quest state, rumors quelled, hours, time, give XP, give fate point, blessing and curse. Census: 80 blocked scripts, 98 sites alone (the smallest of E5-a to E5-c). **Done (ARC-SCRIPT-1165, issue #1165):** set S34 (6 conditions, 13 actions), the viewer's use, tile, sector, heartbeat, dying and dialog hosts, [ledger SQ1–SQ14](../research/ledger/scripts.md#story-and-time-commands-arc-script-1165-e5-c); the item-event host (blessing items) stays open | M | E5-r (done) |
| E5-d | Dialog trainers and special replies (`t:`, `c`, `r`, `z`, `r:`, positive `IN`), skill training window, Educator. **Part done (ARC-SCRIPT-1150, issue #1150):** slice T1 = `t:` (the skill menu, the price on the reaction scale, the payment and the first-tier training; all 315 installed `t:` replies in 58 files; [ledger DTR1–DTR9](../research/ledger/social.md#trainer-reply-arc-script-1150)); slice R1 = the Player reply `r:` (ARC-SCRIPT-1304, issue #1304: one listed rumor the PC does not know and that is not quelled is drawn and told, for a price on the reaction scale when the reply names one, and written to the logbook; all 239 installed `r:` replies in 32 files; [ledger RMR1–RMR9](../research/ledger/social.md#rumor-reply-arc-script-1304)); slice C1 = the Player reply `c:` (ARC-SCRIPT-1321, issue #1321: the story line of the game's story state, the NPC's own record 10000 + state else the `gd_sto_*` entry at the NPC's race + 100 per state, with the reply's own response; all 212 installed `c:` replies in 41 files; [ledger STY1–STY4](../research/ledger/social.md#story-reply-arc-script-1321)); slice H1 = the Player reply `h:` (ARC-SCRIPT-1732, issue #1732: the healer menu by the NPC's college rank, the skill service and the spells for a price on the reaction scale, free from a follower; all 52 installed `h:` replies in 15 files; [ledger HLR1–HLR12](../research/ledger/social.md#healer-reply-arc-script-1732)); slice D1a = `q:` (ARC-SCRIPT-1532, issue #1532, all 5 installed replies); slice D1b = `d:` and `x:` (ARC-SCRIPT-1533, issue #1533); slice G1 = the NPC line `i:` and the NPC's last words after an insult (ARC-SCRIPT-1505, issue #1505); positive `IN` was already built by ARC-WORLD-1131; left, from issue #1150: `u:` and `z:` (8 installed `u:` replies, all repair; issue #1534, blocked by the item repair window) and the generated NPC lines `m:`, `r:` (none installed; issue #1535; the NPC-side `r:` is the same call, 0x416840, with no reply around it) | M–L | — |
| E5-e | Loops and calls: `loop break` (`call script at` occurs once on a line no run reaches). Census: 17 blocked scripts, 42 sites alone. **Done (ARC-SCRIPT-1166, issue #1166):** set S40 (S38 plus loop break 20, ledger PG15); the use, placed, heartbeat and dying sets hold it (of the 22 installed scripts with a loop break S40 admits 12 whole). Call script at (55) stays refused: it occurs once on a line no run reaches | S | E5-r (done) |
| E5-f | Vitals commands (the first item of the census order): get hit points 107, get fatigue points 108, heal poison 128 and get stat of the poison level (24), with the use set's heal, destroy of the item in use and magick-tech adjustment. Census: 1 food use script (30282) on 729 objects, 836 sites alone. **Done in part (ARC-SCRIPT-1481, issue #1481, set S43, [ledger VT1–VT8](../research/ledger/scripts.md#vitals-commands-and-the-food-use-script-arc-script-1481)):** the script is admitted by the use set and runs for the hero (reads its vitals and poison, destroys the food, heals hit points, fatigue and poison through the session's life step); under S43 the census admits 1,575 attached scripts on 6,856 sites (S38: 1,558 on 6,113, 30282 refused). Not built: an NPC that eats (the AI host's item use runs no script), script eye candy, a heal poison of a negative amount | S | E5-r (done) |

**Census result (ARC-RES-1101, [details](../research/script-refusal-census.md)).** The order that frees the most placed sites under S33 is: vitals
(`get_hit_points`, `get_fatigue_points`, `heal_poison`: one food use script on 729 objects; 836 sites alone; done in part, E5-f), E5-a, critter-state conditions
(met before, bad associates, invisible, in dialog, in combat, can see, prowling; 267 sites alone), E5-b, E5-c, spell effects and spell-state queries
(eye candy, magictech adjustment, polymorphed, shrunk, mirror image; 62 sites alone), scene queries (get location, get scroll distance),
E5-e, then the media actions (E7-b). Another 60 attached scripts are also refused for a reason that is not a type (45 for an object operand kind
S33 does not admit, 13 for a computed goto, 2 for a goto beyond the end), a gap no planned slice covers. The vitals, critter-state and spell-effect groups have no slice in this plan; they are proposed to the
coordinator as work items. The raise hook (E4-j, magic audit MA-10) is blocked only on two of its 15 placed sites (`change_script` and
`set_pc_quest_unbotched`); the spell hooks of E4-g are not gated by E5-a or E5-b.

### E6 Traps and placed devices

**Goal.** Traps are a full system: spotted by a roll, disarmed, placed by the player, fired by script 30002. FR: FR-ITEM-016, FR-NAV-006,
FR-CHAR-017, FR-CHAR-019. Skills plan slice **S10**, audit slice K6.

**Exists.** Trap objects on maps with step firing and damage; disarm in the session but without a viewer action.

| Slice | Content | Size | Needs |
| --- | --- | --- | --- |
| E6-a | Spot-trap roll on every path step and use, the master second roll (**done**, ARC-MAGIC-057); disarm action and the skill-bar button; the expert's device (**done**, ARC-SKILLS-1156, [SP3](../research/ledger/technology.md#spot-trap-roll-arc-magic-057): the skill cursor, 4 action points; the skill pane's button is **done**, ARC-UI-1113, [#1113](https://github.com/Simarglok/arcanum-steamwoven/issues/1113); the follower substitution is open) | M | — |
| E6-b | **Built (ARC-WORLD-124 timer bombs, ARC-WORLD-125 script traps).** Placed devices: use an item with the trap-device flag on an object or tile, the device is spent, the marker object, the NPC alarm for record 176 | M | E1-a |
| E6-c | The native trap script 30002 (the shot) | S | E6-a |

### E7 Media, endings and credits

**Goal.** Movies, intro, credits, script-queued slides and the endings. FR: FR-MEDIA-001, FR-MEDIA-002, FR-UI-001. Audit slice K12 (proposed
for after the alpha). **Decision first:** how to decode Bink video and audio under the rules (free tools with free licences, MIT
project); the audit proposed no decoder.

| Slice | Content | Size | Needs |
| --- | --- | --- | --- |
| E7-r | **Done (ARC-RES-1108, [Bink movies](../research/bink-movies.md)):** decoder options and licences (ffmpeg's Bink support is LGPL; or ship no video and show stills), the movie list and where scripts and maps call it. Ten Bink 1 files (`BIKi`), 8 numbered; callers: start-up, logos, View Intro, new game, script action 103 (6 scripts); recommended decoder: ffmpeg as an external program; the owner chooses the scope (issue #1170) | S–M | owner decision on scope |
| E7-a | **Part done (ARC-RENDER-1170, issue #1170, owner decision 2026-11-12):** movie player over an external ffmpeg, menu "View Intro", the new game's two movies and (ARC-RENDER-1242) the start-up logos and movie 8, all only in the game build and the `movies` smoke stage. **Open:** script action 103 (`fade`, six scripts) in the script engine and its host (time advance, track operand, movie by number), a task of its own; the teleport request's own movie numbers are 0 in every shipped script | M–L | E7-r |
| E7-b | Credits screen and slide types 0 and 2; script-queued slides (`queue_slide`), fades; ending selection | M | E5-c (media actions) |

### E8 Player windows and economy leftovers

**Goal.** The windows and small systems the surveys found stubbed. FR: FR-CHAR-024, FR-UI-002, FR-UI-004, FR-UI-005, FR-UI-006, FR-ITEM-019,
FR-ITEM-008, FR-MEDIA-003, FR-COMBAT-002, FR-COMBAT-003.

| Slice | Content | Size | Needs |
| --- | --- | --- | --- |
| E8-a | **Done (ARC-UI-1112, #1112).** Fate window and the Fate Points button (rules and the grant exist); key `F` | S–M | — |
| E8-b | **Done (ARC-UI-1113, #1113).** Skill pane of the game bar (bar mode 2, `K`); PickPocket and DisarmTraps hotkeys | S | E6-a for disarm |
| E8-c | Companions screen and follower menu (read, ARC-RES-1107: slices E8-c1…E8-c3 [below](#e8-c-and-e8-e-slices-read-by-arc-res-1107)); the barter follower switch is **done** (ARC-UI-1120, #1275) | M | — |
| E8-d | **Done (ARC-UI-1114, #1114).** Combat action-point readout and the turn-order list (owner decision 2026-10-28) | M | — (UI-124 runs beside) |
| E8-e | Options windows hidden today: violence filter, brightness, auto-switch weapons, follower skills (combat taunts built by ARC-AI-1527) (read, ARC-RES-1107: slices E8-e1…E8-e5 [below](#e8-c-and-e8-e-slices-read-by-arc-res-1107)); the autosave timer is **dropped** (the original has none, OH6) | M | — |
| E8-f | **Done (ARC-RENDER-1126, #1126).** Sound callers: scenery loops, busting, AI barks, NPC footsteps, item drop | S–M | — |
| E8-g | Identify window, heal another critter, lockpicks on chests (viewer wiring; the session takes containers), junk-pile partial take, poison sources. **Part done (ARC-UI-1127, issue #1127):** heal another critter, lockpicks on containers and the Identify sheet of an NPC (Read Aura). **Open:** the container window of See Contents and the item description window of Identify (what a press in the container's mode 4 may move is not read), the junk pile's and a container's partial take of a stack (the question window of a pile). **Poison sources surveyed (ARC-RES-1329, issue #1329, [poison-sources](../research/poison-sources.md), ledger PZ1–PZ10):** every installed source is built except four gaps, each an issue: the food use script 30282 and the vitals commands ([#1481](https://github.com/Simarglok/arcanum-steamwoven/issues/1481)), wield-on scripts that poison ([#1482](https://github.com/Simarglok/arcanum-steamwoven/issues/1482)), the combat-point scripts ([#1483](https://github.com/Simarglok/arcanum-steamwoven/issues/1483)) and NPC steps onto traps (#1469) | M | — |
| E8-h | Gambling and newspapers queue (K10; proposed for after the alpha) | M | E5-c |

#### E8-c and E8-e slices (read by ARC-RES-1107)

The two Mac reads of section 6 are done (2026-11-12, [ARC-RES-1107](https://github.com/Simarglok/arcanum-steamwoven/issues/1107): ledger [FP1–FP13](../research/ledger/ui-audio.md#follower-panel-and-menu-arc-res-1107) and [OH1–OH6](../research/ledger/ui-audio.md#hidden-options-arc-res-1107)). They show that part of E8-c was already built, and that E8-e had one item with nothing to copy. The slices below are planning labels (one result each, size and what each needs); the coordinator makes the issues and gives the task codes. They are all in the UI/viewer lane except E8-e4 and E8-e5, which touch the session.

| Slice | Content (ledger rows) | Size | Needs |
| --- | --- | --- | --- |
| E8-c1 | Follower panel shell: the show/hide button (art 501, 502) and the scroll arrows (art 506–509) for seven or more followers, the six frames showing `offset .. offset + 5` (FP1, FP3, FP5, FP13). Whether the shown flag and offset travel with the save (FP2: the original saves them; they have no gameplay effect) is the slice's one decision: recommended in the session only, since FR-LIFECYCLE-004 keeps saves for game state | S | — |
| E8-c2 | Menu form and the two disabled rows: the menu as art 826 with eight text rows at 18 pixels (FP8), Wait greyed instead of hidden, Inventory opening the barter follower form when the can-open test passes (FP11, reusing the cycle's `BarterCycleSpeaker`), Character Sheet opening the NPC sheet (FP12, `AreaNpcSheet`), the open menu consuming every release (FP9), no clamp to the screen (FP7) | M | — (E8-c1 is not needed) |
| E8-c3 | A left click on a portrait aims a spell, a skill or an item at that follower in the target modes and is an ordinary click otherwise (FP6; a heal or buff on a follower through the portrait) | S–M | the viewer's target modes (spell, skill, item use); the hover is done (ARC-UI-1281) |
| E8-e1 | Violence filter: the hit effect and the dying animation read it, the blood pool wiring, the options row (OH2) | S | — |
| E8-e2 | Brightness: the Video page's slider 0–9, the final colour multiply 1.0 + 0.1 × value, fades returning to it (OH1) | S–M | — |
| E8-e3 | **Built (ARC-AI-1527):** Combat taunts: the host call at the end of an attack, the options row, default on, never in a network game (OH5, `FollowerRules.Taunt`) | S | the floating text layer (built, ARC-UI-021) |
| E8-e4 | Auto Switch Weapons: the PC's re-wield after no ammunition, a destroyed weapon and (for a struck PC) the item-loss branches of the damage step, the options row, default off (OH3, PI5). **Built in part (ARC-COMBAT-1720):** the destroyed-weapon caller and the settings file line; the options row waits for a layout decision (issue 1729), the other callers are not built | M | the best-item-for-slot function 0x465170 (partly built, starting kit) |
| E8-e5 | Follower Skills and the hand-over: the better follower does a lockpick use or the skill bar's Disarm Traps (only these two, OH4), Ctrl skips it, the hand-over line floats, the options row (PL5; owner recheck 8 was corrected on 2026-11-12: repair does not hand over in the original) | M | E8-b and E6-a for the Disarm Traps bar path |


### E9 Platform, controller and presentation

**Goal.** FR: FR-PRODUCT-001, FR-PRODUCT-002, FR-PRODUCT-005, FR-UI-007, FR-LIFECYCLE-001, FR-LIFECYCLE-004.

| Slice | Content | Size | Needs |
| --- | --- | --- | --- |
| E9-a | Gamepad in the game UI (inventory, logbook, dialogs, map), on the lab's input framework | L | owner's device check later |
| E9-b | Adaptive UI layout and UI scale in game windows (`UiScalePercent` is stored, not applied) | M–L | — |
| E9-c | Windows validation of the packaged build on a Windows machine; Linux and handheld presets | M | owner's hardware |
| E9-d | One settings file as the original's `arcanum.cfg` (done: ARC-UI-1130, [issue #1130](https://github.com/Simarglok/arcanum-steamwoven/issues/1130)) | S | — |

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
| E0-a | **Done (ARC-RES-107).** Refresh the [coverage matrix](../requirements/coverage.md) and the "still missing for a playable game" section of the [status catalogue](status.md) against the code of this document (cloud, docs-only, no checks) | M |
| E0-b | Write the concept documents K2–K12 that the audit proposed and nobody wrote, for the epics the owner chooses (cloud) | S each |

## 5. Order and dependencies

```
E1-r ─┐                         E5-r (done) ──► E5-a, E5-b, E5-c ──► E7-b, E8-h
E1-a ─┴► E1-b                   E3-r ──► E3-a ──► E3-c
E1-a ──► E1-c ──► E1-e ──► E1-f   E3-r ──► E3-d
E1-d ──┬──────────┘              E2-r ──► E2-a, E2-b;  E2-a ──► E2-c
       ├► E4-a ──► E4-b, E4-f     E1-c ──► E2-a, E2-b
       └► E4-c ──► E4-d, E4-e, E4-h   E1-a ──► E6-b
E5-c ──► E4-g                    E6-a ──► E6-c, E8-b
```

**Wave 0, no dependency, can start in parallel** (limit by the Mac): E1-r, E1-a, E1-d, E0-a, E5-r (done), E3-r, E2-r, E3-b, E8-a, E8-d, E8-f, E6-a, E5-d.
**Wave 1:** E1-b (closes two B47 reports), E1-c, E3-a, E5-a…E5-c, E5-e, E4-a, E4-c, E8-b, E6-c.
**Wave 2:** E1-e (closes the third B47 report), E1-f, E2-a, E2-b, E3-c, E3-d, E4-b, E4-d…E4-h, E6-b, E8-g.
**Wave 3:** E2-c, E7 (after the decoder decision), E8-c, E8-e, E8-h, E9, E0-b (any time, cloud).

Conflict rules for threads: one writer per file set. The spell-cast files (`SessionSpellCaster*`) are one lane (E1-b, E1-e, E1-f, E4-a to E4-h); the object directory and session create paths are another (E1-a, E1-c, E2, E6-b); script admission files are a third (E5).
Threads of different lanes run beside each other.

## 6. Reads of the original that the plan needs

Mac reads, each a docs-only research slice that ends in ledger rows: E1-r, E2-r, E3-r, E5-r (a data census, done), E7-r (a research on decoders),
E8-c and E8-e (UI rows; **done**, ARC-RES-1107, 2026-11-12). Everything else uses reads that already exist in the [ledger](../research/original-verification-ledger.md); each
implementing slice still reads the F0 facts of the functions it changes, as the owner decided on 2026-10-20.

## 9. Owner answers (Aleksandr, 2026-11-10)

The plan is the work order, in the order of section 5. The priority of E7 and E8-h, the ffmpeg Bink read and the E10 deferral are in the
[decisions](../architecture/decisions.md) row "Missing systems work order (2026-11-10)". Also:

- Mac concurrency: Godot lanes (one heavy and two short for `window` and `headless`, an export alone; [lanes](../workflow/toolchain.md#godot-lanes-arc-dev-400)) and two `dotnet` slots across all
   threads ([launch queue](../workflow/toolchain.md#launch-queue-arc-dev-033)); executable reads and docs-only slices are not queued.
- Windows validation and the in-game gamepad (E9-a, E9-c) come after the content epics.
