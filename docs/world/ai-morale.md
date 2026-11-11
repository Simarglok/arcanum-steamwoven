# NPC morale: should-flee, flight, surrender and back-off (ARC-WORLD-082)

Status: **Implemented bounded component**, ARC-WORLD-082, NPC combat AI plan slice
[I5](../research/npc-combat-ai-plan.md#phase-1-faithful-reconstruction).
The morale rules of the retail 1.0.7.4 executable ([morale study](../research/npc-ai-morale.md), stage A4; ledger
[MR1–MR16](../research/ledger/npc-ai.md#npc-ai-morale-arc-res-058), two readers, re-read by this task) as pure rules in
`Arcanum.Simulation.Ai.MoraleRules` over an `IMoraleWorld` (an `IHostilityWorld` with the AI row's morale slots and the
follower lists) and an `IAiRollSource`, and the writes they decide in `Arcanum.Session` (`GameSessionMorale.cs`) on the danger
records of slice I3 ([danger state](ai-danger-state.md)). Flight and surrender requests go through the setter
(`SetDangerState`), as the reaction to an attack and the danger update of slice I4 ([hostility and targets](ai-hostility.md))
return them. No executor or host calls them yet: the scheduler (I6, [AI scheduler](ai-scheduler.md)) runs the danger update and perform only as host stages, and the host behind them (I10) and the executor (I7) are their first callers.

## Rules (`MoraleRules`)

The world (`IMoraleWorld`) adds to the hostility world the AI parameters 0 (flee hit-point percent), 1 (flee when outnumbered
by), 2 (flee when outlevelled by), 3 (stand while the target's hit-point percent is below) and 15 (combat distance) as
`MoraleParameters`, and a critter's recursive follower list (0x441260: followers of followers included, unfiltered).

| Rule | Original | What it decides |
| --- | --- | --- |
| `Party` | 0x4AE020, 0x4AE0A0 | `PartyStrength`: a PC counts itself and its follower list; a critter with a leader counts its leader's party (itself among the followers); a leaderless NPC counts itself and its followers (MR3 switch: alone). Size is the member count, the level sum each member's own level (P2). A leader the world no longer knows counts as none. |
| `ShouldFlee` | 0x4AB2F0 | `FleeDecision`, first match wins: `NoFlee`, `MindControlled`, `TargetWeak` (the target's HP% below parameter 3) → fight; `LowHealth` (own HP% at most parameter 0), `Outnumbered` (party size difference at least parameter 1), `Outlevelled` (level sum difference at least parameter 2) → flee; else `Fight`. Both parties are reported once test 5 is reached. |
| `ReactToAttack` | weigh 0x4AB2A0 | `MoraleReaction`: I4's `ReactToAttack` with `ShouldFlee` about the attacker as its should-flee; the answer is returned when it weighed. |
| `FleeSpell` | 0x4AAF50 | `FleeSpellStep` of the danger update's FLEE path: SPELL_FLEE set → cleared, nothing chosen; else the host's chooser over the flee list (slice I8) is asked, and a choice sets SPELL_FLEE and is the pass's action. |
| `StartsFleeGoal` | (0x4AC620) | Whether perform fleeing starts the flee goal (type 25) away from the source after its SURRENDER request: when SURRENDER was applied (MR9 switch: always). |
| `SurrenderEnds` | 0x4AC660 | Perform surrender: the fleeing-from handle none, unknown, dead or unconscious → the caller asks the setter for NONE; otherwise nothing. The calm-down roll is I4's danger update. |
| `StopFleeing` | 0x4AF170 | `StopFleeingStep`: an NPC in FLEE asks for NONE (SURRENDER is kept); every NPC's flee goals are interrupted. |
| `StartsBackOff` | 0x4AC6E0 (start only) | Combat perform's first branch: parameter 15 above 1 and closer than it to the target → a 1..20 roll (`ai.BackOffStart`) of 1 starts a back-off; no roll otherwise. |
| `BackOff` | 0x4AC7B0 | `BackOffRun` before the path request: nothing without BACKING_OFF; the range is parameter 15, or 5 when at most 1; at least the range away → `Reached` (BACKING_OFF cleared), else `Runs`: the "away" path along `AwayOffsets` (4, 5, 3, 6, 2, 7, 1, 0 from the direction toward the target), and with no path BACKING_OFF is cleared (MR15). |
| `KillNotification` | 0x4AA1B0 | `KillNotificationPlan`: the victim's stop fleeing; for a PC killer, the NPCs of the 7×7 square around it (the world's square query, NPCs only) that are alive, lack NO_FLEE, share the victim's faction and are in COMBAT_FOCUS on the killer start backing off. |

The setter (`DangerRules.Set`) gains MR7: in the Steamwoven setting any applied state other than FLEE clears SPELL_FLEE.

## Session (`GameSessionOwner`)

| Operation | Original | What it does |
| --- | --- | --- |
| `RunFleeSpellStep(entity, choosesFleeAction)` | 0x4AAF50 | One revision for an NPC in FLEE (otherwise `InvalidDangerFacts`): the flee-spell step's bit write, the chooser asked at most once. Returns the outcome. |
| `PerformFleeing(request)` | 0x4AC620 | One revision for an NPC in FLEE: the setter's SURRENDER from its fleeing-from (with the surrender's combat-mode exit and CL4, needing `CombatMode` facts in combat mode) and `StartsFleeGoal` (MR9); after a refused surrender the host reads the unchanged `FleeingFrom`. |
| `StopFleeing(entity, facts)` | (0x4AF170) | The setter's NONE for an NPC in FLEE; nothing in any other state. The flee-goal interrupt is the host's. |
| `StartBackOff(entity)` / `EndBackOff(entity)` | (0x4AC6E0, 0x4AC7B0) | BACKING_OFF and CHECK_GRENADE set for an NPC in COMBAT_FOCUS (otherwise `InvalidDangerFacts`) / BACKING_OFF cleared. |
| `ApplyKillNotification(request)` | (0x4AA1B0) | One revision: the victim's stop fleeing on its session record (none for the hero) and BACKING_OFF on each NPC of the plan. |
| `AiRolls` | | Adds the label `ai.BackOffStart`. |

**State.** SPELL_FLEE is a new danger bit (`DangerFlags.SpellFlee`, critter flag 0x10000), saved in both settings: the
snapshot moves to schema 40 (flag name `SpellFlee`; schemas 1..39 are refused, not migrated).

## Settings

| ID | Original 1.0.7.4 (switch on) | Steamwoven | Switch |
| --- | --- | --- | --- |
| MR3 | A leaderless NPC counts as a party of one in the flee test, its own followers ignored, while each of its followers counts the whole group. | It counts its own followers like a PC. | `CombatAnomalyProfile.NpcLeaderPartyCountsAlone` |
| MR7 | SPELL_FLEE is cleared only by the flee-spell step, so a flight left before that step makes the next flight skip its flee spell or item once. | The setter clears it with any state other than FLEE. | `CombatAnomalyProfile.StaleSpellFleeSkipsFleeSpell` |
| MR9 | Perform fleeing starts the flee goal even when the setter refused the surrender (invulnerable → NONE, a leader of extraordinary charisma → COMBAT_FOCUS). | Only after an applied surrender. | `CombatAnomalyProfile.FleeGoalAfterRefusedSurrender` |

P2 (each member's own level) and P6 (a flight lasts one pass, then SURRENDER) follow the original in both settings; MR15's
retry changes nothing, so the back-off simply ends without a clear line.

## Not ready

- **Callers.** The AI pass (I6) runs the danger update and perform (0x4AC180) only as host stages, so nothing runs their FLEE and SURRENDER paths or combat perform yet; no
  combat path reports a kill; no host implements `IMoraleWorld`. The fear effect and the dialog start (flee 0x4AF130 = the
  setter's FLEE), the fear effect's end and resurrection (stop fleeing) are their domains' callers.
- **Execution.** The flee goal (type 25) and the run to the back-off's tile are the executor's ([AI executor](ai-executor.md),
  ARC-WORLD-086), which calls perform fleeing, the back-off step and the back-off start through its world; the "away" path's
  tile is the host's; the flee-spell list chooser is `AiSupportRules.ChooseFleeAction` (slice I8, [AI support](ai-support.md)), which no host calls yet.
- **Set no-flee** (0x4AF1D0): the move-away goal's NO_FLEE is the session's `SetNoFlee` (ARC-WORLD-086, EX12); the NO_FLEE of the
  critter's data stays the caller's fact. The other writer is the start of mind control, whose end (MR13 `MindControlLeavesNoFlee`, one remembered bit) belongs to the magic slice that
  implements mind control.
- **Stop waiting** (0x4AA8C0) of the kill notification's victim is the party owner's; the dialog port applies the surrendered
  NPC's greeting, script flag and "flee from the source's party" rule of the study's section 3; the follower rule (I9) and the
  redirect (P3, `AiGroundRules.Redirect`, slice I8) are their slices'.
