# NPC AI followers in combat and orders

Status: **Research**, work item ARC-RES-067 (2026-10-21), stage A7 of the
[NPC combat AI plan](npc-combat-ai-plan.md#research-stages-source-reading-one-document-each)
(source unit S20). It fixes what the three combat orders the PC can give its
followers do (attack, back off or stop, and curse), how the followers of an
attacker and of a victim join a fight or complain, what the immediate AI pass
after an order does, the no-attack flag's whole life, what is reset after a
fight (spell demaintenance, combat mode, the target lock, and what is *not*
reset), and the two follower comment lines the earlier reading called "witness"
and "near-death" floats. It closes the questions stages A1, A3 and A6 handed to
A7 ([open questions](#open-questions-handed-forward)). It changes no code; the
rules are inputs for slice I9 (followers in combat) of the plan and for the
order entry point of ARC-WORLD-046.

Requirements: [FR-SOCIAL-004](../requirements/story-social-ai.md#fr-social-004)
(companion orders, combat half) and
[FR-AI-002](../requirements/story-social-ai.md#fr-ai-002) (threat hand-off,
follower side).

**Boundary.** The out-of-combat orders (leave, wait, follow, move, stay close,
spread out, walk here) and the portrait menu belong to the companions thread:
they were read by [ARC-RES-064](companion-orders-loyalty-evidence.md) (OL1–OL16)
and land with ARC-WORLD-046 (not merged when this was written); this note only
says where they end. The spread-out order's NO_FLEE side effect is A6's
[EX12](npc-ai-execution.md#9-anomalies-and-decisions) (`SpreadOutSetsNoFlee`,
proposed) and is not repeated. Faction membership and reputation (faction-same
0x45D110, reputation grant 0x4C21E0) belong to the reactions thread
(ARC-WORLD-044) and are used here as named predicates.

Earlier facts reused here, not re-derived: the [core loop](npc-ai-core-loop.md)
(ARC-RES-054: gate, pipeline, setter rules, the entry point "follower orders"),
the [hostility study](npc-ai-hostility.md) (ARC-RES-057: the attack event, the
follower rule, upset attacking, peace, the enemy list and the target lock), the
[morale study](npc-ai-morale.md) (ARC-RES-058), the [support study](npc-ai-support.md)
(ARC-RES-059: spell choice, SP16), the [execution study](npc-ai-execution.md)
(ARC-RES-066: perform, the goal machine, the non-combat perform) and the
[companion orders study](companion-orders-loyalty-evidence.md) (ARC-RES-064:
the broadcast, its keywords, lines and receivers).

## Method

Behaviour comes from the retail executable (`Arcanum.exe` 1.0.7.4, SHA-256
`9bd80511…78d52`, re-hashed by both readers), read statically with `llvm-objdump`
by a first reader and, independently, by a second reader that saw only the
questions ([method step 6](original-executable-check.md); see
[Second reading](#second-reading)). Object fields were named from the
executable's own field table (0x59BEA8); flag bits, object types and script
points come from the project's records of the executable's constants; goal
priorities were read from the executable's goal-type table (0x5B03D0). Order
types and message numbers of installed files are those ARC-RES-064 recorded
(OL3, OL12); no game text is reproduced. The rows and verdicts are in the
[ledger](original-verification-ledger.md#npc-ai-followers-in-combat-arc-res-067).
Everything below is read in the executable unless marked **(inference)**.

Conventions: "the PC" is the broadcasting PC; "a follower" is an entry of its
follower array (direct followers; waiting NPCs are not in it, OL16). "PC
leader" follows the leader chain to a PC (0x45DDA0), null for a PC itself.
NO_ATTACK is NPC flag 0x20000000, FIGHTING 0x1, DEMAINTAIN_SPELLS 0x1000000,
BACKING_OFF 0x10000000; the target lock is critter-flags-2 bit 0x200000; combat
mode is critter flag 0x40000. Setter rules are the numbered rules of the
[core loop](npc-ai-core-loop.md#5-danger-state-machine).

## Answer

1. **Orders are handled one follower at a time, each followed by one AI pass.**
   The broadcast handler walks a copy of the PC's follower array; for each
   follower it addresses (within 10 tiles, by name or `#` for all) it applies
   the order and then runs one full AI pass on that follower at once, whatever
   the order, also when the order did nothing. The 200 limit counts walk steps
   and is never reached in play (it needs 200 followers) ([section 1](#1-the-order-walk-and-the-immediate-ai-pass)).
2. **Attack** takes the object the interface currently highlights; outside the
   PC's combat mode a target in the PC's own party is refused. Otherwise the
   follower's NO_ATTACK is cleared and an *ordered* attack event runs, which
   only asks the follower rule: the follower locks onto the target, or, when it
   is upset by it (party, too good, same origin or faction), complains with a
   line and −5 reaction toward the PC unless the target is already in combat
   mode. No line acknowledges an attack order ([section 2](#2-attack)).
3. **Back off and stop** float an acknowledgement and run "stop attacking":
   peace with its current focus, its running goals of priority up to 3
   interrupted and combat mode left (all three only when it has a focus), then
   NO_ATTACK set and the target lock cleared. No movement
   ([section 3](#3-back-off-and-stop)).
4. **Curse** sends the follower away and makes it an enemy: an unforced leave
   (refused for a PC of extraordinary charisma or a mind-controlled follower),
   then an insult line and an attack event by the PC on the former follower,
   which the rest of the party joins and nearby witnesses hear
   ([section 4](#4-curse)).
5. **NO_ATTACK has one reader and leaks twice.** Only the setter reads it (a
   fight request becomes NONE). It is cleared when the local player's PC
   enters combat mode, by an attack order and when the world-map travel ends.
   It blocks only the danger state: a spell or item the same pass chose for the
   found target is still used (FO8, bug of the original), and it survives the
   NPC leaving the party, so a former follower that was told to stop never
   fights again (FO7, bug of the original) ([section 5](#5-the-no-attack-flag)).
6. **Followers join or complain on every attack event** (re-read of A3's rule):
   the attacker's direct followers first, then the victim's, each in array
   order ([section 6](#6-followers-join-or-complain)).
7. **After a fight** the setter clears FIGHTING and BACKING_OFF and, unless the
   NPC flees, sets DEMAINTAIN_SPELLS; the gate then ends the NPC's maintained
   spells once it has no leader, its leader is dead, or its leader has left
   combat mode. The non-combat perform leaves combat mode, which clears the
   target lock. The combat focus, who-hit-me-last and the enemy list are *not*
   reset; peace is the only operation that clears them. A lock that outlives a
   flight ending in NONE makes the NPC attack its scan's target without the
   fighting flags (FO14, bug of the original)
   ([section 7](#7-after-the-fight)).
8. **Two follower comment lines.** With the "combat taunts" setting on, half of
   the PC's critical hits on the aimed target (and of hits that strike another
   object, under an attack-record flag not traced) make a random follower float
   a comment toward the PC; a follower of a PC that falls from at least 20 % to
   below 20 % HP floats a near-death line toward its PC leader
   ([section 8](#8-follower-comment-lines)).

## 1. The order walk and the immediate AI pass

The handler (0x4C31A0) is the single entry of every order (presets, portrait
menu, typed text, OL1). For a PC speaker:

1. The PC's own text is floated over the PC (OL2).
2. The follower array (field 0xF1) is **copied** into a local buffer, so a
   follower that leaves during the walk (leave, curse) does not disturb it, and
   followers of followers are never reached.
3. **Walk.** Each outer step scans the copy onward from where the last step
   stopped for the next follower that is not null, is within 10 tiles of the PC
   (0x4B96F0) and matches: a text starting with `#` matches every follower in
   reach, one per step; otherwise the first follower whose known name (0x441B60)
   is a case-insensitive prefix of the text matches, and the walk ends after it.
4. **Per matched follower:** the order word (OL3) gives a type 0–13; the
   follower's spell flags are read (mind control is spell flag 0x8000000); the
   type's handler runs ([sections 2–4](#2-attack) for types 10, 12 and 7; OL4–OL9
   for the others); then **one AI pass** (0x4A84F0, call at 0x4C389D) on that
   follower. The pass runs for every matched follower: type 0 (unknown word),
   a mind-controlled follower whose leave, wait or curse was skipped, an attack
   order the party filter refused, types 8 and 9 (join, disband) and type 13
   (follow other, out of the handler's 13-entry table: a debug print, then the
   pass).
5. **The limit.** A counter of outer steps ends the walk above 200. Each step
   uses up at least one array entry, so it reaches at most the follower count
   plus one; reaching 200 needs 200 followers, so it is never reached in play. A second phase (other players by name, join and
   disband) runs when the last step found nobody (no name matched, a `#` walk
   whose last array entries were out of reach, or types 8 and 9); in single
   player it does nothing but a debug print, and has its own 200 limit.

What the immediate pass does is the ordinary pass of the
[core loop](npc-ai-core-loop.md#3-one-pass): it runs after the order changed the
follower's state and before the next follower's order. In a turn-based round it
ends at the gate's turn test (the PC holds the turn while it gives orders), so in
a round an order takes effect at the follower's own subturn; in real time it
lets an ordered attack start at once (the locked focus is fought by the danger
update and combat perform) and makes a stopped follower re-decide at once. The
gate's busy test (a running goal of priority 3 or more) also ends it, so a
follower in the middle of an attack keeps that attack until its run ends.

A speaker that is not a PC only follows the highlighted object on a "follow" or
"follow other" word (0x4346A0) and ignores everything else (companions thread).

## 2. Attack

Type 10 (keyword "attack"; F2 preset and the portrait menu's attack item, OL1,
OL14):

| # | Step | Effect |
| ---: | --- | --- |
| 1 | target | the object the interface currently highlights (0x43C570; the hover handle of [reaction R15](npc-reaction-reputation-evidence.md), the picked object after the menu's target cursor **(inference)**); none → nothing (the AI pass still runs) |
| 2 | party filter | when the PC is **not** in combat mode and the target's PC leader is the PC (one of the PC's followers or their followers, the follower itself included) → nothing. A PC target is not filtered (its PC leader is null) |
| 3 | allow attacking (0x4AA580) | the follower's NO_ATTACK is cleared |
| 4 | attack event (0x4A9650) | attacker the follower, victim the target, loudness 2, flags 4 (**ordered**) |

No acknowledgement line is floated and mind control is not tested. The ordered
attack event runs its usual early exits (a dead, nigh-invulnerable, hidden or
invulnerable victim, the decoy, a wall, attacker = victim; [hostility
7.1](npc-ai-hostility.md#71-attack-event)), then, because of flag 4, needs the
follower's direct leader and runs **only** the follower rule with (follower,
leader, target, attacker side, deliberate, ordered) and stops: no
who-hit-me-last, no enemy list, no reaction change, no tell-followers, no noise.
The target learns of the attack when the follower's first real swing raises an
ordinary attack event.

The follower rule ([section 6](#6-followers-join-or-complain)) then decides:

| Target | Result |
| --- | --- |
| the leader itself (the PC) | nothing |
| the follower itself, or the follower is dead | nothing |
| a critter that upsets the follower (PARTY: another of the PC's followers, possible only in combat mode; GOOD; ORIGIN; FACTION) | target not in combat mode: the "upset" line for that reason toward the PC and the follower's reaction to the PC −5; target in combat mode: nothing, silently |
| any other critter | target lock (0x4AFBD0): the setter is asked for COMBAT_FOCUS on it (FLEE or SURRENDER are left; the global "NPC fighting" switch off makes it NONE and no lock is set) and, when the state read then shows that focus, the lock bit is set |
| a non-critter (a door, a container) | target lock on it: the follower bashes it ([hostility section 2](npc-ai-hostility.md#2-target-validity): valid until busted) |

The lock keeps the focus against every other fight request until the target
becomes invalid, the follower is told to stop, or it leaves combat mode
([hostility 6](npc-ai-hostility.md#6-enemy-list-and-target-lock)). Because the
NO_ATTACK clear comes before the attack event, an attack order whose event then
does nothing (the PC as target, a dead target, an upsetting target) still lifts
an earlier stop (FO9).

## 3. Back off and stop

Type 12 (keywords "back off" and "stop", F5 preset, portrait menu, OL10): the
acknowledgement line (messages 2100–2199, OL12) and "stop attacking" (0x4AA4A0)
on the follower. No mind-control test, no movement.

**Stop attacking (0x4AA4A0).**

| Object | Effect, in order |
| --- | --- |
| an NPC | when its combat focus field is not null: peace with that focus ([section 7.1](#71-peace-and-its-two-helpers)), the interrupt of its running goals at priority 3 (0x424070: every run whose current goal has priority 0–3 ends, overlays are skipped, the walk stops at the first run of priority 4–6), and leaving combat mode (0x4B3F20). Then, when it has a PC leader, NO_ATTACK is set. Last, the target lock is cleared (always). |
| a PC | the same for every follower of the recursive follower list (0x441260: direct followers, then each one's followers) |

So stopping a follower that has no focus (it never fought, or a peace already
cleared it) does not interrupt it or take it out of combat mode; it only sets
NO_ATTACK and clears the lock. A fleeing or surrendered follower keeps its
flight: NO_ATTACK blocks only fight requests. A follower that calmed down after
an earlier fight still carries that fight's focus (section 7), so stopping it
makes peace with that stale focus (raising a PC head's reaction to 50 when it was
a PC's side) — harmless in practice, recorded with FO12.

Callers of stop attacking: the order (0x4C3884), script action 109 on its object
operand (call site 0x44A054 in 0x447220; stage A8) and the world-map travel window when it
opens with the PC visible: it hides the PC and its followers (0x45E1E0) and
stops attacking for the PC (call site 0x5616DA in 0x5615D0).

After the stop, the immediate pass of a real-time follower sees state NONE; the
danger update's scan may still find a target (a PC follower finds critters
fighting its leader through the GUARD row and ally help, [hostility
4.1](npc-ai-hostility.md#41-kill-on-sight)), and the fight request is turned into
NONE by setter rule 4 — but see FO8 for what the pass still does with that target.

## 4. Curse

Type 7 (keyword "curse"; no preset and no menu item, so only typed text reaches
it, OL3). The order sends the follower away as an enemy:

1. A mind-controlled follower: nothing (the AI pass still runs).
2. An **unforced** leave (0x45DFC0): refused, with nothing else happening, when
   the PC has extraordinary charisma (stat 7) or a controlling spell holds the
   follower; otherwise the follower stops waiting, is removed from the array
   and its leader is cleared (OL20's leave).
3. Only when it left: a race insult line (0x413D40) floated from it toward the
   PC (when the line callback is set), then the attack event with **the PC as
   attacker** and the former follower as victim, loudness 1, no flags
   (deliberate, with fan-out).

Through the attack event ([hostility 7.1](npc-ai-hostility.md#71-attack-event))
this means, in order: the PC's remaining followers are told (attacker side), so
each one that is not upset by the former follower treats it as its attacker
(reaction to an attack: fight or flee, [morale](npc-ai-morale.md)), and one that
is (same origin or faction, too good) complains with −5 toward the PC when the
former follower is not in combat mode; the former follower loses concealment and
its KOS override; its own followers are told (victim side); the noise fan-out
runs, so witnesses within the loudness-1 square who protect it may turn on the PC
([awareness section 6](npc-ai-awareness.md#6-noise-and-witnesses-s9)); it turns to
the PC, records the PC as who-hit-me-last, adds the PC's whole team to its enemy
list (its reaction to the PC then reads 0, R1), has its reaction to the PC
lowered to parameter 10 (it is no longer a follower, so the follower row of the
reaction table does not apply) and decides to fight or flee the PC. Its
immediate AI pass then acts on that in real time. A follower that was told to
stop earlier keeps NO_ATTACK after leaving and therefore never fights back (FO7).

## 5. The no-attack flag

| Event | Effect on NO_ATTACK |
| --- | --- |
| stop attacking on an NPC with a PC leader (the stop order; script action 109; the world-map travel opening, for every follower of the PC) | set |
| allow attacking on an NPC with a PC leader (0x4AA580) | cleared |
| the attack order, before its attack event | allow attacking on that follower |
| the local player's PC enters combat mode (toggle 0x4B3F80, call at 0x4B42B2, after the interface callback) | allow attacking for the PC: every follower of the recursive list |
| the world-map travel ends or its window closes with the party hidden (call sites in 0x5615D0 and 0x560F40) | the party is shown again, then allow attacking for the PC |
| the NPC leaves its leader (the leave's removal 0x45E040 and its unwait 0x4AA8C0) | unchanged |

**Reader.** The setter's rule 4 is the only code that reads the flag (both
readers searched every use of the bit): a COMBAT_FOCUS request becomes NONE,
with all of NONE's effects (an NPC that was fighting stops, gets
DEMAINTAIN_SPELLS and runs its exit-combat script). The lock rule 6 comes after
it, so a locked follower with NO_ATTACK is refused too. Nothing else (gate, scan,
spell choice, perform, the follower rule, leaving) reads it.

So a stop given **outside** combat mode lasts only until the PC next enters
combat mode (for example when the PC is attacked and the attack event puts it
into combat mode); a stop given **in** combat mode lasts until the PC leaves and
re-enters it, an attack order to that follower, or a world-map journey.

**FO8: only the danger state is blocked.** In every branch of the danger update
the spell and item choice (0x4ABC20) runs for the found target **before** the
setter is asked for COMBAT_FOCUS ([hostility section 1](npc-ai-hostility.md#1-the-danger-update)),
and the setter's refusal leaves the chosen action in the pass record. Perform
dispatches the action before the danger state ([execution section 1](npc-ai-execution.md#1-perform-and-its-actions)),
so a stopped follower (or any NPC while the global "NPC fighting" switch is off)
that scans a target still casts the chosen summon, defensive or offensive spell,
or uses the chosen item, on it, with the spell chance of
[support section 2](npc-ai-support.md#2-spell-and-item-choice-in-a-fight). It
does not start the attack goal or a grenade throw (those belong to combat
perform, which state NONE does not reach). Recorded as a bug of the original
(an ignored result; [anomalies](#9-anomalies-and-decisions)).

**FO7: the flag outlives the party.** Set and cleared only while the NPC has a
PC leader, the flag is not touched by a leave (the removal clears only the
forced bit, the unwait only the wait bit). An NPC that leaves while stopped —
dismissed, cursed, jilted by a hit or a loyalty refusal, its wait expired —
keeps NO_ATTACK: no engine path clears it (generic flag writers such as script
flag actions and the map patch were not traced) unless it joins a PC again and
that PC enters combat mode or orders it to attack. Until then it never takes
COMBAT_FOCUS: it does not fight back when attacked, which for a cursed follower
undoes the point of the curse. Recorded as a bug of the original (a stale flag).

## 6. Followers join or complain

Re-read of [hostility 7.2](npc-ai-hostility.md#72-followers-join-or-complain)
(HS15), with the details A7 needs. Both readers agree with A3's table.

**Tell followers (0x4A9B80; leader L, other T, side, accidental).** Nothing when
L is T. Otherwise each **direct** follower of L (0x4411C0, follower-array order;
not followers of followers, not waiting NPCs) gets the follower rule with
ordered = 0. The attack event calls it twice, unless its "no fan-out" flag is
set: first for the attacker's followers (side attacker, T the victim) right
after its early exits — before the victim-type test, so the PC's followers also
join the PC against a door or chest it bashes (as COMBAT_FOCUS on the object) —
and later for the victim's followers (side victim, T the attacker), after the PC
victim's combat-mode entry. It runs on every attack event, so every swing, hit
or miss, repeats it.

**Follower rule (0x4A9C00; F, L, T, side, accidental, ordered)**, first match:

| # | Case | Effect |
| ---: | --- | --- |
| 1 | L is T | nothing |
| 2 | T not a PC or NPC | accidental → nothing; ordered → target lock on T; else the setter is asked for COMBAT_FOCUS on T |
| 3 | F is T, or F is dead | nothing |
| 4 | u := upset attacking (F, T, L) is not NONE | victim side → nothing. Attacker side → nothing when T is in combat mode, the hit was accidental or the line callback is unset; otherwise, when F is active, the "upset" line for reason u (0x414370) floated from F toward L, then F's reaction to L changed by −5 (also when F is not active) |
| 5 | u is NONE | accidental → nothing; ordered → target lock on T; else F's reaction to an attack by T (0x4AA620: fight or flee, [hostility 7.4](npc-ai-hostility.md#74-reaction-to-an-attack)) |

The −5 goes through the reaction change rules (R2): it changes the follower's
reaction toward a PC leader and does nothing for an NPC leader. It depends on the
line callback being set, which the game always does (FO10). Upset's
reasons are A3's: PARTY (T's leader is L), GOOD (F's alignment above 0 and T's at
least parameter 12), ORIGIN, FACTION (faction-same, ARC-WORLD-044); a follower
mind-controlled by L is never upset.

So in a fight the PC starts, each follower in array order either joins (fight or
flee by its own morale) or, for a target that is its party, too good for it, or
of its origin or faction, complains once per attack event until the target is in
combat mode. Each complaint is −5 toward the PC.

**Other follower rules that apply in combat**, owned by earlier stages:

| Rule | Stage |
| --- | --- |
| followers of a leader with extraordinary charisma never flee or surrender (setter rule 3) | A1 |
| a follower farther than 30 tiles from its PC leader is moved to it (the far path of the AI event) | A1 |
| a target farther than 20 tiles from the follower's leader is invalid; a follower's own party is never a target | A3 |
| a PC's follower kills on sight only through the GUARD row; it helps allies by the scan | A3 |
| a deliberate hit by the PC on its own follower: reaction ± parameter 9 (reputation A2), the follow check, jilted leave or the "accidental attack" line | A3 |
| heal self, then leader, then the other followers | A5 |
| no spell or item while a healthy follower with a healthy leader faces a weaker enemy | A5 |
| a follower follows only in state NONE (non-combat perform) | A6 |
| a follower keeps its maintained spells while its living leader is in combat mode | A7 ([section 7](#7-after-the-fight)) |

## 7. After the fight

### 7.1 Peace and its two helpers

Peace (0x4AA300; NPC N, other X), re-read: head P := X's PC leader, else X. N's
focus and who-hit-me-last cleared; P removed from N's enemy list; a PC head: N's
reaction to it raised to 50 when below; the setter asked for NONE; then
**0x44E050 (N, P)**; then, when P is a PC or NPC, for each follower F of P's
recursive list: F's focus and who-hit-me-last cleared where they name N, N removed
from F's list, F removed from N's list, then **0x44E0E0 (F, N)**. Peace does not
clear the lock bit (stop attacking does, after it).

Both helpers take the object's **main run** — the first of its animation runs
whose bottom goal is not an overlay (0x423300; overlays are the goal types whose
descriptor marks them, OL18) — and test that run's bottom goal:

| Helper | Test | Effect |
| --- | --- | --- |
| 0x44E050 (N, P) | the bottom goal is attack (18) or attempt attack (19), and its target is P or a critter whose PC leader is P | the run is interrupted at priority 5 |
| 0x44E0E0 (F, N) | the bottom goal is 18 or 19 and its target is exactly N | the run is interrupted at priority 5 |

**Interrupt (0x44E2C0; run, priority p).** The run's current goal decides: an
overlay goal with p below 6 is left running (reported as handled); a goal of
priority 5 refuses; a goal of priority 2 or 3 ends when p is at least its
priority; any other ends when p is above its priority. Ending the run marks it,
removes its pending events and calls each stacked goal's end handler from the
top down. Goals 18 and 19 have priority 3, so with p = 5 an attack run ends; so
does any run whose current goal has priority 0–4 (an approach pushed on top of
the attack). The general interrupt 0x424070 (object, p) applies 0x44E2C0 to each
of the object's runs in turn and stops at the first that refuses.

### 7.2 What ends and what stays

| Item | When a fight ends by calm-down (danger update → setter NONE), a flight (FLEE) or a surrender | Source |
| --- | --- | --- |
| FIGHTING, BACKING_OFF | cleared by the setter's exit branch (rule 10) whenever the result is not COMBAT_FOCUS | setter 0x4AABE0 |
| DEMAINTAIN_SPELLS | set by the same branch when FIGHTING was set and the result is not FLEE; the exit-combat script (point 14) runs then too | setter |
| maintained spells | ended on a later pass by the gate's step 15 once the NPC has no leader, its leader is dead, or its leader is not in combat mode; then the flag is cleared and the magic domain's demaintain (0x457450) ends every spell the NPC maintains (its own exceptions are the magic slices', MT9a). While a living leader stays in combat mode the flag waits | gate 0x4A8570 |
| combat mode | left by the first non-combat perform (state NONE, [execution 5](npc-ai-execution.md#5-following)); a surrender leaves it in the setter (with CL4's stale bit) | 0x4AC380, 0x4B3F20 |
| target lock | cleared when combat mode is left (the toggle's leave branch, 0x4B411C); also by the danger update when the focus turns invalid and by stop attacking | toggle 0x4B3F80 |
| combat focus field | **kept** (the setter writes it only on COMBAT_FOCUS); only peace clears it | setter, peace |
| who-hit-me-last | **kept**; only peace clears it | peace |
| enemy list | **kept**; peace removes the other side, the pick purges null entries; a listed PC's reaction reads 0 until then | [hostility 6](npc-ai-hostility.md#6-enemy-list-and-target-lock) |
| NO_ATTACK | unchanged ([section 5](#5-the-no-attack-flag)) | — |
| CHECK_WIELD, CHECK_GRENADE set when the fight started | left to the gate's re-wield (CL2) and the grenade check | setter rule 7 |

Two consequences:

- **A flight never demaintains (FO13).** The exit branch skips DEMAINTAIN_SPELLS
  for FLEE, and a later NONE or SURRENDER no longer finds FIGHTING set, so an NPC
  that fled keeps its maintained spells after the flight, in surrender and after
  it, until the magic upkeep itself ends them. Kept as the original's behaviour.
- **Stale focus.** After a calm-down the focus field still names the last target.
  The state read ignores it (it tests FIGHTING), avenge reads a *dead* NPC's
  focus (the killer it last fought), and stop attacking makes peace with it
  (section 3). Kept.

**FO14, a locked NPC in state NONE.** A lock survives a FLEE request (rule 6
blocks only COMBAT_FOCUS) and a flight that ends straight in NONE (not through a
surrender, whose combat-mode exit unlocks). In state NONE the danger update's
scan result is then sent to the setter, which, locked, returns COMBAT_FOCUS
without writing anything (no FIGHTING, no focus, no script); the danger update
stores that returned state, so perform runs combat perform on the scan's target
while the NPC's flags still read NONE. When the scan finds nothing, the
non-combat perform leaves combat mode and the lock goes. Both readers read the
setter's locked return; the NONE composition is the first reader's. Recorded
as a bug of the original (stale state; [anomalies](#9-anomalies-and-decisions)).

## 8. Follower comment lines

The earlier reading's "witness" and "near-death" floats are two follower lines
from the generated NPC line files (by gender, OL12), floated by the AI line
callback (0x5F8488):

| Line | Trigger | Speaker → listener | Lines |
| --- | --- | --- | --- |
| combat taunt (0x4AE9E0, from 0x4B6B90) | at the end of every resolved attack (for a missile, when its flight hits): the "combat taunts" setting is on (0x4B82E0; never in a network game), the attacker is a PC, the struck object is set and is not in the PC's own party (its PC leader is not the attacker), and the attack hit (attack-record flag 0x2). Then: the struck object is the aimed one and the hit is critical (flag 0x4) → kind 0; the struck object is not the aimed one and record flag 0x8 is set (its writer was not traced) → kind 1. In the comment function: `random(1, 2)` must not be 1 (half of the cases), the line callback must be set, the PC must have followers; one follower is drawn with `random(0, n − 1)` (no draw for one follower) and must be active | the drawn follower → the PC | kind 0: 2400–2499; kind 1: 2500–2599 |
| near death (0x4AEAB0, from the damage step at 0x4B50F1) | a hit with HP damage above 0 on a critter that has a PC leader, whose HP ratio (0x4AB400) was at least 20 before the damage and is below 20 after | the hurt follower → its PC leader | 5500–5599 |

The line builders return no line when the speaker cannot speak (0x4AD800: for
example dead without the speak-with-dead spell flag 0x2000000, unconscious,
or busy). The near-death function tests neither the speaker's activity nor the
callback for null (a null call, latent: the game always sets it), and floats an
empty line for a follower killed straight through the 20 % mark (FO16). The taunt
function passes the drawn array entry to the activity test (0x45D790), which reads
its flags with no null test (FO17, latent: the leave's removal compacts the array,
so only a destroyed or unresolved handle leaves a null entry).

## 9. Anomalies and decisions

Verdicts and rows: [ledger](original-verification-ledger.md#npc-ai-followers-in-combat-arc-res-067).
Decisions follow D2 and D8; proposed switches stay proposed until the owner
decides. No first-pass anomaly (P1–P7) belongs to A7.

| ID | Original 1.0.7.4 | Verdict | Proposed decision |
| --- | --- | --- | --- |
| FO7 | NO_ATTACK is set and cleared only while the NPC has a PC leader and no leave path clears it, so an NPC that leaves while stopped (dismissed, cursed, jilted, wait expired) never takes COMBAT_FOCUS again unless it rejoins a PC that then enters combat mode or orders it to attack. | Original bug | **(inference)** The flag is a party order; outliving the party leaves a stale flag that silently disables the NPC's fighting, and makes a cursed or jilted follower unable to fight back. Steamwoven clears NO_ATTACK whenever an NPC leaves its leader; Original keeps it (`NoAttackSurvivesLeaving`, proposed). Visible: a follower that was told to stop and then dismissed, cursed or jilted fights normally afterwards. Lands with slice I9. |
| FO8 | The danger update chooses a spell or item for the found target before asking the setter for COMBAT_FOCUS and keeps that choice when setter rule 4 refuses (NO_ATTACK, or the global "NPC fighting" switch off); perform uses it, so a stopped follower still casts or uses items on enemies it scans. | Original bug | **(inference)** An ignored result: the refusal means "do not fight", and the action chosen for that fight is used anyway. Steamwoven drops the action the danger update chose for that target when rule 4 turns the request into NONE; Original keeps it (`NoAttackKeepsChosenAction`, proposed). Visible: followers told to stop no longer cast or use attack items; with the "NPC fighting" switch off NPCs no longer cast at targets (the switch itself is stage A8's). Lands with slices I3/I9. |
| FO9 | The attack order clears NO_ATTACK before its attack event, so an order whose event does nothing (the PC as target, a dead or hidden target, an upsetting target) still lifts an earlier stop. | Original | Kept in both settings: an attack order means "fight again"; no switch. |
| FO10 | The follower rule's −5 complaint is skipped when the line callback is unset, which never happens in the game (the rule tests the callback; no crash). | Original | Kept: Steamwoven always has the line output, so the −5 always comes with the complaint, in both settings. No switch. |
| FO13 | No DEMAINTAIN_SPELLS for a fight left by FLEE, nor for the later NONE or SURRENDER; maintained spells survive a flight. | Original | Kept in both settings: the FLEE exception is explicit and the upkeep ends spells by itself; no switch (a phase 2 patch may revisit). |
| FO14 | A locked NPC in state NONE (after a flight that ended in NONE) gets COMBAT_FOCUS back from the setter without any flag or focus write and attacks the scan's target by combat perform. | Original bug | **(inference)** The lock exists to keep an ordered follower on its target; here it outlives the fight and makes the NPC attack whatever its scan finds, through combat perform, with no FIGHTING flag, no focus write and no enter- or exit-combat script: stale state, as the A6/A7 precedent. Steamwoven clears the target lock whenever the setter applies a state other than COMBAT_FOCUS; Original keeps the lock (`LockedNoneStateFights`, proposed). Visible: a follower ordered to attack that flees and then calms down returns to normal targeting instead of attacking the nearest scanned critter without entering the fight. Lands with slices I3/I9. |
| FO16 | The near-death line function calls the line callback with no null test (as HS22), tests no activity, and floats an empty line for a killed follower. | Original bug (latent) | Both fixed: null call, latent; never reproduce a crash (owner rule). Steamwoven has no unset line output and shows nothing for an empty line. No switch. |
| FO17 | The taunt function passes the drawn follower-array entry to the activity test, which reads the object's flags with no null test; the leave's removal compacts the array, so only a destroyed or unresolved handle leaves a null entry. | Original bug (latent) | Both fixed: null object, latent; a null entry is skipped (no taunt). No switch. |

## 10. How A7 extends the order entry point (ARC-WORLD-046)

ARC-WORLD-046 ([slice table](companion-orders-loyalty-evidence.md#implementation-slices))
plans one session entry point for orders to one follower or all within 10 tiles,
returning each order's state change and line range to the host, and refusing or
passing attack, back off and curse to A7. Proposals for slice I9:

1. **One walk, one pass per follower.** The entry point keeps the walk of
   section 1 (array copy, 10-tile reach, name or `#`, array order) and, after
   each follower's order, requests one AI pass for that follower through the AI
   scheduler of slice I6 — for every addressed follower, also when the order
   was refused or unknown. The 200 limit is kept as a defensive bound.
2. **Three combat order results** join the out-of-combat ones:
   `Attack(target object)` (needs the host's highlighted or picked object; party
   filter outside the PC's combat mode; allow attacking; the ordered attack
   event; no line), `Stop` (acknowledgement range 2100–2199; stop attacking) and
   `Curse` (unforced leave; on success the race-insult line and the PC's attack
   event on the former follower). Each returns the follower rule's outcome
   (`Locked`, `Complained(reason)`, `Ignored`) or the refusal (`Refused:
   PartyTarget`, `Refused: NoTarget`, `Refused: LeaveRefused`) for the overlay.
3. **The AI owner holds the state.** Stop attacking, allow attacking, peace, the
   target lock, NO_ATTACK and DEMAINTAIN_SPELLS are session operations of the AI
   state (slice I3, one schema in both settings); the order entry point only
   calls them. The combat-mode toggle (ARC-COMBAT-004) calls allow attacking for
   the hero's party when the hero enters combat mode.
4. **Comment lines** are host outputs of the attack resolution and the damage
   step (combat taunts setting, the two draws labelled `TauntSpeak` and
   `TauntFollower`; near-death on the 20 % crossing).
5. **Switches** FO7, FO8 and FO14 are checked where the leave, the danger
   update and the setter run.

## Open questions handed forward

| # | Question (from) | Status |
| ---: | --- | --- |
| 1 | The follower fan-out: the join/complain rule, its −5 to the leader, which followers, order ([hostility](npc-ai-hostility.md), "stage A7 the follower fan-out") | **Closed** ([section 6](#6-followers-join-or-complain)): direct followers in array order, attacker side first; the −5 only on the attacker side for a target not in combat mode and a deliberate hit, through R2 |
| 2 | One AI pass per ordered follower after the order; follower orders 0x4C31A0, call at 0x4C389D; the 200 limit ([core loop entry points](npc-ai-core-loop.md#1-entry-points)) | **Closed** ([section 1](#1-the-order-walk-and-the-immediate-ai-pass)) |
| 3 | The animation helpers 0x44E050 and 0x44E0E0 ([execution open question 14](npc-ai-execution.md#open-questions-handed-forward), [hostility U3](npc-ai-hostility.md#unknowns)) | **Closed** ([section 7.1](#71-peace-and-its-two-helpers)) |
| 4 | DEMAINTAIN_SPELLS: set by the setter, consumed by the gate ([core loop](npc-ai-core-loop.md#3-one-pass)) | **Closed** ([section 7.2](#72-what-ends-and-what-stays)); what 0x457450 skips is the magic slices' (MT9a) |
| 5 | What else is reset after a fight: focus, enemy list, combat mode, target lock ([hostility](npc-ai-hostility.md#6-enemy-list-and-target-lock)) | **Closed** ([section 7.2](#72-what-ends-and-what-stays)) |
| 6 | The witness and near-death floats (plan S20) | **Closed** ([section 8](#8-follower-comment-lines)); the writer of attack-record flag 0x8 is handed to the ranged-combat slice (FR-COMBAT-007) with the missile flight (execution question 12) |
| 7 | What "curse" is in the order set (this stage's brief) | **Closed** ([section 4](#4-curse)) |
| 8 | Entering combat mode when the turn-based start fails: the toggle has already written the combat-mode bit and returns before the music, the art change and allow attacking (seen by the second reader in 0x4B3F80) | **Handed to** the combat-mode owner (ARC-COMBAT-004); when the start can fail was not read |
| 9 | The out-of-combat orders and the portrait menu | Out of scope: companions thread (ARC-RES-064, ARC-WORLD-046) |
| 10 | Script action 109 (stop attacking on its operand) and the global "NPC fighting" switch | **Handed to** stage A8 (S21) |

## Unknowns

- **U1** How often a follower complains or joins in play, and how often a stop
  given outside combat mode is lifted by the next combat entry, is a runtime
  question for slice I11.
- **U2** Which object the portrait menu's target cursor leaves highlighted when
  the attack order reads it was inferred, not traced through the interface.

## Second reading

A second reader that saw only the questions (function addresses and what to
determine) re-read the broadcast walk, the three combat orders, stop and allow
attacking with their callers, the ordered attack event, the follower rule, peace
and its helpers, the setter's exit branch, the gate's demaintain step, the
combat-mode toggle, the danger update's order of spell choice and setter, the
setter's locked return and the two comment functions. Both readers agree on every
behaviour above. The second reader added that NO_ATTACK survives a leave (FO7),
which the first reader then confirmed in the removal and unwait functions; the
script action number (109) and the world-map callers' show and hide steps; the
missing null test of the drawn follower in the taunt function (FO17); and the
turn-based start failure in the toggle (open question 8). The second reader also
flagged the enemy-list branch's spell-target focus, which is support's SP16 and is
not repeated. Details are in the
[ledger section](original-verification-ledger.md#npc-ai-followers-in-combat-arc-res-067).
