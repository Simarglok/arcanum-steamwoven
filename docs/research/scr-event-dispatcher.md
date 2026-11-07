# Shared SCR event dispatcher: source reading and slices

Status: **Research**, work item ARC-RES-042 (2026-10-16), the research part of
top-20 item 15. It reads how the earlier source reading (F0) runs a script for an event
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

- **Source (S)**: an earlier source reading (provenance in git history),
  cited by the executable address of its function; level F0 (not checked in
  the executable) unless a ledger row is cited; for the meaning of the
  mechanism only. Several functions have no known name; a reading of their
  intent is marked as such.
  Original-executable parity is **NOT_VERIFIED** throughout, except the rows
  the [verification ledger](original-verification-ledger.md) checked (section 11).
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
  counters changed (object 0x441980). A slot holding script 0
  means "no script" and returns *run default* without running anything.
  Tile scripts, sector scripts, deferred calls, call-script, the end-game
  slides and traps build the same invocation by hand and call the interpreter
  directly (section 3).
- **36 attachment points, 35 fired, about 75 call sites (S).** The header
  enumerates 36 points, 0..35 (the attachment-point enumeration); the earlier requirement text
  said 35. 35 are fired through the object entry point from combat, AI,
  items, skills, UI and map code; `SAP_DIALOG_OVERRIDE` (31) is never fired by
  the engine (a call-ex action could still run that slot), its slot names a
  DLG file for generated lines
  (dialog 0x418870). Section 2 lists every point with its
  caller, roles and whether the result gates the default.
- **Heartbeat (S).** The AI time event of an NPC runs `SAP_FIRST_HEARTBEAT` on
  the first tick after a wake-up and `SAP_HEARTBEAT` on every real-time tick
  while the NPC is alive and within 30 cells of the local PC; *run default*
  from the heartbeat is what lets the AI pass (or a monster generator) run, and
  *skip default* from the first heartbeat skips that tick's heartbeat and AI
  (AI 0x4AD200). The next tick is due after
  `250 + 4750 × min(d, 30) / 30` ms (AI 0x4AD5D0). In turn-based
  mode the heartbeat runs at the start of each NPC subturn and again after
  each animation the NPC finishes during its subturn, and gates the AI pass
  the same way (combat 0x4B7010, animation 0x423560).
  The ARC-RES-039 plan's "once at the start" reading misses the second caller.
- **Combat enter and exit (S).** `SAP_ENTER_COMBAT` fires when an NPC first
  takes a combat focus (it was not already fighting), `SAP_EXIT_COMBAT` when a
  fighting NPC's danger source changes to anything else; the triggerer is the
  new danger target (the opponent on enter; on exit the critter fled from or
  surrendered to, or none when the danger is cleared) (AI 0x4AABE0). `SAP_START_COMBAT` and
  `SAP_END_COMBAT` are not the transitions their names suggest: while an NPC is
  in combat mode every AI pass runs END then START in real time, and START
  only in turn-based mode, where END runs at the end of each NPC subturn
  (AI 0x4A8570, combat 0x4B7630). All four
  ignore the result.
- **Dialogue (S).** A talk request runs `SAP_DIALOG` from line 0; *run default*
  means "no script conversation", and the UI then shows the generic greeting
  (or a surrendered NPC flees when its danger source is in the talker's party) (UI callback 0x57CC70). The script's
  dialog action starts the DLG of the same number through a callback when the
  triggerer is a PC, and returns *skip default* in every case (script 0x447220). When the
  conversation ends with response 0 or −N the UI resumes the same script at
  the line after the dialog action or at line N
  (dialog UI 0x568480). The interpreter skips the dialog script
  of a dead or surrendered NPC unless the script file flags allow it, and runs
  script 2188 instead for a mind-controlled NPC (script 0x4449B0).
- **Use (S).** Use on a portal, on scenery other than a bed and on other
  non-container, non-critter objects runs `SAP_USE` with the user as both
  triggerer and extra object; *skip default* cancels the use
  (animation 0x428A10). Containers and bodies run it from the loot
  UI, beds from the sleep UI, items from item use. Before any `SAP_USE` the
  entry point gives the triggerer a chance to spot a trap on the object; a
  spotted trap cancels the use (object 0x441980). Tile scripts
  run as `SAP_USE` for **any critter** entering the tile, and the sector script
  as `SAP_USE` when the PC enters the sector, followed by `SAP_NEW_SECTOR` for
  each of the PC's followers (object 0x444270,
  sector object list 0x4F2330).
