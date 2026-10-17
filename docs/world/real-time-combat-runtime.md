# Real-time combat runtime

Status: **Implemented bounded component**, ARC-COMBAT-004 (2026-10-17). Slice 4 of the
[combat core research](../research/combat-core-evidence.md#proposed-implementation-slices-project-policy-not-accepted)
(ARC-RES-030, C1–C5): the per-critter combat mode with its toggle, exit gate and turn-based switch, the attack
animation's cadence in real time (rate from Speed and weapon, pause from weapon speed, the action-frame hit, the pistol,
rifle and bow repeat shot), the auto-attack repeat, and the fatigue charge of running in combat mode. The rules are pure
functions in `Arcanum.Simulation.Combat.RealTimeCombatRules`; combat mode is a domain of the
[game session](game-session-runtime.md) (snapshot schema 15); the AREA viewer toggles it with the original R key.
Requirements: [FR-COMBAT-001](../requirements/combat-magic-tech.md#fr-combat-001) (mode and switching),
[FR-COMBAT-012](../requirements/combat-magic-tech.md#fr-combat-012) (the auto-attack part) and
[FR-RUNTIME-003](../requirements/world-travel-runtime.md#fr-runtime-003) (the attack's action frame runs its hit once). The hero's attack in the viewer is
[ARC-COMBAT-005](hero-attack-runtime.md); AI and NPC fights are not part of it ([Not ready](#not-ready)).

## Evidence

- CE at the pinned commit `a7ff41b300ef712f0e7d088183a3d08957110cdd`: `combat.c` (toggle, exit gate, turn-based switch,
  auto-attack setting, `combat_init`), `anim.c` (attack begin and update, the Speed-scaled rate, the frame pause, the
  attack and attempt-attack goal tables and their runner, the auto-attack check, the run step), `item.c` (weapon
  speed), `critter.c` (the fatigue write), `ui/intgame.c` (the combat-mode key), `ui/dialog_ui.c`, `ui/sleep_ui.c`,
  `ui/wmap_ui.c` and `teleport.c` (leaving combat mode).
- The retail 1.0.7.4 `Arcanum.exe` (SHA-256 `9bd80511…78d52`), read statically by two independent readers per the
  [original executable check](../research/original-executable-check.md); the rows RT1–RT12 are in the
  [verification ledger](../research/original-verification-ledger.md#real-time-combat-arc-combat-004). The original does
  what CE reads except three CE slips: the `auto attack` default (RT6), Speed 8 (LA1, RT10) and the invisibility test of
  the attack start (RT8). None is a bug of the original, so there is no rule-mode switch.

## Rules (`RealTimeCombatRules`)

| Rule | Value |
| --- | --- |
| Toggle | a dead critter skips every refusal; else stoned and paralysed refuse, stunned refuses leaving, inactive (unconscious, stunned, paralysed, stoned) refuses entering, an NPC in its concealed animation refuses (`ToggleRefusal`) |
| Exit gate | out of combat mode or not active: open; attempting an attack: closed; an active, fighting NPC in the vicinity with the critter or one of its followers as combat focus: closed (`CanExitCombatMode`) |
| Attack rate | ART rate for a non-critter ART, a body-of-element spell or a weapon slot without an entry; else (middle, low, high) per slot: attack unarmed/sword 15/7/23, dagger/bow 10/6/14, axe/mace/two-handed 14/8/20, pistol 8/3/12, rifle 6/3/10, staff 12/8/16; low attack unarmed 13/8/18, dagger/bow/staff 10/6/14, sword 14/8/20, axe/mace 11/7/15, pistol/rifle 6/3/10, two-handed 13/9/17; Speed < 8 `low + Speed·(middle − low)/8`, 8 the middle, < 30 `middle + Speed·(high − middle)/30`, else high; no dwarf or halfling offset (`AttackFramesPerSecond`) |
| Frame pause | `clamp(1000 / rate, 30, 800)`, then `− 10 × (weapon speed − 10)` and clamped to 30..800 again; weapon speed is `TurnBasedRules.WeaponSpeed` (10 unarmed) (`AttackFramePauseMs`) |
| Playback | the start shows frame 0 and marks the action for action frame 0; updates follow at once and then one pause apart: a stoned, paralysed or stunned attacker or a shown last frame ends it; the frame before the action frame marks the action; the update that shows the action frame runs the hit once (the host first checks range and line of fire, else `Abort`); frame `f ≥ 1` appears at `(f − 1) × pause`, the end one pause after the last frame (`Begin`, `Update`, `Schedule`) |
| Repeat shot | in real time, a pistol or rifle two frames after the action frame, or a bow on it, goes back three frames and turns to the target while it has ammunition, the attacker is a PC or an NPC not backing off and the target is valid: another hit every four pauses |
| Auto attack | the goal starts again on the same target 5 ms after it ended for a PC in combat mode with the `auto attack` setting, no round, a target neither destroyed, off, dead nor busted, no stop flag, no non-lethal flag and no boomerang in flight (`Repeats`, `RepeatDelayMs`); the setting's original default is on (`AutoAttackDefault`), turn-based off |
| Attack sound | a critter's attack start rolls 1..4 and plays its attacking sound on 1 (`AttackSoundSides`; a draw a host must make for seeded parity) |
| Run charge | every fifth run step of one move in combat mode (×4 fatigue-limiting, then ÷4 at least 1 draining) adds 1 fatigue damage; a runner without fatigue walks (`RunChargePeriod`, `RunStep`, `LifeRules.ChargeFatigue`) |

Project guard: a repeat shot that would go before frame 0 (a bow with action frame below 3, a gun with action frame 0)
does not happen; the original would set an out-of-range frame.

## Session API (`GameSessionOwner`)

| Entry | Contract |
| --- | --- |
| `QueryCombatMode(combatant)`, `QueryCombatModes()` | The hero's flag (false without a hero) and the NPCs in combat mode. |
| `SetCombatMode(expected, combatant, active, context)` | The source's activate/deactivate: NoChange when already so, else the toggle; a rule refusal is NoChange with `SessionCombatModeRefusal`. An NPC needs `context.Facts` (its stoned and paralysed traits are read from them); `context.Concealed` is its concealed animation. The hero entering with `context.TurnBased` starts a round through `context.World` (`StartCombatRound`, its own revision first); a start the rules refuse (`CombatUnavailable`) leaves the hero in combat mode with no round, as the original; any other refusal of the round call changes nothing. Leaving with `TurnBased` ends the round. |
| `ToggleHeroCombatMode(expected, context, exit)` | The R key: a dead hero `HeroDead`; in combat mode the exit gate (`UnderAttack`) then leaving; else entering. Needs a hero. |
| `LeaveHeroCombatMode(expected, context, exit)` | Before a dialog, sleep or the world map: NoChange out of combat mode, `UnderAttack` through the gate (the caller does not start), else the deactivation (a stunned hero stays and the caller goes ahead, as the source). |
| `QueryCanExitCombatMode(exit)` | The gate for the hero: `exit.AttemptingAttack` and `exit.Threats` (focus, active, fighting); a focus on the hero or a following companion (not a waiting one) counts. |
| `SwitchTurnBased(expected, current, requested, world, interruptible)` | The setting's callback: `Store` says whether the host keeps the new value; with the hero in combat mode the round starts or ends. |
| `ChargeCombatRun(expected, target)` | One run charge for a critter in combat mode (`CombatModeInactive` otherwise): fatigue damage + 1 through the life domain, which schedules the fatigue recovery and ends a subturn on a knockout. |

A death through the life domain leaves combat mode (the source's death notice), in the same revision; the hero's death in
combat mode during a round also ends the round, as the source's toggle does for the PC with turn-based on.

## Snapshot schema 15

`combatMode` follows `combat`:

```json
"combat":null,"combatMode":{"hero":true,"critters":["oid:bandit","oid:wolf"]},"events":{...}
```

`critters` are in ordinal order, unique, at most 4096. Decoding refuses misordered or repeated entities (`NotCanonical`),
wrong types, missing and unknown fields; restore refuses a hero flag without a hero (`InvalidCombatMode`).

**Migration decision: none.** Schemas 1..14 are `UnsupportedSchema`; a schema-14 quick save loads as "written by an
incompatible version" and the next F7 overwrites it. No host kept combat mode before.

## Viewer (AREA)

- R (the original Ready/Unready key) toggles the hero's combat mode in real time; the status line shows
  `combat mode: ON (real time)` or `off` with the hero's fatigue. Without `--hero` it says combat mode needs a hero.
- A dialog start leaves combat mode first and does not start while the hero is under attack; a map change (the cave
  transfer) leaves combat mode, as the source's teleport.
- Running in combat mode charges one fatigue every fifth run step of a move; a hero out of fatigue walks. Native smoke:
  `ARC-COMBAT-004 native-smoke combat-mode PASS` (exterior with `--hero`, after the run stage).

## Not ready

- The hero's attack is the next slice, ARC-COMBAT-005 ([hero attack runtime](hero-attack-runtime.md)): the combat-mode
  click on a living NPC, the attack cursor and ART, damage, death and loot, played by these rules. Ranged attacks and the
  repeat shot have no host yet.
- AI: NPC combat mode, combat focus and fighting (the exit gate's threats are always empty in the viewer), the attacked
  NPC's AI pass, NPC attacks.
- The turn-based setting in the viewer (Space) and its round world; the session supports both.
- The event-queue pump in the viewer: fatigue spent running in combat mode does not recover there (quick load resets);
  an unconscious hero still moves (movement does not read life yet).
- The viewer leaves combat mode before its own dialog-start checks (the source checks the speaker first); a dialog that
  then fails to start leaves the hero out of combat mode.
- Encumbrance, fatigue-limiting and fatigue-draining heroes, the stunned-animation activation (a no-op for an inactive
  critter), combat music, the arms-away art, the combat toggle effect, invisibility, multiplayer branches.
- Original-runtime parity of the cadence: NOT_VERIFIED (static reading only).

## Tests

`tests/Arcanum.Simulation.Tests/Combat/RealTimeCombatRulesTests.cs` (toggle and gate tables, rate tables for every slot
and both animations, pause clamps, the playback plan, repeat shots, the repeat check, the run charge),
`LifeRulesTests.AFatigueChargeAddsToTheDamageWhateverTheImmunities` and
`tests/Arcanum.Session.Tests/CombatModeTests.cs` (toggle refusals, death, NPC facts, exit gate with followers, the
turn-based round start and end, the setting switch, run charges, schema 15 round trip and refusals); synthetic only.
