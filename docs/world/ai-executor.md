# NPC AI executor: perform, the starters and the goal machine (ARC-WORLD-086)

Status: **Implemented bounded component**, ARC-WORLD-086, NPC combat AI plan slice
[I7](../research/npc-combat-ai-plan.md#implementation-slices-proposals-after-the-matching-research).
How the retail 1.0.7.4 executable carries out what an AI pass chose ([execution study](../research/npc-ai-execution.md), stage
A6; ledger [EX1–EX23](../research/ledger/npc-ai.md#npc-ai-action-execution-arc-res-066), two readers, re-read by this task:
EX21–EX23): perform's dispatch, the starters with their start gate, and the animation goals of an attack, an approach, a flight,
a back-off run and following, in real time and in a turn-based round. The rules are drivers in
`Arcanum.Simulation.Ai.AiExecutorRules` over an `IAiPerformWorld` (perform and the starters) and an `IAiGoalWorld` (the goals),
with labelled `IAiRollSource` rolls; the session stores the NO_FLEE they set. No host runs them yet: the viewer's AI host is
slice I10.

## Perform and the starters

World members are called in the original's order. Ids are the host's object ids; `AiSchedulerRules.Pc` names the local PC.

| Rule | Original | What it decides |
| --- | --- | --- |
| `Perform` | 0x4AC180 | `AiPerformResult`: the kind, the run the object keeps (a started or retargeted one with `Started`, else the current run it was given; a default-throws weapon gives the throw starter's run (goal 37) or its refusal) and a starter's refusal. In a round nothing unless it is the NPC's turn; BACKING_OFF → the back-off: I5's `BackOff` step, then on `Runs` the run to the "away" tile (goal 4, running unless encumbered; its starter 0x433C80 asks no gate, EX22), else BACKING_OFF cleared (reached, or no tile, MR15); a cast, item or skill action → its starter with the pass's choice ([action goals](ai-action-goals.md), slice I8b); otherwise by the danger state: NONE → leave combat mode, then a leaderless NPC's routine, a waiting follower nothing more, another follower the follow starter and the loyalty check; COMBAT_FOCUS → the back-off start (I5), else the grenade step and its throw starter (I8, I8b), else the attack starter with the pass's alerted sound; FLEE → perform fleeing (I5's session step) and, when it says so, the flee starter; SURRENDER → perform surrender. |
| `StartGate` | 0x4348E0 | Active; outside a round passes; in a round only the current critter, a PC always, an NPC when the cost fits its action points or its fatigue is above 35 % (`TurnBasedRules.NpcMayStartAction`). |
| `StartAttack` | 0x434B00 | Self refused; a removable "default throws" weapon is taken off and the throw starter runs with it at the target's tile (slice I8b); the gate with the weapon's attack cost; Body of Air without the elemental flag, an attack on the same target already queued (or any attack already swinging), a failed interrupt of lower-priority animations and a boomerang in flight refuse, each asking the critters on the tile to step apart; an attack run still approaching another target restarts as one attack goal on the new target (inference: the original updates the running goal); the goal holds the target and the sound; running for an NPC with fatigue above 8. |
| `StartFlee` | 0x434980 | The gate with cost 0; goal 25 away from the source with the run's `Flight` flag (0x400). |
| `StartFollow` | 0x4346A0 | NPCs only; none while the leader is within 4 tiles (7 spread out); a follow, walk-near or run-near run is kept; the gate with cost 0 and the interrupt, then goal 24 walking. |
| `StartSpreadOut` | 0x436960 | The gate with cost 0, then goal 69 (no flight flag). The order that sets the NPC flag and calls it is slice I9's. |
| `AttackPossible` | 0x4294F0 | Attacker active, not in Body of Air unless elemental, the target neither destroyed, off nor dead, and a path when the line of fire reports a foreign blocker. |
| `InReach` | (0x429960) | Distance at most the weapon's range (at least 1) and a clear line (cost below 26, no foreign blocker), as `LineOfFireRules.InReach`. |
| `RangedApproachStops` | 0x425590 | EX9: Steamwoven stops at the first tile in reach; Original only when the attack is impossible, the line clear and a weapon wielded. |
| `EscapeDestination` | (0x426A80) | The rotation from the source's tile to the NPC's (0x4B8D50) and the tile `range + 1` neighbour steps from the source in it (EX23). |

## The goal machine (`Step`, 0x423560)

A run (`AiGoalRun`) is an immutable stack of at most 8 goals (`AiGoalFrame`: goal, node, target, tile, range, sound, planned
segment). `Step` runs nodes of the top goal until one waits: a node names the next node, pushes a goal with a copy of the parent's
data (a ninth ends the run, `Overflow`), pops (the parent restarts at its first node in the same step; popping the bottom goal
finishes the run with no end step, EX21), pops two, replaces itself, restarts (the step ends), waits, chooses the end step or kills
the run. The 101st node of a step ends the owner's subturn and kills the run. The result carries the next run, the delay in game
milliseconds (0: the next frame), how it ended and the last reason code (`AiExecutorReason`, the overlay's "why").

| Goal | What it does |
| --- | --- |
| Attack (18) | Target there and not dead, else the goal pops (AG2: no end step); attack possible, else (in a round, ending the attacker's subturn) the end step; get up, unconceal (not a PC); in reach → attempt attack (19); range 1; within it with the first step clear, else push the combat approach (79); adjacent but out of reach → no way (below). |
| Attempt attack (19) | Reach again; the weapon's attack cost (a run that cannot pay it is killed); the world's turn to the target and attack animation; at its action frame reach again → the world's hit step (resolver, wear, missile, notification), else no hit; then the goal pops. An NPC's attack goal ends after one attack: its next attack is the next AI pass (real-time cadence, or the subturn hook in a round). |
| Combat approach (79) | Within range 1 with a clear step → pop; get up, unconceal; the attack's path (0x4280D0) or no path (below); the alerted sound once; push the ranged walk (80) for a weapon range above 1, else the melee walk (61), with the planned segment (at most 8 steps). |
| Walks (5, 61, 80) | The melee walk pops when the target left its planned tile; the ranged walk pops two when it did, or when `RangedApproachStops` (writing the weapon's range into the goals); the end of the segment pops. A next step on an occupied tile (a living critter near the path's end, a spotted trap, a flight's blocking tile) waits 0..300 ms (`ai.BlockedStepWait`) and one that cannot start draws no wait but also ends the step (inference: the next frame goes on); then node 10 charges 2 and the plain and ranged walks end the run while the melee walk pops to re-plan (EX20). Each step: in a round the step check (walk 2, run 1 with fatigue above 0; the pending cost written; an unpaid step kills the run, its cost left pending, EX6 and EX21) and the consume, then the world's step. Running needs the run's flag and encumbrance below 4. |
| Flee (25) | Running unless encumbered at 4 or more; range 9; within it with the way back clear (the source's tile not blocking) → push move away; else 2 action points and the end step (`FleeSafe`). |
| Move away (52) | Get up, unconceal; within range with the clear step, else pop; the escape line to `EscapeDestination` (a flight's straight line that accepts every step and does not search; the spread-out one may search): a bent line after a partial straight one sets NO_FLEE; push the plain walk. No line (also a gone source or an entangled NPC): in a round a non-PC's subturn ends; a stoned, stunned or paralysed NPC ends there; any other faces the source, gets NO_FLEE and ends (`FleeCornered`). Under the spread-out goal Steamwoven sets no NO_FLEE (EX12). |
| Run to tile (4) | At the tile → pop; the path to the tile or no path; push the plain walk. |
| Follow (24), spread out (69) | Range 2 (5 spread out). The spread-out goal pushes move away (range inherited) while the leader is closer than 3 (1 without the flag). Within range with a clear step → 2 action points and a restart after 100 ms; otherwise the walk near (8) within range + 2, else the run near (56): the follow goal becomes it, the spread-out goal pushes it (inference). |
| Walk near (8), run near (56) | Within range with a clear step → pop; the follower's path (0x427990, to within the range) or no path; push the melee walk; the run near runs unless encumbered. |
| Pause (6) | One second, then the end step. |

**No way.** A planner without a path (and the attack goal adjacent but out of reach) pushes the pause in real time, so the run
ends a second later and the next AI pass decides again; in a round the run ends at once, and a planner's failure ends a non-PC's
subturn (0x4B7C90).

**Action points.** The check (0x4B7790) and the consume (0x4B7CD0) act only in a round and only for the critter whose turn it is.
The world holds the round's pending cost (`PendingCost`) and sets it to 0 at each subturn start. 0x433270 (the blocked step, the
flight's end, the follower's re-check) charges 2 to anyone but the local PC. At the end step the Original setting charges an NPC
the pending cost again (EX6); Steamwoven charges nothing there.

## Session (`GameSessionOwner`)

| Operation | Original | What it does |
| --- | --- | --- |
| `SetNoFlee(entity)` | 0x4AF1D0 | `DangerFlags.NoFlee` on the NPC's danger record, for good, one revision (none when set). The setter's rule 2 reads it besides the caller's `DangerSetterFacts.NoFlee`. |

**State.** `DangerFlags.NoFlee` (critter flag 0x8000000 as the AI sets it) is saved in both settings: the snapshot moves to schema
45 (flag name `NoFlee`; schemas 1..44 are refused, not migrated). A map change keeps it. The run itself is the host's and is not
saved: a load leaves every NPC without a run, so its next AI pass starts one.

## Settings

| ID | Original 1.0.7.4 (switch on) | Steamwoven | Switch |
| --- | --- | --- | --- |
| EX6 | An end step in a round charges an NPC the round's pending cost again, so throws, item uses and aborted actions after a walk pay their last step twice. | Nothing is charged at the end step. | `CombatAnomalyProfile.EndStepRechargesPendingCost` |
| EX9 | The ranged approach stops at a clear shot only when the attack has become impossible, so it walks to the tile next to a still target. | It stops at the first tile in reach. | `CombatAnomalyProfile.RangedApproachSkipsClearShot` (ARC-COMBAT-015) |
| EX12 | The spread-out goal's move away sets NO_FLEE for good when its escape line bends or fails. | Not from a spread-out move away; flights keep it. | `CombatAnomalyProfile.SpreadOutSetsNoFlee` |

EX20 (a blocked step ends a ranged approach's run but only the segment of a melee one), EX21–EX23 and the dead branches of EX13
follow the original in both settings.

## Not ready

- **Host.** The AREA viewer's AI host ([AI viewer host](ai-host.md), ARC-WORLD-092, slice I10's first part) implements both worlds,
  holds one run per NPC, steps it on the world clock and wires the scheduler's perform stage; in a turn-based round
  (ARC-WORLD-095, [turn-based round](ai-host.md#turn-based-round-arc-world-095)) its runs pay the round's costs through the session
  and its pending cost reads 0 after each subturn start.
- **Other goals.** The casting (26, 82), item use (39), skill (33), throw (37) and pickup (15, 16) goals and their starters are
  [slice I8b](ai-action-goals.md) (the decisions are [AI support](ai-support.md), slice I8); the PC's attack goal and its
  auto-attack repeat stay the hero attack's ([hero attack](hero-attack-runtime.md)).
- **EX15.** The notification of a ranged hit on a line-of-fire blocker is the attack resolution's, inside the world's hit step;
  its switch `RangedBlockerHitNotifiesAimedTarget` lands with the ranged-combat slice (FR-COMBAT-007).
- **Run details.** The end nodes' animation clean-up, the priority interrupt test after a run and the subturn hook that follows a
  run's end in a round are the host's. The NO_FLEE in the critter's data is still the caller's fact for the hostility and morale
  rules; the host ORs the session bit into it.
- **Orders.** The attack, stop and curse orders and the follower rule are slice I9's ([AI followers](ai-followers.md)); the spread-out
  order (0x45F600) that calls `StartSpreadOut` is the companions thread's.

## Tests

`ExecutorRulesTests` (Simulation, an invented open grid with straight-line paths): reach, attack possible, escape destination, the
start gate; the run's 1..8 goals, the 101st node; melee and ranged attacks with approach, sound, action frame and target loss; get up and unconceal; no path in real time
and in a round; EX9 and EX20 in both settings; the round's charges, an unpaid step and EX6; the flight to 10 tiles, encumbered,
cornered (stunned, gone source) and blocked; EX12 and the spread-out walk; the back-off run; following and its in-range charge;
perform's dispatch, the attack starter's refusals, a retarget during an approach and a kept swing; a step that cannot start; the
stack overflow. `ExecutorTests` (Session): `SetNoFlee`, the setter's
rule 2 over it, and the schema-45 round trip.