- **Unknown commands are silent no-ops as read (S).** An unknown condition type
  runs neither branch and continues with the next line
  (script 0x4451B0); an unknown action type continues with
  the next line (script 0x447220). A goto to a line outside
  the script stops the run with *skip default* (a goto value of −1..−4 is read
  as a control code instead, X6); 1,000 executed lines without a return
  also stop it with *skip default* and without auto-removal
  (script 0x4449B0). FR-SCRIPT-002 asks for the opposite, an
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
| E1 | An invocation holds the script attachment (number, header flags, four byte counters), the start line, triggerer, attachee, extra object and attachment point. | the invocation record |
| E2 | The object entry point returns *run default* without running anything when the editor is active or the slot's number is 0. Only for `SAP_USE`, an unspotted trap on the attachee gets a spot roll first; a successful spot returns *skip default*. | object 0x441980 |
| E3 | After the run the slot is written back when number, flags or counters differ from before; if the attachee is a trap object whose script became 0, the trap object is destroyed instead. | object 0x441980 |
| E4 | The interpreter returns *skip default* at once when the script number is not a host-allowed one in a networked client (multiplayer only), when the script number is a native trap script (handled by trap code, not interpreted), and when the script file cannot be opened. | script 0x4449B0, trap 0x4BCB70 |
| E5 | It returns *run default* without running anything for an NPC attachee that is a monster generator. | script 0x4449B0 |
| E6 | Dialog-only gates: a dead NPC without the death-speech file flag, or a surrendered NPC without the surrender-speech flag, returns *run default*; a mind-controlled NPC runs script 2188 in place of its own, and its own number is restored afterwards. | script 0x4449B0 |
| E7 | Execution starts at the invocation line with fresh local variables and a fresh loop state; each line evaluates its condition and runs the true or the false action; the action returns "next line", a jump target, or one of two return codes. Past the last line counts as *skip default*. | script 0x4449B0, 0x447220 |
| E8 | On either return code a script whose file flags carry auto-removing sets its number to 0. "Return and run default" (action 2) gives *run default*; "return and skip default" (action 1) and the dialog action give *skip default*. | script 0x4449B0 and the script file flags |
| E9 | A line read outside the script (a goto target outside 0..count−1 other than the codes −1..−4, unsigned compare) ends the run with *skip default*; so does reaching 1,000 executed lines. Neither path applies auto-removal. | script 0x4449B0, 0x44C1B0 |
| E10 | "Remove this script" and "change this script" do not stop the run; they record a new number that replaces the attachment's number after the run. | script 0x447220, 0x4449B0 |
| E11 | Local flags (32 bits of the header) and counters (four bytes) are changed in the invocation's own copy, so only the object entry point, the sector caller and the dialog action, which commits them in the middle of a run, persist them (E3, section 3; the dialog action added from [SA8](../architecture/original-scripts-authority.md#2-a-run-writeback-gates-and-nesting) by ARC-RES-086); the tile caller compares and then discards them (X3). | script 0x447220 |
| E12 | One loop at a time: loop-for resolves a focus group (at most 100 objects), loop-end jumps back, break leaves; a nested loop-for is ignored with a debug message. | script 0x447220 |
| E13 | Unknown condition type: neither action runs, next line. Unknown action type: next line. No error reaches the caller. | script 0x4451B0, 0x447220 |

## 2. Event catalogue (S)

Roles are given as triggerer / attachee / extra. "Gates" means the caller
changes its behaviour on *skip default*; "ignored" means it does not look.
Line 0 unless noted.

