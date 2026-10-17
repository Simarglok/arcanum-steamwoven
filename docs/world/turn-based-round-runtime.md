# Turn-based round runtime

Status: **Implemented bounded component**, ARC-COMBAT-003 (2026-10-17). Slice 3 of the
[combat core research](../research/combat-core-evidence.md#proposed-implementation-slices-project-policy-not-accepted)
(ARC-RES-030, C6–C14 and C30): who takes part in a turn-based round and in which order, the action points of a
subturn and the cost of actions, the local PC's overexertion, the end of a round one game second later, and the round
as a domain of the [game session](game-session-runtime.md) (snapshot schema 11). The rules are pure functions in
`Arcanum.Simulation.Combat.TurnBasedRules`; the session runs the round over a host port. Requirements:
[FR-COMBAT-001](../requirements/combat-magic-tech.md#fr-combat-001) (the round part),
[FR-COMBAT-002](../requirements/combat-magic-tech.md#fr-combat-002) and
[FR-COMBAT-003](../requirements/combat-magic-tech.md#fr-combat-003). Real-time combat, the combat mode flag, AI, the
attack animation and the viewer are not part of it ([Not ready](#not-ready)).

## Evidence

- CE at the pinned commit `a7ff41b300ef712f0e7d088183a3d08957110cdd`: `combat.c` (turn-based start, begin turn, next
  subturn, end turn, subturn start, the animations-complete check and its time event, action-point check and charge,
  costs, insert, sort, save and load), `critter.c` (`critter_is_active`, the fatigue write), `anim.c` (step cost, the
  goal-start gate), `item.c` (weapon speed), `object.c`, `sector.c` and `obj_find.c` (scan order), `timeevent.c` (game
  time during a round).
- The retail 1.0.7.4 `Arcanum.exe` (SHA-256 `9bd80511…78d52`), read statically by two independent readers per the
  [original executable check](../research/original-executable-check.md); the rows are in the
  [verification ledger](../research/original-verification-ledger.md#turn-based-round-arc-combat-003). The original does
  what CE reads for every rule below except the two CE slips already known (A1 in the sort, A4 in the run step), and it
  answers the open question O2 (scan order). Three behaviours of the original are bugs and get a switch of the Original
  rule mode: A9, A11 and A12 ([Rule modes](#rule-modes)).

## Rules (`TurnBasedRules`)

| Rule | Value |
| --- | --- |
| Scan square | `PC ± max(10, PE / 2 + 5)` from the PC's effective perception, bounds included |
| Scan order (O2) | sectors by rows from the top, each row from the left; in a sector the critter that entered it last first; destroyed, switched-off and carried objects are not listed, dead, unconscious and hidden critters are |
| Round order | scan order, then the PCs and the PCs' followers moved to the end, each group keeping its order (A1: the original moves followers too); no initiative roll |
| Later rounds | the round rescans and merges: participants of earlier rounds stay where they were (also when they left the square or died), critters new to the round go in front, the last one found first |
| Skipped participant | not active (destroyed, switched off, dead, unconscious, stunned, paralysed, stoned), or hidden from drawing and not sleeping |
| Action points | `max(5, Speed)` at the start of each subturn |
| Costs | walk step 2, run step 1 (2 with fatigue ≤ 0), move estimate 2 per step (1 for a PC that always runs), attack `1` above weapon speed 24, `2` above 20, else `max(1, 8 − speed / 3)` (unarmed speed 10 → 5); weapon speed = speed factor + magic adjustment + 5 with at least apprentice training; a reach-1 attack on a target farther than 1 adds the adjacent move; use object 2, pick up 1 (each plus the move when not on the cell); cast, use skill, use item 4; get up 5 (A3, own subturn only) |
| Charge | paid when the action points cover it; else the local PC with action points > 0 and fatigue > 1 **overexerts**: action points 0 and 2 fatigue damage less `¾ × normal resistance` percent; anyone else is refused: action points 0, next subturn |
| Preview | the same test without the charge |
| NPC gate | an NPC may start an action it cannot pay while `100 × fatigue / max fatigue > 35` |
| Stalled actor | after the current actor's actions complete, the hero keeps its subturn while it has action points; an actor checked twice in a row with unchanged action points is forced to spend the rest; an actor without action points passes on |
| End of round | after the last subturn the clock moves 1000 game ms and the next round begins; while a round is active ordinary game time does not flow |
| Nobody can act | the active hero takes the subturn; with an inactive hero the round ends |

`TurnBasedStall<TActor>` is the stalled-actor check as a value; the round resets it at every round start.

## Session domain (`Arcanum.Session`)

The host answers through `ISessionCombatWorld`: `ScanCombatants(map)` lists the critters of the actor's map in scan
order (the hero once, at its place), and `CombatantFacts(map, entity)` gives an NPC's effective speed, life facts,
loaded life state, hidden and sleeping flags, or null when it is gone. The session reads the hero's speed, perception and
life itself, and an NPC's life record wins over the loaded state. Neither answer may call back into the owner; a call
that moves the revision makes the round call `StaleVersion` with no effect.

| Entry | Contract |
| --- | --- |
| `QueryCombatRound()` | The round (turn number, participants, current index, action points left) or null. |
| `StartCombatRound(expected, world)` | Round 1 and the first subturn. Needs a hero (`CombatUnavailable`); NoChange when a round is active; `InvalidCombatFacts` for a scan without the hero, a repeated or invalid participant, or invalid facts; `CombatUnavailable` when nobody can act. |
| `EndCombatRound(expected)` | The round ends (the PC leaves combat mode, turn-based combat is switched off). |
| `ConsumeActionPoints(expected, combatant, cost, world)` | The charge above. `Free` (NoChange) without a round or outside the combatant's subturn. An overexertion takes the hero's fatigue damage through the life domain in the same revision, with its recovery timer; if it knocks the hero out its subturn ends at once. |
| `EndCombatTurn(expected, combatant, world)` | Action points 0 and the next subturn (the source's end of a critter's turn). |
| `CompleteCombatActions(expected, world)` | The animations-complete check and its 2 ms turn event as one step: the stalled-actor check, a forced spend, the next subturn. |
| `AddCombatant(expected, combatant)` | An NPC joins the round (the AI's insert): appended, re-ordered, whose subturn it is unchanged. |
| `QueryActionPointCheck(combatant, cost)` / `QueryCombatActionGate(combatant, cost, facts)` | The preview and the goal-start gate. |

Every mutation takes the expected version, decides the whole step on a copy (reading the host's answers once per NPC and
call), refuses before any effect, and adds one revision when the saved round changes (the clock's own revision moves
when a round ends). Results list what happened in order: `RoundBegan(turn)`, `SubturnStarted(combatant, action points)`
(for an NPC the host then runs its heartbeat script and AI), `SubturnEnded(combatant)` (for an NPC its end-of-combat
script) and `RoundEnded`. A life step that knocks out the participant whose subturn it is sets its action points to 0;
the host's next `CompleteCombatActions` passes the turn on (the source does it inside the fatigue write).

`AdvanceGameTime` with `OrdinaryAdvance` is NoChange while a round is active (after the clock's own checks): in the
original single-player game time does not flow during a round and moves only by the round's second. An explicit wait
still advances.

## Rule modes

`CombatAnomalyProfile` (read from `GameSessionOwner.Rules` at each call) gains three switches; Steamwoven fixes, Original
keeps the bug of the original game ([rule modes](rule-modes.md#registry)):

| Switch | Ledger ID | Original behaviour | Steamwoven |
| --- | --- | --- | --- |
| `FirstSubturnStartedTwice` | A9 | when the skip loop of the next subturn runs past the end, the new round's first subturn starts twice (two `SubturnStarted` events: heartbeat and AI twice) | once |
| `StallCheckKeepsPreviousActionPoints` | A11 | when the actor changes, the stalled-actor check keeps the previous actor's action points (0 at a round start) as the reference, so a new actor's second check may force it to stop although it spent | the new actor's own action points |
| `OverexertionKnockoutClearsNextActionPoints` | A12 | when the overexertion's fatigue damage knocks the PC out, the turn passes on inside the damage step and the overexertion then zeroes the action points of the participant that just started | that participant keeps its action points |

## Snapshot schema 11

`GameSessionSnapshot.Combat` (`SessionCombatRound?`): null without a round. JSON, between `reactions` and `events`:

```json
"combat":{"turn":1,"participants":["oid:wolf","oid:bandit",null],"current":0,"actionPoints":3}
```

`null` in `participants` is the hero. The codec checks shapes and ranges (turn ≥ 1, current 0..1023, action points
0..1000, at most 1024 participants, entity ids); `Restore` refuses (`InvalidCombatRound`) a round without a session
hero, without the hero or with it twice, with a repeated participant, an empty list or an index outside it.
**Migration decision: none.** Schema 10 and older are refused; a quick save of schema 10 loads as "written by an
incompatible version". The stalled-actor check is not saved and starts empty after a restore, as after the source's load.

Deliberate difference from the original's load: the original restarts the round on load, so it rescans (participants
merged over earlier rounds and their order are lost; the load fails when the saved actor is not found again), and the
turn counter restarts at 1. The session keeps the saved participants, order and turn number instead: a restore has no
world to scan, and nothing in the original reads the turn counter (`combat_turn_based_turn_get` has no caller). The
saved required action points (reset on load) and the unused second actor handle are not kept.

## Tests

- `Arcanum.Simulation.Tests/Combat/TurnBasedRulesTests`: radius and square bounds, the party-last order, skips, the
  grant, attack, weapon, move, step and action costs, every charge outcome, the overexertion damage with resistance, the
  NPC gate at 35 % and a zero maximum, the stalled-actor check in both modes.
- `Arcanum.Session.Tests/CombatRoundTests`: the square and order with a follower, start refusals, skips (dead, hidden,
  gone, hidden sleeper), nobody able to act, paid and refused charges, the end of a round with the clock and the merge
  of a later scan, newcomers in front, A9/A11/A12 in both modes, overexertion with its timer and revision, a knockout
  by a life step, the gate and the preview, a joining NPC, stopped game time, a world that calls back, the canonical
  text and restore, and the restore and decode refusals.

## Not ready

- **No host yet.** The AREA viewer starts no round; the turn-based setting, combat mode, its toggle and the exit rule
  (C1–C3), animation interrupts and the per-participant calls of the round start are the host's. Real-time combat is
  slice 4.
- **No AI.** `SubturnStarted` and `SubturnEnded` tell the host to run the NPC's heartbeat, AI and end-of-combat scripts;
  there is no SCR heartbeat dispatch or AI yet (RES-039 I6/I7). With A9 the host runs them twice.
- **Movement and attacks do not charge yet.** The step, attack and use costs are rules; the movement runtime and the
  attack resolver do not call `ConsumeActionPoints` until a host wires them. Get up (A3) is the cost and the own-subturn
  gate; the get-up animation is the host's.
- **Map changes.** A round is on the actor's map; the host ends it before a map change. The session does not refuse a
  map change during a round.
- **Fast turn-based mode** (several steps per goal) and the turn-based UI callbacks are not modelled.
