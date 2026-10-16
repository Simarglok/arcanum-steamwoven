# Shared SCR event dispatcher: source reading and slices

Status: **Research**, work item ARC-RES-042 (2026-10-16), the research part of
top-20 item 15. It reads how the pinned source runs a script for an event
(use, heartbeat, combat enter and exit, dialogue and the other attachment
points), what the result of that run does to the caller, what is written back,
and what happens on a command the interpreter does not know. It maps what the
current code already covers, splits ownership with ARC-RES-039 stage A8 and
the whole-world stage M3, and proposes implementation slices. It changes no
production API, viewer, session schema, launcher or other research document.

Requirements: [FR-SCRIPT-001](../requirements/story-social-ai.md#fr-script-001)
(events and the roles a script receives) and
[FR-SCRIPT-002](../requirements/story-social-ai.md#fr-script-002) (conditions,
actions and the explicit refusal of an unknown command); deferred calls touch
[FR-SCRIPT-003](../requirements/story-social-ai.md#fr-script-003).

## Vocabulary and evidence discipline

- **Source (S)**: read from the pinned CE specification
  [`a7ff41b300ef712f0e7d088183a3d08957110cdd`][ce] (Sustainable Use License)
  at the linked line ranges, for the meaning of the mechanism only; nothing is
  copied. CE is a reconstruction lineage with decompiler names (`sub_…`); a
  reading of a name's intent is marked as such. Original-executable parity is
  **NOT_VERIFIED** throughout.
- **Code (C)**: this repository at `origin/main` 55ed52a.
- **Data (D)**: counts already published by earlier research
  ([ARC-RES-035](world-transitions.md), [ARC-RES-037](virgil-script-attachments.md));
  this task read no game data (cloud session, no installed game).
- **Project policy (P)**: a proposal for this project where the source leaves
  a choice or where the source is silent; open until the slice that owns it.
- **Dispatcher** here means the path from "something happened" to a finished
  script run and its result: choosing the script, building the invocation,
  running the interpreter, writing the attachment back, returning the outcome.
- **Run default / skip default**: the boolean a script run returns. *Run
  default* lets the caller do its normal behaviour (open the door, show the
  greeting, run the AI); *skip default* tells it not to. Many callers ignore it.

## Answer

- **One entry point for objects (S).** Every object event goes through one
  function that takes triggerer, attachee, extra object, attachment point and
  start line, reads the attachee's script slot for that point, runs the
  interpreter, and writes the slot back when its number, local flags or
  counters changed ([object.c 3866–3908][obj-exec]). A slot holding script 0
  means "no script" and returns *run default* without running anything.
  Tile scripts, sector scripts, deferred calls, call-script, the end-game
  slides and traps build the same invocation by hand and call the interpreter
  directly (section 3).
- **36 attachment points, 35 fired, about 75 call sites (S).** The header
  enumerates 36 points, 0..35 ([script.h 7–44][sap]); the earlier requirement text
  said 35. 35 are fired through the object entry point from combat, AI,
  items, skills, UI and map code; `SAP_DIALOG_OVERRIDE` (31) is never fired by
  the engine (a call-ex action could still run that slot), its slot names a
  DLG file for generated lines
  ([dialog.c 3233–3269][dlg-override]). Section 2 lists every point with its
  caller, roles and whether the result gates the default.
- **Heartbeat (S).** The AI time event of an NPC runs `SAP_FIRST_HEARTBEAT` on
  the first tick after a wake-up and `SAP_HEARTBEAT` on every real-time tick
  while the NPC is alive and within 30 cells of the local PC; *run default*
  from the heartbeat is what lets the AI pass (or a monster generator) run, and
  *skip default* from the first heartbeat skips that tick's heartbeat and AI
  ([ai.c 2859–2936][ai-tick]). The next tick is due after
  `250 + 4750 × min(d, 30) / 30` ms ([ai.c 2998–3002][ai-delay]). In turn-based
  mode the heartbeat runs at the start of each NPC subturn and again after
  each animation the NPC finishes during its subturn, and gates the AI pass
  the same way ([combat.c 3024–3037][tb-heartbeat], [anim.c 3810–3815][anim-tb]).
  The ARC-RES-039 plan's "once at the start" reading misses the second caller.
- **Combat enter and exit (S).** `SAP_ENTER_COMBAT` fires when an NPC first
  takes a combat focus (it was not already fighting), `SAP_EXIT_COMBAT` when a
  fighting NPC's danger source changes to anything else; the triggerer is the
  new danger target (the opponent on enter; on exit the critter fled from or
  surrendered to, or none when the danger is cleared) ([ai.c 1686–1727][ai-danger]). `SAP_START_COMBAT` and
  `SAP_END_COMBAT` are not the transitions their names suggest: while an NPC is
  in combat mode every AI pass runs END then START in real time, and START
  only in turn-based mode, where END runs at the end of each NPC subturn
  ([ai.c 349–355][ai-startend], [combat.c 3272–3280][tb-end]). All four
  ignore the result.
- **Dialogue (S).** A talk request runs `SAP_DIALOG` from line 0; *run default*
  means "no script conversation", and the UI then shows the generic greeting
  (or a surrendered NPC flees when its danger source is in the talker's party) ([tb_ui.c 248–259][tb-talk]). The script's
  dialog action starts the DLG of the same number through a callback when the
  triggerer is a PC, and returns *skip default* in every case ([script.c 1513–1534][sat-dialog]). When the
  conversation ends with response 0 or −N the UI resumes the same script at
  the line after the dialog action or at line N
  ([dialog_ui.c 400–407][dlg-resume]). The interpreter skips the dialog script
  of a dead or surrendered NPC unless the script file flags allow it, and runs
  script 2188 instead for a mind-controlled NPC ([script.c 322–337][exec-dialog]).
- **Use (S).** Use on a portal, on scenery other than a bed and on other
  non-container, non-critter objects runs `SAP_USE` with the user as both
  triggerer and extra object; *skip default* cancels the use
  ([anim.c 6581–6633][anim-use]). Containers and bodies run it from the loot
  UI, beds from the sleep UI, items from item use. Before any `SAP_USE` the
  entry point gives the triggerer a chance to spot a trap on the object; a
  spotted trap cancels the use ([object.c 3877–3881][obj-exec]). Tile scripts
  run as `SAP_USE` for **any critter** entering the tile, and the sector script
  as `SAP_USE` when the PC enters the sector, followed by `SAP_NEW_SECTOR` for
  each of the PC's followers ([object.c 5248–5250][tile-trigger],
  [sector_object_list.c 680–730][sector-exec]).
- **Unknown commands are silent no-ops in CE (S).** An unknown condition type
  runs neither branch and continues with the next line
  ([script.c 1492–1496][cond-default]); an unknown action type continues with
  the next line ([script.c 2822–2823][act-default]). A goto to a line outside
  the script stops the run with *skip default* (a goto value of −1..−4 is read
  as a control code instead, X6); 1,000 executed lines without a return
  also stop it with *skip default* and without auto-removal
  ([script.c 347–386][exec-loop]). FR-SCRIPT-002 asks for the opposite, an
  explicit refusal; section 7 proposes a static admission that refuses a whole
  script before any effect (P).
- **Current code (C)** runs exactly one event, `SAP_DIALOG` of an NPC, as a
  query-only entry walk (TRUE, local flag, following, jilted; advance, goto,
  dialog) and hands the computed DLG node to the conversation runtime; there
  is no general interpreter, no other event, no writeback and no resumption
  (section 8).
- **Ownership (P, section 9).** The dispatcher, the interpreter core, the
  outcome and writeback rules and the refusal policy belong to new SCRIPT
  slices from this note. ARC-RES-039 A8 keeps the meaning of each AI-facing
  event and every condition and action that reads or writes AI state. The
  whole-world M3 keeps unconditional transitions; conditional transitions run
  through this dispatcher and request the teleport from the world
  transition coordinator. The timed-event queue (top-20 item 3) owns time.

## 1. The invocation and the interpreter (S)

| ID | Claim | Source |
| --- | --- | --- |
| E1 | An invocation holds the script attachment (number, header flags, four byte counters), the start line, triggerer, attachee, extra object and attachment point. | [script.h 327–351][inv] |
| E2 | The object entry point returns *run default* without running anything when the editor is active or the slot's number is 0. Only for `SAP_USE`, an unspotted trap on the attachee gets a spot roll first; a successful spot returns *skip default*. | [object.c 3866–3886][obj-exec] |
| E3 | After the run the slot is written back when number, flags or counters differ from before; if the attachee is a trap object whose script became 0, the trap object is destroyed instead. | [object.c 3888–3908][obj-exec] |
| E4 | The interpreter returns *skip default* at once when the script number is not a host-allowed one in a networked client (multiplayer only), when the script number is a native trap script (handled by trap code, not interpreted), and when the script file cannot be opened. | [script.c 292–310][exec-head], [trap.c 579–660][trap-exec] |
| E5 | It returns *run default* without running anything for an NPC attachee that is a monster generator. | [script.c 313–320][exec-head] |
| E6 | Dialog-only gates: a dead NPC without the death-speech file flag, or a surrendered NPC without the surrender-speech flag, returns *run default*; a mind-controlled NPC runs script 2188 in place of its own, and its own number is restored afterwards. | [script.c 322–337][exec-dialog] |
| E7 | Execution starts at the invocation line with fresh local variables and a fresh loop state; each line evaluates its condition and runs the true or the false action; the action returns "next line", a jump target, or one of two return codes. Past the last line counts as *skip default*. | [script.c 339–386][exec-loop], [script.c 48–50][codes], [script.c 1507–1512][sat-return] |
| E8 | On either return code a script whose file flags carry auto-removing sets its number to 0. "Return and run default" (action 2) gives *run default*; "return and skip default" (action 1) and the dialog action give *skip default*. | [script.c 369–380][exec-loop], [script.h 85–93][sf] |
| E9 | A line read outside the script (a goto target outside 0..count−1 other than the codes −1..−4, unsigned compare) ends the run with *skip default*; so does reaching 1,000 executed lines. Neither path applies auto-removal. | [script.c 352–356][exec-loop], [script.c 3332–3341][line-read] |
| E10 | "Remove this script" and "change this script" do not stop the run; they record a new number that replaces the attachment's number after the run. | [script.c 1535–1540][sat-remove], [script.c 388–394][exec-tail] |
| E11 | Local flags (32 bits of the header) and counters (four bytes) are changed in the invocation's own copy, so only the object entry point and the sector caller persist them (E3, section 3); the tile caller compares and then discards them (X3). | [script.c 1544–1553][sat-local] |
| E12 | One loop at a time: loop-for resolves a focus group (at most 100 objects), loop-end jumps back, break leaves; a nested loop-for is ignored with a debug message. | [script.c 1622–1665][loops] |
| E13 | Unknown condition type: neither action runs, next line. Unknown action type: next line. No error reaches the caller. | [script.c 1492–1496][cond-default], [script.c 2822–2823][act-default] |

## 2. Event catalogue (S)

Roles are given as triggerer / attachee / extra. "Gates" means the caller
changes its behaviour on *skip default*; "ignored" means it does not look.
Line 0 unless noted.

| SAP | Point | Fired by | Roles | Result |
| ---: | --- | --- | --- | --- |
| 0 | EXAMINE | examine of an object by the local PC, before the description is built ([tb_ui.c 271–285][tb-examine]) | PC / object / — | gates: no examine text |
| 1 | USE | use goal on portal, scenery, other objects ([anim.c 6581–6633][anim-use]); item use ([item.c 1450][item-use]); container open and loot ([inven_ui.c 584–594][inven-use]); loot of a dead critter by pickpocket ([skill_ui.c 269][skill-use]); bed ([sleep_ui.c 314][sleep-use]); trap disarm and trap step ([trap.c 505][trap-disarm], [anim.c 11516][anim-trap]); magic-tech use component ([magictech.c 3025][mt-use]) | user / object / user (goal); user / item / target (item use) | gates in goal, item, loot and bed; ignored for disarm, trap step, magic-tech |
| 2 | DESTROY | object destruction ([object.c 1294][destroy]) | object / object / — | gates: *skip default* keeps the object |
| 3 | UNLOCK | any critter's attempt to open a portal or container that is not busted, before the jam, magic-hold and lock checks: PC loot UI, door animation goals, movement into a door, script open actions, AI ([ai.c 3745–3760][ai-portal], [ai.c 3843][ai-container], [inven_ui.c 570][inven-open]) | critter / portal or container / — | gates the open |
| 4 | GET | item pick-up ([item.c 635][item-get]) | critter / item / — | gates the pick-up |
| 5 | DROP | item drop ([item.c 726][item-drop]) | holder / item / — | gates the drop |
| 6 | THROW | thrown attack ([combat.c 1141][throw]) | attacker / weapon / target | gates the throw |
| 7, 8 | HIT, MISS | attack outcome, on the weapon ([combat.c 596–782][combat-hit], [combat.c 1000–1052][combat-melee]) | attacker / weapon / target | ignored |
| 9 | DIALOG | talk request ([tb_ui.c 248–259][tb-talk]); resumption after DLG ([dialog_ui.c 400–407][dlg-resume]) | PC / NPC / —; start line 0 or the resume line | gates: *run default* shows the generic greeting, or makes a surrendered NPC flee when its danger source is in the talker's party; resumption ignored |
| 10 | FIRST_HEARTBEAT | first AI tick after a wake-up ([ai.c 2876–2891][ai-tick]) | NPC / NPC / — | gates that tick's heartbeat and AI |
| 11 | CATCHING_THIEF_PC | pickpocket noticed ([skill.c 1689–1696, 1753–1760][steal]); guard notified of a portal or container crime ([ai.c 3926][ai-guards]) | thief / victim or guard / item or target | gates the attack or reaction drop |
| 12 | DYING | critter killed ([critter.c 670–673][dying]) | killer / victim / — | gates the whole death processing (combat exit, XP, alignment, logbook, follower notices) |
| 13 | ENTER_COMBAT | NPC takes a combat focus while not fighting ([ai.c 1686–1699][ai-danger]) | opponent / NPC / — | ignored |
| 14 | EXIT_COMBAT | fighting NPC's danger source changes ([ai.c 1708–1727][ai-danger]) | new danger target, may be none / NPC / — | ignored |
| 15 | START_COMBAT | every AI pass of an NPC in combat mode ([ai.c 349–355][ai-startend]) | danger source / NPC / — | ignored |
| 16 | END_COMBAT | same AI pass, real time only, before START ([ai.c 349–355][ai-startend]); end of an NPC turn-based subturn ([combat.c 3272–3280][tb-end]) | danger source / NPC / —; NPC / NPC / — | ignored |
| 17 | BUY_OBJECT | barter offer, on the buyer ([item.c 947][item-buy], [inven_ui.c 3808][inven-buy]) | item / buyer NPC / seller PC | gates: "won't buy" |
| 18 | RESURRECT | resurrection ([combat.c 2140][resurrect]) | caster / target / weapon | gates: refused |
| 19 | HEARTBEAT | real-time AI tick within 30 cells ([ai.c 2893–2909][ai-tick]); NPC subturn start and after each finished animation in its subturn ([combat.c 3024–3037][tb-heartbeat], [anim.c 3810–3815][anim-tb]) | NPC / NPC / — | gates the AI pass and the generator |
| 20 | LEADER_KILLING | the PC (or its follower) killed an NPC, for each PC follower ([critter.c 711–717][dying]) | victim / follower / PC | ignored |
| 21 | INSERT_ITEM | item, gold or ammunition inserted ([item.c 2487, 3139, 3725, 3760][item-insert]) | holder / item / — | ignored |
| 22 | WILL_KOS | before the kill-on-sight decision ([ai.c 3423–3440][ai-kos]) | target / NPC / — | gates the KOS checks |
| 23 | TAKING_DAMAGE | damage, on the target and on worn armor at a critical hit ([combat.c 1420–1430, 1509–1519][dmg]) | attacker / target / —; attacker / armor / target | gates the damage on the target (re-entry guarded); ignored for armor |
| 24, 25 | WIELD_ON, WIELD_OFF | equip and unequip ([item.c 2984–2985, 4274, 4411][item-wield]) | wielder / item / — | ignored |
| 26 | CRITTER_HITS | hit landed, on the attacker ([combat.c 750][combat-hit], [combat.c 1015][combat-melee]) | target / attacker / — | ignored |
| 27 | NEW_SECTOR | PC entered a sector, for each follower ([sector_object_list.c 723–728][sector-exec]) | PC / follower / — | ignored |
| 28 | REMOVE_ITEM | forced removal ([item.c 4380][item-remove]) | holder / item / — | ignored |
| 29 | LEADER_SLEEPING | the leader sleeps, for each follower ([sleep_ui.c 738][sleep-leader]) | sleeper / follower / — | ignored |
| 30 | BUST | object or portal broken ([object.c 2580–2671][bust], [portal.c 38][portal-bust]) | breaker / object / — | ignored |
| 31 | DIALOG_OVERRIDE | never fired by the engine; the slot's number names a DLG of override lines ([dialog.c 3233–3269][dlg-override]) | — | — |
| 32 | TRANSFER | item handed to a critter, on the recipient ([item.c 786][item-transfer]) | giver / recipient / item | gates the transfer |
| 33 | CAUGHT_THIEF | pickpocket noticed, on the thief ([skill.c 1689, 1753][steal]) | victim / thief / item | ignored |
| 34, 35 | CRITICAL_HIT, CRITICAL_MISS | critical outcome, on the weapon, next to HIT/MISS ([combat.c 596–782][combat-hit], [combat.c 1000–1052][combat-melee]) | attacker / weapon / target | ignored |

Reading notes. The catalogue lists call sites of the pinned CE, not every
place the original may fire them. `SAP_UNLOCK` is a permission check on every open
attempt, not a lock event; whether the original also fires it on lock picking
is U1. The bed use is inverted: the sleep UI refuses sleep on *run default*,
so *skip default* is what lets the PC sleep ([sleep_ui.c 314][sleep-use]). The built-in
prototype defaults put scripts into item slots (for example scripts 29006 on
`SAP_HIT` and `SAP_MISS`, 28001 and 28002 on `SAP_WIELD_ON` and `SAP_WIELD_OFF`)
([proto.c 2047–2051, 2680–2683][proto-scripts]); for the dispatcher these are
ordinary slots.

## 3. Entry points that are not an object slot (S)

| Entry | Script | Roles and line | Writeback | Source |
| --- | --- | --- | --- | --- |
| Tile script | the tile's node in the sector list | entering critter / none / none, line 0, point USE | compared, then the unmodified record is stored again, so changes are lost (X3) | [tile_script.c 360–389][tile-exec], [object.c 5248–5250][tile-trigger] |
| Sector script | the sector's attachment | PC / none / none, line 0, point USE | flags and counters only: a changed number is **not** stored | [sector_object_list.c 680–720][sector-exec] |
| Deferred call (call-in 54, call-at 55) | the named script with its file header as initial state | stored triggerer / attachee, stored line, point USE | none | [script.c 1981–2014][call-in], [script.c 559–574][call-timeevent] |
| Remove call (action) | clears queued deferred calls of one script and attachee | — | — | [script.c 2665–2670][remove-call] |
| Call script (action 7) | the named script with its file header, the caller's attachment point, every triggerer × attachee of two focus groups | from the action, line from the action | none; result ignored | [script.c 1541–1543][sat-remove], [script.c 2835–2878][call-script] |
| Call-ex (action) | the slot of one object for a given attachment point, through the object entry point | from the action | as the object entry point | [script.c 2246–2248][call-ex-case], [script.c 2880–2887][call-ex] |
| Busted trapped object | `SAP_USE` slot of an object with a trap that an attack brought to 0 HP | melee attacker or none (ranged) / object | none: an auto-removing trap's clear is lost | [trap.c 562–575][trap-invoke], [combat.c 1928–1935][trap-bust] |
| Native trap scripts | trap script numbers inside the interpreter, for any caller (E4) | as the caller | as the caller (stepping on a trap goes through the object entry point, E3) | [trap.c 579–680][trap-exec], [anim.c 11516][anim-trap] |
| End-game slides | scripts 997, 998, 999 | none, line 0, point USE | none | [slide_ui.c 166–180][slides] |

Call-in turns its operand into `operand × 1000 × 8` ms of game time (reading:
seconds of real time at the 8× game rate, inference); call-at counts the game
seconds to a time of day without the factor ([script.c 1993–2012][call-in]). Both are queued as
game-time events and saved with the time-event queue.

## 4. Heartbeat and the AI tick (S)

| ID | Claim | Source |
| --- | --- | --- |
| H1 | An NPC's AI time event carries the NPC and a "first" marker. When a loaded sector's objects are folded in, each NPC gets an immediate event whose marker comes from comparing the sector's stored time with the current time plus 3 s (meaning not read further); creating an NPC schedules a normal delayed one. | [ai.c 3035–3058][ai-schedule], [object.c 4150–4158][fold], [sector_object_list.c 456–471][fold-list], [sector.c 1443][sector-fold], [object.c 4285–4288][create] |
| H2 | On a first tick of a living NPC: it gets the re-check-weapon flag, a leaderless NPC outside combat moves to its waypoints or standpoints, then `SAP_FIRST_HEARTBEAT` runs; *skip default* skips the heartbeat for this tick. | [ai.c 2876–2891][ai-tick] |
| H3 | `SAP_HEARTBEAT` runs only when the NPC is alive, real-time mode is on and it is within 30 cells of the local PC; *run default* then runs the generator logic for a generator NPC or the AI pass for others. | [ai.c 2893–2909][ai-tick], [ai.c 2940–2954][ai-active] |
| H4 | Otherwise (skipped, out of range, turn-based) the follower catch-up and encounter clean-up rules apply and the next event is scheduled, except for an NPC out of range in an unloaded sector without catch-up, and a generator whose spawn did not succeed, which return without the normal reschedule; a first tick adds 0–5 s. | [ai.c 2910–2936][ai-tick] |
| H5 | Turn-based: at the start of an NPC subturn and after every animation the NPC finishes in it, `SAP_HEARTBEAT` runs and *run default* runs the AI pass. | [combat.c 3024–3037][tb-heartbeat], [anim.c 3810–3815][anim-tb] |

The heartbeat is therefore the first step of the AI pipeline that ARC-RES-039
names (its S2 tick and S5 pipeline); it is also how many non-combat scripts (barks, routines,
generators) run.

## 5. Dialogue path (S)

| ID | Claim | Source |
| --- | --- | --- |
| G1 | The talk goal ends by calling the UI's talk handler, which runs `SAP_DIALOG` from line 0. | [anim.c 8018–8047][anim-talk], [tb_ui.c 248–259][tb-talk] |
| G2 | The dialog action copies the run's local flags and counters to the attachee's slot before the conversation starts, so the DLG sees them; the CE line assigns the counters to both fields (anomaly X1). It calls the start callback only when the triggerer is a PC. | [script.c 1513–1534][sat-dialog] |
| G3 | The start callback refuses a dead or unconscious PC, an NPC that cannot speak, a second dialog of the local PC, and a PC that cannot leave combat mode; it leaves combat mode otherwise, loads the DLG of the script number and shows the first node or a one-line float. | [dialog_ui.c 118–210][dlg-start] |
| G4 | Response 0 resumes at the dialog action's line + 1, response −N at line N, through the object entry point (so the slot is written back again). | [dialog_ui.c 400–407][dlg-resume] |

## 6. Combat-facing events (S)

The transitions behind events 13–16 and 22 belong to the AI danger state of
ARC-RES-039 (its S4 danger state and S10 KOS); this section only fixes where the hooks
sit.

- `SAP_ENTER_COMBAT`: inside the danger update, when the new danger is a
  combat focus, the NPC has no target lock and is not yet fighting; the
  fighting, re-check-weapon and re-check-grenade flags are set first
  ([ai.c 1686–1699][ai-danger]). Virgil's enter-combat script 1997 copies the
  player's alignment ([virgil-level-replay](virgil-level-replay.md)).
- `SAP_EXIT_COMBAT`: same update, any other danger type, when the fighting flag
  was set, with the new danger target (possibly none) as triggerer; it is cleared first, and spells are de-maintained unless the NPC
  flees ([ai.c 1708–1727][ai-danger]).
- `SAP_START_COMBAT` / `SAP_END_COMBAT`: per AI pass and per subturn as in the
  answer (an AI pass also runs when an NPC is attacked or given a party order, so START can fire
  outside its own turn); the names describe a turn, not the fight (reading of intent).
- `SAP_WILL_KOS`: before the KOS rules; *skip default* means "not KOS"
  ([ai.c 3423–3440][ai-kos]).
- `SAP_DYING`: *skip default* stops the whole death processing, including
  leaving combat mode and experience ([critter.c 670–717][dying]).

## 7. Unknown commands: source and proposed policy

**Source.** E9 and E13: the interpreter never reports an error. An unknown
condition is skipped with both branches, an unknown action is skipped, a bad
jump or a runaway loop ends the run with *skip default*, a missing script file
returns *skip default* before running (E4).

**Proposed policy (P).**

1. **Static admission before any effect.** A script is admitted for a given
   interpreter version only if every line's condition, both actions and every
   operand (value type, focus type, literal ranges, jump targets inside the
   script) are in the admitted set. A script with anything else is refused as
   a whole with a typed reason (line, component, type number), before the
   first effect, so a run never leaves a partial change. This extends the
   current NPC-entry rule ("unsupported is a frontier, not a no-op", both arms
   checked statically) from one event to all of them.
2. **Refusal is an outcome, not a default.** The dispatcher returns
   `Refused(reason)` separately from *run default* and *skip default*; each
   caller states what it does on a refusal (for example the talk request
   shows "not supported" in the diagnostic viewer, a heartbeat logs and skips
   the AI pass), and tests can assert it.
3. **"Like original" as a named mode.** Like the content readers' strict and
   "like original" modes, a later switch of the Original setting (D8) may execute
   unknown types as the CE no-op, with every skip counted in the trace; strict
   stays the default until a census shows what the data needs.
4. **Bounds are typed too.** 1,000 executed lines, a bad jump and a missing
   file stay source behaviour under the Original setting, and are typed
   `Budget`/`BadJump`/`MissingScript` results in the default.

## 8. What the code already covers (C)

| Piece | Status | Where |
| --- | --- | --- |
| SCR framing (64-byte header, 132-byte lines) and strict SCR name selection | inside the NPC-entry resolver, private | [npc-dialog-entry-runtime](../scripting/npc-dialog-entry-runtime.md), `src/Arcanum.Scripting/NpcDialog` |
| `SAP_DIALOG` of an NPC: effective whole-array attachment, typed invocation, versioned query ports, TRUE / LF / FOLLOWING / JILTED, advance / GOTO / DIALOG; unknown is a frontier | query-only entry walk, no effects | same |
| DLG conversation with end `ReturnToScript` (0 → caller + 1, −N → N) | delivered; the line is returned, nothing resumes it | [conversation runtime](../scripting/dialog-conversation-runtime.md) |
| Global flags, quests and rumors in the session; per-NPC LF as the viewer's `dialog.lf` map-change key | delivered (ARC-SCRIPT-006) | [game session](../world/game-session-runtime.md) |
| Attachment point numbers | `SessionScriptPoint` constants for six points; loot refuses a holder with a Use script; pickup and loot refuse an item with an InsertItem, Get (pickup) or RemoveItem (loot) script | `src/Arcanum.Session/GameSessionInventory.cs` |
| Tile and sector script words of a sector | read as raw words by the map catalogue | `src/Arcanum.Data/Maps/WorldModels.cs` |
| Cave entrance and exit use | endpoints are profile literals with SAP and script recorded; no script runs | `src/Arcanum.Data/Area/{OriginalProfile,CaveProfile}.cs`, `tools/terrain-viewer/Core/AreaTransition.cs` |
| Session clock | delivered; no time-event queue | `src/Arcanum.Simulation/Clock` |

Missing: a general event type, the object, tile and sector entry points,
outcome and writeback rules, an interpreter beyond the entry walk, deferred
calls, dialog resumption, and every event other than `SAP_DIALOG`.

## 9. Ownership (P)

| Topic | Owner |
| --- | --- |
| Event record, entry points of section 1 and 3, outcome (*run default*, *skip default*, refused), writeback per source kind, budgets, trace, refusal policy of section 7 | **SCRIPT slices below** (this note) |
| Interpreter core: control flow, return codes, local flags and counters, variables, global flags and story state, goto, loops, call, call-ex, remove and change script, dialog action and resumption | SCRIPT slices below |
| What the AI-facing events mean for the AI and when the danger state machine reaches them (13–16, 19, 22, the dialog gates for surrendered NPCs), and every condition and action that reads or writes AI state (attack, flee, KOS, wait, standpoints, follow, combat focus) | **ARC-RES-039 stage A8**, consumed by its slices I3 (danger state publishes typed hook events) and I6 (scheduler) |
| Unconditional transitions: jump points, the 250 unconditional paths as data, the map identity in the session | **whole-world M3** (owner-accepted whole-world plan, [ARC-RES-035 consequences](world-transitions.md#consequences-for-the-whole-world-plan)) |
| Conditional transitions (34 maps behind guards of [ARC-RES-035](world-transitions.md#conditions)) | **SCRIPT slice S4** evaluates the guards; the teleport itself is a request to the M3 transition coordinator (one pending teleport, arrival checks) |
| Time: the queue that holds AI ticks and deferred calls, game-time rate, save of pending events | **top-20 item 3** (event queue on the session clock) |
| DLG text, eligibility and effects | conversation runtime (ARC-SCRIPT-005/006) |
| Item, combat and life-state effects of the events in section 2 | the matching domain slices; the dispatcher only fires and reports |

A8 keeps its row in the AI plan; this note answers the generic half of it, so
A8 reads section 2 rows 10–16, 19 and 22 as fixed hook positions and does not
re-read the dispatcher.

## 10. Proposed implementation slices

Codes are allocated when each task is created. All slices run on the Mac
(code and tests), except where noted.

| Slice | Needs | Result |
| --- | --- | --- |
| S0 Opcode and attachment census (RES, Mac) | installed data | Probe mode: which attachment points carry scripts on every map, prototype, tile and sector, and which condition, action, value and focus types occur in all 2,399 scripts, weighted by reachable lines; picks the admitted set for S1. |
| S1 SCR program and admission | S0 | Public strict SCR reader in Data (moved out of the NPC-entry resolver), typed tables of the 53 conditions, 142 actions, 24 focus and 7 value types, static admission with typed refusal (section 7), first admitted set: control flow, local flags and counters, global flags, story state, goto, return codes. |
| S2 Dispatcher core ([ARC-SCRIPT-008](../scripting/scr-dispatcher-runtime.md)) | S1, session | Event record (point, roles, line, source kind), object/tile/sector entry points behind ports, outcome and atomic writeback, budgets and trace; synthetic tests for every row of section 1. |
| S3 Dialogue through the dispatcher ([ARC-SCRIPT-009](../scripting/scr-dispatcher-runtime.md#dialogue-arc-script-009-slice-s3)) | S2 | Talk request and resumption run `SAP_DIALOG` through S2; the NPC-entry resolver becomes an adapter or is retired; generic greeting on *run default*. |
| S4 Use, tile and sector events | S2, M3 coordinator | Use on scenery and portals, tile scripts for any critter, sector scripts and `SAP_NEW_SECTOR`; teleport action as a port to the transition coordinator; conditional transitions of ARC-RES-035. |
| S5 Deferred calls | S2, top-20 item 3 | Call-in, call-at and remove-call on the event queue, in the snapshot. |
| S6 Heartbeat hook | S2, RES-039 I6 | First heartbeat and heartbeat on the AI time event, at subturn start and after each animation in a subturn, gating the AI pass. |
| S7 Combat hooks | S2, RES-039 I3/I4, combat slices | Enter/exit/start/end, WILL_KOS, dying, hit/miss and damage events at the positions of section 2. |
| S8 Item events | S2, inventory owner | Get, drop, transfer, insert, remove, wield, buy; lifts the current "item with script refused" rule. |

Suggested order: S0 → S1 → S2 → S3 (proves the core on the event that already
works) → S4 (opens the conditional maps) → S5 → S6/S7 with the AI slices →
S8 with inventory.

## 11. Anomalies recorded, not adopted (S)

**Original check (ARC-RES-048):** these rows were checked in the retail 1.0.7.4 executable; the verdicts in the [verification ledger](original-verification-ledger.md#scripts-event-queue-and-randomness-arc-res-042-arc-res-045-arc-script-007-arc-res-046-arc-world-019) supersede the CE readings below where they differ.

Under owner decisions D2 and D8 (fix confirmed slips; only a bug of the original
game keeps its behaviour behind a switch of the Original setting) these are
settled by the slice that implements them.

| ID | Source behaviour | Note |
| --- | --- | --- |
| X1 | The dialog action writes the counters into both the flags and the counters of the slot ([script.c 1517–1524][sat-dialog]). | Likely a CE slip. Visible only to the first-node evaluation inside the synchronous start callback: the object entry point rewrites the slot from the invocation right after (object.c 3897–3903). |
| X2 | The attachee type is read only for a non-null attachee but compared for every dialog invocation ([script.c 312–322][exec-head]). | Undefined for a dialog call without attachee (only reachable through call-script). |
| X3 | The sector-script writeback ignores a changed number, so "remove this script" on a sector script does not persist ([sector_object_list.c 704–719][sector-exec]). The tile-script caller runs a copy and then stores the original record, so a tile script's number, flags and counters never persist ([tile_script.c 371–388][tile-exec], [tile_script.c 292–301][tile-set]). | The CE TODO on the tile caller asks the same question; the self-removal of tile script 2498 that ARC-RES-037 describes holds only if the original persists it. |
| X4 | Call-script runs from the file header, so local flags and counters written by the called script are lost ([script.c 2835–2878][call-script]). | May be intended (a called script has no attachment). |
| X5 | A missing script file returns *skip default* before running, while the later "file not found" branch would return *run default* ([script.c 308–310][exec-head], [script.c 382–386][exec-loop]). | The second branch is reachable only through the mind-control swap to script 2188 between the two file opens (script.c 335, 347). |
| X6 | Control codes share the line-number space: a goto whose value is −1, −2, −3 or −4 means next line, skip default, run default and run default ([script.c 1511–1512][sat-return], [script.c 369–378][exec-loop]). | No named action returns −4; it is reachable only through a goto value. A typed implementation should refuse negative goto targets. |

Anomalies X7–X10 (divide, counter write, action 86, global-flag write) are
recorded in the [S0 census](scr-census.md#anomalies-for-s1-s); the next new ID
is X11.

## Unknowns

- **U1** Whether the original, like CE, fires `SAP_UNLOCK` on every open
  attempt (and whether lock picking fires it too) is NOT_VERIFIED.
- **U2** Which points and opcodes the installed data uses: answered by the
  [S0 census](scr-census.md) (ARC-RES-045).
- **U3** Whether tile and sector scripts fire during turn-based combat and in
  what order against jump points on the same step: the step handler runs the
  jump point before the tile script ([object.c 5236–5250][tile-trigger]); the
  timing against animation is not read.
- **U4** The original order of heartbeat against a dialog that is open, and
  whether the AI tick runs while the dialog UI is modal, is not read.
- **U5** Multiplayer gates (E4) are out of scope.

[ce]: https://github.com/alexbatalov/arcanum-ce/tree/a7ff41b300ef712f0e7d088183a3d08957110cdd
[sap]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.h#L7-L44
[sf]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.h#L85-L93
[inv]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.h#L327-L351
[codes]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L48-L50
[exec-head]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L278-L320
[exec-dialog]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L322-L337
[exec-loop]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L339-L386
[exec-tail]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L388-L401
[call-timeevent]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L559-L574
[cond-default]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L1492-L1496
[sat-return]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L1507-L1512
[sat-dialog]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L1513-L1534
[sat-remove]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L1535-L1543
[sat-local]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L1544-L1553
[loops]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L1622-L1665
[call-in]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L1981-L2014
[call-ex-case]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L2246-L2248
[remove-call]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L2665-L2670
[act-default]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L2822-L2823
[call-script]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L2835-L2878
[call-ex]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L2880-L2887
[line-read]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L3332-L3341
[obj-exec]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L3866-L3908
[destroy]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L1294
[bust]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L2580-L2671
[fold]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L4150-L4158
[tile-trigger]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L5236-L5250
[portal-bust]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/portal.c#L38
[tile-set]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/tile_script.c#L292-L301
[tile-exec]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/tile_script.c#L360-L389
[sector-exec]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector_object_list.c#L680-L730
[fold-list]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector_object_list.c#L456-L471
[trap-disarm]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/trap.c#L505
[trap-invoke]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/trap.c#L562-L575
[trap-bust]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L1928-L1935
[trap-exec]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/trap.c#L579-L680
[ai-startend]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L349-L355
[ai-danger]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L1686-L1727
[ai-tick]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L2859-L2936
[ai-active]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L2940-L2954
[ai-delay]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L2998-L3002
[ai-schedule]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L3035-L3058
[sector-fold]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector.c#L1443
[create]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L4285-L4288
[ai-kos]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L3423-L3440
[ai-portal]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L3745-L3760
[ai-container]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L3843
[ai-guards]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L3926
[anim-use]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L6581-L6633
[anim-tb]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L3810-L3815
[anim-talk]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L8018-L8047
[anim-trap]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L11516
[combat-hit]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L596-L782
[combat-melee]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L1000-L1052
[throw]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L1141
[dmg]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L1420-L1519
[resurrect]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L2140
[tb-heartbeat]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L3024-L3037
[tb-end]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L3272-L3280
[dying]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/critter.c#L670-L717
[dlg-override]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/dialog.c#L3233-L3269
[item-get]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/item.c#L635
[item-drop]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/item.c#L726
[item-transfer]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/item.c#L786
[item-buy]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/item.c#L947
[item-use]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/item.c#L1450
[item-insert]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/item.c#L2487-L3760
[item-wield]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/item.c#L2984-L4411
[item-remove]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/item.c#L4380
[mt-use]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L3022-L3031
[proto-scripts]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/proto.c#L2047-L2051
[steal]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/skill.c#L1685-L1760
[tb-talk]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/tb_ui.c#L248-L259
[tb-examine]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/tb_ui.c#L271-L285
[dlg-start]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/dialog_ui.c#L118-L210
[dlg-resume]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/dialog_ui.c#L400-L407
[inven-use]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/inven_ui.c#L584-L594
[inven-open]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/inven_ui.c#L570
[inven-buy]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/inven_ui.c#L3808
[skill-use]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/skill_ui.c#L269
[sleep-use]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/sleep_ui.c#L314
[sleep-leader]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/sleep_ui.c#L738
[slides]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/slide_ui.c#L166-L180