| SAP | Point | Fired by | Roles | Result |
| ---: | --- | --- | --- | --- |
| 0 | EXAMINE | examine of an object by the local PC, before the description is built (UI callback 0x57CD60) | PC / object / — | gates: no examine text |
| 1 | USE | use goal on portal, scenery, other objects (animation 0x428A10); item use (item 0x462CC0); container open and loot (inventory UI 0x572370); loot of a dead critter by pickpocket (skill UI 0x57A320); bed (sleep UI 0x57B180); trap disarm and trap step (trap 0x4BC7B0, animation 0x4305D0); magic-tech use component (magic-tech 0x452ED0) | user / object / user (goal); user / item / target (item use) | gates in goal, item, loot and bed; ignored for disarm, trap step, magic-tech |
| 2 | DESTROY | object destruction (object 0x43CCA0) | object / object / — | gates: *skip default* keeps the object |
| 3 | UNLOCK | any critter's attempt to open a portal or container that is not busted, before the jam, magic-hold and lock checks: PC loot UI, door animation goals, movement into a door, script open actions, AI (AI 0x4AEB70, 0x4AED80, inventory UI 0x572370) | critter / portal or container / — | gates the open |
| 4 | GET | item pick-up (item 0x461810) | critter / item / — | gates the pick-up |
| 5 | DROP | item drop (item 0x461AB0) | holder / item / — | gates the drop |
| 6 | THROW | thrown attack (combat 0x4B3C00) | attacker / weapon / target | gates the throw |
| 7, 8 | HIT, MISS | attack outcome, on the weapon (combat 0x4B2870, 0x4B2F60, 0x4B3770) | attacker / weapon / target | ignored |
| 9 | DIALOG | talk request (UI callback 0x57CC70); resumption after DLG (dialog UI 0x568480) | PC / NPC / —; start line 0 or the resume line | gates: *run default* shows the generic greeting, or makes a surrendered NPC flee when its danger source is in the talker's party; resumption ignored |
| 10 | FIRST_HEARTBEAT | first AI tick after a wake-up (AI 0x4AD200) | NPC / NPC / — | gates that tick's heartbeat and AI |
| 11 | CATCHING_THIEF_PC | pickpocket noticed (skill 0x4C7160); guard notified of a portal or container crime (AI 0x4AEE50) | thief / victim or guard / item or target | gates the attack or reaction drop |
| 12 | DYING | critter killed (critter 0x45DA20) | killer / victim / — | gates the whole death processing (combat exit, XP, alignment, logbook, follower notices) |
| 13 | ENTER_COMBAT | NPC takes a combat focus while not fighting (AI 0x4AABE0) | opponent / NPC / — | ignored |
| 14 | EXIT_COMBAT | fighting NPC's danger source changes (AI 0x4AABE0) | new danger target, may be none / NPC / — | ignored |
| 15 | START_COMBAT | every AI pass of an NPC in combat mode (AI 0x4A8570) | danger source / NPC / — | ignored |
| 16 | END_COMBAT | same AI pass, real time only, before START (AI 0x4A8570); end of an NPC turn-based subturn (combat 0x4B7630) | danger source / NPC / —; NPC / NPC / — | ignored |
| 17 | BUY_OBJECT | barter offer, on the buyer (item 0x462170, inventory UI 0x578330) | item / buyer NPC / seller PC | gates: "won't buy" |
| 18 | RESURRECT | resurrection (combat 0x4B58C0) | caster / target / weapon | gates: refused |
| 19 | HEARTBEAT | real-time AI tick within 30 cells (AI 0x4AD200); NPC subturn start and after each finished animation in its subturn (combat 0x4B7010, animation 0x423560) | NPC / NPC / — | gates the AI pass and the generator |
| 20 | LEADER_KILLING | the PC (or its follower) killed an NPC, for each PC follower (critter 0x45DA20) | victim / follower / PC | ignored |
| 21 | INSERT_ITEM | item, gold or ammunition inserted (item 0x464830, 0x4658E0, 0x466640) | holder / item / — | ignored |
| 22 | WILL_KOS | before the kill-on-sight decision (AI 0x4AE120) | target / NPC / — | gates the KOS checks |
| 23 | TAKING_DAMAGE | damage, on the target and on worn armor at a critical hit (combat 0x4B4390) | attacker / target / —; attacker / armor / target | gates the damage on the target (re-entry guarded); ignored for armor |
| 24, 25 | WIELD_ON, WIELD_OFF | equip and unequip (item 0x465530, 0x4677B0, 0x467CB0) | wielder / item / — | ignored |
| 26 | CRITTER_HITS | hit landed, on the attacker (combat 0x4B2F60, 0x4B3770) | target / attacker / — | ignored |
| 27 | NEW_SECTOR | PC entered a sector, for each follower (sector object list 0x4F2330) | PC / follower / — | ignored |
| 28 | REMOVE_ITEM | every removal from an inventory (item 0x466E00 → 0x467860; ledger [IE3](ledger/scripts.md#item-events-arc-script-041), not only a forced one) | holder / item / — | ignored |
| 29 | LEADER_SLEEPING | the leader sleeps, for each follower (sleep UI 0x57BAC0) | sleeper / follower / — | ignored |
| 30 | BUST | object or portal broken (object 0x43F1C0, portal 0x4F06E0) | breaker / object / — | ignored |
| 31 | DIALOG_OVERRIDE | never fired by the engine; the slot's number names a DLG of override lines (dialog 0x418870) | — | — |
| 32 | TRANSFER | item handed to a critter, on the recipient (item 0x461CA0) | giver / recipient / item | gates the transfer |
| 33 | CAUGHT_THIEF | pickpocket noticed, on the thief (skill 0x4C7160) | victim / thief / item | ignored |
| 34, 35 | CRITICAL_HIT, CRITICAL_MISS | critical outcome, on the weapon, next to HIT/MISS (combat 0x4B2870, 0x4B2F60, 0x4B3770) | attacker / weapon / target | ignored |

Reading notes. The catalogue lists the call sites found by the earlier
reading, not every place the original may fire them. `SAP_UNLOCK` is a permission check on every open
attempt, not a lock event; whether the original also fires it on lock picking
is U1. The bed use is inverted: the sleep UI refuses sleep on *run default*,
so *skip default* is what lets the PC sleep (sleep UI 0x57B180). The built-in
prototype defaults put scripts into item slots (for example scripts 29006 on
`SAP_HIT` and `SAP_MISS`, 28001 and 28002 on `SAP_WIELD_ON` and `SAP_WIELD_OFF`)
(prototype 0x468930); for the dispatcher these are
ordinary slots.

## 3. Entry points that are not an object slot (S)

| Entry | Script | Roles and line | Writeback | Source |
| --- | --- | --- | --- | --- |
| Tile script | the tile's node in the sector list | entering critter / none / none, line 0, point USE | compared, then the unmodified record is stored again, so changes are lost (X3) | tile script 0x4C0980, object 0x444270 |
| Sector script | the sector's attachment | PC / none / none, line 0, point USE | flags and counters only: a changed number is **not** stored | sector object list 0x4F2330 |
| Deferred call (call-in 54, call-at 55) | the named script with its file header as initial state | stored triggerer / attachee, stored line, point USE | none | script 0x447220, 0x445140 |
| Remove call (action) | clears queued deferred calls of one script and attachee | — | — | script 0x447220 |
| Call script (action 7) | the named script with its file header, the caller's attachment point, every triggerer × attachee of two focus groups | from the action, line from the action | none; result ignored | script 0x447220, 0x44B030 |
| Call-ex (action) | the slot of one object for a given attachment point, through the object entry point | from the action | as the object entry point | script 0x447220, 0x44B170 |
| Busted trapped object | `SAP_USE` slot of an object with a trap that an attack brought to 0 HP | melee attacker or none (ranged) / object | none: an auto-removing trap's clear is lost | trap 0x4BCB00, combat 0x4B4390 |
| Native trap scripts | trap script numbers inside the interpreter, for any caller (E4) | as the caller | as the caller (stepping on a trap goes through the object entry point, E3) | trap 0x4BCB70, animation 0x4305D0 |
| End-game slides | scripts 997, 998, 999 | none, line 0, point USE | none | slide UI 0x569720 |

Call-in turns its operand into `operand × 1000 × 8` ms of game time (reading:
seconds of real time at the 8× game rate, inference); call-at counts the game
seconds to a time of day without the factor (script 0x447220). Both are queued as
game-time events and saved with the time-event queue.

## 4. Heartbeat and the AI tick (S)

| ID | Claim | Source |
| --- | --- | --- |
| H1 | An NPC's AI time event carries the NPC and a "first" marker. When a loaded sector's objects are folded in, each NPC gets an immediate event whose marker comes from comparing the sector's stored time with the current time plus 3 s (meaning not read further); creating an NPC schedules a normal delayed one. | AI 0x4AD730, 0x4AD790, object 0x441FC0, sector object list 0x4F1CD0, sector 0x4D1530, object 0x442260 |
| H2 | On a first tick of a living NPC: it gets the re-check-weapon flag, a leaderless NPC outside combat moves to its waypoints or standpoints, then `SAP_FIRST_HEARTBEAT` runs; *skip default* skips the heartbeat for this tick. | AI 0x4AD200 |
| H3 | `SAP_HEARTBEAT` runs only when the NPC is alive, real-time mode is on and it is within 30 cells of the local PC; *run default* then runs the generator logic for a generator NPC or the AI pass for others. | AI 0x4AD200, 0x4AD420 |
| H4 | Otherwise (skipped, out of range, turn-based) the follower catch-up and encounter clean-up rules apply and the next event is scheduled, except for an NPC out of range in an unloaded sector without catch-up, and a generator whose spawn did not succeed, which return without the normal reschedule; a first tick adds 0–5 s. | AI 0x4AD200 |
| H5 | Turn-based: at the start of an NPC subturn and after every animation the NPC finishes in it, `SAP_HEARTBEAT` runs and *run default* runs the AI pass. | combat 0x4B7010, animation 0x423560 |

The heartbeat is therefore the first step of the AI pipeline that ARC-RES-039
names (its S2 tick and S5 pipeline); it is also how many non-combat scripts (barks, routines,
generators) run.

## 5. Dialogue path (S)

| ID | Claim | Source |
| --- | --- | --- |
| G1 | The talk goal ends by calling the UI's talk handler, which runs `SAP_DIALOG` from line 0. | animation 0x42AF00, UI callback 0x57CC70 |
| G2 | The dialog action copies the run's local flags and counters to the attachee's slot before the conversation starts, so the DLG sees them; as first read the line assigns the counters to both fields, which the original does not (anomaly X1). It calls the start callback only when the triggerer is a PC. | script 0x447220 |
| G3 | The start callback refuses a dead or unconscious PC, an NPC that cannot speak, a second dialog of the local PC, and a PC that cannot leave combat mode; it leaves combat mode otherwise, loads the DLG of the script number and shows the first node or a one-line float. | dialog UI 0x567460 |
| G4 | Response 0 resumes at the dialog action's line + 1, response −N at line N, through the object entry point (so the slot is written back again). | dialog UI 0x568480 |

## 6. Combat-facing events (S)

The transitions behind events 13–16 and 22 belong to the AI danger state of
ARC-RES-039 (its S4 danger state and S10 KOS); this section only fixes where the hooks
sit.

- `SAP_ENTER_COMBAT`: inside the danger update, when the new danger is a
  combat focus, the NPC has no target lock and is not yet fighting; the
  fighting, re-check-weapon and re-check-grenade flags are set first
  (AI 0x4AABE0). Virgil's enter-combat script 1997 copies the
  player's alignment ([virgil-level-replay](virgil-level-replay.md)).
- `SAP_EXIT_COMBAT`: same update, any other danger type, when the fighting flag
  was set, with the new danger target (possibly none) as triggerer; it is cleared first, and spells are de-maintained unless the NPC
  flees (AI 0x4AABE0).
- `SAP_START_COMBAT` / `SAP_END_COMBAT`: per AI pass and per subturn as in the
  answer (an AI pass also runs when an NPC is attacked or given a party order, so START can fire
  outside its own turn); the names describe a turn, not the fight (reading of intent).
- `SAP_WILL_KOS`: before the KOS rules; *skip default* means "not KOS"
  (AI 0x4AE120).
- `SAP_DYING`: *skip default* stops the whole death processing, including
  leaving combat mode and experience (critter 0x45DA20).

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
   unknown types as the silent no-op read earlier, with every skip counted in the trace; strict
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
| Cave entrance and exit use | endpoints are profile literals with SAP and script recorded; no script runs | `src/Arcanum.Data/Area/{OriginalProfile,CaveProfile}.cs`, `src/Arcanum.World/AreaTransition.cs` |
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
| S4 Use, tile and sector events (dispatcher part done: ARC-SCRIPT-014, [teleports and guards](../scripting/scr-dispatcher-runtime.md#teleports-and-guards-arc-script-014-set-s4); map-mode use clicks done: ARC-SCRIPT-015, [guarded use transitions](../rendering/area-map-mode.md#guarded-use-transitions-arc-script-015); map-mode tile and sector scripts for the player done: ARC-SCRIPT-017, [tile and sector scripts](../rendering/area-map-mode.md#tile-and-sector-scripts-arc-script-017)) | S2, M3 coordinator | Use on scenery and portals, tile scripts for any critter, sector scripts and `SAP_NEW_SECTOR`; teleport action as a port to the transition coordinator; conditional transitions of ARC-RES-035. |
| S5 Deferred calls ([ARC-SCRIPT-010](../scripting/scr-dispatcher-runtime.md#deferred-calls-arc-script-010-slice-s5)) | S2, top-20 item 3 | Call-in, call-at and remove-call on the event queue, in the snapshot. |
| S6 Heartbeat hook | S2, RES-039 I6 | First heartbeat and heartbeat on the AI time event, at subturn start and after each animation in a subturn, gating the AI pass. |
| S7 Combat hooks | S2, RES-039 I3/I4, combat slices | Enter/exit/start/end, WILL_KOS, dying, hit/miss and damage events at the positions of section 2. |
| S8 Item events (transfer half done: ARC-SCRIPT-041, [item events](../world/inventory-runtime.md#item-events-arc-script-041)) | S2, inventory owner | Get, drop, transfer, insert, remove, wield, buy; lifts the current "item with script refused" rule. Left: wield on and off with the item-effect actions they need, drop, buy. |

Suggested order: S0 → S1 → S2 → S3 (proves the core on the event that already
works) → S4 (opens the conditional maps) → S5 → S6/S7 with the AI slices →
S8 with inventory.

## 11. Anomalies recorded, not adopted (S)

**Original check (ARC-RES-048):** these rows were checked in the retail 1.0.7.4 executable; the verdicts in the [verification ledger](ledger/scripts.md#scripts-event-queue-and-randomness-arc-res-042-arc-res-045-arc-script-007-arc-res-046-arc-world-019) supersede the earlier readings below where they differ.

Under owner decisions D2 and D8 (fix confirmed slips; only a bug of the original
game keeps its behaviour behind a switch of the Original setting) these are
settled by the slice that implements them.

| ID | Source behaviour | Note |
| --- | --- | --- |
| X1 | The dialog action writes the counters into both the flags and the counters of the slot (script 0x447220). | A reconstruction slip: the original copies flags to flags and counters to counters (read at 0x4472F1–0x4472FA, ARC-RES-048 and ARC-SCRIPT-009; ledger row X1). Visible only to the first-node evaluation inside the synchronous start callback: the object entry point rewrites the slot from the invocation right after (object 0x441980). |
| X2 | The attachee type is read only for a non-null attachee but compared for every dialog invocation (script 0x4449B0). | Undefined for a dialog call without attachee (only reachable through call-script). |
| X3 | The sector-script writeback ignores a changed number, so "remove this script" on a sector script does not persist (sector object list 0x4F2330). The tile-script caller runs a copy and then stores the original record, so a tile script's number, flags and counters never persist (tile script 0x4C0980, 0x4C0830). | The earlier reading left the same question open on the tile caller (ledger row X3 answers it for both callers); the self-removal of tile script 2498 that ARC-RES-037 describes holds only if the original persists it. |
| X4 | Call-script runs from the file header, so local flags and counters written by the called script are lost (script 0x44B030). | May be intended (a called script has no attachment). |
| X5 | A missing script file returns *skip default* before running, while the later "file not found" branch would return *run default* (script 0x4449B0). | The second branch is reachable only through the mind-control swap to script 2188 between the two file opens (script 0x4449B0). |
| X6 | Control codes share the line-number space: a goto whose value is −1, −2, −3 or −4 means next line, skip default, run default and run default (script 0x447220, 0x4449B0). | No named action returns −4; it is reachable only through a goto value. A typed implementation should refuse negative goto targets. |

Anomalies X7–X10 (divide, counter write, action 86, global-flag write) are
recorded in the [S0 census](scr-census.md#anomalies-for-s1-s); the next new ID
is X11.

## Unknowns

- **U1** Whether the original, as read, fires `SAP_UNLOCK` on every open
  attempt (and whether lock picking fires it too) is NOT_VERIFIED.
- **U2** Which points and opcodes the installed data uses: answered by the
  [S0 census](scr-census.md) (ARC-RES-045).
- **U3** Whether tile and sector scripts fire during turn-based combat and in
  what order against jump points on the same step: the step handler runs the
  jump point before the tile script (object 0x444270); the
  timing against animation is not read.
- **U4** The original order of heartbeat against a dialog that is open, and
  whether the AI tick runs while the dialog UI is modal, is not read.
- **U5** Multiplayer gates (E4) are out of scope.

