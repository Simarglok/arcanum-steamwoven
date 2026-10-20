# NPC AI action execution

Status: **Research**, work item ARC-RES-066 (2026-10-21), stage A6 of the
[NPC combat AI plan](npc-combat-ai-plan.md#research-stages-source-reading-one-document-each)
(source unit S19 and the animation goals behind it). It fixes how the action an
AI pass chose becomes movement, casting, item and skill use and attacks, in real
time and in a turn-based round: what perform starts for each action, how the
animation goals that carry it out run (approach, attack, flee, move away, run,
follow), what each costs in action points, when a flight stops, and how the
attack executor uses the line of fire. It closes companion open question
[O5](companion-follow-evidence.md#open-questions) and, together with stages
A1–A5, combat open question [O5](combat-core-evidence.md#open-questions) of
ARC-RES-030. It changes no code; the rules are inputs for slice I7 (melee and
ranged executor) and parts of I5 and I8 of the plan.

Earlier facts reused here, not re-derived: the [core loop](npc-ai-core-loop.md)
(ARC-RES-054: the pass record, the gate and pipeline, the turn-based subturn
hook), the [line of fire](npc-ai-awareness.md#5-line-of-fire-s8-combat-o4) and
its 26 threshold (ARC-RES-056), the [danger update and target
choice](npc-ai-hostility.md) (ARC-RES-057), the [flee, surrender and
back-off decisions](npc-ai-morale.md) (ARC-RES-058), the [heal, spell, item and
grenade choices](npc-ai-support.md) (ARC-RES-059), the turn-based action-point
rules of the [turn-based round](original-verification-ledger.md#turn-based-round-arc-combat-003)
(ARC-COMBAT-003, TB5–TB14), the attack animation, pause and repeat of the
[real-time combat](original-verification-ledger.md#real-time-combat-arc-combat-004)
(ARC-COMBAT-004, RT7–RT12) and the walk, run and step costs of the
[locomotion study](locomotion-evidence.md) (L5, L9, W6).

## Method

Behaviour comes from the retail executable (`Arcanum.exe` 1.0.7.4, SHA-256
`9bd80511…78d52`, re-hashed by both readers), read statically with `llvm-objdump`
by a first reader and, independently, by a second reader that saw only the
questions ([method step 6](original-executable-check.md)); the paths the
pull request's independent review corrected (EX6, EX8, EX12, EX20) were then
re-read by the first reader and by a fresh second reader. Object fields were
named from the executable's own field table (0x59BEA8); flag bits from the
executable's own flag-name tables (critter, NPC, spell, item and weapon flags);
animation goal names from the executable's own goal-name table (0x5A164C); goal
node tables through the executable's goal-type table (0x5B03D0). The rows and
verdicts are in the [ledger](original-verification-ledger.md#npc-ai-action-execution-arc-res-066).
Everything below is read in the executable unless marked **(inference)**.
Functions of other domains that these call (the magic run, the skill invocation,
the attack resolution, the path planners) are named with their address and only
their part in this flow is stated.

Conventions: "goal N" is the animation goal type N with the executable's own
name in brackets ([ag_flee] for 25). A **run** is one animated object's stack of
goals (at most eight levels); a goal is a list of nodes, each a test whose true or
false result names the next step. Distances are Chebyshev tile distances. "In a
round" means a turn-based round is active; "charged" means taken through the
action-point consume (0x4B7CD0), which acts only for the critter whose turn it
is and always passes outside a round (TB9). Parameter numbers are the slots of
the [dictionary](npc-ai-core-loop.md#4-parameter-dictionary).

## Answer

1. **Perform hands each action to an animation goal (S19).** After the turn test
   and an active back-off, perform runs the chosen action: a **cast** builds a
   magic invocation (from the item when one was chosen) and, when the invocation
   check passes, starts the magic run, which starts the casting goal; an **item**
   action starts the item-use goal (approach to one tile, then use); a **skill**
   action starts the skill goal (approach to one tile, then the skill). With no
   action it acts by the danger state: follow or routine, combat (back-off start,
   grenade, attack goal), flee (surrender and flee goal) or surrender
   ([section 1](#1-perform-and-its-actions)).
2. **Goals run node by node and always restart after a subgoal.** A node either
   names the next node, pushes a subgoal, pops back, restarts its goal or ends the
   whole run; a parent goal resumes at its first node when a subgoal returns, so
   its conditions are re-tested (only the parent's: a grandparent waits until the
   parent itself returns). In real time a goal that finds no path waits one second
   and then ends the whole run, so the next try is the next AI pass; in a round it
   ends at once, and a failed path ends the NPC's subturn
   ([section 2](#2-how-a-goal-runs)).
3. **An attack approaches to one tile, or shoots from where it stands.** The
   attack goal attacks at once when the target is within the weapon's range
   (at least 1) with a clear line of fire (cost below 26, nothing but the target
   blocking); otherwise it walks (or runs) toward the target until it is adjacent
   with a clear step. A ranged attacker re-tests its shot only when the target
   changes tile: against a target that stands still it walks to the adjacent tile
   (EX9). One attack ends an NPC's attack goal; the next attack is the next AI
   pass's decision, so in real time an NPC attacks at the AI cadence, and in a
   round it attacks again after each finished attack while its action points last
   ([section 3](#3-attack-and-approach)).
4. **A flight runs straight away to more than 9 tiles.** The flee goal runs (walks
   when encumbered at *significant* or worse) along the straight line from what it
   flees through the NPC to a tile 10 tiles from the source, re-planned every few
   steps, until the NPC is more than 9 tiles away or its first step back toward the
   source is blocked; an obstacle on the line ends the run where it stands. When no
   escape line can be planned the NPC turns to face the source and gets NO_FLEE for
   good ([section 4](#4-flight-and-moving-away)). This closes
   [morale U1](npc-ai-morale.md#unknowns).
5. **Costs in a round.** A walked step costs 2 action points and a run step 1; an
   attack its weapon cost at the attack's start; a cast, a skill and a non-magical
   item use 4; the end of a flight or a throw 2; a step blocked on the way 2; a
   follower standing in range 2 every 100 ms. Every run that ends through an end
   step (an aborted goal, but also every finished throw, item use and pickup)
   charges the round's pending cost once more (EX6). In real time
   none of these is charged; actions take the time of their animations
   ([section 6](#6-real-time-and-turn-based-compared)).
6. **Followers (companion O5).** A follower stops within 2 tiles of its leader (5
   when spread out) with a clear step, walks while within 4 (7) and runs beyond,
   follows only while it is out of danger, and in a round pays 2 action points on
   every 100 ms check while it stands in range ([section 5](#5-following)).
7. **Anomalies.** No first-pass anomaly (P1–P7) belongs to A6. Four things look
   unintended in the original: an end step charges the last checked cost again
   (EX6); the ranged approach's clear-shot stop is reached only when the attack has
   become impossible (EX9); the move-away goal that the spread-out order runs sets
   NO_FLEE for good when its escape line bends or fails (EX12); and the attack
   resolution decides "accidental" and notifies the aimed target before it replaces
   the struck object with a line-of-fire blocker (EX15). A blocked step ends a
   ranged approach's whole run but only the segment of a melee one (EX20, kept);
   dead branches with no effect are recorded too (EX13)
   ([anomalies](#9-anomalies-and-decisions)).

## 1. Perform and its actions

**Perform (0x4AC180)**, the last pipeline step, with the pass record (NPC at +0,
target at +8, danger state at +0x10, action type at +0x14, spell at +0x18, skill at
+0x1C, item at +0x20, sound request at +0x30):

1. in a round, nothing unless the NPC is the critter whose turn it is;
2. NPC flag BACKING_OFF → back off (0x4AC7B0, [morale](npc-ai-morale.md#4-back-off));
3. action 1 → cast, 2 → use item, 3 → use skill (below);
4. otherwise by danger state: NONE → non-combat perform (0x4AC380), COMBAT_FOCUS →
   combat perform (0x4AC6E0), FLEE → perform fleeing (0x4AC620), SURRENDER →
   perform surrender (0x4AC660).

**Cast (0x4AC250).** A magic invocation (0x455A20) is built with the chosen item as
its source when there is one, otherwise the NPC, and the chosen spell; its target
is the pass target. When the invocation check (0x4564E0, the magic domain's MT7)
refuses, nothing happens and the item is kept. Otherwise the invocation runs
(0x455AC0, in single player 0x455C30), which starts the casting goal (goal 26
[ag_throw_spell], or goal 82 [ag_throw_spell_w_cast_anim] for an invocation the run
marks; [section 2](#casting-goal)); then, when an item was the source, a magic call
on the item (0x4574D0, not read here) and an item of type food (9) or scroll (10)
is destroyed. The cast itself starts no approach.

**Use item (0x4AC320)** calls the item-use starter (0x435450) with the NPC, the pass
target, the item and no flags. **Use skill (0x4AC350)** calls the skill starter
(0x4350F0) with the NPC, the target, the item and the skill (skill 14, pick locks,
goes to its own starter, 0x436220).

**Starters**, the functions that create a goal run. Each first asks the **start
gate** (0x4348E0) with a cost: the object must be active; outside a round it
passes; in a round only the critter whose turn it is passes, a PC always, an NPC
when the cost is at most its action points left or its current fatigue is above
35 % of maximum ([combat core C11](combat-core-evidence.md#action-points-fr-combat-003)).

| Starter (address) | Goal | Gate cost | Notes |
| --- | --- | ---: | --- |
| item use (0x435450) | 39 [ag_use_item_on_object] | 4 | target and item stored; run flag 0x4000 (the approach may end beside an occupied tile) |
| skill (0x4350F0) | 33 [ag_use_skill_on] | 4 | target, item, skill stored; run flag 0x4000 |
| attack (0x434B00) | 18 [ag_attack] | the weapon's attack cost (0x4B7C30) | [section 3](#3-attack-and-approach) |
| attack, no sound (0x434AE0) | through 0x434B00 | as above | used by a PC victim's attack back (0x4A9AD0) |
| flee (0x434980) | 25 [ag_flee] | 0 | the source stored; then run flag 0x400 (0x436CB0) |
| back off from (0x434A30) | 85 [ag_back_off_from] | 0 | no caller and no pointer in the executable: dead (EX13) |
| throw (0x434F80) | 37 [ag_throw_item] | 0 | the item and the target's tile stored |
| follow (0x4346A0) | 24 [ag_follow] | 0 | [section 5](#5-following) |

The item-use, skill and attack starters turn running on for an NPC whose current
fatigue is above 8 (for the PC only with always-run); running is still refused at
encumbrance *significant* or worse when a step begins ([locomotion L5](locomotion-evidence.md#walk-and-run-s-o-d)).

**The item-use goal (39)** sets its range to 1 and, when the NPC is not within 1
tile of the target with a clear step, pushes the move-near goal (8); then it uses
the item (0x428CD0 → 0x462CC0). So an NPC that uses a magick item on an enemy
walks up to it first. **The skill goal (33)** approaches the same way (range 1, by
path), then starts the pick-locks, pick-pocket or generic skill attempt (goals 67,
86, 34); the attempt charges 4 action points and runs the skill invocation
(0x42A2A0 → 0x4C7160, the skills domain) when the target is neither destroyed nor
off. A heal-skill action of the [heal ladder](npc-ai-support.md#1-the-heal-ladder)
therefore walks to the patient.

**Immediate item use.** An AI-action item chosen by the list chooser
([support section 3](npc-ai-support.md#3-the-chooser)) is used at once through
the item use (0x462CC0) without any goal or approach. In a round it charges 4
action points when the item's use script stops the use, and otherwise only for an
item with an empty mana store and no magical flag; a magical item runs its magic
at once without a charge in this function. Food and scrolls are destroyed after
use. This closes [support U2](npc-ai-support.md#unknowns).

## 2. How a goal runs

**The stepper (0x423560)** runs the current goal's nodes. Each node is a function
with two parameter slots and a literal (the slots name goal data: 0 the animated
object, 1 its target object, and so on); its true or false result selects a step
and a delay:

| Step | Effect |
| --- | --- |
| a node number | go on at that node |
| restart | back to the goal's first node; the step ends |
| push N | goal N is pushed on top, with a copy of the parent's data (range, target, sound); at most eight levels, an overflow ends the run |
| pop | back to the parent, which restarts at its first node in the same step; popping the bottom goal ends the run |
| pop two | as pop, twice |
| push and pop together | the goal replaces itself with goal N |
| end | the whole run ends: the end nodes of every goal run (the standing pose is restored); in a round, an object other than the local PC is charged the round's pending cost (EX6). Some goals end this way on success too: the throw (37), the item use (39) and the attempt pickup (16) |

Delays: 0 continues in the same step; a positive delay is in milliseconds; "pace"
waits the run's frame time (1000 / frame rate, the attack's own pace during an
attack, RT8); a random 0..300 ms delay is used when a step is blocked by another
walker. At most 100 nodes run in one step; the 101st ends the owner's subturn and
kills the run. A node that kills its run (a step it cannot pay, a failed step check)
ends it at once, without the node's transition and without the end step's charge. When a run ends in a round, the subturn hook runs again for a PC or
NPC owner ([core loop section 1](npc-ai-core-loop.md#1-entry-points)), so the
NPC's next AI pass follows each finished action.

Because a parent restarts after every subgoal, the goal that pushed a walk re-tests
its own conditions after each path segment: the combat approach (79) tests "within 1
tile" and re-plans, the move-away goal (52) tests "within range" and re-plans. The
goals above it are re-tested only when that goal itself returns, so the attack
goal's tests (target alive, attack possible, reach) run again only when the approach
pops (adjacent) or the ranged walk pops two (below). A path segment is at most 8
steps (the path builder 0x41F3C0 caps its result), so long approaches and flights
re-plan at least every 8 tiles.

**No path.** The approach goals end their attempt when the planner finds no path;
then a common node (0x4246D0, "no round running") pushes the pause goal (6
[ag_move_to_pause]) after a 1000 ms delay in real time, whose only reachable node
ends the whole run on either result, so there is no retry inside the run and the
next try is the next AI pass; in a round the node ends the run at once. In a round a
planner that fails for a non-PC also ends that critter's subturn (0x4B7C90).

**Blocked step.** The walk goals (5 [ag_attempt_move], 61 [ag_attempt_move_near],
80 [ag_attempt_move_near_combat]) test each next step: when a living critter stands
on it near the path's end, or a newly spotted trap lies on it (0x4270B0 → 0x427110;
then a random 0..300 ms wait), or the step cannot start (0x42E9B0), their node 10
charges 2 action points (0x433270) and then the plain walk (5) and the ranged
approach (80) end the whole run, while the melee approach (61) pops back to its
parent, which re-plans (EX20). A step the NPC cannot pay is refused by the step check (0x4B7790): the run is
killed without an end step, its action points unchanged, and the unpaid cost stays
pending (EX6). Only the consume inside 0x430FC0, reached after the check passed,
zeroes the action points and ends the turn (TB9).

<a id="casting-goal"></a>**The casting goal (26 or 82)** gets the caster up and
unconcealed, charges 4 action points (0x429B50; when they cannot be paid the goal
ends) and plays the cast through its attempt goal (27 or 83), whose release frame
starts the spell's BEGIN ([magic core](magic-core-evidence.md)). It never moves the
caster: its approach node (range 8, then move-near) has no transition leading to it
(EX13).

## 3. Attack and approach

**Combat perform (0x4AC6E0)** ([morale](npc-ai-morale.md#4-back-off),
[support](npc-ai-support.md#7-grenades)): back-off start, else a grenade, else
the attack starter with the NPC, the pass target and the pass record's sound
request.

**Attack starter (0x434B00).** An attack on oneself is refused. A wielded weapon
with the "default throws" flag that can be unwielded is taken off and thrown at the
target's tile (the throw goal) instead. Otherwise, after the gate: the attacker in
Body of Air without the elemental flag, an equivalent goal already queued, an
attack run already going (it is updated instead, **(inference)** retargeted), an
interrupt of lower-priority animations (priority 3) that fails, or a boomerang in
flight refuse; on most refusals the critters sharing the attacker's tile are asked
to step apart (0x4364D0). The goal stores the target and, in its sound slot, the
third argument: combat perform passes the alerted sound the danger setter returned
([core loop rule 7](npc-ai-core-loop.md#5-danger-state-machine)), which the
approach plays once when it starts moving (below); the PC's attack back passes
none.

**Attack goal (18):**

| # | Test | Next |
| ---: | --- | --- |
| 1 | the target exists and is not a dead critter | else the run ends |
| 2 | the attack is possible (wrapper 0x4294A0 around the test 0x4294F0): attacker active and not in Body of Air (unless elemental), target neither destroyed, off nor dead, and when the [line of fire](npc-ai-awareness.md#5-line-of-fire-s8-combat-o4) reports a blocker other than the target, a path to the target exists (0x426500) | else, in a round, the attacker's subturn ends (when it is its turn; 0x4294A0 → 0x4B7C90), and the run ends |
| 3, 4 | knocked down → get up (goal 63); concealed (non-PC) → unconceal (goal 54) | |
| 5 | **reach** (0x429960): distance ≤ the wielded weapon's range (1 without a weapon or below 1), line-of-fire cost below 26 and no blocker but the target | the goal becomes the attempt-attack goal (19) |
| 6 | range := 1 | |
| 7 | within 1 tile with the first step toward the target clear (0x425130) | else push the combat approach (79) |
| 8 | adjacent but out of reach (for example a blocked line) | real time: a one-second pause, then the run ends (the next AI pass decides again); round: the run ends |

**Combat approach (79):** done when within 1 tile with a clear step; gets up and
unconceals; plans a path to the target (0x4280D0; no path: real-time pause or end
in a round, as above); plays the goal's sound once (the alerted sound) and clears
it; then pushes the ranged approach (80) when the weapon's range is above 1, else
the plain approach (61). Both walk the path step by step (doors opened, windows
jumped, [locomotion](locomotion-evidence.md)).

- **Melee (61)** stops early only when the target leaves the tile the path was
  planned to; it then pops and the combat approach (79) re-plans; a blocked step
  also pops back to 79 (EX20).
- **Ranged (80)** asks the callback 0x425590 at each step: when the target changed
  tile it stops (back to the attack goal, which tests reach with the full weapon
  range again); otherwise, when the attack is possible (test 2 above) it **keeps
  walking**; only when the attack has become impossible while the line is clear
  (cost below 26, no foreign blocker) and a weapon is wielded does it write the
  weapon range into the goals' range and stop. So a ranged attacker whose first
  reach test failed walks to the adjacent tile of a target that stands still, and
  re-tests its shot each time the target moves (EX9). A blocked step on the way
  ends the whole attack run (EX20).

**Attempt attack (19)** turns to the target, re-tests reach, starts the attack
animation (0x432700: the weapon's attack cost is charged, the run is killed when it
cannot be paid; pause and battle sound, RT8), plays it frame by frame (RT9) and, at
the action frame, tests reach again: in reach → the hit step (0x42A630 → 0x4B3BB0
→ the attack resolution 0x4B3170, with the aim stored in the goal); out of reach
(the target stepped away or the line closed) → no hit, the action points are spent.
An NPC's hit takes the same hit step and resolution as the PC's: nothing from the
hit step through the resolution and its melee and ranged branches (0x42A630,
0x4B3BB0, 0x4B3170..0x4B3BB0) tests whether the attacker is a PC. What a landed hit
does to items (wear of the armour at the hit location and of the weapon) is the
combat wear owner's one session step (ARC-COMBAT-007); slice I7 calls that step and
has no wear rule of its own. Whether the wear inside the damage step (0x4B4390)
treats PC and NPC attackers alike is that owner's reading, not repeated here.
Last, the repeat test (0x429760, RT7) lets only a PC in combat mode with auto-attack,
outside a round, replace the goal with a new attack after 5 ms. **An NPC's attack
goal ends after one attack.** Its next attack is the next AI pass: in real time the
next AI event ([cadence](npc-ai-core-loop.md#2-cadence), 250 ms next to the PC); in
a round the subturn hook right after the run ends, while action points remain.

**How the attack uses the line of fire (S8)**, in one place:

| Where | Use |
| --- | --- |
| attack goal test 2 (0x4294A0 → 0x4294F0) | a blocker other than the target needs a path; the cost is ignored |
| reach (0x429960), attack goal and attempt attack | cost below 26 and no blocker but the target, within the weapon's range |
| ranged approach callback (0x425590) | stops the approach only as described above (EX9) |
| attack resolution (0x4B3170), ranged branch | a blocker becomes the struck object (EX15) |
| grenade (0x4AC910) | any blocker cancels the throw ([support section 7](npc-ai-support.md#7-grenades)) |

**Grenades** are thrown with the throw goal (37), which starts with no approach and
charges 2 action points at its end (0x433270); the hit is the thrown-item
resolution (0x4B3C00). This closes the plan's question on 0x434B00: it is the attack
starter, which turns into a throw only for a default-throws weapon; the AI's grenade
throw is 0x4AC910's own call of the throw starter.

## 4. Flight and moving away

**Perform fleeing** asks the setter for SURRENDER and starts the flee goal away from
the source ([morale section 2](npc-ai-morale.md#2-flight-and-surrender)); a
surrendered NPC attacked by someone else starts the same goal from the attacked
reaction (0x4AA620). The starter (0x434980) gives the run flag 0x400, which every
later path request of the flight carries.

**Flee goal (25):**

1. running is turned on unless the NPC is encumbered at *significant* or worse
   (encumbrance level 4 or more);
2. range := 9;
3. when the NPC is within 9 tiles of the source **and** its first step from its tile
   toward the source is clear (0x425130 with 0x425840: the source's own tile is not
   blocking for this walker and the step is not blocked by anything but the source)
   → push the move-away goal (52); when that returns, the flee goal restarts here;
4. otherwise 2 action points are charged (0x433270, any object but the local PC)
   and the run ends.

So the **flight stops** when the NPC is 10 or more tiles from the source, or when
the way back toward the source is blocked next to it **(inference: the NPC counts
as out of reach)**, or when the walk is stopped (below). The surrendered NPC then
stands ([morale section 3](npc-ai-morale.md#3-a-surrendered-npc)).

**Move away (52)**, with the flight's range 9 and source (node numbers are the
executable's):

- nodes 1, 2: knocked down → get up; concealed (non-PC) → unconceal;
- node 3: within range with a clear step toward the source (as above), else back to
  the flee goal; node 4 is never reached;
- node 5, **escape path** (0x426A80): the direction from the source's tile to the NPC's
   (one of eight); the destination is the tile 10 (range + 1) steps from the source
   in that direction, so on the line through the NPC, beyond it. The request's flags
   include, for a run with flag 0x400, the bits that make the straight walk accept
   every step without any tile, object or trap test and forbid the search fallback
   (with 6 more for a polymorphed NPC, and the shared set-up's portal, window,
   prowling and fire bits, 0x425BF0). So the plan is the straight line, which fails
   only on length (more than 32 tiles or the step budget); the walk itself (goal 5
   [ag_attempt_move]) meets the obstacles: with flag 0x400 it tests the next tile
   for blocking, and a blocked step charges 2 action points and ends the run;
- nodes 6, 7: no escape path (also when the source is destroyed or off, or the NPC
  is entangled) → in a round a non-PC's subturn ends; the NPC turns to face the
  source (node 6, 0x42C240; not when stoned, stunned or paralysed, then the run just
  ends) and **NO_FLEE is set for good** (node 7, 0x4293D0 → set no-flee 0x4AF1D0);
  the run ends.

**NO_FLEE in the escape-path node.** Node 5 also sets NO_FLEE when a path was found
after the straight line stopped short after at least one step (the planner's
partial-step count is not zero; 0x426D9A). With the flight's flag 0x400 the straight
line never stops short, so a flight never takes this branch. A move-away run without
that flag does exist in single player: the spread-out order (below) runs the same
goal away from the leader, and there both node 5's branch and node 7 set NO_FLEE
(EX12). A concealed NPC without prowling training gets the planner's "no straight
line" bit as well, which with "no search" makes every escape fail **(inference:
then it turns and gets NO_FLEE)**; the unconceal step before normally removes the
concealment first.

**Spread out.** The spread-out order (0x45F600, from the dialog action `SO` and the
follower broadcast; the order itself belongs to the companions thread, ARC-WORLD-046)
sets the NPC flag "spread out" and starts goal 69 [ag_attempt_spread_out] toward the
leader (0x436960: gate cost 0, no flag 0x400). Its node 1 sets the range to 5 (2
without the flag); node 2 pushes the move-away goal (52) away from the leader while
the follower is closer than 3 tiles (1 without the flag); otherwise it follows like
the follow goal ([section 5](#5-following)), with the same 2 action points every
100 ms in range. Goal 52 inherits range 5, so the follower aims for the tile 6 steps
from the leader through itself; its path request has neither the "accept every
step" nor the "no search" bits, so the straight line may stop short and a search
finish the path, which sets NO_FLEE (node 5), and a failed path makes it face the
leader and set NO_FLEE (node 7). A follower spread out in a cramped place thus never
flees again: every later FLEE request becomes COMBAT_FOCUS (setter rule 2) (EX12).

This answers [morale U1](npc-ai-morale.md#unknowns): the flee goal runs the move-away
goal as long as the source is within 9 tiles; in a flight node 5's NO_FLEE branch is
never taken and node 7 is reached when no escape line can be planned; in a spread-out
run both set NO_FLEE. So the live run-time ways NO_FLEE is set are mind control, a
flight's node 7 and the spread-out move-away
([morale MR12, MR13](original-verification-ledger.md#npc-ai-morale-arc-res-058)).

**Back off.** The AI back-off runs to its tile with the run-to-tile goal (4
[ag_run_to_tile]: running unless encumbered, a path, the same no-path rule)
([morale section 4](npc-ai-morale.md#4-back-off)). Goal 85, a copy of the flee goal,
has no live starter (EX13).

## 5. Following

Closes [companion O5](companion-follow-evidence.md#open-questions).

**Non-combat perform (0x4AC380)** first takes the NPC out of combat mode, then, for
an NPC with a leader and without "wait here", asks for the follow goal (0x4346A0) and
runs the loyalty check of [companion C13](companion-follow-evidence.md#findings);
an NPC without a leader runs its routine (S22). A follower therefore follows only in
danger state NONE; while fighting, fleeing or surrendered it acts by that state.

**Follow starter (0x4346A0):** only for an NPC; no new goal while the leader is
within 4 tiles (7 with "spread out"); an existing follow, walk-near or run-near run
is kept; otherwise the follower's lower-priority animations are interrupted
(priority 3) and goal 24 starts, without running.

**Follow goal (24):** range := 2 (5 spread out); when within range with the first
step toward the leader clear → 2 action points are charged and the goal re-checks
after 100 ms; otherwise, within range + 2 (4, 7) → it becomes the move-near goal (8,
walking), beyond → the run-near goal (56, running unless encumbered). Both stop
within the range with a clear step and handle a missing path as above.

| Question of O5 | Answer |
| --- | --- |
| stopping distance | within 2 tiles of the leader (5 spread out), with the first step toward it clear |
| walk or run | walk while within 4 (7), run beyond, both re-tested after each segment |
| speed recalculation | joining marks the follower's running animation to recompute its frame time on its next step (0x436FA0 → run flag 0x80); no stat changes ([companion C5](companion-follow-evidence.md#findings)) |
| in combat | the follow goal starts only from non-combat perform, so a fighting follower does not follow |
| turn-based | the in-range check charges 2 action points every 100 ms; a following NPC standing in range spends its whole subturn this way until it cannot pay, which ends its subturn (TB9) |
| spread out | the spread-out order's goal (69) follows the same way with range 5 and moves away from a leader closer than 3 tiles, which can set NO_FLEE ([section 4](#4-flight-and-moving-away), EX12) |

## 6. Real time and turn-based compared

| Action | Real time | Turn-based round (action points) |
| --- | --- | --- |
| start of any goal | gate passes | NPC: cost ≤ action points left, or fatigue above 35 % |
| walked / run step | step animation time | 2 / 1 (run needs fatigue above 0, L9); checked before each step; a step it cannot pay is refused by the step check: the run is killed without an end step, action points unchanged, the unpaid cost stays pending (EX6) |
| blocked step (walk goals 5, 61, 80) | random 0..300 ms wait, then the walk gives up | 2 (node 10); then the run ends (5, 80) or the melee approach re-plans (61) |
| attack | animation and pause (RT8); NPC's next attack at its next AI event | weapon cost at the attack's start; next attack from the subturn hook |
| cast | casting animation | 4 at the casting goal's start |
| skill | approach, then the skill animation | 4 at the skill action |
| item through the item goal | approach, then use | 4 in the use (non-magical items), then the end step's charge (EX6) |
| immediate item use | instant | 4 (non-magical items), no goal |
| throw | throw animation | 2 at the throw goal's end, then the end step's charge (EX6) |
| flight | until beyond 9 tiles | steps, then 2 at its end; the end node failing to pay ends the run |
| follower in range | re-check every 100 ms | 2 per re-check |
| any end step (aborted goals; finished throw, item use, pickup) | nothing | the round's pending cost, charged again (EX6) |
| no path | one-second pause, then the run ends; the next AI pass decides again | the run ends; a non-PC's subturn ends |

The round's **pending cost** is a global the action-point check (0x4B7790) writes
for the critter whose turn it is, before it compares: 0 at its subturn start
(0x4B75D0), the next step's cost before each turn-based or path step (call sites
0x42EB01 in 0x42E9B0 and 0x430E73 in 0x4305D0), and the interface's cost previews
for the PC. When that check passes the step pays its own cost (0x430FC0), so a later
end step repeats a paid cost; when the check fails the run is killed without an end
step and the pending value is the unpaid cost, which the next end step in the same
subturn charges, emptying the action points and ending the subturn (EX6).

In both modes an NPC does one action per AI pass; in a round the pass repeats after
each finished run (the subturn hook) and the subturn ends when a pass starts nothing
([core loop section 6](npc-ai-core-loop.md#6-real-time-and-turn-based-compared)).

## 7. Combat O5: how the AI chooses attacks, flees or ends its subturn

[Combat O5](combat-core-evidence.md#open-questions) is answered by the stages
together:

- **when a pass happens and when the subturn ends**: the AI event and the subturn
  hook; a pass that starts no animation sets the action points to 0
  ([core loop](npc-ai-core-loop.md#1-entry-points));
- **whom it fights**: the danger update's target order and the decision tables
  ([hostility](npc-ai-hostility.md));
- **whether it flees, surrenders or backs off**: should-flee, the flight to
  surrender step and the back-off rules ([morale](npc-ai-morale.md));
- **whether it heals, casts, uses an item or throws a grenade instead of attacking**:
  the heal ladder, the spell lists, the chooser and the grenade test
  ([support](npc-ai-support.md));
- **how the chosen action is carried out and what it costs**: this note.

## 8. Other answers

**The talk goal's distance (0x42AE9F).** The call is in the talk goal's (21) node
0x42AE10: talking starts when the distance to the partner is **strictly below**
the dialog distance (0x4AFBB0: 5 for a PC talker, 10 for anyone else) and the
partner is neither destroyed nor off; a talker farther away walks until it is within
1 tile with a clear step (move-near), then tests again. This refines
[hostility section 6](npc-ai-hostility.md#6-enemy-list-and-target-lock) ("5
tiles"): a PC must be within 4.

**Combat flag 0x800 (awareness AU5).** The executable sets the "accidental" bit
0x800 of a combat record in the missile's flight (0x4B2870): when a missile meets an
object other than its target and its launcher (a blocker on its way, or a living
object picked one time in three on a tile it crosses), it marks its combat flags
with 0x800 and builds a record against that object, whose attack notification
(0x4B2690) then carries the accidental bit: no noise, a −10 reaction instead of the
hostile one, no fight against a faction-same attacker
([hostility section 7.1](npc-ai-hostility.md#71-attack-event)). The missile's own
flags also get it when the missile ends on its tile (an instruction at 0x4B2A88
inside the missile flight 0x4B2870). The attack
resolution has a second test (0x4B3170) that sets the bit when the struck object
differs from the aimed one, but it runs before the only change of the struck object
(the line-of-fire retarget of the ranged branch), so it is never true (EX15).

**Tie order of the global AI spell lists** ([support U1](npc-ai-support.md#unknowns)).
The lists are sorted by the runtime's quicksort (0x58A548: middle element as pivot,
a selection sort for parts of eight or fewer that moves the first maximum to the
end), with a comparator (0x4CC2D0) that orders by value, highest first. The input
is spells 0..222 in number order. The sort is not stable: spells of equal value come
out in an order the algorithm fixes (inside a short part, equal values end up in
reverse number order). Slice I8 reproduces the same algorithm on the same input, or
records the resulting lists from the installed records.

## 9. Anomalies and decisions

Verdicts and rows: [ledger](original-verification-ledger.md#npc-ai-action-execution-arc-res-066).
Decisions follow D2 and D8: a bug of the original gets a Steamwoven fix and a
proposed Original switch; where only the reconstruction differs, both settings
follow the original. The switches below are proposals awaiting the owner.

| ID | Original 1.0.7.4 | Verdict | Proposed decision |
| --- | --- | --- | --- |
| EX6 | When a goal run ends through an end step in a round, an NPC is charged the round's pending cost, which the last action-point check set: 0 at its subturn start, otherwise the next step's cost. The end step is the failure exit of many goals and also the normal end of the throw (37), the item use (39) and the attempt pickup (16). After a passed check that step was paid, so an NPC whose run walked and then ends this way (a grenade throw, an item or skill use after an approach, an aborted attack or flight) pays 1 or 2 action points again; after a failed check (the run was killed then) the value is the unpaid step, and the next end step in that subturn empties the action points and ends the subturn. | Original bug | **(inference)** A stale value charged at an unrelated moment: a double charge, as the combat-core A6/A7 precedent. Steamwoven charges nothing at the end step (each action pays its own cost, and an unaffordable step is already refused by its own check); Original keeps the charge (`EndStepRechargesPendingCost`, proposed). Visible: in a round, NPCs keep 1–2 more action points after throws, item uses and aborted actions that followed a walk. Lands with slice I7. |
| EX9 | The ranged approach's callback stops at a clear shot only when the attack-possible test fails; when the attack is possible it keeps walking, so against a target that stands still a ranged attacker walks to the adjacent tile and shoots from there; it re-tests its shot only when the target changes tile. | Original bug | **(inference)** Both readers read the test as inverted: the combat approach differs from the plain one only by this stop, which as written fires almost never. Steamwoven stops the approach at the first tile where the target is within the weapon's range with a clear line (cost below 26, no foreign blocker), which also needs the range test the callback lacks (without it a clear but too long line would stop the approach forever); Original keeps walking (`RangedApproachSkipsClearShot`, proposed). Visible: archers and gunmen, NPCs and the PC, shoot from range instead of closing in. Lands with slice I7; plan phase 2 M3 (keeping range) builds on it. |
| EX15 | The attack resolution decides the accidental bit and sends the attack notification to the aimed target before its ranged branch replaces the struck object with a line-of-fire blocker. For a ranged weapon without a missile object (no missile art) the blocker is struck while the aimed target is told it was attacked and the blocker is not; the resolution's own accidental test is never true. A missile object instead notifies whatever it strikes, marked accidental. | Original bug | **(inference)** The resolution's comparison of struck and aimed object can only matter after the retarget, and the missile path does exactly that. Steamwoven notifies the struck object, marked accidental when it is not the aimed target, as the missile path does; Original keeps the notification of the aimed target (`RangedBlockerHitNotifiesAimedTarget`, proposed). Visible: a shot that hits a bystander makes the bystander react (accidentally) and no longer turns the aimed target hostile. The damage step's own reactions were not traced. Lands with slice I7 and the ranged-combat slice (FR-COMBAT-007). |
| EX12 | The spread-out order's goal (69) pushes the move-away goal (52) away from the leader without the flight's flag 0x400: its escape path may bend after a partial straight line, which sets NO_FLEE (node 5, 0x426D9A), and a failed path makes the follower face the leader and set NO_FLEE (node 7). A follower spread out in a cramped place never flees again. In a flight node 5's branch is never taken. | Original bug | **(inference)** The move-away goal's NO_FLEE nodes are flight logic ("cornered, turn and fight"); reused for spreading out they mark a follower that was never in danger. Steamwoven sets no NO_FLEE from a move-away run that the spread-out goal started (nodes 5 and 7); flights keep node 7 as before; Original keeps it (`SpreadOutSetsNoFlee`, proposed). Visible: spread-out followers keep their normal morale and can flee. The order itself belongs to the companions thread (ARC-WORLD-046); this effect is the AI's. Lands with slice I7 (and I9 for the order). |
| EX20 | A blocked step (a living critter on the next tile near the path's end, a newly spotted trap, a step that cannot start) charges 2 action points in the walk goals; then the ranged approach (80) and the plain walk (5) end the whole run, while the melee approach (61) pops back to the combat approach, which re-plans. | Original | Kept in both settings: intent unclear; in real time the ended run is followed by the next AI pass, and in a round by the subturn hook, which starts a new attack goal, so the difference is one decision later rather than a lost attack. No switch. |
| EX13 | Dead branches: move-away node 4, the casting goal's approach node, the flee goal's end on node 2, the back-off-from starter (0x434A30) and the resolution's accidental test (EX15). | Original | No effect; not modelled. No switch. |

The other rows (EX1–EX5, EX7, EX8, EX10, EX11, EX14, EX16–EX19) and EX20 are the original's
behaviour and are kept in both settings.

## 10. Consequences for Steamwoven (project policy proposals)

1. **Slice I7** implements perform's dispatch and a small goal machine: a stack of
   goals that re-test their conditions after each subgoal, with typed steps (next,
   push, pop, restart, end), the one-second real-time pause before a run without a
   path ends and the turn-based give-up, driven by the session clock and the round owner (ARC-COMBAT-003); the
   path planner and step walker come from the click-route and movement owners, with
   the 8-step segment cap.
2. **Attack**: the reach test (weapon range at least 1, line-of-fire cost below 26,
   no foreign blocker), the approach to one tile, the one-attack goal for NPCs and
   the PC's auto-attack repeat; the alerted sound plays when the approach starts.
3. **Flight**: the straight-line escape to 10 tiles from the source, re-planned per
   segment, the 9-tile stop, the blocked-step stop and the face-and-NO_FLEE rule;
   the spread-out move-away under EX12.
4. **Following**: range 2/5, walk to 4/7, run beyond, the 100 ms re-check with its
   2 action points in a round.
5. **Action points** follow [section 6](#6-real-time-and-turn-based-compared), with
   EX6 as a switch of the end step's charge.
6. **Reason codes** for the overlay: `ATTACK_IN_REACH`, `ATTACK_APPROACH`,
   `ATTACK_NO_PATH`, `FLEE_ESCAPE_LINE`, `FLEE_SAFE`, `FLEE_CORNERED` (NO_FLEE),
   `FOLLOW_IN_RANGE`, `FOLLOW_WALK`, `FOLLOW_RUN`.

## Open questions handed forward

| # | Question (from) | Status |
| ---: | --- | --- |
| 1 | The flee, move-away and attack goals and the flee goal's stopping rule ([morale U1](npc-ai-morale.md#unknowns)) | **Closed** ([sections 3](#3-attack-and-approach) and [4](#4-flight-and-moving-away)) |
| 2 | Who sets combat flag 0x800 ([awareness AU5](npc-ai-awareness.md#unknowns)) | **Closed**: the missile's flight; the resolution's test is dead ([section 8](#8-other-answers), EX15) |
| 3 | Whether a skill or item use costs time or action points ([support U2](npc-ai-support.md#unknowns)) | **Closed** ([sections 1](#1-perform-and-its-actions) and [6](#6-real-time-and-turn-based-compared)) |
| 4 | How perform executes a skill | **Closed**: the skill goal, approach to one tile, 4 action points, the skill invocation; the invocation's own rules belong to the skills domain (ARC-SKILLS slices) |
| 5 | The grenade/attack goal 0x434B00 | **Closed**: the attack starter ([section 3](#3-attack-and-approach)) |
| 6 | The animation path at 0x42AE9F ([hostility](npc-ai-hostility.md#6-enemy-list-and-target-lock)) | **Closed**: the talk goal's distance ([section 8](#8-other-answers)) |
| 7 | How the attack executor uses the line of fire | **Closed** ([section 3](#3-attack-and-approach), EX9, EX15) |
| 8 | [Combat O5](combat-core-evidence.md#open-questions) | **Closed** with A1–A5 ([section 7](#7-combat-o5-how-the-ai-chooses-attacks-flees-or-ends-its-subturn)) |
| 9 | [Companion O5](companion-follow-evidence.md#open-questions) | **Closed** ([section 5](#5-following)) |
| 10 | Tie order of the global spell lists ([support U1](npc-ai-support.md#unknowns)) | **Closed** for the algorithm; the resulting order on the installed records is slice I8's data step |
| 11 | What the magic call on a cast item (0x4574D0) and the casting goal's choice between goals 26 and 82 do | **Handed to** the magic slices MT9a/MT10 ([magic plan](magic-implementation-plan.md)) |
| 12 | The missile's flight (0x4B2870) in full: when it picks an object on its way, and the damage step's own reactions to a struck bystander | **Handed to** the ranged-combat slice (FR-COMBAT-007) |
| 13 | The walk's per-step rules (doors, windows, blocked steps of goal 5) | Already the movement domain's ([locomotion](locomotion-evidence.md)); only the blocked-step cost and exits are added here (EX20) |
| 14 | The animation helpers of peace and stop attacking: 0x44E050, 0x44E0E0, 0x424070 ([hostility U3](npc-ai-hostility.md#unknowns)) | **Partly closed**: 0x424070 interrupts an object's running goals below a priority (used here with priority 3 by the starters and with the goal's own priority by the goal set-up); 0x44E050 and 0x44E0E0 (interrupt or retarget attack goals at a party) were not read; **handed to** stage A7 (stop attacking and post-fight cleanup); closed by ARC-RES-067 ([followers 7.1](npc-ai-followers.md#71-peace-and-its-two-helpers)) |
| 15 | The pickup goal started by the item search, 0x4356C0 ([support section 6](npc-ai-support.md#6-items-on-the-ground)) | **Partly closed**: gate cost 4, goal 15 [ag_pickup_item] walks onto the item's tile (range 0) and pushes the attempt pickup (16), which moves the item (0x42AA70; 1 action point for an item from a junk pile, description 3023) and ends with an end step (EX6); the full pickup rules **handed to** slice I8 and the inventory owner ([session inventory](../world/inventory-runtime.md)) |

## Unknowns

- **U1** How often each path is taken in play (aborted goals, ranged approaches to a
  still target, flights ending on an obstacle) is a runtime question for slice I11.
- **U2** The run flags and path bits are named by what they do in the functions read;
  their full meaning in the path planner (0x41F3C0 and its helpers) belongs to the
  movement domain.
