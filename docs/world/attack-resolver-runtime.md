# Attack resolver runtime (ARC-COMBAT-001)

Status: **Implemented, not wired.** Work item ARC-COMBAT-001 (2026-10-16), slice 2
of the [combat core research](../research/combat-core-evidence.md#proposed-implementation-slices-project-policy-not-accepted)
(ARC-RES-030). Requirements touched:
[FR-COMBAT-004](../requirements/combat-magic-tech.md#fr-combat-004) (target
validity, partly), [FR-COMBAT-005](../requirements/combat-magic-tech.md#fr-combat-005)
(hit, dodge, miss, critical) and
[FR-COMBAT-008](../requirements/combat-magic-tech.md#fr-combat-008) (typed
damage, immunities, resistances, portal and container material).

`Arcanum.Simulation.Combat` resolves **one attack** as a pure function:

```csharp
AttackResolution AttackResolver.Resolve(
    AttackRequest request, ICombatRollSource rolls,
    ICriticalEffects? criticalEffects = null, CombatAnomalyProfile? profile = null);
```

It reads only its arguments, changes nothing, and returns the outcome, the
difficulty parts, both skill checks, the fate used, the damage packet and every
roll it made. Numbers are the CE reading at
`a7ff41b300ef712f0e7d088183a3d08957110cdd` (the research's C15–C21 and the
"Hit, dodge and critical" steps), re-read for this task in `combat.c`,
`skill.c`, `item.c`, `object.c` and `random.c`; no CE code or structure was
copied. Original executable parity is **NOT_VERIFIED**, except for the
anomaly switches below, which were checked against the original 1.0.7.4
executable.

## Inputs

- `AttackRequest`: attacker and target (`Combatant`), target kind (critter,
  portal, container, wall, other object), `CombatWeapon`, skill (melee, bow,
  throwing, firearms) with the attacker's effective level 0..20 and training,
  optional aimed location (head, arm, leg; the torso means "not aimed"),
  backstab kind, distance, cover rating, blocked line of fire, "target was in
  bed", "target unaware" and the game difficulty.
- `Combatant`: every value is **effective** (stats, effects, worn items and
  aptitude scaling already applied by the caller): PC flag, level, ST, PE,
  extraordinary IN, DamageBonus, magick/tech aptitude, AC 0..95, resistances,
  condition flags (dead, unconscious, paralysed, stunned, stoned, sleeping,
  falling, blinded, crippled arms/legs, undead, mechanical, fatigue immune,
  limiting, draining, invulnerable, nigh-invulnerable, magnetic inversion,
  dark sight), own light and the light at its cell, dodge and backstab
  levels and training, additive stand-ins for the four critical chance
  effects, the unarmed body spell, armed fates and the object material.
- `CombatWeapon`: kind (unarmed or natural attack, weapon, thrown non-weapon
  item), base damage per type, range, to-hit, minimum ST, complexity, the
  magic adjustments (dropped when the weapon's magic is cancelled), condition,
  ignore-resistance, damage-armour and uber flags, critical charts.
  `CombatWeapon.Unarmed(gauntletBonus)` and `CombatWeapon.Natural(table)`
  build the source's bare-hands and monster attacks.
- `ICombatRollSource`: the injected randomness, `Roll(purpose, min, max)`
  inclusive. A seeded source replays a fight exactly; tests script each roll.
  `GameSessionOwner.CombatRolls` is the seeded session generator as such a
  source, its rolls labelled by purpose ([session randomness](session-random-runtime.md),
  ARC-WORLD-019); nothing calls the resolver with it yet.
- `ICriticalEffects`: the critical-effect tables of FR-COMBAT-009 (stun,
  knockout, knockdown, crippling, blinding, scars, drops, weapon and armour
  damage; weapon destruction and ammunition loss on a critical miss). The
  default `NoCriticalEffects` returns none.
- `CombatAnomalyProfile`: the two rule settings of owner decision D8,
  `Steamwoven` (the original plus the recorded D2 fixes; the default when
  `profile` is null) and `Original` (the retail 1.0.7.4 behaviour), with stable
  names `steamwoven` and `original` (`FromName`, `All`); see below.

Helpers in `AttackRules` expose each formula on its own: effectiveness, dodge
effectiveness, critical hit and miss chances, true-backstab bonus,
technology-failure and magick-cancel chances, light penalty, armour class,
backstab eligibility, weapon damage range, material reduction and the hit
location table.

## Resolution order

The combat rolls are made at the same points and in the same order as in the
source, except where a D2 fix removes one; a damage range with equal bounds
draws nothing, like the source's `random_between`. CE draws from the same
generator outside these rolls (the AI's reaction to the attack, weapon sounds,
the effect tables' stat checks), so a seeded run matches a CE run only once
those draws are modelled as well.

1. **Refusal** without rolls: an inactive attacker (dead, unconscious,
   paralysed, stunned, stoned) or a dead target critter.
2. **Location**: aimed, or rolled (d100 ≤ 70 torso, ≤ 85 leg, ≤ 95 arm, else head).
3. **Technology failure** roll (a technological weapon in magickal hands; not
   for bare hands or throwing): a critical miss.
4. **Magick cancel** roll against the target's opposing aptitude: a magickal
   melee weapon swings without its magic, anything else misses.
5. A non-critter target is **hit** without a roll.
6. **Attack check**: hit when `difficulty + d100 ≤ effectiveness`; a second
   d100 decides the critical (hit: effectiveness/20 + aim + weapon magic +
   true backstab + effects; miss: (100 − effectiveness)/7 + wear + magic +
   effects, at least 2; a melee master never fails critically). The difficulty
   parts are reported: invocation (bed), weapon (to-hit, too weak), awareness
   (paralysed, unconscious, falling or stunned, sleeping, unaware), aim (⅔
   for firearms experts), armour (`effectiveness × (AC/2)/100`, skipped by a
   trained true backstab), range (out of range or blocked = +1 000 000,
   5 per cell beyond PE/2 unless master), cover, light, injury.
7. **Dodge** of a hit critter: `2 × level` capped at 95 (unreachable: at most 75; the original's
   formula; CE's `5 × level` is a reconstruction slip, see the
   [verification ledger](../research/original-verification-ledger.md)), difficulty from
   blindness and crippled legs, auto-fail when it cannot move; a critical
   dodge becomes a critical miss with 0/10/50/100 % by dodge training.
8. **Fate**: a PC attacker's critical-hit fate forces a critical hit; else a
   PC target's critical-miss fate forces a critical miss. The caller clears
   the consumed fate (`AttackResolution.Fate`).
9. **Critical miss**: the attack turns on the attacker, not aimed, which always
   hits itself; with 50 % the self-hit is critical and the critical-miss table
   rolls first.
10. **Damage** for the target (or the attacker on a critical miss): per type
    uniform in the weapon range (melee DamageBonus, magic adjustment, cap
    3 × max or 2 × unarmed), backstab +level (×5 true), fatigue ×4 draining /
    ÷4 limiting, no poison against undead, mechanical or stoned, unarmed body
    spells; then stoned zeroes everything, invulnerable, nigh-invulnerable
    (unless uber) and walls take nothing; then the critical-hit procedure
    (×3 at chance + 10, else ×2 at chance + 30, else ×1.5 at chance + 60, none
    when an NPC hits a PC; the effect table; ×1.5 when nothing at all came
    out); then resistance (fatigue against ¾ of normal) unless the weapon
    ignores it; hit points `normal + electrical + fire`, ×1.5 / ×0.75 for a PC
    attacker on easy / hard, times the multiplier; a portal or container
    subtracts its material (stone, brick, metal, force 10; wood 5; others 0).

`AttackDamage` is a packet: hit-point, fatigue and poison damage, the rolled
and final values per type, multiplier, critical effects, armour wear and why
damage was prevented. Applying it to HP, fatigue, poison, knockout, death and
injuries is the [life state](life-state-runtime.md)'s job (ARC-COMBAT-002:
`LifeDamage.FromAttack`, `LifeRules.ApplyDamage`, `GameSessionOwner.ApplyDamage`);
item wear, drops and busting stay with the item owners.

## Rule settings and anomaly switches

Owner decision D8 gives combat two settings: **Original**, the retail 1.0.7.4
behaviour with its bugs, and **Steamwoven**, the original with the D2 fixes
(the default). Each switch below is a bug the
[original check](../research/combat-core-evidence.md#original-executable-check-arc-combat-001)
found in the retail executable; the full decision table is in the
[NPC combat AI plan](../research/npc-combat-ai-plan.md#anomaly-decisions).
CE-only reconstruction slips get no switch: A8 (the critical effect chance with
cancelled weapon magic is 0) behaves as the original in both settings.

| Anomaly | Steamwoven (fixed) | Original (switch on) |
| --- | --- | --- |
| A2 | The bed's −30 counts once. | `InvocationModifierCountedTwice`: −60. |
| A6 | `AttackRules.ArmorClass` adds AC-adjustment effects once, through the effective AcAdjustment stat. | `ArmorClassEffectsCountedTwice`: effects also applied to base + worn AC. |
| A7 | A critical self-hit rolls the critical-hit procedure once, at damage time. | `CriticalSelfHitRolledTwice`: also in the critical-miss branch; flags add up. |
| A10 | The backstab bonus is not added to a critical self-hit. | `BackstabDamageOnSelfHit`: added. |

A10 was found by this task's re-reading: `combat_calc_dmg` keeps the backstab
flags after a critical miss retargets the attack to the attacker.

## Not ready

- Not wired into the session, the turn-based round, AI or the viewer; no
  snapshot schema change.
- Ammunition, approach to melee range, line of fire and cover (the caller
  passes the effective target, cover and blocked flag), projectile flight and
  where a missed throw lands (FR-COMBAT-007).
- Scripts (`SAP_TAKING_DAMAGE` veto, hit/miss events), sounds, floating text,
  AI reaction to the attack, experience and weapon wear.
- Critical-effect tables (FR-COMBAT-009) beyond the injected hook; effects
  are additive stand-ins, not the effect engine.
- Skill levels, AC, resistances and weapon data come from the caller (for the hero, the worn part of AC and resistances,
  its light and the `CombatWeapon` it strikes with come from `QueryHeroEquipmentCombat`, [equipment](equipment-runtime.md),
  ARC-WORLD-028); no
  prototype data is read (research O6).
- Body of Air, traps, explosions and the source's forced-hit path.

## Tests

`tests/Arcanum.Simulation.Tests/Combat`: synthetic, scripted rolls only, no
original data. Formula tables and boundaries (`AttackRulesTests`), resolution
paths and thresholds, dodge, fate, technology failure, magick cancel,
objects, invulnerability, difficulty parts, awareness order, backstab,
damage immunities and body spells, refusals, roll trace and seeded replay
(`AttackResolverTests`), and both settings and each switch (`CombatAnomalyProfileTests`).
