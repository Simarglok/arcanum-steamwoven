# NPC AI action goals: casting, items, skills, throws and pickups (ARC-WORLD-089)

Status: **Implemented bounded component**, ARC-WORLD-089, NPC combat AI plan slice
[I8b](../research/npc-combat-ai-plan.md#implementation-slices-proposals-after-the-matching-research) (split from I8 by the owner on
2026-10-28). The goals that carry out what the [support rules](ai-support.md) (slice I8) choose, and the starters that create them,
read in the retail 1.0.7.4 executable through its goal-type table: perform's cast, item and skill actions, the grenade's and a
thrown weapon's throw, the item search's pickup, and the item use an AI-action item gets at once
([execution study](../research/npc-ai-execution.md) sections 1, 2 and 6, ledger [EX1–EX4,
EX19](../research/ledger/npc-ai.md#npc-ai-action-execution-arc-res-066); this task's reading, one reader:
[AG1–AG16](../research/ledger/npc-ai.md#npc-ai-action-goals-arc-world-089)). The rules extend `Arcanum.Simulation.Ai.AiExecutorRules`
(`ExecutorActionGoals.cs`) over the [executor's](ai-executor.md) `IAiPerformWorld` and `IAiGoalWorld`; animations, the magic run, the
skill invocation, the throw, the pickup and the item's own effects are the world's. The viewer's AI host (slice I10) runs
perform's cast and the casting goals since ARC-WORLD-102 ([casting](ai-host.md#casting-arc-world-102)); the other goals have no host yet.

Requirements: [FR-AI-002](../requirements/story-social-ai.md#fr-ai-002) (the NPC's actions in a fight),
[FR-COMBAT-003](../requirements/combat-magic-tech.md#fr-combat-003) (their action points in a round),
[FR-MAGIC-004](../requirements/combat-magic-tech.md#fr-magic-004) (an NPC's cast through the cast host).

## Perform's actions

`Perform` now takes the pass record's choice (`AiPassChoice`: the action, spell, skill and item the support rules wrote) and runs:

| Action | Original | What happens |
| --- | --- | --- |
| Cast (1) | 0x4AC250 | `StartCast`: the magic domain builds the invocation (from the item when one is set, else the NPC), checks it and runs it (`IAiPerformWorld.Cast`, which also makes an item source's magic call and destroys a food or scroll); a run it started gets its casting goal, 26 when the record's casting animation is −1, else 82, with no start gate, replacing the object's run (AG3, AG14). No run, or a cast queued under a running cast (SU13), keeps the current run. |
| Use item (2) | 0x4AC320 → 0x435450 | `StartUseItem` with the pass target and the item. |
| Use skill (3) | 0x4AC350 → 0x4350F0 | `StartUseSkill` with the pass target, the skill and the tool item; pick locks (14) through 0x436220. |

The pass target of these actions is the choice's (`AiPassChoice.Target`, which the chooser writes into the pass record, SP6); the
`target` argument stands in when the choice has none.
| Grenade | 0x4AC910 | Combat perform asks the host's grenade step (`IAiPerformWorld.Grenade`, i.e. `AiGroundRules.Grenade`); a throw runs `StartThrow` with the grenade at the target's tile, and perform goes no further whether the starter started or refused (AG13). |
| Thrown weapon | 0x434B00 | The attack starter takes a removable "default throws" weapon off (`Unwield`, 0x466E00) and answers `StartThrow`'s answer; a refused throw leaves the weapon in the inventory (AG13). |

## Starters

Each asks the [start gate](ai-executor.md#perform-and-the-starters) with its cost. Run flag 0x4000 (`AiRunFlags.NearOccupied`, "the
approach may end beside an occupied tile") is passed to the planners; running is on for an NPC with current fatigue above 8.

| Starter | Original | Gate | Goal | Refusals and flags |
| --- | --- | ---: | --- | --- |
| `StartUseItem` | 0x435450 | 4 | 39 (target, item) | No target or item; 0x4000; running. |
| `StartUseSkill` | 0x4350F0 | 4 | 33 (target, item, skill) | Skill 14 → `StartPickLocks`; no target (inference: the original asserts); 0x4000; running. |
| `StartPickLocks` | 0x436220 | 4 | 33 (skill 14) | No art for the skill animation 12 (`HasSkillArt`); 0x4000; no running. The animation's loop count is the world's. |
| `StartThrow` | 0x434F80 | 0 | 37 (item, tile) | No item or tile (after the gate); an equal throw (same item and tile) already in the run; a failed interrupt (priority 3); no flags. |
| `StartPickup` | 0x4356C0 | 4 | 15 (the item) | No item; an NPC while the item system's pickup switch (`NpcPickupsOn`, global 0x5E8800) is off; 0x4000. |

## The goals

| Goal | What it does |
| --- | --- |
| Casting (26, 82) | Get up, unconceal; the start (`StartCast`: the cast host's 4 action points and pre-animation check, then a target destroyed or off); the turn (`FaceCast`, 0x42C850); then the attempt goal replaces it. A refusal ends the magic run's goal (`EndCast(refused: true)`, 0x429CD0) and takes the end step. The approach node (range 8) is unreachable (EX13). |
| Cast attempt (27, 83) | The play (`BeginCast`; 83 plays the casting animation): without one (the target re-check at its start failed, or no art) the release (`ReleaseCast`, 0x429C80) and the end step. Otherwise at the action frame `CastActionFrame` (a missile spell launches its missile, whose flight is the missile flight's; any other is released; a play whose art has no reachable action frame answers false, ledger EC7): not released → pop, else the play's rest, then pop. The goal's end node ends the magic run (`EndCast(refused: false)`) on its pop or the end step (AG1, AG4). |
| Item use (39) | Range 1; within it with a clear first step the use, else the walk near (8). The use needs the item still in the user's inventory (`ItemHolder`), then runs `UseItemNow`; both end with the end step (AG5). |
| Skill (33) | Range 1; within it the attempt goal replaces it: 67 for pick locks, 86 for pickpocket, 34 for any other skill (heal included); otherwise the follower's path and the plain walk (goal 5: a blocked step ends the run, EX20), or no path (AG6). |
| Skill attempt (34) | The turn (`FaceForAction`) and the art, else the end step; an animation that cannot start: 2 action points and the end step; at the action frame the attempt: a target destroyed or off, or 4 action points the consume refuses, end the run, else the skill invocation (`AttemptSkill`); the goal pops after the animation (AG7). |
| Pickpocket (86) | The turn, then the attempt before any animation; a result that sets the run's flag 0x40000 pops at once, any other plays the skill animation (no art: the end step; cannot start: 2 and the end step) and pops (AG8). |
| Pick locks (67) | The turn and the art, else the end step; the lock-pick loops (none left: the goal pops), then the attempt (`AttemptPickLocks`), which pays no action points, and a pop (AG9). |
| Throw (37) | The turn to the tile, the art (14) and the start (the original tests the art before the turn: without the art the thrower here still turns); at the action frame the throw (`ThrowItem`, 0x4B3C00 inside). Every path, refusals included, charges 2 and takes the end step (AG10). |
| Pickup (15, 16) | On the item's tile the attempt (16), else range 0 and the walk near (8) onto it; the attempt picks it up unless it is destroyed or off (`PickUp`; an item from a junk pile costs 1 action point there), then the end step either way (AG11). |

The walk near (8) keeps the run's gait, so an item-use approach of a running run runs (AG16), and a spread-out run whose run near
(56) turned running on walks near running afterwards; the slice I7 code walked. A pickup run never runs (its starter sets no
running), and the skill goal walks through goal 5. The attack
goal's first node now pops for a gone or dead target instead of taking the end step, so no EX6 charge follows (AG2).

## The item use (`UseItemNow`, 0x462CC0)

Run by the item-use goal and, for the chooser's AI-action item (`AiChooserOutcome.ItemUsedAtOnce`), by the host at once, without a
goal or an approach (EX4). In order (AG15, ledger TP1): a scroll whose spell needs more intelligence than the user has → nothing; no
target and OIF_NEEDS_TARGET → the targeting cursor; a trap device → its use (TP2); the use script, which may stop the use → 4 action
points; then an item with neither spell charges nor the magical flag pays 4 action points and is read (written), used with the pick
locks (a lockpick on a lockable target) or heal skill (a healing kit) through goal 40, or spent (food, scroll); an item with either
casts its slot-0 spell without a charge, then a food or scroll is spent. The action points are charged only in a round on the user's
subturn. `AiItemUseResult` names the branch, the charge and the spending.

## Action points in a round

| Action | Charge |
| --- | --- |
| cast | 4 at the casting goal's start (the cast host's charge) |
| skill attempt (34, 86) | 4 at the attempt; pick locks (67) none |
| item use | 4 when the use script stops it or for an item without charges or magic; magic none |
| throw | 2 at its end on every path, then the end step's pending cost (EX6) |
| an animation that cannot start (34, 86, 37) | 2 |
| end step (39, 37, 16, refused goals) | the round's pending cost again in the Original setting (EX6) |

## Session

Nothing new: the runs are the host's and not saved (as in [slice I7](ai-executor.md#session-gamesessionowner)).

## Replaced runs

The executor keeps one run per object. When perform or a starter answers a started run while the object holds another, or when the
host drops a run (killed, node limit, overflow, a map change), the host calls `AiExecutorRules.EndRun` on the old run: it runs the
end nodes top first, so a cast's attempt goal ends its magic run (0x429CD0) instead of leaking the cast host's slot. In the original
an object may hold several runs at once (inference: the call stands in for that).

## Not ready

- **Host (slice I10).** No host implements the new world members (the animations and their timing, the cast host's
  `StartGoal`/`Release`/`EndGoal` behind `StartCast`/`ReleaseCast`/`EndCast`, the skill invocation, the throw and the pickup), keeps
  the runs or calls `UseItemNow` for the chooser's AI-action item; the item search's `StartPickup` is the host's call of
  `AiExecutorRules.StartPickup`.
- **Queued cast.** A cast the magic domain queues under the caster's running cast (SU13) is kept by the host, not by the run; the
  viewer's queue for the hero ([queued cast](spell-cast-host.md), ARC-MAGIC-019, QC1–QC6) starts the next cast after a pop and drops
  the queue after an end step, which a host can follow with the run ends this executor reports.
- **Missiles.** A spell missile's flight and the release at its arrival are the missile flight's; the attempt goal ends its run
  (`EndCast(refused: false)`) while the missile flies, so the host's `EndGoal` must keep a run whose missile is pending.
- **Frame flag 0x80.** A refused release ends the play through 0x431A40, which takes the end step instead of the pop when the goal's
  frame flag 0x80 is set; the executor always pops.
- **Paths kept.** The skill and walk-near goals plan again on each return; the original's "path still valid" node (0x425740) is not
  modelled (as goal 8 in slice I7).
- **Skill-with-item goal (40).** Started by the item use for a lockpick or a healing kit (0x4352C0), it is the world's.
- **Pick-locks loops.** Their count (the art's frames, halved by an expertise test) and the frame flag 0x400 the pick-locks starter sets
  are the world's or not modelled.

## Tests

`ActionGoalsTests` (Simulation, an invented open grid): perform's cast and its casting run; the casting goal's play, release and end
node (also without a play, with EX6, and a refused or unturnable start); readiness before a cast; the item-use starter's refusals,
gate and flags, its goal's running approach and use; every branch of the item use with its charge; the skill starter's pick-locks
branch, the heal skill's walk and attempt with its action points, a blocked approach, the attempt's refusals; pickpocket and pick
locks; the throw starter and the throw goal's charges on every path; the grenade's throw from combat perform; the pickup starter and
goal; `EndRun` on a replaced cast; the choice's target; a spread-out run's walk near after a run near (AG16). `ExecutorRulesTests`: the attack goal's pop for a dead target (AG2) and the starters' throw of a default-throws weapon.
