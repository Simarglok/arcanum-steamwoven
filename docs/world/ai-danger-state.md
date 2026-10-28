# NPC danger state: the setter, the target lock and the enemy list (ARC-WORLD-079)

Status: **Implemented bounded component**, ARC-WORLD-079, NPC combat AI plan slice
[I3](../research/npc-combat-ai-plan.md#implementation-slices-proposals-after-the-matching-research).
The danger state machine of the retail 1.0.7.4 executable ([core loop section 5](../research/npc-ai-core-loop.md#5-danger-state-machine),
stage A1; [enemy list and target lock](../research/npc-ai-hostility.md#6-enemy-list-and-target-lock), stage A3;
[ledger CL4, CL10](../research/ledger/npc-ai.md#npc-ai-core-loop-arc-res-054) and
[HS18](../research/ledger/npc-ai.md#npc-ai-hostility-and-targeting-arc-res-057), two readers, re-read by this task)
as pure rules in `Arcanum.Simulation.Ai.DangerRules` and per-NPC session state in
`Arcanum.Session` (`GameSessionDanger.cs`, snapshot schema 38; 40 since ARC-WORLD-082). No AI pass or host
calls it yet: the rules of slice I4 ([hostility and targets](ai-hostility.md), ARC-WORLD-080) decide its requests, and peace,
stop attacking and the attack event's writes are session operations there.

## Rules (`DangerRules`)

- **State** (`Read`, 0x4AAB00): `Fleeing` → FLEE, else `Surrendered` → SURRENDER, else `Fighting` →
  COMBAT_FOCUS, else NONE. The session's `SessionDanger.Source` is fleeing-from for FLEE and SURRENDER,
  the combat focus for COMBAT_FOCUS.
- **Bits** (`DangerFlags`, one set for both settings): `Fighting` (NPC flag 0x1), `CheckWield` (0x40),
  `CheckGrenade` (0x40000000), `BackingOff` (0x10000000), `DemaintainSpells` (0x1000000), `NoAttack`
  (0x20000000), `Fleeing` (critter flag 0x10), `Surrendered` (0x4000), `TargetLocked` (critter flag 2
  0x200000), `SpellFlee` (critter flag 0x10000, the flee-spell step of slice I5, [morale](ai-morale.md)). NO_FLEE, invulnerability, the leader's charisma, the global "NPC fighting" switch and the
  float callback are the caller's `DangerSetterFacts`.
- **Setter** (`Set`, 0x4AABE0) returns a `DangerTransition` in the original's rule order:
  1. invulnerable, nigh-invulnerable or the decoy → NONE (`DangerSetterRule.Invulnerable`);
  2. FLEE with NO_FLEE → COMBAT_FOCUS, skipping rule 3 (`NoFlee`);
  3. FLEE or SURRENDER under a leader of extraordinary charisma → COMBAT_FOCUS (`LeaderCharisma`);
  4. COMBAT_FOCUS with the switch off or `NoAttack` → NONE (`FightingOff`, `NoAttack`; FO8 of slice I4 reads it);
  5. FLEE or SURRENDER sets its bit and clears the other, asks for the fleeing line (only when
     neither was set and the host can float it), writes fleeing-from := target, and a surrender asks
     to leave combat mode; any other state clears both bits;
  6. COMBAT_FOCUS on a locked NPC stops here (`TargetLocked`): no NPC bit, focus or reaction change,
     but FLEEING and SURRENDERED are already cleared;
  7. COMBAT_FOCUS on an NPC not fighting sets `Fighting`, `CheckWield`, `CheckGrenade`, the
     enter-combat hook (point 13) and the alerted sound;
  8. COMBAT_FOCUS on a PC target lowers the NPC's hostility-variant reaction toward it to AI
     parameter 10 when above (`LowersReaction`), on every call;
  9. COMBAT_FOCUS writes focus := target, on every call;
  10. any other state clears `BackingOff` and, when fighting, clears `Fighting`, sets
     `DemaintainSpells` unless fleeing and runs the exit-combat hook (point 14, the requested target,
     maybe none, as triggerer).
  The network rule 0 does not apply (single player, CL3).
- **Enemy list**: `SkipsEnemy` (0x4AF930) skips an entry whose PC leader (itself for a PC) is the
  owner's, or one already listed; `RemoveEnemy` (0x4AF9D0) removes every equal entry, the last entry
  filling the hole, so the order is not kept.

## Session (`GameSessionOwner`)

`SessionDanger` per NPC entity: `Flags`, `Focus`, `FleeingFrom`, `HitBy` (who-hit-me-last) and
`Enemies`, each handle a `SessionCombatant` (the hero or an object by entity id). Every operation is
one revision or a typed refusal (`InvalidDangerTarget`, `InvalidDangerFacts`, `StaleVersion`,
`BudgetExceeded`, `RevisionExhausted`); a record back at the baseline is dropped.

| Operation | Original | What it does |
| --- | --- | --- |
| `SetDangerState(request)` | setter 0x4AABE0 | The rules above under the active combat setting, applied together: bits, fleeing-from, focus; rule 8 through the reaction owner (the hostility-variant reaction with the session's enemy list, the delta through the PC's effects, a follower's leader check armed as any reaction drop does), needing `SessionDangerRequest.Reaction` for a fight on the hero; a surrender of an NPC in combat mode runs the toggle's refusals (`SessionDangerRequest.CombatMode` facts needed) and, when it acts, unlocks the target and clears the combat-mode bit (kept in the Original setting, CL4). Returns the transition, the hook (`SessionDangerHook`: point, triggerer, attachee), `LeftCombatMode` and the reaction adjustment. |
| `LockDangerTarget(request)` | lock 0x4AFBD0 | Unlock, the setter for COMBAT_FOCUS on the target with no sound, and the lock only when the NPC then reads COMBAT_FOCUS on that target (`Locked`). |
| `UnlockDangerTarget` | unlock 0x4AFC70 | Clears the lock. |
| `AddDangerEnemy`, `AddDangerEnemyTeam` | add 0x4AF930, team 0x4AF8C0 | The PC leader of an NPC is the hero when it follows or waits for it; the hero's team is the hero and then its follower array in order, any other head alone. |
| `RemoveDangerEnemy` | remove 0x4AF9D0 | As `RemoveEnemy`. |
| `SetDangerHitBy` | field 0x11C | Who-hit-me-last (the attack event and peace of slice I4 write it). |
| `AllowAttacking` | 0x4AA580 | `NoAttack` cleared for an NPC led by the hero, or for every NPC of the hero's follower array. |
| `QueryDanger`, `QueryDangerRecords` | | A record (baseline when none) and the detached domain. |

The combat-mode toggle (ARC-COMBAT-004) now also unlocks an NPC's target when it leaves combat mode,
also on death (0x4B411C), and allows the hero's followers to attack when the hero enters combat mode
(0x4B42B2).

Budgets: `GameSessionLimits.MaxDangerRecords` (8,192, as life and reaction) and
`MaxDangerEnemies` (65,536 entries of all lists together; the original's lists have no limit).

## Snapshot schema 42

`"danger":{"records":[{"entity","flags","focus","fleeingFrom","hitBy","enemies"}]}` after
`concealment`, records in ordinal entity order, flag names in bit order, each handle the entity id,
`""` for the hero (never a valid entity id) or `null` for none. Restore refuses a baseline record,
an undefined bit, an invalid handle, a repeated enemy and the budgets. Schema 40 (ARC-WORLD-082) adds the flag name
`SpellFlee`, schema 42 (ARC-WORLD-084, [AI scheduler](ai-scheduler.md)) `CheckWeapon`, schema 45 (ARC-WORLD-086,
[AI executor](ai-executor.md)) `NoFlee`, schema 47 (ARC-WORLD-088, [AI support](ai-support.md)) `LookForAmmo`, `LookForWeapon` and
`LookForArmor`; schemas 1..46 are refused, not migrated.

## Settings

| ID | Original 1.0.7.4 (switch on) | Steamwoven | Switch |
| --- | --- | --- | --- |
| CL4 | A surrender's combat-mode exit is undone for the bit: the setter writes back critter flags read before, so the NPC still counts as in combat mode (the target unlock stays done). | The bit stays cleared. | `CombatAnomalyProfile.SurrenderKeepsCombatModeFlag` |
| FO14 | The setter keeps the target lock when it applies NONE, FLEE or SURRENDER; a locked NPC whose flight ended in NONE then gets COMBAT_FOCUS back with no write. | The setter clears the lock with any state other than COMBAT_FOCUS. | `CombatAnomalyProfile.LockedNoneStateFights` |

## Not ready

- **Callers.** The AI pass (slice I6, [AI scheduler](ai-scheduler.md)) runs the danger update only as a host stage, and no host or follower order (I9) calls the setter yet (morale, I5, decides its flight requests), and nothing calls the danger
  update, attack event, reaction to an attack, kill-on-sight or scan rules of slice I4; the viewer shows nothing.
  The hosts run no enter or exit combat script: the hook is returned, and the dispatcher's combat
  hooks (S7) are to carry it. The start and end combat points (15, 16) run in I6's gate through the host (`ISessionAiWorld.RunScript`).
- **Hook order.** The host runs the hook after the call, so a script sees the focus and reaction
  already written; the original runs the enter-combat script before rules 8 and 9.
- **Peace and stop attacking** (0x4AA300, 0x4AA4A0), the only writers of `NoAttack` besides a restore, FO7 and FO8 landed with
  slice I4 ([hostility and targets](ai-hostility.md), ARC-WORLD-080), as did the enemy-list pick and target validity; the
  reaction owner's queries now read the session's list.
- **Not read by other owners yet.** The exit gate's threats (`SessionCombatThreat`) still come from the host; the combat
  category refresh after an add (0x4B80E0) is presentation.
- **Data.** NO_FLEE, invulnerability and the leader's charisma are caller facts; spells and scripts that
  write them (MR13, EX12, SU17) are later slices'.
