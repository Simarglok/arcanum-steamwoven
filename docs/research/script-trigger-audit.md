# Script trigger audit: what the original fires and what Steamwoven runs

## Status and scope

Status: **Research and fix**, work item ARC-SCRIPT-047 (2026-10-29). The owner asked whether the scripts that do not run in
Steamwoven (for example Virgil's talk at the start of a new game, which the original opens at once) are a systemic gap or one
script each. This note answers it from the retail 1.0.7.4 `Arcanum.exe` (SHA-256 `9bd80511…23678d52`) and the installed data:
every attachment point and other entry the original runs a script from, with its call sites, against the Steamwoven host that
runs it (sections 2 and 3); the interpreter's condition and action tables against the host admission sets (section 4); and a
prioritised list of fix slices, each its own task (section 6).

Evidence levels follow the [original executable check](original-executable-check.md). Rows TG1–TG8 of the
[ledger](ledger/scripts.md#script-triggers-arc-script-047) were read with `llvm-objdump` and, independently, by a second reader
(method step 6). The event catalogue of the [dispatcher research](scr-event-dispatcher.md#2-event-catalogue-s) (E rows, F0)
supplied the names; every call site below was found by scanning all direct calls of the object script entry 0x441980 and reading
the point argument each one pushes. Behaviour is described in prose; no listing, byte or oracle output is in git. Original parity
beyond the rows named here is **NOT_VERIFIED**.

The firing conditions of every trigger kind (when, for whom, which gates, what the return changes) and the corrections to
this note (TG2 callers, points 15 and 16, the first heartbeat, unlock, catching thief) are in
[script trigger conditions](script-trigger-conditions.md) (ARC-RES-2815, rows TQ1–TQ28); its section 2 is newer than the
host column of section 2 here.

## Answer

**Systemic, three causes.** A script of the original does not run in Steamwoven when (a) no Steamwoven host fires its trigger
(section 2), (b) its host's admission set refuses one of its commands (the original's executor has 142 action types and 53 condition
types (ledger TG7); each host admits a subset, section 4), or (c) the host runs it but cannot apply an effect (a teleport from a heartbeat, a
creation off the shown map). Per-script fixes would never end: the slices of section 6 are per trigger and per command group.

## 1. Virgil's opening talk (TG3–TG6)

Fixed in ARC-SCRIPT-047. It was cause (b): Virgil's **heartbeat** (point 19, script 1100), not a dialog or tile trigger. The AI
tick runs it when the new game's first tick finds the PC within 30 tiles (TG3, 0x4AD200, 0x4AD420); it loops over everyone in his
vicinity (the PCs and NPCs in the play-window box centred on the attachee, at most 100, destroyed or switched-off objects skipped,
TG4), and for the PC calls his own dialog point (script 1324, line 4; the dialog gates of a call-ex apply, TG5) whose dialog
action opens DLG 1324 at node 1, then removes itself and skips the tick's default (TG6). Steamwoven added set S17 (critter
vicinity, `IScrWorldQueries.CrittersInVicinity`), call-ex of the dialog point, a wider heartbeat set and the viewer's
`OpenHeartbeatConversation`; the smoke stage `opening` observes it. Both rule modes behave the same (the original's own
behaviour). A talk asked for while another conversation is open is dropped, not kept (the start callback returns silently in dialog mode, ARC-SCRIPT-2760, U5); one asked for while a window outside the stack is open or the hero is held in combat mode stays pending and opens
once it can (Steamwoven's choice, U5). A call-ex of the dialog point refuses a callee that reaches call script.

## 2. Attachment points: who fires them

`0x441980` has 72 direct call sites. Each pushes the point as an immediate, except call-ex (0x44B1D6, the script's operand) and the
damage on armor (0x4B47F8, a register). Every point but 31 (dialog override, a DLG number, never run) has at least one (TG1). Rows
and admission counts are the installed attachments of the 79 maps map mode opens (census, section 5); "admitted" is the host's set
from line 0, static.

**Correction (ARC-SCRIPT-2825):** these rows count the shown windows of the 79 maps, so the main map's NPCs and doors outside its window are missing. The full slot census ([scr-census](scr-census.md)) finds 1 slot at unlock (3), 1 at leader killing (20) and 10 slots (3 scripts) at caught thief (33), and none at buy object (17); the "none" rows for 3, 20 and 33 below are wrong ([#2860](https://github.com/Simarglok/arcanum-steamwoven/issues/2860), [#2859](https://github.com/Simarglok/arcanum-steamwoven/issues/2859), [#2858](https://github.com/Simarglok/arcanum-steamwoven/issues/2858)).

| Point | Name | Call sites (0x441980 callers) | Steamwoven host | Rows (scripts) | Admitted |
| ---: | --- | --- | --- | ---: | ---: |
| 0 | examine | 0x57CD93 | the description step of the world hover, hotkey bar and inventory pointer (`AreaUseScriptHost.RunExamine`, ARC-DEV-326); barter and dialog windows not | 54 (31) | under the use set |
| 1 | use | 0x428C21, 0x428CAB, 0x430CE3, 0x452EEB, 0x462E0C, 0x4BC9A7, 0x572477, 0x5724E5, 0x57A558, 0x57B31F | use click, item use (`AreaUseScriptHost.Admission`) | 2467 (245) | 2448 (ARC-SCRIPT-2248: a run of the opt-in trigger census with `ARC_SCRIPT_TRIGGER_CENSUS`, not pinned by a test, 3 more than the 2445 of ARC-SCRIPT-1734, since the use set admits toggle state, 56) |
| 2 | destroy | 0x43CD0E | **none** | 26 (7) | — |
| 3 | unlock | 0x4AEBC5, 0x4AEDC4 | open attempts of portals and containers, shown map (`RunUnlock`, use set; ARC-SCRIPT-2860) | 0 shown (1 on the main map) | — |
| 4 | get | 0x4618F2 | hero's item moves (`SessionItemScripts`) | 0 | — |
| 5 | drop | 0x461B2A | hero's item moves | 0 | — |
| 6 | throw | 0x4B3C6A | **none** | 0 | — |
| 7 | hit | 0x4B2B7F, 0x4B2F90, 0x4B37A3 | the weapon's hit script before the damage of every resolved hit, hero and NPC (`AreaUseScriptHost.RunCombatPoint`, ARC-SCRIPT-1483; the hero's thrown item's is ARC-MAGIC-042's) | 26 (11) | 26 under the combat-point set (ARC-SCRIPT-1562: the placed set plus 91, 80, 120 and condition 37, 24 rows; ARC-SCRIPT-1661: plus condition 31 and action 137, all 26) |
| 8 | miss | 0x4B2F06, 0x4B311F, 0x4B3972 | **none** | 0 | — |
| 9 | dialog | 0x57CC90, 0x5684AA | talk with a hero, heartbeat call-ex | 180 (122) | 124, 1 missing (ARC-SCRIPT-1733: 118 before; the critter-state conditions) |
| 10 | first heartbeat | 0x4AD2B2 | AI tick, shown map (`HeartbeatAdmission`) | 23 (6) | 23 (was 21) |
| 11 | catching thief | 0x4AEFCD, 0x4C76E5, 0x4C7900 | pick pocket (use set) | 0 | — |
| 12 | dying | 0x45DA57 | the session's life step asks the shown map's use-script host (`AreaUseScriptHost.RunDying`, `DyingAdmission`, ARC-DEV-312; every kill path) | 167 (61) | the installed 1805, 2137, 2173 whole; the others not counted yet |
| 13 | enter combat | 0x4AAE29 | the session's danger setter asks the host (`GameSessionOwner.DangerHookScript`, `AreaUseScriptHost.RunDangerPoint`, `DyingAdmission`, ARC-AI-1530) | 9 (8) | not counted yet |
| 14 | exit combat | 0x4AAF32 | as 13 (ARC-AI-1530) | 0 | — |
| 15 | start combat | 0x4A86A7 | **none** | 2 (2) | — |
| 16 | end combat | 0x4A8688, 0x4B7681 | **none** | 0 | — |
| 17 | buy object | 0x4621D7, 0x578446 | **none** | 0 | — |
| 18 | resurrect | 0x4B5A46 | **none** | 8 (8) | — |
| 19 | heartbeat | 0x4AD2E1, 0x4B704A | AI tick, shown map (`HeartbeatAdmission`) | 141 (84) | 98 (was 32; ARC-SCRIPT-1733: 95 before; ARC-SCRIPT-1771: 96 before; ARC-SCRIPT-1815: 97 before, the opt-in census reads the shown windows of the maps, so the 21 main-map sites of script 2231 beyond the Crash Site window are not in this count) |
| 20 | leader killing | 0x45DB95 | kill step, shown map (`RunLeaderKilling`, dying set; ARC-SCRIPT-2859) | 0 shown (1 on the main map) | — |
| 21 | insert item | 0x4648C2, 0x465977, 0x46687A, 0x46697F | hero's item moves | 28 (12) | 3 |
| 22 | will KOS | 0x4AE179 | **none** | 60 (7) | — |
| 23 | taking damage | 0x4B446B (ARC-DEV-254: 0x4B47F8 is points 34/35, not 23) | script damage, attacks, throws, spell damage, burns, traps, knockback bumps (`AreaUseScriptHost.RunTakingDamage`, ARC-DEV-261; not the kill, poison tick, acid) | 1 (1) | taking-damage set (heartbeat set plus 120 and 68, ARC-SCRIPT-2513) |
| 24 | wield on | 0x46558E, 0x467847 | hero's item moves | 98 (29) | 92 |
| 25 | wield off | 0x46557D, 0x467D49 | hero's item moves | 92 (28) | 91 |
| 26 | critter hits | 0x4B3003, 0x4B37FC | the attacker NPC's script before the damage of every resolved hit (`RunCombatPoint`, ARC-SCRIPT-1483; the hero has no script data here) | 18 (2) | 18 under the placed set |
| 27 | new sector | 0x4F24A3 | **none** | 3 (3) | — |
| 28 | remove item | 0x467C7C | hero's item moves | 0 | — |
| 29 | leader sleeping | 0x57BB03 | **none** | 0 | — |
| 30 | bust | 0x43F30D, 0x43F42A, 0x43F4C0, 0x43F574, 0x4F076E | **none** | 64 (25) | — |
| 31 | dialog override | none | — (DLG number) | 0 | — |
| 32 | transfer | 0x461D18 | hero's item moves | 6 (4) | 3 |
| 33 | caught thief | 0x4C76CE, 0x4C78E9 | NPC thief's pick pocket: `AreaAiHost.PickPocketAttempt` → `AreaUseScriptHost.RunCaughtThief` (ARC-SCRIPT-2858); the hero thief has no script | 10 | — |
| 34 | critical hit | 0x4B2BAE, 0x4B2FD4, 0x4B37CD (and 0x4B47F8) | the weapon's script for a critical hit, after the hit script (`RunCombatPoint`, ARC-SCRIPT-1483); the worn armour pieces' (0x4B47F8, `AreaUseScriptHost.RunArmourCritical`, ARC-SCRIPT-2828: 2 of the 4 rows are armour, scripts 30219 and 30237, admitted whole; 35 never runs on armour) | 4 (4) | 4 under the combat-point set (ARC-SCRIPT-1562, 3 rows; ARC-SCRIPT-1661: plus condition 31, all 4) |
| 35 | critical miss | 0x4B2F35, 0x4B3163, 0x4B399C (and 0x4B47F8) | **none** | 2 (2) | — |


Points the original fires for which Steamwoven has **no host**: 2 destroy, 3 unlock, 6 throw, 8 miss,
15 start combat, 16 end combat, 17 buy object, 18 resurrect, 20 leader killing, 22 will KOS, 
27 new sector, 29 leader sleeping, 30 bust, 33 caught thief, 35 critical miss. Points with a **partial** host:
7 hit, 26 critter hits and 34 critical hit (the hit step of the session's attacks, ARC-SCRIPT-1483: not the miss points, the hero's own script, a weapon without a row), 1 use (use clicks on guarded sites and written items; not the use goal on portals and containers, the bed, the trap disarm or the
magick-tech component), 4/5/21/24/25/28/32 (the session's item scripts for the hero's own moves only), 9 dialog (talk,
resumption, deferred calls, a heartbeat's call-ex; not the dead/surrender speech gates of a talk), 10 and 19 (real-time AI ticks
of the shown map; the turn-based subturn heartbeat 0x4B704A runs no script yet), 11 catching thief (pick pocket only; the
guard's crime notice 0x4AEFCD is not hosted), 12 dying, 23 taking damage and 0 examine (hosted, with the limits in the table).


## 3. Entries that are not an object slot

| Entry | Original | Steamwoven |
| --- | --- | --- |
| Tile script | 0x4C0980, from the location change 0x444270 for a PC or NPC (type 15/16) whenever a location set passes mode 1 or 2 (TS2): moves (0x43E770, 23 callers), object placement (0x4415C0, 12), creation (0x442260, 4), 0x43EA20; mode 0 (0x4423E0, 5 callers) runs none. A second caller walks a tile path (0x455350 from 0x452630) and runs each tile's script (TG2). | Map mode: the PC's steps (ARC-SCRIPT-017) and the companions' (ARC-SCRIPT-046). AI-host movers since ARC-DEV-206 and pushed companions since ARC-DEV-298; since ARC-DEV-328 also companions carried from another map (steps and arrival), a companion's catch-up jump and waypoint relocation (TG9); since ARC-DEV-351 also a leaderless ex-follower's arrival when its home map opens (the saved teleported mark, schema 86) and the PC's own arrival tile after a map change, a landing or a same-map teleport (a same-map arrival drops its jump point and teleport, as the original refuses requests during the change). **Not** a created or placed critter, a follower that travels with a same-map teleport, or the path walk. Of 3,012 tile scripts 1,186 ran and 1,696 were refused by their set at ARC-SCRIPT-052 (the tile census of `AreaUseScriptsOriginalTests`). |
| Sector script | 0x4F2330 for the local player entering a sector (TS3); followers then get point 27. | Map mode, PC crossing a sector. Point 27 not run. |
| Call script (7) | 0x44B030: the callee from its header at the caller's point, every triggerer × attachee pair (OV1, X11). | Use, tile and sector sets; not the talk or heartbeat sets. |
| Call-ex (81) | 0x44B170 → 0x441980 (WE3, TG5). | Item set, heartbeat set, since ARC-SCRIPT-052 the tile and sector set; point 1 refused. |
| Deferred calls (54, 55) | Game-time events (one shared handler, TG7). | Call in (54) through the session queue (talk and use sets); call at (55) refused. |
| End-game slides | Scripts 997–999 from the slide UI (0x569720). | None. |
| Native trap scripts | Inside the interpreter (0x4BCB70). | Trap state (ARC-WORLD-110). |

## 4. Conditions and actions

The interpreter dispatches conditions 0..52 through the table at 0x44714C and actions 1..141 by type − 1 through 0x44ADBC (type 0
and anything above 141 fall to the default: next line). Every action type has its own case except call in and call at (54, 55),
which share one (TG7). Each Steamwoven host admits a subset: talk with a hero (`AreaScriptHost.SkillAdmission` and
`SkillTalkAdmission`), use click, item use and catching thief (`AreaUseScriptHost.Admission`), tile and sector
(`PlacedAdmission`), heartbeat (`HeartbeatAdmission`) and the hero's item scripts (`SessionItemScripts.Admission`). The sets
live in `ScrAdmission` and the host classes; the
[dispatcher contract](../scripting/scr-dispatcher-runtime.md) says what each set holds. Counts of what the installed attachments
reach first come from the census (section 5), not from a table here.

## 5. Census of the installed attachments

`ScriptTriggerCensusTests` (opt-in, the installed 1.0.7.4 data; command under Reproduction) counts, over the 79 maps map mode
opens, one row per object entity and point (every point but 31) and the first command each host refuses; numbers only, the file
it writes stays private. Run it again for current counts. The first census (2026-10-29) found 3,479 attachments on 22 points, 2,467
of them use (1) on doors and containers; points with no host held 444 attachments (dying 167, bust 64, will KOS 60, examine 54,
destroy 26, hit 26, critter hits 18, enter combat 9, resurrect 8, critical hit 4, new sector 3, start combat 2, critical miss 2,
taking damage 1; dying, examine and taking damage have hosts since).

- **Opening heartbeats.** Nine installed heartbeats call their NPC's own dialog point as 1100 does: the Crash Site's 1100 (Virgil)
  and 1717 (the Molochean agent there, after a story flag), Wheel Clan 2700, the Void 1130 and 3081 (Arronax's interior), the Panarii
  temple 2602, Drog's cave 1490, Dernholm castle 1015 and Vollinger's basement 2106. The heartbeat set admits 1100, 2700, 1130, 3081
  and 2106 (ARC-SCRIPT-047, 049). The other four stopped on a command outside the set when read: 1717 on set day standpoint (45), then
  set night standpoint (46), get location (95) and action run to (74) (45, 46 and 74 have been admitted since ARC-SCRIPT-1163 and ARC-DEV-231, 95 since ARC-SCRIPT-1359, so 1717 now runs whole); 2602 on obj can see obj (condition 28), then obj within range
  (34); 1490 on obj is in dialog (condition 26); 1015 on a global variable operand (admitted by the viewer's heartbeat set since
  ARC-DEV-100). Recount with the census before relying on this.
- **Tile scripts** (not object slots): 3,012 in the installed data; 1,186 ran and 1,696 were refused at ARC-SCRIPT-052 (slice 8).


## 6. Fix slices (prioritised)

Each slice is its own task and thread; codes come from the coordinator. "Reach" is what the census says it unlocks. Done slices
carry their task code; the open ones are 5, 6, 7, 10, 11, 12, 13 and the rest of 2, 3, 4, 8 and 9.

| # | Slice | Covers | Reach |
| ---: | --- | --- | --- |
| 1 | Heartbeat conditions and world queries | **Done** by ARC-SCRIPT-049 (conditions obj is switched off, obj is named, obj is dead, has reputation; actions distance between, call script in, set faction in the heartbeat set; damage and destroy went to their own slices) | |
| 2 | Dialog-point commands | **Done in part** by ARC-SCRIPT-050 (S19) and ARC-DEV-178 (S23, get reaction). Left: critter disband (22) comes only with transform into basic prototype (40), all 17 rows script 30009; obj is wielding over everyone in the group (focus 13, 14 rows, script 1160) is a list condition (slice 11); set reaction (30, 0x447ABA: reaction_adj by the difference) and give gold (33); the loop-for talks 1105, 1111, 1116 need conditions over lists and actions 99, 81, 36 | talks that run default |
| 3 | Tile scripts for every moved critter | The location change of TG2 for AI movers, visitors, a jump or teleport arrival, a created or placed critter, and the path walk 0x455350. **Done** for AI-host movers (ARC-DEV-206), pushed companions (ARC-DEV-298), carried companions (ARC-DEV-328) and arrivals (ARC-DEV-351). Open: a follower that travels with a same-map teleport, created or placed critters, the path walk | traps, guards and story tiles NPCs step on |
| 4 | Dying and destroy | **Point 12 done** by ARC-DEV-312 (ledger [DY1–DY8](ledger/scripts.md#creature-dying-script-point-12-arc-dev-312-b21-follow-up)). Point 2 (destroy, 0x43CD0E) is open | 26 attachments |
| 5 | Combat points | **Done in part** by ARC-SCRIPT-1483: hit, critter hits and critical hit (7, 26, 34) from the session's hit step (ledger [CP1–CP5](ledger/combat.md#combat-point-scripts-arc-script-1483); the poison weapon scripts and the hunting weapon run since [#1562](https://github.com/Simarglok/arcanum-steamwoven/issues/1562): ledger CP7–CP10; the area weapons and the alignment weapon since [#1661](https://github.com/Simarglok/arcanum-steamwoven/issues/1661): CP11–CP13, so every installed hit, critter-hits and critical-hit row is admitted). Enter and exit combat (13, 14) are done by ARC-AI-1530 (the danger setter's hook). Left: start and end combat (15, 16), miss and critical miss (8, 35) (taking damage, 23, is done by ARC-DEV-261) | 61 attachments (Virgil's 1997 at enter combat), 48 of them at 7, 26 and 34 |
| 6 | Crime and KOS | will KOS (22, 0x4AE179), bust (30), caught thief (33) and the guard's catching thief (11 at 0x4AEFCD) | 124 attachments |
| 7 | Use-set commands | get hit points (107), set lock state (53), change art (59), computed goto, focus 8 in use scripts | 246 refused use attachments |
| 8 | Tile-script command groups | **Done in three groups**: ARC-SCRIPT-052 (S20, ledger TD1–TD3: call-ex, destroy, stop attacking), ARC-DEV-157 (S22, ledger GS1–GS4, bugs B20 and B23: get stat for the PC, vicinity loops) and ARC-DEV-298 (float line in the tile set; stage `tile-ghost`). Open: the remaining first refusals by group (action 32, action 22, change art 59, sector is blocked 45, condition 51, damage 60, play script eye candy 120, action 112, set day standpoint 45, monster generator disabled 46; 1779 stops at a computed goto) | tile scripts |
| 9 | Examine, new sector, resurrect | **Examine done** by ARC-DEV-326 (ledger HK24; made items ARC-SCRIPT-1119). Point 27 for followers (0x4F24A3, Virgil's 1568) done by ARC-SCRIPT-2822; open: 18 (0x4B5A46) | 11 attachments |
| 10 | Turn-based heartbeat | the subturn heartbeat 0x4B704A (point 19) in the round host | heartbeats in combat |
| 11 | List conditions and vicinity order | conditions over focus kinds 8, 9, 15 (SG6, U2) and the list order (U1) | scripts that test "anyone near" |
| 12 | Item-script commands | toggle state (56) and loop for (18) at insert item, call script in (54) at wield on | 35 item attachments |
| 13 | Conversation resume and DLG LF | TG8: "return to script" runs the speaker's dialog point; a use- or tile-opened talk then resumes his dialog slot, so its LF reads that slot; read the DLG LF field in the executable (now F0) | talks opened by use and tile scripts |


## Reproduction

```sh
# the census (all 79 maps, about six minutes); numbers only
DAT_ROOT="$GAME" ARC_SCRIPT_TRIGGER_CENSUS="$PRIVATE/script-triggers.json" "$DOTNET" test tests/Arcanum.TerrainViewer.Tests/Arcanum.TerrainViewer.Tests.csproj \
  --no-build --filter FullyQualifiedName~ScriptTriggerCensusTests
# Virgil's heartbeat over the installed Crash Site
DAT_ROOT="$GAME" "$DOTNET" test tests/Arcanum.TerrainViewer.Tests/Arcanum.TerrainViewer.Tests.csproj --no-build --filter FullyQualifiedName~AreaHeartbeatScriptTests
# the native observation
./arcanum smoke pr --stages opening --hero human-male
```

The call-site table of section 2 comes from one full disassembly of `.text` into the session scratchpad and a scan of every
`call 0x441980` for its pushed point and line; the scan is not kept.

## Unknowns

- **U1** The order of the vicinity list (TG4) is not read; Steamwoven lists the NPC rows in snapshot order, then the PC. A script that
  acts on the first object of a list (not a loop) may pick another object than the original.
- **U2** A condition over a list focus kind (8, 9; SG6: every, or for the "any" kinds some) is not modelled; the critter vicinity
  kinds are admitted in actions only.
- **U3** Whether mode 1 of the location change (same tile, TS2) really re-runs the tile script on every sub-tile offset change was
  not observed in the running game.
- **U4** ~~The surrender and mind-control states of an NPC are not modelled by the viewer's use host (it answers dead only), so their
  dialog gates never trigger there.~~ Closed by ARC-SCRIPT-2464: the use host and the talk host answer dead, surrendered (the session's danger record) and
  mind-controlled (spell flag 0x8000000) for an NPC of the shown map.
- **U5** What the original does with a heartbeat's talk while the PC is in another dialog is read (ARC-SCRIPT-2760: the dialog start callback 0x567460 returns silently when the dialog mode is on top, so the talk is lost, never retried; the heartbeat's other effects stay);
  what it does while a window outside the stack is open or the PC is under attack is not read, and Steamwoven keeps that talk pending until it can open.