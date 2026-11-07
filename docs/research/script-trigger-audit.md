# Script trigger audit: what the original fires and what Steamwoven runs

## Status and scope

Status: **Research and fix**, work item ARC-SCRIPT-047 (2026-10-29). The owner asked whether the scripts that do not run in
Steamwoven (for example Virgil's talk at the start of a new game, which the original opens at once) are a systemic gap or one
script each. This note answers it from the retail 1.0.7.4 `Arcanum.exe` (SHA-256 `9bd80511…23678d52`) and the installed data:

1. every attachment point and every other entry the original runs a script from, with its call sites, against the Steamwoven
   host that runs it (section 2 and 3);
2. the interpreter's condition and action tables against the sets each Steamwoven host admits, with counts of what the installed
   attachments need (section 4);
3. the chain behind Virgil's opening talk, traced in the executable and fixed in the same change (section 1);
4. a prioritised list of fix slices, each its own task (section 6).

Evidence levels follow the [original executable check](original-executable-check.md). Rows TG1–TG8 of the
[ledger](ledger/scripts.md#script-triggers-arc-script-047) were read for this task with `llvm-objdump` and, independently, by a
second reader that saw only the questions and the addresses (method step 6). The event catalogue of the
[dispatcher research](scr-event-dispatcher.md#2-event-catalogue-s) (E rows, F0) supplied the names; every call site below was
found again in the executable by scanning all direct calls of the object script entry 0x441980 and reading the point argument each
one pushes. Behaviour is described in prose; no listing, byte or oracle output is in git. Original parity beyond the rows named here
is **NOT_VERIFIED**.

## Answer

- **Systemic, three causes.** A script of the original does not run in Steamwoven when (a) no Steamwoven host fires its trigger
  (23 of the 35 points the original fires have no host at all, section 2), (b) its host's admission set refuses one of its commands
  (the interpreter runs 52 of 142 action types and 14 of 53 condition types; each host admits a subset, section 4), or (c) the host
  runs it but cannot apply an effect (a teleport from a heartbeat, a creation off the shown map). Per-script fixes would never end:
  the slices of section 6 are per trigger and per command group.
- **Virgil's opening talk** is cause (b): his **heartbeat** (point 19, script 1100), not a dialog or tile trigger. The AI tick runs
  it as soon as the new game's first tick finds the PC within 30 tiles; it loops over everyone in his vicinity, and for the PC it
  calls his own dialog point (script 1324) at line 4 with the PC as triggerer, whose dialog action opens DLG 1324 at node 1; then it
  removes itself and skips the tick's default. Steamwoven refused 1100 (the heartbeat set had no loop, no vicinity focus, no get
  object type and no call-ex, and call-ex refused the dialog point). **Fixed here** (section 1): new set S17 (critter vicinity),
  call-ex of the dialog point with the original's dialog gates, a wider heartbeat set, and the viewer opens the talk after the tick.
  A new game at the Crash Site now opens Virgil's talk without any input (smoke stage `opening`).

## 1. Virgil's opening talk (TG3–TG6)

| Step | Original | Before | Now |
| --- | --- | --- | --- |
| Tick | Sector load gives each NPC an immediate AI tick; a living NPC's first tick runs point 10 (Virgil has none), then the near test (alive, no turn-based round, the local PC within 30 tiles) runs point 19 with the NPC as triggerer and attachee; *run default* goes on to the AI pass, *skip default* reschedules (TG3, 0x4AD200, 0x4AD420). | Same (ARC-WORLD-114, `AiSchedulerRules`). | Same. |
| 1100 | Line 0 loop for over focus 8 (everyone in the vicinity), 1 get object type of the looped object, 2 not 15 (a PC) → loop end; 3 call-ex: attachee, point 9, line 4, triggerer the looped object; 4 remove this script; 5 return and skip default (TG6, data). | Refused: actions 18, 19, 34, 81 and focus 8, 10 outside the heartbeat set; the tick went on as without a script. | Admitted by the heartbeat set; the vicinity is the PCs and NPCs within 10 tiles on both axes (TG4). |
| Vicinity | Focus 8 and 9: the PCs and NPCs (type mask 0x18000) in the play-window box centred on the attachee, the attachee itself included; destroyed or switched-off objects skipped (mask 0x5E2F88 = 3 outside the editor); at most 100 (TG4, 0x44B4E0, 0x440FC0, 0x440B40). | Not modelled. | `IScrWorldQueries.CrittersInVicinity`, set S17; the viewer answers NPC rows at their live cells (AI-moved, companions) and the PC. |
| Call-ex | Runs the object's point-9 slot through the object entry 0x441980, so the interpreter's dialog gates apply: a dead NPC without the death-speech file flag 0x8 or a surrendered one without 0x10 runs nothing, a mind-controlled one runs 2188 (TG5, 0x44B170, 0x4449B0). | Refused for points 1 and 9; a dialog action in a call-ex callee refused. | Point 9 runs with the gates; a dialog action in the callee is allowed (its X11 store and the call-ex writeback are the same slot). Point 1 stays refused (the trap spot of a nested use is not modelled). |
| 1324 | Line 4: dialog node 1 (X11: the PC triggerer starts the talk with DLG 1324; the run's flags go to Virgil's dialog slot). A "return to script" end resumes his point 9 (G4). | — | `ScrDialogStart` with `Point = 9`; the viewer opens it after the event pump (`OpenHeartbeatConversation`), keeps the dialog slot under the dialog host's keys (`dialog.*`), lets the DLG use LF and resumes his dialog slot. |

Steamwoven both rule modes: the same (the original's own behaviour, no anomaly). At the Crash Site the PC starts 2 tiles from
Virgil, so the talk opens on the first ticks; the smoke stage `opening` observes it (2 frames, node 1, heartbeat removed, no second
talk). The run is applied before the talk opens (the heartbeat has removed itself), so a talk asked for while another
conversation or a window is open or the hero is under attack stays pending and opens once it can (Steamwoven's choice, U5). A
call-ex of the dialog point refuses a callee that reaches call script, as a dialog-point event does (the called script would skip
the dialog gates). The vicinity leaves out an NPC its map data has destroyed or off (object flags 0x1, 0x2) and one gone in the
session.

## 2. Attachment points: who fires them

`0x441980` has 72 direct call sites. Each pushes the point as an immediate, except call-ex (0x44B1D6, the script's operand) and the
damage on armor (0x4B47F8, a register). Every point but 31 (dialog override, a DLG number, never run) has at least one (TG1). Rows
and admission counts are the installed attachments of the 79 maps map mode opens (census, section 5); "admitted" is the host's set
from line 0, static.

| Point | Name | Call sites (0x441980 callers) | Steamwoven host | Rows (scripts) | Admitted |
| ---: | --- | --- | --- | ---: | ---: |
| 0 | examine | 0x57CD93 | **none** | 54 (31) | — |
| 1 | use | 0x428C21, 0x428CAB, 0x430CE3, 0x452EEB, 0x462E0C, 0x4BC9A7, 0x572477, 0x5724E5, 0x57A558, 0x57B31F | use click, item use (`AreaUseScriptHost.Admission`) | 2467 (245) | 2221 |
| 2 | destroy | 0x43CD0E | **none** | 26 (7) | — |
| 3 | unlock | 0x4AEBC5, 0x4AEDC4 | **none** | 0 | — |
| 4 | get | 0x4618F2 | hero's item moves (`SessionItemScripts`) | 0 | — |
| 5 | drop | 0x461B2A | hero's item moves | 0 | — |
| 6 | throw | 0x4B3C6A | **none** | 0 | — |
| 7 | hit | 0x4B2B7F, 0x4B2F90, 0x4B37A3 | **none** | 26 (11) | — |
| 8 | miss | 0x4B2F06, 0x4B311F, 0x4B3972 | **none** | 0 | — |
| 9 | dialog | 0x57CC90, 0x5684AA | talk with a hero, heartbeat call-ex | 180 (122) | 52, 1 missing |
| 10 | first heartbeat | 0x4AD2B2 | AI tick, shown map (`HeartbeatAdmission`) | 23 (6) | 23 (was 21) |
| 11 | catching thief | 0x4AEFCD, 0x4C76E5, 0x4C7900 | pick pocket (use set) | 0 | — |
| 12 | dying | 0x45DA57 | **none** | 167 (61) | — |
| 13 | enter combat | 0x4AAE29 | **none** | 9 (8) | — |
| 14 | exit combat | 0x4AAF32 | **none** | 0 | — |
| 15 | start combat | 0x4A86A7 | **none** | 2 (2) | — |
| 16 | end combat | 0x4A8688, 0x4B7681 | **none** | 0 | — |
| 17 | buy object | 0x4621D7, 0x578446 | **none** | 0 | — |
| 18 | resurrect | 0x4B5A46 | **none** | 8 (8) | — |
| 19 | heartbeat | 0x4AD2E1, 0x4B704A | AI tick, shown map (`HeartbeatAdmission`) | 141 (84) | 81 (was 32) |
| 20 | leader killing | 0x45DB95 | **none** | 0 | — |
| 21 | insert item | 0x4648C2, 0x465977, 0x46687A, 0x46697F | hero's item moves | 28 (12) | 3 |
| 22 | will KOS | 0x4AE179 | **none** | 60 (7) | — |
| 23 | taking damage | 0x4B446B (ARC-DEV-254: 0x4B47F8 is points 34/35, not 23) | a script's damage only (`AreaUseScriptHost.RunTakingDamage`) | 1 (1) | heartbeat set |
| 24 | wield on | 0x46558E, 0x467847 | hero's item moves | 98 (29) | 92 |
| 25 | wield off | 0x46557D, 0x467D49 | hero's item moves | 92 (28) | 91 |
| 26 | critter hits | 0x4B3003, 0x4B37FC | **none** | 18 (2) | — |
| 27 | new sector | 0x4F24A3 | **none** | 3 (3) | — |
| 28 | remove item | 0x467C7C | hero's item moves | 0 | — |
| 29 | leader sleeping | 0x57BB03 | **none** | 0 | — |
| 30 | bust | 0x43F30D, 0x43F42A, 0x43F4C0, 0x43F574, 0x4F076E | **none** | 64 (25) | — |
| 31 | dialog override | none | — (DLG number) | 0 | — |
| 32 | transfer | 0x461D18 | hero's item moves | 6 (4) | 3 |
| 33 | caught thief | 0x4C76CE, 0x4C78E9 | **none** | 0 | — |
| 34 | critical hit | 0x4B2BAE, 0x4B2FD4, 0x4B37CD (and 0x4B47F8) | **none** | 4 (4) | — |
| 35 | critical miss | 0x4B2F35, 0x4B3163, 0x4B399C (and 0x4B47F8) | **none** | 2 (2) | — |

Points the original fires for which Steamwoven has **no host**: 0 examine, 2 destroy, 3 unlock, 6 throw, 7 hit, 8 miss, 12 dying,
13 enter combat, 14 exit combat, 15 start combat, 16 end combat, 17 buy object, 18 resurrect, 20 leader killing, 22 will KOS,
23 taking damage, 26 critter hits, 27 new sector, 29 leader sleeping, 30 bust, 33 caught thief, 34 critical hit, 35 critical miss.
Points with a **partial** host: 1 use (use clicks on guarded sites and written items; not the use goal on portals and containers, the
bed, the trap disarm or the magick-tech component), 4/5/21/24/25/28/32 (the session's item scripts for the hero's own moves only),
9 dialog (talk, resumption, deferred calls, now a heartbeat's call-ex; not the dead/surrender speech gates of a talk), 10 and 19
(real-time AI ticks of the shown map; the turn-based subturn heartbeat 0x4B704A runs no script yet), 11 catching thief (pick pocket
only; the guard's crime notice 0x4AEFCD is not hosted).

## 3. Entries that are not an object slot

| Entry | Original | Steamwoven |
| --- | --- | --- |
| Tile script | 0x4C0980, from the location change 0x444270 for a PC or NPC (type 15/16) whenever a location set passes mode 1 or 2 (TS2): moves (0x43E770, 23 callers), object placement (0x4415C0, 12), creation (0x442260, 4), 0x43EA20; mode 0 (0x4423E0, 5 callers) runs none. A second caller walks a tile path (0x455350 from 0x452630) and runs each tile's script (TG2). | Map mode: the PC's steps (ARC-SCRIPT-017) and the companions' (ARC-SCRIPT-046). **Not** the AI movers, visitors, a jump or teleport arrival, a created or placed critter, or the path walk. Of 3,012 tile scripts 1,186 run and 1,696 are refused by their set (the tile census of `AreaUseScriptsOriginalTests`; 829 and 2,053 before ARC-DEV-100 and ARC-DEV-157, 684 and 2,198 before ARC-SCRIPT-052). |
| Sector script | 0x4F2330 for the local player entering a sector (TS3); followers then get point 27. | Map mode, PC crossing a sector. Point 27 not run. |
| Call script (7) | 0x44B030: the callee from its header at the caller's point, every triggerer × attachee pair (OV1, X11). | Use, tile and sector sets; not the talk or heartbeat sets. |
| Call-ex (81) | 0x44B170 → 0x441980 (WE3, TG5). | Item set, heartbeat set, since ARC-SCRIPT-052 the tile and sector set; point 1 refused. |
| Deferred calls (54, 55) | Game-time events (one shared handler, TG7). | Call in (54) through the session queue (talk and use sets); call at (55) refused. |
| End-game slides | Scripts 997–999 from the slide UI (0x569720). | None. |
| Native trap scripts | Inside the interpreter (0x4BCB70). | Trap state (ARC-WORLD-110). |

## 4. Conditions and actions

The interpreter dispatches conditions 0..52 through the table at 0x44714C and actions 1..141 by type − 1 through 0x44ADBC (type 0
and anything above 141 fall to the default: next line). Every action type has its own case except call in and call at (54, 55),
which share one (TG7). Steamwoven's interpreter runs at most set **S19** (ARC-SCRIPT-050; S18 of ARC-SCRIPT-049 plus float line and get area): 56 action
types and 16 condition types.

| Host (set) | Point(s) | Actions admitted | Conditions | Focus kinds |
| --- | --- | --- | --- | --- |
| Talk with a hero (`AreaScriptHost.SkillAdmission`) | 9, deferred calls | 0–6, 8–14, 16, 42–43, 47, 49–50, 54, 64–65, 72–73, 82–83, 87, 100–101, 114, 125, 129, 139 (34) | 0, 3–5, 18, 39, 43 | 0, 1, 12, 21 |
| Talk with the shown map's queries (`AreaScriptHost.SkillTalkAdmission`, ARC-SCRIPT-050) | 9, deferred calls | the talk set plus 23, 37, 84 (37) | 0, 3–6, 17–18, 20–22, 27, 33, 39, 43 | as talk |
| Use click, item use, catching thief (`AreaUseScriptHost.Admission`) | 1, 11 | 0–3, 5–16, 18–19, 24, 34, 37, 42–44, 49–50, 52, 54, 64–65, 72–73, 82–83, 87, 100–102, 114, 125 (40) | 0, 3–6, 17–18, 20–21, 33, 39, 43 | 0, 1, 10–12, 15, 21 |
| Tile and sector (`PlacedAdmission`) | tile, sector | the use set plus 4 (41) | as use | as use |
| Heartbeat (`HeartbeatAdmission`, this task and ARC-SCRIPT-049) | 10, 19 | 0–6, 8–14, 18–19, 34, 42–43, 54, 56, 58, 71, 81, 100–101, 134 (27) | 0, 3–5, 18, 20, 22, 27, 33, 43 | 0, 1, 8–10, 12, 21 |
| Item scripts (`SessionItemScripts.Admission`) | 4, 5, 21, 24, 25, 28, 32 | 0–3, 5–6, 8–14, 24, 77–78, 81–82, 91, 100–101, 120 (22) | 0, 3–5, 18, 39, 43, 47, 50 | 0, 1, 12, 21 |

No host admits actions 17, 20–22, 25–33, 35–36, 38–41, 45–46, 48, 51, 53, 55, 57, 59–63, 66–70, 74–76, 79–80, 85–86, 88–90,
92–99, 103–113, 115–119, 121–124, 126–128, 130–133, 135–138, 140–141 (87 types) or conditions 1–2, 7–16, 19, 23–26, 28–32, 34–38,
40–42, 44–46, 48–49, 51–52 (37 types). The census names the ones the installed attachments reach first (section 5).

## 5. Census of the installed attachments

Census of 2026-10-29 (`ScriptTriggerCensusTests`, opt-in, the installed 1.0.7.4 data): the 79 maps map mode opens, one row per
object entity (its first snapshot row), every point but 31. "Admitted" is the host's set from line 0 (region admission); a run can
still stop on an effect its host cannot apply. Numbers only; the file it writes stays private.

- **3,479 attachments on 22 points**; 2,467 of them are use (1), most on doors and containers that one script serves (245 scripts).
  The points with no host hold 444 attachments: dying 167, bust 64, will KOS 60, examine 54, destroy 26, hit 26, critter hits 18,
  enter combat 9, resurrect 8, critical hit 4, new sector 3, start combat 2, critical miss 2, taking damage 1.
- **First refusals per host** (the first command of the reachable lines outside the set; condition and action numbers as in
  [the opcode census](scr-census.md)):
  - use (1), 246 of 2,467 refused: get hit points (107) 215, computed goto 7, set lock state (53) 5, the vicinity of everyone in a use
    script (focus 8) 5, change art (59) 4, then one each of unfog town map, grant fate point, eye candy, destroy item named, damage;
  - dialog (9), 127 of 180 refused, 1 missing: float line (23) 45, critter disband (22) 17, obj is wielding (condition 21) 14, get
    area (84) 10, random (37) 8, obj is dead (condition 22) 6, get reaction (29) 5, PC quest state (condition 6) 4, hours since
    start (105) 3, loop for (18) 3. Since ARC-SCRIPT-050 (the talk set "S3-area-talk-queries-skill", S19, with ARC-SCRIPT-049's obj is dead) 64 of 180
    refused, 1 missing; the largest: critter disband (22) 17, a condition over everyone in the group (focus 13) 14, get reaction (29) 11,
    obj is in combat (condition 10) 4, hours since start (105) 3, loop for (18) 3, adjust money (36) 3, goto beyond the end 2, give gold
    (33) and daytime (condition 1) 2 each. ARC-DEV-178 (2026-11-07, bug B19): get reaction (29) is admitted in the talk set (S23) and get area on the start map is answered; the census still admits 115 of 180 and the first refusals are now critter disband (22) 17, a condition over everyone in the group (focus 13) 14, action 30 7, action 33 6, loop for (18) 4, obj is in combat (condition 10) 4, hours since start (105) 3, action 36 3, goto beyond the end 2, daytime (condition 1) 2;
  - heartbeat (19), 108 of 141 refused: obj is switched off (condition 27) 51, obj is named (condition 20) 26, set faction (134),
    damage (60) and distance between (71) 4 each, obj is dead and has reputation (conditions 22, 33) 3 each, call script in (54) and
    destroy (68) 2 each; first heartbeat (10): 21 of 23 admitted, 2 refused by condition 27. After ARC-SCRIPT-049 (slice 1, recount of the
    same day): heartbeat 81 of 141 admitted, first heartbeat 23 of 23; the heartbeat's first refusals are create object (52) 13, critter
    follow (21) 10, attack (36) 7, the scenery vicinity in a heartbeat (focus 15) 5, damage (60) 4, call script (7) 4, set day standpoint
    (45) 3, destroy (68), PC quest state (condition 6) and a global variable operand (value kind 1) 2 each;
  - item scripts: insert item (21) 25 of 28 refused (toggle state 16, loop for 5), wield on (24) 6 of 98 (call script in 4), wield
    off (25) 1 of 92, transfer (32) 3 of 6.
- **Opening heartbeats.** Nine installed heartbeats call their NPC's own dialog point as 1100 does: the Crash Site's 1100 (Virgil)
  and 1717 (the Molochean agent there, after a story flag), Wheel Clan 2700, the Void 1130 and 3081 (Arronax's interior), the Panarii
  temple 2602, Drog's cave 1490, Dernholm castle 1015 and Vollinger's basement 2106. ARC-SCRIPT-047 admitted 1100 only; since
  ARC-SCRIPT-049 the set admits 1100, 2700, 1130, 3081 and 2106. The other four still stop on a command outside the set: 1717 on set day
  standpoint (45), then set night standpoint (46), get location (95) and action run to (74); 2602 on obj can see obj (condition 28), then
  obj within range (34); 1490 on obj is in dialog (condition 26); 1015 on a global variable operand (value kind 1; admitted by the viewer's heartbeat set since ARC-DEV-100).
- **Tile scripts** (not object slots): 1,186 of 3,012 run, 1,696 refused (829 and 2,053 before ARC-DEV-100, whose global variable, PC variable and PC flag operands admit 27 more, and ARC-DEV-157, whose get stat and vicinity loops admit 330 more; 684 and 2,198 before ARC-SCRIPT-052, slice 8's first group).

## 6. Fix slices (prioritised)

Each slice is its own task and thread; codes come from the coordinator. "Reach" is what the census says it unlocks.

| # | Slice | Covers | Reach |
| ---: | --- | --- | --- |
| 1 | Heartbeat conditions and world queries (ARC-SCRIPT-049: done but damage and destroy, which go to their own slice) | conditions obj is switched off (27), obj is named (20), obj is dead (22), has reputation (33); actions distance between (71), call script in (54), set faction (134), damage (60), destroy (68) in the heartbeat set | most of the 108 refused heartbeats, the 2 refused first heartbeats, the eight other opening talks (1717 at the Crash Site first) |
| 2 | Dialog-point commands | float line (23), critter disband (22), obj is wielding (21), get area (84), random (37), obj is dead (22), get reaction (29), loop for (18) in the talk set | most of the 127 refused dialog scripts (talks that now run default). **Done in part** by ARC-SCRIPT-050 (S19: float line, get area, random, the S4 guard conditions and ARC-SCRIPT-049's obj is dead and obj is switched off in the talk set; 63 more rows admitted). Left: critter disband (22) comes only with transform into basic prototype (40), all 17 rows script 30009; obj is wielding over everyone in the group (focus 13, 14 rows, script 1160) is a list condition (slice 11); get reaction (29) with set reaction (30, 0x447ABA: reaction_adj by the difference) and give gold (33); the loop-for talks 1105, 1111, 1116 need conditions over lists and actions 99, 81, 36 |
| 3 | Tile scripts for every moved critter | the location change of TG2 for AI movers, visitors, a jump or teleport arrival, a created or placed critter, and the path walk 0x455350. **AI-host movers done** by ARC-DEV-206 (a step, a knockback slide, a route relocation; smoke `npc-tile`, the agent's exit tile 1724); visitors, jumps, teleports and created critters still open | traps, guards and story tiles NPCs step on |
| 4 | Dying and destroy | points 12 and 2 (0x45DA57, 0x43CD0E), from the session's death and destroy; a heartbeat's kill then runs the dying script | 167 + 26 attachments |
| 5 | Combat points | enter, exit, start, end combat (13–16), hit, miss, critter hits, critical hit and miss, taking damage (7, 8, 26, 34, 35, 23) from the combat hosts | 62 attachments (Virgil's 1997 at enter combat) |
| 6 | Crime and KOS | will KOS (22, 0x4AE179), bust (30), caught thief (33) and the guard's catching thief (11 at 0x4AEFCD) | 124 attachments |
| 7 | Use-set commands | get hit points (107), set lock state (53), change art (59), computed goto, focus 8 in use scripts | 246 refused use attachments |
| 8 | Tile-script command groups | the first refusals of the 2,198 refused tile scripts, counted and admitted by group. **First group done** by ARC-SCRIPT-052 (S20, ledger TD1–TD3): call-ex (81), destroy (68) and stop attacking (109) in the tile set; Virgil's altar talk (1328 → 1324 line 14, node 250) opens, 145 more tiles run. **Second group done** by ARC-DEV-157 (S22, ledger GS1–GS4, bug B20 and B23): get stat (33), answered for the PC only, and the loops over everyone or anyone in the vicinity (focus 8, 9) in the tile and sector set (an empty list without an attachee, PG3); 45 more scripts are admitted whole (1,244 of 2,399 from line 0); the loop of Brego's ghost (2768) now stops at cast spell on (61, MT21), the first refusal of 16 scripts. First refusals left before that group (rows): get stat (33) 608, a loop over everyone in the vicinity (focus 8) 443, action 32 98, action 22 93, change art (59) 91, sector is blocked (condition 45) 89, condition 51 85, damage (60) 75, play script eye candy (120) 68, action 112 62, set day standpoint (45) 61, monster generator disabled (condition 46) 57; 1779 now stops at a computed goto. The agent's exit tile 1724 (destroy) needs slice 3 (tiles for AI movers) | tile scripts |
| 9 | Examine, new sector, resurrect | points 0 (0x57CD93), 27 for followers (0x4F24A3, Virgil's 1568), 18 (0x4B5A46) | 65 attachments |
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
- **U4** The surrender and mind-control states of an NPC are not modelled by the viewer's use host (it answers dead only), so their
  dialog gates never trigger there.
- **U5** What the original does with a heartbeat's talk while the PC is in another dialog, a window is open or the PC is under
  attack is not read (the dialog start refuses a second dialog of the PC, G3, but whether the talk is lost or retried is not known);
  Steamwoven keeps it pending until it can open.
