# Turn-based round runtime

Status: **Implemented bounded component**, ARC-COMBAT-003 (2026-10-17). Slice 3 of the
[combat core research](../research/combat-core-evidence.md#proposed-implementation-slices-project-policy-not-accepted)
(ARC-RES-030, C6–C14 and C30): who takes part in a turn-based round and in which order, the action points of a
subturn and the cost of actions, the local PC's overexertion, the end of a round one game second later, and the round
as a domain of the [game session](game-session-runtime.md) (snapshot schema 11). The rules are pure functions in
`Arcanum.Simulation.Combat.TurnBasedRules`; the session runs the round over a host port. Requirements:
[FR-COMBAT-001](../requirements/combat-magic-tech.md#fr-combat-001) (the round part),
[FR-COMBAT-002](../requirements/combat-magic-tech.md#fr-combat-002) and
[FR-COMBAT-003](../requirements/combat-magic-tech.md#fr-combat-003). Real-time combat and the combat mode flag
([real-time combat](real-time-combat-runtime.md), ARC-COMBAT-004), AI and the attack animation are not part of it ([Not ready](#not-ready)).
The AREA viewer hosts the round since ARC-COMBAT-020 ([Viewer host](#viewer-host-arc-combat-020)): the turn-based setting,
the round started with combat mode, the hero's actions charging its action points, the NPCs' subturns passing and the round's
second feeding the event pump ([FR-COMBAT-010](../requirements/combat-magic-tech.md#fr-combat-010), the recovery timers).

## Evidence

- The original's functions, read first: the combat functions (turn-based start, begin turn, next subturn, end turn,
  subturn start, the animations-complete check and its time event, action-point check and charge, costs, insert, sort,
  save and load), the critter activity test (0x45D790) and the fatigue write, the animation step cost and goal-start
  gate, weapon speed, the object, sector and object-find scan order, and game time during a round.
- The retail 1.0.7.4 `Arcanum.exe` (SHA-256 `9bd80511…78d52`), read statically by two independent readers per the
  [original executable check](../research/original-executable-check.md); the rows are in the
  [verification ledger](../research/ledger/combat.md#turn-based-round-arc-combat-003). The original does
  what that first reading describes for every rule below except the two differences already known (A1 in the sort: the
  original moves followers too; A4 in the run step, ledger TB14), which both rule modes follow, and it
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
| Stalled actor | after the current actor's actions complete, an active hero keeps its subturn while it has action points; an actor checked twice in a row with unchanged action points is forced to spend the rest; an actor without action points passes on |
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
script) and `RoundEnded`. A life step that knocks out or stuns the participant whose subturn it is sets its action points
to 0; the host's next `CompleteCombatActions` passes the turn on (the original does it inside the fatigue write
(0x45D5A0) and when the stunned animation goal starts (0x4357B0)). An overexertion decides the rest of its step with the hero's state after the damage, so a
knockout that leaves nobody able to act ends the round. The hero holds its subturn with action points left only while
it is active: a hero killed on its own subturn (the original ends the game then) passes on at the next completion check.
The hero is never hidden from drawing, so only its activity decides whether it is skipped.

`AdvanceGameTime` with `OrdinaryAdvance` is NoChange while a round is active (after the clock's own checks): in the
original single-player game time does not flow during a round and moves only by the round's second. An explicit wait
still advances.

## Rule modes

`CombatAnomalyProfile` (read from `GameSessionOwner.Rules` once per round call) gains three switches; Steamwoven fixes, Original
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
world to scan, and nothing in the original reads the turn counter (its turn counter getter, 0x4B80D0, has no caller). The
saved required action points (reset on load) and the unused second actor handle are not kept.

## Tests

- `Arcanum.Simulation.Tests/Combat/TurnBasedRulesTests`: radius and square bounds, the party-last order, skips, the
  grant, attack, weapon, move, step and action costs, every charge outcome, the overexertion damage with resistance, the
  NPC gate at 35 % and a zero maximum, the stalled-actor check in both modes.
- `Arcanum.Session.Tests/CombatRoundTests`: the square and order with a follower, start refusals, skips (dead, hidden,
  gone, hidden sleeper), nobody able to act, paid and refused charges, the end of a round with the clock and the merge
  of a later scan, newcomers in front, A9/A11/A12 in both modes, overexertion with its timer and revision, a round
  ended by an overexertion knockout, a knockout and a stun by a life step, a hero killed on its subturn, the stall reset
  at every round, the gate and the preview, a joining NPC, stopped game time, a world that calls back, the canonical
  text and restore, and the restore and decode refusals.

## Viewer host (ARC-COMBAT-020)

Alpha-gap item N3. The AREA viewer (every map it shows) hosts the round over this domain; the host's decisions are
`Arcanum.TerrainViewer.Core.AreaTurnBased` and its world is `AreaCombatWorld`
([core](../../tools/terrain-viewer/Core/AreaCombatRound.cs), [viewer](../../tools/terrain-viewer/Godot/TerrainViewerTurnBased.cs)).
The original's charges and keys the host follows were read in the executable by two readers
([ledger CT1–CT6](../research/ledger/combat.md#turn-based-host-arc-combat-020)).

| Part | Behaviour |
| --- | --- |
| Setting | Space (on the press) toggles the turn-based setting through `SwitchTurnBased`: on only for an active hero, off always (RT4, CT6), also during another participant's subturn (CT6); off by default, as the original's setting; the same setting as the options window's Default Combat Mode, saved as a user preference (ARC-UI-013). Out of combat mode it only stores; with the hero in combat mode it starts or ends the round. Refused (unchanged) while the hero's attack or cast plays: the original refuses when the PC runs an animation it cannot interrupt (whose goal priorities are not read; the viewer counts those two). A quick load that brings a round turns the setting on (and saves it). |
| Round start | R entering combat mode with the setting on starts the round (`ToggleHeroCombatMode` with the turn-based context); a round that begins stops the hero's walk and its approaches (the original interrupts animations at its start). Leaving combat mode (R, a dialog, a map change) ends it. The session's actor is placed on the hero's cell before each round call, so the scan square is around where the hero stands. |
| Scan | Every NPC of the shown map with its stored cell (less what the session destroyed or switched off and the companions away), the companions here at their live cells and the hero, in the original's scan order (TB13): sector rows from the top, sectors from the left, in a sector the critter attached last first. Critters that walked into a sector are attached after those loaded with it, the latest entry first; the load order of the others is approximated by their source order (terrain, static, then mobile objects, each in file order). An NPC's facts are its home map's stored fields: effective Speed in its session life state, life facts, hidden from drawing (object flag 0x100) and sleeping (critter flag 0x1000). |
| NPC subturns | An NPC's subturn ends at once (`EndCombatTurn`): the viewer has no AI, and the original ends the subturn of an NPC that starts no animation (TB5). The host passes at most 64 subturns per frame and stops after a new round began, so the event pump runs between rounds; each round end moves the clock 1000 game ms, and the timers then due (fatigue, poison, wake-up) fire on the next frame. |
| Hero's subturn | The hero keeps its subturn while it is active and has action points; once it is idle (settled, no walk, attack, cast or held stick) with no points left, or inactive, the completion check passes it on. E (on the release) ends it when the hero is idle (CT5). Clicks on another participant's subturn are ignored, as the original's world handler does; companions stand while a round runs (they would move on their own subturns by their AI). |
| Charges | In a round each step the hero starts charges its action points when it starts (walk 2, run 1 while it has fatigue; CT1), the attack's start its attack cost (CT2), a pickup 1, a body's loot and a door's or window's use 2, a cast 4 (the casting goal, ARC-MAGIC-014), a heal and a lock pick 4 (the item use; the original goes on whatever they answer) (CT3). A charge the points cover is paid; beyond them an active hero with fatigue above 1 overexerts (the action goes ahead); else the action does not happen and the turn passes on (TB9, CT4): a refused step stops the walk (`ROUTE_STOPPED_BY_TURN_BASED`), so it does not continue in a later turn. Another participant's subturn or an inactive hero refuses a step, an attack, a pickup, a loot or a use without a charge (a heal or a lock pick is charged by the session only on the hero's subturn and otherwise goes on). Leaving combat mode ends a running round even when the host cannot scan the map. |
| Status line | `turn-based: off`, `on, no round`, or `round N, hero's turn` / `<entity>'s turn`, the action points and the place in the order; combat mode reads `ON (turn-based)` during a round. |

Native smoke (map mode, the Crash Site cave, `ARC_AREA_MAP_SMOKE_ONLY=turn-based`, also in the map battery after the cast stage):
`ARC-COMBAT-020 native-smoke turn-based PASS`: Space without combat mode stores the setting; R begins round 1 with the NPC
first, whose subturn takes no press; a one-cell walk costs 2; E passes the turn and round 2 begins one game second later with
the grant again; next to the NPC the attack's start charges its cost; a long walk spends the rest, overexerts and is refused,
which passes the turn on; Space off ends the round and the event pump recovers the overexertion's fatigue as the clock flows.

## Not ready

- **No AI.** `SubturnStarted` and `SubturnEnded` tell the host to run the NPC's heartbeat, AI and end-of-combat scripts;
  the session's subturn hook (`RunAiSubturnHook`, 0x4B7010, ARC-WORLD-084, [AI scheduler](ai-scheduler.md)) runs the
  heartbeat and the pass through an `ISessionAiWorld` and ends the subturn when no animation runs, but no host implements
  that world yet (the AI lane's I10 takes over the NPC's subturn from the viewer host). With A9 the host runs them twice. An NPC never joins a round on its own (the AI's insert).
- **Charges not wired.** Reading, repairing, assembling and the other inventory and window actions charge nothing in a
  round; the get-up (A3) has no host; a reach-1 attack's adjacent move is charged step by step as the approach walks, not
  as one estimate; the path cost preview and the turn-based cursor are not shown (the HUD is alpha-gap N4).
- **No turn-based UI.** No end-turn button, action-point bar or participant list (N4); the status line only.
- **Approximations.** The scan's load order of objects loaded with the map is their source order; the interruptible test
  of the setting toggle counts the hero's attack and cast only; the hero and its companions start with no sector entry
  after a load.
- **Map changes.** A round is on the actor's map; the host ends it before a map change. The session does not refuse a
  map change during a round.
- **Fast turn-based mode** (several steps per goal) and the turn-based UI callbacks are not modelled.
