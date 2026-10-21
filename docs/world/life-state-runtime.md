# Life state runtime (ARC-COMBAT-002)

Status: **Implemented bounded component, not wired.** Work item ARC-COMBAT-002 (2026-10-17), slice 1 "vital state" of
the [combat core research](../research/combat-core-evidence.md#proposed-implementation-slices-project-policy-not-accepted)
(ARC-RES-030). Requirements: [FR-COMBAT-010](../requirements/combat-magic-tech.md#fr-combat-010) (fatigue, knockout
and death stay separate outcomes) and the state part of [FR-CHAR-026](../requirements/characters-skills.md#fr-char-026)
(death follows from current hit points).

Two layers, both Godot-free:

- `Arcanum.Simulation.Combat.LifeRules`: pure functions over one critter's `LifeState` (hit-point damage, fatigue
  damage, poison level, stun and injury conditions, scars) and its `LifeFacts` (pools, effective stats, traits).
- `GameSessionOwner` life domain: the hero's life and every NPC life the session changed, with the session's version
  guard, revision, seeded rolls and snapshot schema 8 (schema 9 since ARC-WORLD-024 adds the timers' event queue).

Numbers and order are a reading of the original's functions at the cited addresses, evidence level F0 (not checked in
the executable) unless the [verification ledger](../research/original-verification-ledger.md) lists them. The fatigue
recovery step was checked in the original 1.0.7.4 executable (`Arcanum.exe` at 0x45E610, read-only `llvm-objdump`):
elapsed seconds `>> 3`, `/ 10`, at least 1, times the HealRate stat.
Everything else keeps original-runtime parity **NOT_VERIFIED**.

## Source rules

| Rule | Source |
| --- | --- |
| Max HP = `4 × HP points + HP adjustment + WP + 2 × (ST + level) + 4`; current = max − HP damage; dead = current ≤ 0 | the hit-point functions (0x43D410, 0x43D430, 0x43D4C0, 0x43D4E0, 0x43D510, 0x43D530, 0x43D5A0, 0x43D600, 0x43D630), the critter state tests (0x45D790, 0x45D800, 0x45D870, 0x45D8D0) |
| Max fatigue = `4 × fatigue points + adjustment + 2 × (level + CN) + WP + 4`; unconscious = current fatigue ≤ 0 and not undead; active = neither, not stunned, paralysed or stoned | fatigue (0x45D5A0, 0x45D670, 0x45D700), states (0x45D790, 0x45D800, 0x45D870, 0x45D8D0) |
| Poison lowers ST and DX by `min(3, poison / 100)` before effects; crippled legs lower speed by 5 | the effective stat getter (0x4B0490) |
| The poison level is 0..1000 and set to 0 for a critter with extraordinary CN, on every write | the base stat setter (0x4B0980) |
| Damage step: undead/mechanical take no poison, stoned take no HP/fatigue/poison; an invulnerable or dead critter takes nothing; then stun (not with a knockdown/knockout, and since ARC-COMBAT-009 only on a critter that can act: not unconscious, paralysed or stoned, ledger CR12), knockdown, knockout (`fatigue damage = max fatigue + 10..20` while conscious), scar, blind, arm (first, else second at d100 ≤ 50; wielded weapon and shield drop), legs, HP damage (≥ 0), fatigue (positive, not fatigue-immune), poison; a critter now dead dies | combat damage (0x4B4390); the critical-effect tables that set these effects are ARC-COMBAT-009 ([attack resolver](attack-resolver-runtime.md#critical-effects-arc-combat-009)) |
| Death: HP damage pinned at 32,000, death time stored, dying animation plain for a kill or ≤ 20 damage, else head, leg or body by location | combat damage (0x4B4390), the kill notification (0x45DA20) |
| Kill: the damage step with the death flag (current HP as damage, then the poison write), then HP damage 32,000 even when invulnerable; a critter the poison write brings back above 0 HP gets no death step but the pin | the kill (0x45D900), combat damage (0x4B4390) |
| Heal: five scars without the full flag; a dead critter only with resurrect (refused for unrevivifiable + reanimate or unresurrectable without it; clears stun, fatigue damage 10); full heal (all HP, all fatigue when asked, five scars, blind and crippled cleared) or single cures; then HP, fatigue, poison while there is damage | the heal (0x4B58C0) |
| Fatigue recovery event: `HealRate × max(1, elapsed s / 80)`, not for the dead | fatigue event (0x45E610) |
| Resting: `HealRate × hours` HP and three times that fatigue; the hourly NPC event uses `max(1, elapsed s / 3600)` hours | rest (0x45E910, 0x45EA00) |
| Poison ticks: damage 1 / 2 (≥ 200) / 3 (≥ 550) normal damage every 15 s through the damage step without a weapon; recovery −PoisonRecovery every 120 s | poison event (0x4B1170, 0x4B1310, 0x4B1350) |

Anomaly A5 (ARC-RES-030): healing crippled legs clears crippled legs, not blindness, in the original 1.0.7.4
([anomaly decisions](../research/npc-combat-ai-plan.md#anomaly-decisions)). Both rule settings follow the original, so
there is no switch.

## Simulation API

| Entry | Contract |
| --- | --- |
| `LifeState(HitPointDamage, FatigueDamage, PoisonLevel, Conditions, Scars)` | Damage 0..1,000,000, poison 0..1000, conditions `Stunned`, `Blinded`, `CrippledArmOne`, `CrippledArmsBoth`, `CrippledLegsBoth`, scars 0..64. `LifeState.Unharmed` is all zero. |
| `LifeFacts` | Pools (points 0..5000, adjustments ±5000, so every maximum stays under 32,000), level 0..51, effective ST (without the poison penalty), `PoisonedStrength` (ST under penalty 1, 2, 3 when effects make it differ from `max(1, ST − penalty)`), CN, WP, HealRate, PoisonRecovery (0..100), normal resistance (±1000) and `LifeTraits` (undead, mechanical, fatigue-immune, stoned, paralysed, invulnerable, nigh-invulnerable, unrevivifiable, unresurrectable). |
| `LifeRules.Evaluate(state, facts)` | `LifeStatus`: max and current HP and fatigue, dead, unconscious, active. |
| `LifeRules.ApplyDamage(state, facts, LifeDamage, rolls)` | The damage step above. `LifeDamage.FromAttack(AttackResolution)` builds the packet for the resolution's recipient (null when refused, missed or prevented); the resolver already applied resistance, difficulty and the critical multiplier. Rolls: `KnockoutFatigue` (10..20) and `CrippleBothArms` (d100), drawn only when the source draws them. |
| `SkillHeal(state, facts, hitPoints, cure)`, `FateFullHeal(state, facts)` | The heal skill's outcome (ARC-SKILLS-005: a cure removes one scar and the first injury, also on a dead critter, then the hit points heal as `Heal` heals them) and the full-heal fate (ARC-WORLD-054). |
| `Kill`, `Heal(LifeHeal)`, `RecoverFatigue(steps)`, `RestHeal(hours)`, `PoisonDamage`, `PoisonRecoveryTick`, `ClearStun` | The other transitions above. `FatigueRecoverySteps(scheduledAt, now)` and `RestingHours(scheduledAt, now)` (at most 87,600, the `RestHeal` bound) turn an event's scheduling time into steps in whole game seconds, as the source counts them. Consciousness events are not reported for a critter that is dead afterwards. |
| `LifeOutcome(Before, After, Events, HitPointDamage, Death)` | `LifeEvents` tell the host what to show and other owners what to do: already dead, stunned, knocked down or out, scarred (or scar limit), blinded, crippled arm/both arms/legs, wielded items dropped by an arm hit, lost or regained consciousness, died, resurrected or refused, stun ended. |

Inputs out of range throw `ArgumentException`; the session checks them first and returns typed failures.

`CharacterStatContext` gained `PoisonLevel` (0..1000) and `CrippledLegs`; `CharacterStatsOwner.Query` applies the
penalties before effects, so a hero's derived stats (carry weight, damage bonus, AC, speed) follow its life state when
the caller passes the hero's poison level and legs (`QueryHeroLife().Record.State`).

## Session API

| Entry | Contract |
| --- | --- |
| `SessionLifeTarget.Hero` / `SessionLifeTarget.Critter(entity, facts, loaded)` | The hero, or an NPC by entity id with the host's facts and the state its map data loads with (stored HP and fatigue damage, poison level, injury flags). The loaded state counts only until the session holds a record for the entity. |
| `QueryHeroLife()` / `QueryLife(target)` / `QueryLifeRecord(entity)` | `SessionLifeView(Record, Status, Recorded)`; null without a hero; the stored record or null for an NPC the session never changed. |
| `ApplyDamage`, `Kill`, `Heal`, `RecoverFatigue(scheduledAtGameMs)`, `RestHeal(hours)`, `PoisonDamageTick`, `PoisonRecoveryTick`, `ClearStun` | Take the expected `GameSessionVersion`. Order: `InvalidLifeTarget` (no hero, bad entity id), `InvalidLifeFacts`, `InvalidLifeState` (loaded state), `InvalidLifeAmount` (amounts, undefined effect bits or location, hours, a recovery scheduled after the clock), `StaleVersion`; then the step on a trial copy of the generator (`RandomExhausted` when it runs out); a step that changes nothing is NoChange and still returns its outcome (a knockdown on a new NPC with the record budget full still reports `KnockedDown`); then `EventBudgetExceeded` (since ARC-WORLD-024: the step's new timers do not fit in the event queue, or their deadline would pass the clock's range; the queue is sized so that the first cannot happen under the policy limits), `BudgetExceeded` (a new NPC record over `MaxLifeRecords`, policy 8192 since ARC-DATA-029) and `RevisionExhausted`. Since ARC-WORLD-024 the session's event queue fires `RecoverFatigue`, `RestHeal` (for NPCs), `PoisonDamageTick` and `PoisonRecoveryTick` itself when the host runs a pass; a host that also calls them for the same timer applies the step twice. A refusal draws nothing; a change adds one revision. A death stamps the record with the clock's game time. `SessionLifeTarget.Critter` throws on a null entity id or facts, so an unresolved id never reaches the hero. |

The hero's facts come from the character owner: level, effective ST (and under each poison penalty), CN, WP, HealRate,
PoisonRecovery, with crippled legs as a query condition; pools are 0, because every pre-made PC prototype stores no HP
or fatigue points or adjustment (all 13 checked on the installed original, slots 26/27 and 42/43), and buying them
belongs to levelling. The hero has no traits and no resistance yet (no worn armour).

Rolls come from the session generator as `combat.KnockoutFatigue` and `combat.CrippleBothArms`, so a fight resolved with
`CombatRolls` and applied here replays from a save. A step draws on a copy of the generator that the session adopts
only when the step runs to the end; a refusal draws nothing. As in the source, a step that rolls but changes nothing
(the second arm roll fails on a critter whose state is otherwise unchanged) still spends its draw.

## Snapshot schema 8

`life` is encoded between `random` and `lighting` (since schema 9 `events` follows it,
[event queue](game-event-queue-runtime.md#snapshot-schema-9), and since schema 10 `reactions` comes between them,
[NPC reaction](npc-reaction-runtime.md#snapshot-schema-10)):

```json
"life":{"hero":{"hpDamage":4,"fatigueDamage":6,"poison":0,"conditions":[],"scars":0,"deathTimeMs":null},
        "critters":[{"entity":"oid:wolf","hpDamage":32000,"fatigueDamage":0,"poison":0,
                     "conditions":["CrippledLegsBoth"],"scars":1,"deathTimeMs":54005000}]}
```

`hero` is an object exactly when the session has a hero. `critters` are in ordinal entity order, at most 8192.
`conditions` are names in flag order, each once. Decoding refuses unknown or misspelled names and out-of-range numbers
(`OutOfRange`), unordered or repeated conditions and entities (`NotCanonical`), more than five conditions or 8192
records (`BudgetExceeded`), wrong types, missing and unknown fields.
Restore refuses a hero record without a hero or the reverse (`InvalidSnapshot`), a death time after the saved clock
(`InvalidLifeState`), an invalid entity id (`InvalidLifeTarget`) and more records than the caller's limit
(`BudgetExceeded`).

**Migration decision: none.** Schemas 1..7 are `UnsupportedSchema`. A schema-7 quick save loads as "written by an
incompatible version" with the running session kept, and the next F7 overwrites it. No host kept any life state
before this schema, so nothing is lost.

## Not implemented

- No host calls it: the viewer applies no damage, the attack resolver's output is applied only in tests.
- Timers: since ARC-WORLD-024 the 80-second fatigue recovery, the 15/120-second poison ticks and the hourly NPC resting
  heal are scheduled by every life step on the session's game event queue and saved with it
  ([session wiring](game-event-queue-runtime.md#session-wiring-arc-world-024)); no host runs the pass yet. Body decay,
  encumbrance fatigue damage and the combat-focus wipe have no timer.
- What other owners do on these events: alignment on a kill (the kill's experience is the session's
  [experience](experience-runtime.md) `RecordNpcKill`, ARC-WORLD-030, which a host calls), the party leaving of a dead follower,
  AI focus and near-death reactions, spell demaintenance on unconsciousness, item drops and wear, the death slide and
  return to the main menu for a dead PC, logbook injuries.
- The dying and taking-damage scripts (no SCR runtime). The raise script is a port of `LifeRules.Heal` and the session's
  `Heal` since ARC-MAGIC-012 (asked even when the target's flags refuse the raise; either refusal stops it); the host runs it.
  The session asks it while computing the step, before the call's later refusals, so it must have no side effects and must not
  call the session.
  A damage packet's `PlainDeath` (a spell's death flag) picks the plain dying animation ([spell damage and
  heal](spell-damage-heal.md)). Encumbrance fatigue, the scarring effect's stat change
  (only the count is kept), Tempus Fugit and background stat adjustments. Effects on maximum HP or fatigue and their damage
  compensation came with [durable effects](durable-effects-runtime.md) (ARC-WORLD-029): `LifeFacts.MaxHitPointEffects` and
  `MaxFatigueEffects` apply to the whole sum, and facts whose maxima reach the pinned death damage are invalid; the session
  fills them for its hero only.
- Hero pools from levelling (`maxhps`, `maxfatigue` purchases; a level itself raises both maxima through the formulas
  since ARC-WORLD-030). Six pre-made PC prototypes (016093..016098) store fatigue damage
  2 or 4; whether a new PC keeps it was not verified, and the session starts every hero unharmed.

## Tests

- [`LifeRulesTests`](../../tests/Arcanum.Simulation.Tests/Combat/LifeRulesTests.cs): maxima, poison strength, death at
  exactly 0 HP and the dying animations, knockout without death, undead never unconscious, stoned and immune critters,
  stun and knockdown, both arms, scars, poison bounds and extraordinary constitution, a death caused by the new poison
  level, heals and cures (A5), resurrection, recovery and rest steps, poison ticks, kill, the attack packet, input
  bounds.
- [`ConditionContextTests`](../../tests/Arcanum.Simulation.Tests/Character/ConditionContextTests.cs): poison and crippled
  legs in the character owner, before effects and the clamp.
- [`LifeStateTests`](../../tests/Arcanum.Session.Tests/LifeStateTests.cs): the hero's start and poison, a resolved attack
  applied to an NPC, the loaded state, a knockout replayed from a save, death time and the canonical snapshot text,
  heal/rest/recovery on the session clock, kill, stun and resurrection, refusals before any roll, the record budget,
  an exhausted generator, revision capacity, decode and restore failures, disposal.

```sh
"$DOTNET" test tests/Arcanum.Simulation.Tests/Arcanum.Simulation.Tests.csproj --no-build --filter "FullyQualifiedName~LifeRules|FullyQualifiedName~ConditionContext"
"$DOTNET" test tests/Arcanum.Session.Tests/Arcanum.Session.Tests.csproj --no-build --filter "FullyQualifiedName~LifeState"
```

