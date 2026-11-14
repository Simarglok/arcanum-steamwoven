# NPC AI script and dialog surface

Status: **Research**, work item ARC-RES-070 (2026-10-21), stage A8 of the
[NPC combat AI plan](npc-combat-ai-plan.md#phase-1-faithful-reconstruction)
(source unit S21). It lists every script attachment point (SAP) the AI fires or
whose result it uses, every SCR condition and action that reads or writes AI
state, and every dialog path that reads or writes it: the danger state and its
flags, the focus and enemy list, kill-on-sight, the leader, the wait flag, the
conversation slot, the encounter and generator flags, and the global "NPC
fighting" switch with its cheat toggle. It closes the questions stages A3, A4 and
A7 handed to A8 ([open questions](#open-questions-handed-forward)). It changes no
code; the rules are inputs for slices I3 (danger state, typed hook events), I4
(hostility), I6 (scheduler) and I9 (followers), and for the SCRIPT dispatcher
slices S6 (heartbeat) and S7 (combat hooks) of the
[dispatcher research](scr-event-dispatcher.md#10-proposed-implementation-slices).

Requirements: [FR-AI-002](../requirements/story-social-ai.md#fr-ai-002) (threat
hand-off: the script hooks and the dialog refusals),
[FR-SCRIPT-001](../requirements/story-social-ai.md#fr-script-001) (events and
roles) and [FR-SCRIPT-002](../requirements/story-social-ai.md#fr-script-002)
(conditions and actions).

**Boundary.** The generic dispatcher (the object entry point, outcomes,
writeback, the interpreter, opcode numbering) is the
[dispatcher research](scr-event-dispatcher.md) (ARC-RES-042), the
[census](scr-census.md) (ARC-RES-045) and the
[dispatcher runtime](../scripting/scr-dispatcher-runtime.md); this note only fixes
what the AI-facing points and opcodes do to AI state. Faction membership,
reputation and the reaction sum (FactionSame, GrantsFaction, ReactionSum in
`Arcanum.Simulation`) belong to the reactions thread (ARC-WORLD-044, PR #318) and
the reaction runtime (ARC-WORLD-026); the out-of-combat companion orders (follow,
leave, wait, stay close, spread out) belong to the companions thread
([ARC-RES-064](companion-orders-loyalty-evidence.md), ARC-WORLD-046); the
dialog's reaction binding, insult and fate point are
[ARC-WORLD-043](ledger/social.md#dialog-reaction-binding-arc-world-043)'s
(DR1–DR7) and [ARC-WORLD-054](../world/fate-points-runtime.md)'s; the magic
object-flag component is the magic slices' (MT13). They are named, not re-derived.

Earlier facts reused here: the [core loop](npc-ai-core-loop.md) (A1: gate, tick,
setter, P5), the [awareness study](npc-ai-awareness.md) (A2: conditions 28 and 29),
the [hostility study](npc-ai-hostility.md) (A3: kill-on-sight, the attack event,
peace, stop attacking), the [morale study](npc-ai-morale.md) (A4: surrender and
its readers, MR8–MR11), the [support study](npc-ai-support.md) (A5: spell order),
the [execution study](npc-ai-execution.md) (A6) and the
[followers study](npc-ai-followers.md) (A7: stop attacking, NO_ATTACK, FO8).

## Method

Behaviour comes from the retail executable (`Arcanum.exe` 1.0.7.4, SHA-256
`9bd80511…78d52`, re-hashed by every reader), read statically with `llvm-objdump`
by a first reader and, independently, by second readers that saw only the
questions ([method step 6](original-executable-check.md); see
[Second reading](#second-reading)). Object fields were named from the
executable's own field table (0x59BEA8); flag bits, object types and attachment
points come from the project's records of the executable's constants; action and
condition numbers are the [census](scr-census.md)'s (the executor's jump table at
0x44ADBC is indexed by action type − 1, the condition table at 0x44714C by type).
DLG opcode names were read from the executable's own opcode table (0x5A0750). No
game text or script is reproduced. The rows and verdicts are in the
[ledger](ledger/npc-ai.md#npc-ai-script-and-dialog-surface-arc-res-070).
Everything below is read in the executable unless marked **(inference)**.

Conventions: the danger states are NONE 0, COMBAT_FOCUS 1, FLEE 2, SURRENDER 3
([core loop section 5](npc-ai-core-loop.md#5-danger-state-machine)). Critter flags
(field 0xDA): FLEEING 0x10, SURRENDERED 0x4000, encounter 0x20000, combat mode
0x40000. NPC flags (field 0x118): FIGHTING 0x1, WAIT 0x8, JILTED 0x20, generator
0x80000, NO_ATTACK 0x20000000. Object flags (field 0x13): off 0x2, invulnerable
0x400000. The conversation slot is entry 0 of the NPC's reaction-PC array (field
0x129), filled by the dialog start and cleared by the dialog end (R5, DR2, DR3).
"Run default" is the script result 1, "skip default" 0.

## Answer

1. **The AI fires nine attachment points and reads the result of five.**
   First heartbeat (10) and heartbeat (19) gate the AI pass; will-KOS (22) vetoes
   kill-on-sight; catching thief (11) gates a guard's reaction and unlock (3) an
   NPC's door or container; enter-combat (13), exit-combat (14), start-combat (15)
   and end-combat (16) are notifications whose result is ignored. Dying (12) is
   fired by the death processing before the AI's kill notification and can veto
   all of it. Pick pocket's caught-thief (33, ignored) and catching-thief (11)
   points gate a detected theft's attack event (call sites confirmed here; the
   skills plan's task S15, ARC-SKILLS-010, not yet merged). The roles and
   moments are in [section 1](#1-attachment-points).
2. **Scripts read AI state only through five conditions and the reaction.**
   Surrendered (25), in a conversation (26), in combat mode (31: the combat-mode
   bit, not the danger state), jilted (39) and waiting (52); no condition reads
   FIGHTING, FLEEING, the focus or the enemy list. Get reaction (action 29) reads
   0 while the PC is on the NPC's enemy list ([section 2](#2-conditions)).
3. **No script action writes a danger flag.** The executor never writes the
   NPC flags and writes the critter flags only to mark an NPC made by create
   object (52) as an encounter creature. Scripts change AI state through the
   attack event (attack, 36), stop attacking (109), follow, disband and wait (21,
   22, 67), invulnerability and off (57, 56), kill and damage (58, 60, 138), the
   generator table (110), reaction, faction and reputation actions, and spells
   ([section 3](#3-actions)). A spell's object-flag component handles only its
   first special bit: FLEEING goes through the setter (flee, stop fleeing) only
   when it is that first bit; after the undead bit it is written or cleared
   directly, and its other critter-flag and NPC-flag bits are written directly
   (SU17; which records do so is MT13's data question).
4. **Dialog refuses fighters on both sides.** An NPC in COMBAT_FOCUS or FLEE
   cannot speak (code 2); a PC in combat mode may start a scripted dialog only when
   it may leave combat mode, which it then does; the dialog start and the step
   after a reply whose actions do not branch end the talk with a generated line (group 1000) when the NPC is kill-on-sight
   toward the PC (its will-KOS script included); while a talk is open the NPC's AI
   does nothing, and if it starts fighting or fleeing its AI closes the talk. The
   DLG action `co` makes the PC attack the NPC ([section 4](#4-dialog)).
5. **The "NPC fighting" switch is always on in the retail game.** Its only
   writer is the cheat key P, gated by a cheat level the retail build fixes at 0;
   setter rule 4 and the spell order read it ([section 5](#5-the-npc-fighting-switch)).
6. **Anomalies.** Two bugs of the original: a single-object operand of an
   empty object set reads an uninitialised slot (SU8, both fixed), and the spell
   object-flag component handles only its first special bit, so FLEEING after the
   undead bit bypasses the setter (SU17, latent pending MT13's census; Original
   switch `ObjFlagHandlesFirstSpecialBitOnly`, owner decision 2026-10-21). The fate point spent before
   a hostile dialog refusal (SU16) and the dead cheat toggle (SU1) are the
   original's and kept. Script actions 57 and 21 are real script routes into MR9's
   latent branch ([section 7](#7-anomalies-and-decisions)).

## 1. Attachment points

Roles are triggerer / attachee / extra; "result" says what the caller does with
it. Every row was read at the call site named (the call site is in brackets).

| SAP | Point | Fired by (call site) | Roles | When | Result |
| ---: | --- | --- | --- | --- | --- |
| 10 | FIRST_HEARTBEAT | AI event 0x4AD200 (0x4AD2B2) | NPC / NPC / — | an AI event carrying the wake-up marker, NPC alive; after "check wield" and the leaderless NPC's waypoints or standpoints | anything but run default takes the far path of that event, so no heartbeat and no pass ([core loop CL7](npc-ai-core-loop.md#2-cadence)) |
| 19 | HEARTBEAT | AI event (0x4AD2E1); subturn hook 0x4B7010 (0x4B704A) | NPC / NPC / — | real time: alive, no round, within 30 tiles of the PC; round: the NPC's subturn start and after each animation it finishes in its subturn | run default: a generator NPC (NPC flag 0x80000) runs its spawner, any other NPC one AI pass; otherwise no pass (the next event is still scheduled; in a round, no animation ends the subturn) |
| 15 | START_COMBAT | gate 0x4A8570 (0x4A86A7) | danger source (may be null) / NPC / — | every pass that reaches gate step 6 with the NPC in combat mode (not in a conversation) | ignored (P5) |
| 16 | END_COMBAT | gate (0x4A8688), real time only, just before 15; subturn end 0x4B7630 (0x4B7681) | danger source / NPC / —; the critter / the critter / — | as above; the end of every subturn that is not the local PC's | ignored |
| 13 | ENTER_COMBAT | setter 0x4AABE0 (0x4AAE29) | the new target / NPC / — | rule 7: COMBAT_FOCUS requested, not target-locked, FIGHTING not yet set; after FIGHTING, CHECK_WIELD and CHECK_GRENADE are set | ignored; the alerted sound follows |
| 14 | EXIT_COMBAT | setter (0x4AAF32) | the requested target (may be null) / NPC / — | rule 10: any other state while FIGHTING was set; after FIGHTING and BACKING_OFF are cleared and DEMAINTAIN_SPELLS set (not for FLEE) | ignored |
| 22 | WILL_KOS | kill-on-sight 0x4AE120 (0x4AE179) | candidate / NPC / — | after the validity test passes, before every kill-on-sight rule; kill-on-sight runs from find target, the dialog start 0x412FD0 and the reply step 0x413130 | skip default: not kill-on-sight |
| 11 | CATCHING_THIEF_PC | portal and container guards 0x4AEE50 (0x4AEFCD) | criminal / guard / the object | for each nearby guard that sees or hears the crime (S22) | run default only: the guard's reaction −20 and, at or below parameter 10, the attack event (criminal on guard); skip default: the guard is skipped (built by ARC-AI-2084, ledger GW4: the actor is the triggerer, the guard the attachee, the guarded object the extra object) |
| 33, 11 | CAUGHT_THIEF, CATCHING_THIEF_PC | pick pocket, skill use 0x4C7160 (steal: 0x4C76CE, 0x4C76E5; plant: 0x4C78E9, 0x4C7900) | victim / thief / item (33); thief / victim / item (11) | a detected theft or plant | 33 is ignored; on 11 anything but run default cancels the rest; then, unless the victim is an NPC the thief leads directly, the attack event (thief on victim, loudness 1, flags 0; calls 0x4C7728, 0x4C793B). The call sites and the attack event were read here; the skills plan's task S15 (ARC-SKILLS-010, ledger PP1–PP8, not yet merged) reads the rest of the theft path |
| 3 | UNLOCK | AI portal open 0x4AEB70 (0x4AEBC5), container open 0x4AED80 (0x4AEDC4) | NPC / portal or container / — | an NPC's open attempt (S22) | gates the open |
| 12 | DYING | death 0x45DA20 (0x45DA57) | killer / victim / — | any death | skip default ends the whole processing at once (see below) |
| 20 | LEADER_KILLING | death (0x45DB95) | victim / each PC follower / PC | a kill by the PC or a PC-led critter | ignored |
| 9 | DIALOG | talk request 0x57CC70 (0x57CC90); resumption 0x568480 | PC / NPC / — | [section 4](#4-dialog) | run default: generic greeting, or a surrendered NPC flees |
| 18 | RESURRECT | heal step 0x4B58C0 (0x4B5A46) | caster / target / weapon | resurrection | skip default refuses; a resurrected non-PC stops fleeing and gets a delayed AI event ([core loop](npc-ai-core-loop.md#1-entry-points)) |

**Death order (0x45DA20).** After a run-default dying script: a destroyed
object (object flag 0x1) stops; the victim leaves combat mode; its death time is stored; the spells it
holds end; the round's reaction categories are refreshed; for an NPC a decay
event is queued, its combat focus field is set to the killer (which the scan's
"avenge" step reads, [hostility](npc-ai-hostility.md)) and the kill notification
(0x4AA1B0, the victim stops fleeing and waiting, PC-kill back-off) runs; then
experience and LEADER_KILLING. With skip default none of this happens.

**Item points the AI causes.** The AI's own goals fire the ordinary item points:
the gate's re-wield fires WIELD_ON/WIELD_OFF (24, 25), the item search's pickup
GET (4), an item use USE (1), a grenade throw THROW (6, which gates the throw);
the census finds no installed script on 4 or 6. Attacks fire HIT/MISS/critical
points (7, 8, 34, 35) on the weapon, CRITTER_HITS (26) on the attacker and
TAKING_DAMAGE (23) on the target ([dispatcher](scr-event-dispatcher.md#2-event-catalogue-s));
none of them reads or writes AI state. The interpreter itself skips every point
of a generator NPC (run default) and, at point 9, a dead NPC without the
death-speech file flag (0x8) or a surrendered one without surrender speech
(0x10); a mind-controlled NPC (spell flag 0x8000000) runs script 2188 instead of
its own (0x4449B0: the surrendered test at call site 0x444AC8, the mind-control
test and the swap to 2188 at 0x444AF4 and 0x444B10).

## 2. Conditions

Each condition resolves its object operand to a set; a set condition is true
when every object passes, and for the "any" focus kinds (3, 5, 7, 9, 14, 16, 18,
20, 23) when at least one does (shared tail of 0x4451B0).

| Type | Name | What is tested per object | AI meaning |
| ---: | --- | --- | --- |
| 25 | `obj_has_surrendered` | an NPC (type 16) whose state read gives SURRENDER (0x4AF210, call site 0x4460E8); PCs never count | SURRENDERED set and FLEEING clear |
| 26 | `obj_is_in_dialog` | an NPC: its conversation slot is set (0x4C1110); any other object: the local dialog window is active (0x460460) | the same slot that ends every AI pass (gate step 5) |
| 31 | `obj_is_in_combat` | a PC or NPC with the combat-mode bit (0x4B3D50) | not the danger state: under CL4's Original behaviour a surrendered NPC keeps the bit and still tests "in combat" |
| 39 | `obj_jilted` | an NPC with JILTED | set by the attack event's follower rule ([hostility 7.1](npc-ai-hostility.md#71-attack-event)) |
| 52 | `waiting_for_leader` | an NPC with WAIT | the companions thread's wait (no live clause) |
| 30, 27 | `obj_is_invulnerable`, `obj_is_switched_off` | object flags invulnerable, off | setter rule 1, attack event step 1, gate step 2 |
| 28, 29 | `obj_can_see_obj`, `obj_can_hear_obj` | the AI's sight and hearing | [awareness section 8](npc-ai-awareness.md#8-call-sites-of-sight-and-hearing) |
| 18, 22, 23 | following, dead, max followers | the leader, HP ≤ 0, the follower count | companions and life state |

**Reaction reads the enemy list.** Get reaction (action 29) uses the standard
effective reaction (0x4C0CC0), which, like the AI variant (0x4C0CE0), returns 0
while the PC is on the NPC's enemy list (R1); so does the DLG reaction test.
Every hit on an NPC adds the attacker's team to that list (HS19), so after any
attack by the PC a script or dialog sees reaction 0 until peace.

## 3. Actions

No other executor case reads or writes AI state; the census lists all 142.

| Action | What it does to AI state (0x447220, case address) |
| ---: | --- |
| 36 `attack` | for each object of operand 0 (a set) the attack event (0x4A9650) runs with **operand 1** (one object) as attacker, that object as victim, loudness 1 and flags 2 ("no fan-out"). An NPC victim therefore loses its KOS override, turns to the attacker, records it as who-hit-me-last, adds its team to the enemy list, has its reaction to a PC attacker lowered and reacts (fight or flee) ([hostility 7.1](npc-ai-hostility.md#71-attack-event)); no noise, no followers told, no immediate AI pass (it acts at its next AI event). A **PC** victim gets nothing but the loss of concealment: flag 2 skips its combat-mode entry, and the event stops at the non-NPC victim (case 0x447D2F) |
| 109 `action_stop_attacking` | stop attacking (0x4AA4A0, case 0x44A038) on operand 0: an NPC with a focus makes peace with it, has its goals interrupted at priority 3 and leaves combat mode; with a PC leader NO_ATTACK is set; the target lock is cleared; a PC: all its followers ([followers section 3](npc-ai-followers.md#3-back-off-and-stop)). No acknowledgement line and no AI pass, unlike the stop order (broadcast 0x4C31A0, type 12 at 0x4C3849) |
| 21 `critter_follow` | forced follow (0x45DE90, force 1): operand 0 joins operand 1, skipping the follow check (0x447787). A new leader feeds setter rule 3 (extraordinary charisma) |
| 22 `critter_disband` | forced leave (0x45DFC0, force 1) (0x4477D9); NO_ATTACK survives it (FO7) |
| 67 `action_wait_for_leader` | the wait (0x4AA7A0): a follower that is not waiting leaves (forced), keeps its leader remembered, gets WAIT and a wait-expiry event (0x448CEA). Unused in the data |
| 57 `toggle_invulnerability` | toggles object flag invulnerable on each object (0x448858): from the next setter call the NPC's every request becomes NONE (rule 1), attacks on it stop at step 1 |
| 56 `toggle_state` | toggles object flag off (0x4487CE): the gate ends every pass of an awake "off" NPC (step 2) |
| 58 `kill` | death with no killer (0x45D900, damage step with a null attacker, then 32000 HP damage): DYING with a null triggerer; the kill notification's PC-kill back-off does not apply (0x4488E8) |
| 60, 138 `damage`, `apply_unresistable_damage` | the damage step with a null attacker (0x4489F9, 0x44AAF6): no attack event, no enemy list or reaction change; a damaged living NPC gets one immediate AI pass (0x4B52FC) |
| 52 `create_object` | a created NPC gets the critter flag **encounter** (0x448537, written at 0x448621): the AI event's far path destroys it, outside a round, when it is beyond 30 tiles of the PC, not caught up as a follower and its sector is not loaded (0x4AD328), or when a generator's spawner fails (CL7), and schedules nothing more |
| 110 `toggle_monster_generator` | toggles bit 0x80 of entry n (0..255, others ignored) of the generator table (0x44A067), saved with the game; the AI event's spawner (0x4BA790) spawns nothing while it is set; condition 46 reads it |
| 29–31 reactions, 72/73 reputation, 134 `set_faction` | inputs of kill-on-sight rows 6 and 7, protect and the follow checks; the reactions thread's (ARC-WORLD-044/026) |
| 40 `transform_attachee_into_basic_prototype` | replaces the attachee by a new object of its basic prototype keeping its HP and fatigue ratios (0x4AB400, 0x4AB430) and items (0x447E89): the new NPC starts with fresh AI state |
| 61, 118, 121, 122, 136 spells | through the magic run: fear (FLEEING via the setter), mind control (NO_FLEE, follow), charm (+30 reaction, KOS override), animal peace, the AI redirect (P3) ([magic core](magic-core-evidence.md)) |
| 4 `dialog` | [section 4](#4-dialog) |

**Single-object operands.** Actions 21, 22, 67 and 109 take one object through
0x44B960, which returns the first entry of the focus set it resolves. When the
set is empty (for example the followers of an object without followers) the
resolver writes nothing and 0x44B960 returns the uninitialised first slot of its
local array (SU8).

**No direct danger-flag writes.** Both readers searched the executor: besides
action 52's encounter bit no case writes the critter flags or the NPC flags; the
other direct field writes are names, art, inventory display, written-text and
standpoint fields. The spell path is the only script route that writes AI flags
outside the AI. The object-flag component (0x454A10) reads the field once, then
handles only the **first** special bit of its mask, in the order undead (0x4),
FLEEING (0x10), blind (0x80), paralysed (0x40) when turning on, undead then
FLEEING when turning off:

- undead sets or clears object flag 0x20000000 and then falls through to the common
  write (near 0x4550C6), which stores the field read at entry with the **whole**
  mask added or removed;
- FLEEING calls flee (0x4AF130, the setter's FLEE) or stop fleeing (0x4AF170) and
  returns without the common write, so other bits of the same mask are not written;
- blind calls its own function and returns, also without the write; paralysed
  interrupts movement goals and then writes;
- any other mask goes straight to the common write, every NPC-flag bit too.

So FLEEING goes through the setter only when it is the first special bit: a mask
with undead and FLEEING writes FLEEING straight into the field when turning on (no
fleeing-from source, no float line, FIGHTING and the focus kept, no exit-combat
script) and clears it straight from the field when turning off (no flee goal
interrupted); blind or paralysed after undead also lose their side calls (SU17).
Which installed spell records combine undead with FLEEING or name SURRENDERED,
FIGHTING, NO_ATTACK, NO_FLEE or KOS bits in an object-flag component is a data
question for the magic slice MT13 ([open question 1](#open-questions-handed-forward)).

## 4. Dialog

**Talk request (0x57CC70).** The PC's talk goal ends in the UI's talk handler,
which runs the NPC's dialog script (point 9) with no combat test: an NPC that is
fighting still runs it. Anything but run default ends the request there (a
script dialog action has already run, below). On run default: a surrendered NPC
whose source is in the talker's party flees from the talker (0x4AF130; with
NO_FLEE the setter makes it a fight) and nothing opens (MR11); otherwise the
generic greeting (0x5681C0 → 0x4132A0): **can speak** with argument 0; a refusal
gives an empty line, else the greeting (a surrendered NPC's is its fleeing
message, 0x419260). The greeting is a floating line over the NPC, no window
(ARC-DEV-324, [GT220–GT222](../architecture/goal-table.md#the-b24-read-of-the-executable-arc-dev-304)).

**Can speak (0x4AD800; NPC, other, flag).**

| Code | Condition, in order |
| ---: | --- |
| 5 | object flags destroyed or off (any object) |
| 0 | not an NPC |
| 5 | stunned, paralysed or mute (critter flags 0x2060) |
| 6 | asleep (critter flag 0x1000) |
| 5 | under spell 58; stoned (spell flag 0x10000) |
| 1 / 0 | dead: 1, or 0 under spell flag 0x2000000, which also skips the tests below |
| 6 | unconscious |
| 2 | flag 0 and state COMBAT_FOCUS or FLEE |
| 3 | flag 0 and the NPC's running goals refuse an interrupt at priority 2 (0x424070); the attempt itself ends the goals that accept it |
| 4 | the conversation slot holds another PC |
| 0 | otherwise |

SURRENDER passes. With flag 0 the test interrupts the NPC's low-priority goals as
a side effect, so a talk click stops an NPC's walk even when the greeting is
refused later; the follower and comment lines call it with flag 1 (no state test,
no interrupt).

**Script dialog start (action 4 → 0x567460).** The dialog action calls the start
callback only when the triggerer is a PC and always returns skip default (case
0x447282). The callback refuses a dead or unconscious PC, a can-speak refusal
(flag 0) and a local PC whose interface is in mode 3; then, for a PC in combat
mode, it continues only when the PC may leave combat mode (0x4B3D90: not in an
attempt-attack goal, and no active NPC on screen in COMBAT_FOCUS on the PC or one
of its followers, [RT2](ledger/combat.md#real-time-combat-arc-combat-004))
and then leaves combat mode (0x4B3F20). This is the "dialog refusal in combat":
fighting NPCs refuse through can speak, a PC under attack through RT2. Every
refusal is silent.

**Dialog start (0x412FD0) and the reply step (0x413130).** The start tests can speak
(refusal: nothing starts), opens the reaction conversation (the slot := the PC,
the "force good reaction" fate point applied, DR2, DR6), then, for a living NPC,
**kill-on-sight toward the PC** (0x4AE120, will-KOS script included): when it
holds, the NPC speaks a generated line of group 1000 and the dialog ends at once
(state 4). The reply step: an ended state (4..9) does nothing; can speak again
(refusal: state 1, the dialog ends); in state 3 (back from another screen) the
stored response is decoded instead of running actions; otherwise the reply's
actions run (0x415BA0) and, when they return 0, the step ends there, before the
test (the actions that jump to a node or end the talk, `jo` and `uw` among them);
then the same living-NPC kill-on-sight test, with the same line and end; else the
next node (closes A3's open question 3; DR5's insult follows the test). So after a
reply whose actions do not branch, an action that lowers the reaction to parameter
10 or below, sets a faction or starts a fight (`co`) ends the talk at once with the
"won't talk" line.

**DLG actions that touch AI state (0x415BA0, table 0x41672C).**

| Opcode | Effect |
| --- | --- |
| `co` | sets a flag; after the whole action string the attack event runs with **the PC as attacker on the NPC**, loudness 1, flags 0 (deliberate, with fan-out: noise, followers told) (call 0x416717). The NPC reacts (fight or flee); the reply's kill-on-sight test then usually ends the talk, and the NPC's next AI pass closes it if still open |
| `re`, `al`, `or`, `rp` | reaction, alignment, origin, reputation: kill-on-sight rows 6–8, protect ORIGIN, upset GOOD, the follow checks (reactions thread) |
| `jo`, `uw` | join or stop waiting, both after the follow check (0x4AD950); `lv` forced leave; `wa` wait; `sc`/`so` clear/set NPC flag 0x10 (stay close / spread out) — companions thread |
| `nk` | kill the NPC (0x45D900, no killer) |

**While the talk is open.** Gate step 5 ends every AI pass of an NPC whose
conversation slot is set. When the partner is the local PC and the NPC's state is
COMBAT_FOCUS or FLEE (not NONE, not SURRENDER), the pass calls the AI's
conversation callback, registered by the dialog UI at start-up (0x567330 →
0x4A84D0): 0x5678D0 closes that PC's dialog window, releases the DLG and calls the
dialog end (the slot is cleared, R6); no script is resumed (closes the core loop's
U3). The second callback (0x568430) floats a speech line; the AI's flee and comment
lines use it.

## 5. The "NPC fighting" switch

The switch is the word at 0x5B50CC, initialised to 1 (on) in the executable's
data and not saved. Its one writer, 0x4AF860, flips it and floats "NPC fighting is
ON" or "… OFF". The one caller is the main input loop (0x401560, call site
0x4019A7): a key event with scan code 0x19 (P) inside the block for cheat level 3
or more. The cheat level comes from 0x402CB0, which returns 0 in the retail build,
so the level-3 block (P and three other keys) and the level-2 block that follows
are dead code: the switch is always on in the game the player runs. Readers: setter
rule 4 (off: a COMBAT_FOCUS request becomes NONE) and the spell order 0x4ABC70 (off:
no spell or item is chosen; [support](npc-ai-support.md#2-spell-and-item-choice-in-a-fight)).
The loudness table that ends just before it would read it only with index 3,
which no caller passes (A2).

## 6. Surrendered NPCs: one object set

Consolidating A4 and the readings above:

- **Writers.** SURRENDERED is set only by setter rule 5 for a SURRENDER request,
  and the only request comes from perform fleeing (0x4AC620); the setter clears it
  on any other state (rule 5′). Stop fleeing keeps it. No script action writes it;
  a spell object-flag component naming it would write it directly (MT13).
- **Direct reader.** Only the state read (0x4AAB00) tests the bit; FLEEING wins
  over it.
- **Readers through the state.** surrendered (0x4AF210): the talk request (flee
  from the source's party), the interpreter's point-9 gate with the surrender-speech
  flag 0x10 (3 installed scripts carry it, [census](scr-census.md#answer)),
  condition 25 (NPCs only) and the greeting (fleeing message); can speak (passes);
  gate step 5 (keeps the talk open); kill-on-sight row 3 (never toward its source);
  the reaction to an attack, the witness rule, find target and the lock; leaving
  combat mode (does not block it) and condition 31 (the combat-mode bit CL4 keeps in
  the Original setting).
- **Dialog with a surrendered NPC.** Talk request → its script runs only with
  surrender speech; on run default the source's party makes it flee, anyone else
  gets the fleeing greeting; a scripted dialog opens (can speak passes, the
  kill-on-sight test is false toward its source but may hold toward another PC).

## 7. Anomalies and decisions

Verdicts and rows: [ledger](ledger/npc-ai.md#npc-ai-script-and-dialog-surface-arc-res-070).
Decisions follow D2 and D8. The owner accepted the proposed Original switch of this
table (SU17) on 2026-10-21 (ARC-RES-071).

| ID | Original 1.0.7.4 | Verdict | Proposed decision |
| --- | --- | --- | --- |
| SU1 | The "NPC fighting" toggle exists but is unreachable: the cheat level is fixed at 0, so the switch stays on. | Original | Steamwoven models the switch as always on, with no rule-mode switch; plan phase 2 M5 may expose it as a player option. |
| SU8 | A single-object operand (actions 21, 22, 67, 109 and others using 0x44B960) of an empty focus set returns an uninitialised stack slot as the object. | Original bug (latent: which installed clauses use a focus kind that can resolve to an empty set is not counted, [open question 6](#open-questions-handed-forward)) | Both fixed: an empty set gives no object and the action does nothing (owner rule: never reproduce an undefined read). No switch. Belongs to the SCRIPT interpreter slices. |
| SU16 | The dialog start opens the reaction conversation, which spends an armed "force good reaction" fate point, before the hostile check; a talk refused for a faction kill-on-sight still uses it up. | Original | Kept in both settings: the owner kept the same pattern in MG20 (a resource spent before a refusal, 2026-10-20), and whether the order is intended is unknown **(inference)**. No switch. An alternative the owner could pick: run the kill-on-sight test with the boosted reaction before committing the fate point, which would keep the point for a faction refusal and still let it save a talk refused for low reaction. |
| SU17 | The spell object-flag component handles only the first special bit of its mask: after the undead bit, FLEEING (and blind or paralysed) is written or cleared directly in the critter flags, past the setter; with FLEEING or blind first, the other bits of the mask are not written. | Original bug (latent: no installed record is known to combine them; MT13's census) | Steamwoven handles every special bit of the mask (FLEEING always through flee or stop fleeing) and writes the remaining bits; the Original setting keeps the first-bit-only handling (`ObjFlagHandlesFirstSpecialBitOnly`, owner decision 2026-10-21). A bypass of the setter that leaves a fleeing NPC without a source, still FIGHTING (inference: unintended). Visible only if an installed record combines the bits. Lands with MT13 and slice I3. |
| MR9 (note) | Script actions 57 (invulnerability during a flight) and 21 (a new leader of extraordinary charisma) reach MR9's latent branch; no script action writes FLEEING; a spell component with undead and FLEEING would (SU17, MT13). | Original bug (latent), unchanged | The decided switch `FleeGoalAfterRefusedSurrender` covers these routes. |

The other rows (SU2–SU7, SU9–SU15) are the original's behaviour and are kept in
both settings.

## 8. Consequences for Steamwoven (project policy proposals)

1. **Slice I3** publishes typed hook events at the positions of section 1 (enter
   and exit combat from the setter with their triggerers; start and end combat from
   the gate and the subturn end, P5's repetition included; will-KOS as a veto port
   that kill-on-sight asks before its rules); the dispatcher's S7 slice carries them.
2. **Slice I6** gates the pass on first heartbeat and heartbeat exactly as listed
   (dispatcher slice S6), runs the spawner for generators, honours the generator
   table and destroys script-created encounter NPCs on the far path.
3. **The dialog port** applies can speak (codes 0–6, the interrupt with flag 0),
   the PC's combat-mode exit (RT2) and the kill-on-sight test at the start and in
   the reply step (not after branching actions), and the AI ends an open talk when the NPC fights or flees.
4. **Script and DLG actions** reach AI state only through session operations the
   AI slices own: the attack event (36 with flags 2, `co` with flags 0), stop
   attacking, the setter-free flag toggles (invulnerable, off), the follow, leave and
   wait operations of the companions thread, and the reaction owner.
5. **Switches.** One Original switch, SU17 `ObjFlagHandlesFirstSpecialBitOnly`
   (owner decision 2026-10-21). SU8 is fixed in both settings.

## Open questions handed forward

| # | Question (from) | Status |
| ---: | --- | --- |
| 1 | Scripts writing FLEEING or other danger flags past the setter ([morale MR9](npc-ai-morale.md#5-anomalies-and-decisions)) | **Closed** for script actions ([section 3](#3-actions)): none writes them; actions 57 and 21 reach MR9's branch. The spell object-flag component writes FLEEING directly after the undead bit and its other critter and NPC flag bits always (SU17): "does any installed spell record combine undead with FLEEING or name another AI bit?" is **handed to** the magic slice MT13 |
| 2 | The surrendered-NPC object set (morale section 3, the dialog start 0x57CC70, surrender speech) | **Closed** ([section 6](#6-surrendered-npcs-one-object-set)) |
| 3 | 0x413130, the second dialog caller of kill-on-sight ([hostility 4.1](npc-ai-hostility.md#41-kill-on-sight)) | **Closed** ([section 4](#4-dialog)): the reply step; after a reply whose actions do not branch (or a decoded response in state 3) a living NPC that is kill-on-sight toward the PC speaks group 1000 and the dialog ends |
| 4 | Script action 109 and the global "NPC fighting" switch ([followers open question 10](npc-ai-followers.md#open-questions-handed-forward)) | **Closed** ([section 3](#3-actions), [section 5](#5-the-npc-fighting-switch)) |
| 5 | Which installed spells use the redirect's `MinIQ:` threshold (P3, HS17) | **Handed to** slice I8 with the magic slice that implements the AI redirect component: the [component census](../formats/spell-records.md) counts 2 AIRedirect components (1 BEGIN, 1 MAINTAIN); their records and thresholds need a data probe (not cheap without code) |
| 6 | Which operand order the 111 installed `attack` clauses (60 live) use (a clause naming a PC as operand 0 does nothing in the original), and which installed clauses of actions 21, 22, 67, 109 and 52 use a focus kind that can resolve to an empty set (SU8's reach) (this stage) | **Handed to** dispatcher slice S7 / AI slice I4 (census of the clauses' focus kinds) |

## Unknowns

- **U1** How often each hook fires in play is a runtime question for slice I11.
- **U2** The talk goal and the interface decide whether a PC in combat mode can
  issue a talk request at all; not traced.
- **U3** What the decay event queued at death (type 0x1A, 600 s) does belongs to the
  life-state and object owners.

## Second reading

Three second readers, each with only its questions (function addresses and what to
determine), re-read the executor cases (36, 21, 22, 67, 109, 56, 57, 58, 60, 52, 110,
4), the single-object resolver, the AI event's encounter test, the conditions (25,
26, 31, 39, 52), can speak, the dialog start and reply step, the script dialog start
and the combat-mode exit, the conversation callbacks, the DLG action interpreter,
the hook call sites, kill-on-sight's first tests, the death order, the magic
object-flag component, the interpreter's gates and the talk request; an earlier
reader read the cheat toggle and its gate (its other answers were lost); after the
independent review a fifth reader re-read the object-flag component's undead,
FLEEING and clear paths from a question only and confirms SU17. All agree
with the first reading on every behaviour above. They added: action 21's forced
follow skips the follow check and clears JILTED; the generator table is saved and
also read by the generator record reader; the object-flag component writes only the
first special bit (section 3); the interpreter's point-9 type test reads an
uninitialised local when the attachee is null (the dispatcher's X2, here read in the
executable); and that the gate's combat scripts do not run while a conversation is
open. Details are in the
[ledger section](ledger/npc-ai.md#npc-ai-script-and-dialog-surface-arc-res-070).
