# Attack resolver runtime (ARC-COMBAT-001)

Status: **Implemented, not wired.** Work item ARC-COMBAT-001 (2026-10-16), slice 2
of the [combat core research](../research/combat-core-evidence.md#proposed-implementation-slices-project-policy-not-accepted)
(ARC-RES-030). Requirements touched:
[FR-COMBAT-004](../requirements/combat-magic-tech.md#fr-combat-004) (target
validity, partly), [FR-COMBAT-005](../requirements/combat-magic-tech.md#fr-combat-005)
(hit, dodge, miss, critical) and
[FR-COMBAT-008](../requirements/combat-magic-tech.md#fr-combat-008) (typed
damage, immunities, resistances, portal and container material; since ARC-COMBAT-007
the armour and weapon wear of the damage step, below) and
[FR-COMBAT-009](../requirements/combat-magic-tech.md#fr-combat-009) (the hook, since
ARC-COMBAT-007 the item effects of its weapon and armour criticals as wear, and since
ARC-COMBAT-009 the original's critical-effect tables, [below](#critical-effects-arc-combat-009)).

`Arcanum.Simulation.Combat` resolves **one attack** as a pure function:

```csharp
AttackResolution AttackResolver.Resolve(
    AttackRequest request, ICombatRollSource rolls,
    ICriticalEffects? criticalEffects = null, CombatAnomalyProfile? profile = null);
```

It reads only its arguments, changes nothing, and returns the outcome, the
difficulty parts, both skill checks, the fate used, the damage packet and every
roll it made. Numbers are a reading of the original's combat, skill, item,
object and random functions (the research's C15–C21 and the "Hit, dodge and
critical" steps), re-read for this task. Original executable parity is
**NOT_VERIFIED** (evidence level F0), except for the anomaly switches below,
which were checked against the original 1.0.7.4 executable, and the attack and
dodge checks (steps 2, 6 and 7), which run through the shared
[skill check](skill-check-runtime.md) and were read in the executable with the
attack's invocation by two readers (ARC-SKILLS-003, ledger SC1–SC17).

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
  damage; weapon destruction and ammunition loss on a critical miss).
  `OriginalCriticalEffects` is the original's (ARC-COMBAT-009, below), which the
  session passes; the resolver's default when none is passed stays
  `NoCriticalEffects`, which returns none.
- `CombatAnomalyProfile`: the two rule settings of owner decision D8,
  `Steamwoven` (the original plus the recorded D2 fixes; the default when
  `profile` is null) and `Original` (the retail 1.0.7.4 behaviour), with stable
  names `steamwoven` and `original` (`FromName`, `All`); see below.

Helpers in `AttackRules` expose each formula on its own: effectiveness, dodge
effectiveness, critical hit and miss chances and the light penalty (these four call
`SkillCheckRules`), true-backstab bonus,
technology-failure and magick-cancel chances, light penalty, armour class,
backstab eligibility, weapon damage range, material reduction and the hit
location table.

## Resolution order

The combat rolls are made at the same points and in the same order as in the
original, except where a D2 fix removes one; a damage range with equal bounds
draws nothing, like the original's random-range function (0x4CEF00). The
original also draws from the same generator outside these rolls (the AI's
reaction to the attack, weapon sounds; not checked in the executable, F0), so a
seeded run matches an original run only once those draws are modelled as well.
The effect tables' constitution checks are modelled since ARC-COMBAT-009; a
critical's item drop draws a distance and a direction that are not
([below](#critical-effects-arc-combat-009)).

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
   5 per cell beyond PE/2 unless master), cover, light (a small own light keeps
   ¾ of the penalty, SC9; before ARC-SKILLS-003 it kept ⅔), injury.
7. **Dodge** of a hit critter: `2 × level` capped at 95 (unreachable: at most 75; the original's
   formula, ledger row Dodge (SKX1) in the
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
applying the wear below is the session's (`GameSessionOwner.ApplyAttackWear`); drops and busting
stay with the item owners.

## Wear (ARC-COMBAT-007)

`AttackResolution.Wear` (`AttackWear`, built by `CombatWearRules.Of`) names the item steps of the
original's damage step (0x4B4390), read in the executable by two readers
([ledger CW1–CW10](../research/original-verification-ledger.md#combat-wear-arc-combat-007), F2). It is
null without a damage packet and when invulnerability, nigh-invulnerability or a wall prevented the
damage (the original returns before any item step). The wearer is whoever took the damage: the
target, or the attacker of a critical self-hit, which keeps the attack's location.

| Step | Rule |
| --- | --- |
| Armour (`ArmorDamage`, `ArmorSlot`) | Fire damage above 0 after resistance, a weapon with the damage-armour flag or the "damage armour" critical: the wearer's armour at the hit location (`CombatWearRules.ArmorSlot`, 0x4B54B0: head helmet, arm gauntlets, leg boots, torso body armour) loses 10, once. A critter wearer only. |
| The wearer's weapon (`WeaponDamage`, `DestroyWeapon`, `ExplodeWeapon`) | "Damage weapon" 10, then "destroy weapon" or "explode weapon" deletes it without a break; the session passes an invulnerable weapon by. A critter wearer only. These come from the critical-effect tables (FR-COMBAT-009), which the session rolls since ARC-COMBAT-009. |
| The attacker's side (`AttackerWear`, `AttackerWearAmount`) | A melee attack (armed or bare-handed; never a throw, bow or firearm) that dealt damage (hit points above 0, or for a critter fatigue above 0): `CombatWearRules.WeaponWear` (0x4B5580) by the struck one's material, stone 4, brick 3, wood 2, plant 1, metal 5, fire 5, glass 5 bare-handed only, others 0; a weapon's wear then falls by `trunc(wear × min(|complexity|, 100) / 100)` and by 2 for art subtype 4 or 1 for subtypes 5 and 13 (`CombatWeapon.ArtSubtype`); bare hands hurt the attacker itself, never its last hit point (`HandsWear`). On an object target only while the hit left it whole (`AttackerWearNeedsWholeTarget`). No roll. |

The material is `Combatant.Material`, now read for critters too (a stone golem wears a sword; an NPC whose
stored material is outside the fourteen defends as flesh, which wears nothing, as in the original). The art
subtype is item data the caller passes with the weapon (`CombatWeapon.ArtSubtype`; the session takes it from
the host's `SessionWearItem` and saves none of it). The
original applies the armour and critical weapon steps before the hit-point damage and the attacker's
wear last, after death and experience; the session step keeps that order within the wear.

## Critical effects (ARC-COMBAT-009)

`OriginalCriticalEffects` is the effect part of the original's critical-hit procedure (0x4B5F40) and the effect rolls of its
critical-miss procedure (0x4B6410), read in the retail executable by two readers
([ledger CE1–CE15](../research/original-verification-ledger.md#critical-effects-arc-combat-009), F2). The thresholds are the
code's; the data are the weapon's charts and the recipient's critical-hit body type (`Combatant.CriticalBody`, critter field
227). The resolver keeps the multiplier rolls before the table and the ×1.5 fallback after it (CE3); the table's rolls follow,
in this order, each a d100 that passes at ≤ its threshold (`chance` as in step 10):

| Step | Who | Threshold and condition | Effect |
| --- | --- | --- | --- |
| Stun | not body type 2, not undead | chance + 5, +10 crushing or electrical chart, +10 head without a helmet; then a failed constitution check at −5 | `Stun` |
| Knockout | stunned by this step, no PC, not fatigue-immune | the same threshold; a failed check at −5 | `Knockout` |
| Arm | the same; location arm; not body types 4, 5 | chance + 1; a failed check at −5 | `CrippleArm` |
| Leg | the same; location leg; not body type 4 | chance + 1; a failed check at −5 | `CrippleLeg` |
| Head, crushing | the same; location head; a worn helmet | chance + 5, no check | `DropHelmet` |
| Head, other charts | the same; location head; unless an NPC hits a PC | chance + 1 without a helmet, + 0 with one; a failed check at 0 | `Blind` |
| Scar | the same; a PC; not a scarred PC that hit itself | chance + 5 | `Scar` |
| Knockdown | body type 0, undead too | chance + 5, +5 crushing, +10 leg | `Knockdown` |
| Weapon | an item in slot 1004 | chance + 10, then a second d100 at chance + 10 unless an NPC hits a PC | `DamageWeapon`, `DropWeapon` |
| Armour | armour at the hit location (`CombatWornItems`, CW1) | chance + 10 | `DamageArmor` |

A constitution check (0x45F060) rolls `1 .. Combatant.ConstitutionMaximum` (the race's, `CharacterStatRules.Range`) and passes at
≤ `Combatant.Constitution` + modifier; only a failed check gives the effect. Only one location step runs. The critical miss
(after the resolver's 50 % critical self-hit roll): "destroy weapon" (not bare hands) at chance + 1, "explode weapon" (explosive,
fire, electrical charts) at chance + 1, and for a weapon with an ammunition type (`CombatWeapon.UsesAmmunition`) all its
ammunition at chance + 1, else five shots' worth at a second d100 ≤ chance + 11. Every roll carries its own purpose
(`CombatRollPurpose.CriticalStun` … `CriticalLoseAmmo`).

Applying the result (the damage step 0x4B4390, CE12–CE13) is shared out: the life rules take stun (only on a critter that can
act, CEA2), knockdown, knockout, scars, blinding and crippling (`LifeRules.ApplyDamage`, [life state](life-state-runtime.md));
the wear takes the weapon's and the armour's damage and the weapon's destruction ([wear](#wear-arc-combat-007)); the session takes
the drops ([hero attack](hero-attack-runtime.md#critical-effects-arc-combat-009)).

## Rule settings and anomaly switches

Owner decision D8 gives combat two settings: **Original**, the retail 1.0.7.4
behaviour with its bugs, and **Steamwoven**, the original with the D2 fixes
(the default). Each switch below is a bug the
[original check](../research/combat-core-evidence.md#original-executable-check-arc-combat-001)
found in the retail executable; the full decision table is in the
[NPC combat AI plan](../research/npc-combat-ai-plan.md#anomaly-decisions).
Rows that only correct an earlier reading get no switch: A8 (the critical effect chance with
cancelled weapon magic is 0) behaves as the original in both settings.

| Anomaly | Steamwoven (fixed) | Original (switch on) |
| --- | --- | --- |
| A2 | The bed's −30 counts once. | `InvocationModifierCountedTwice`: −60. |
| A6 | `AttackRules.ArmorClass` adds AC-adjustment effects once, through the effective AcAdjustment stat. | `ArmorClassEffectsCountedTwice`: effects also applied to base + worn AC. |
| A7 | A critical self-hit rolls the critical-hit procedure once, at damage time. | `CriticalSelfHitRolledTwice`: also in the critical-miss branch; flags add up. |
| A10 | The backstab bonus is not added to a critical self-hit. | `BackstabDamageOnSelfHit`: added. |

A10 was found by this task's re-reading: the damage calculation (0x4B6680) keeps the backstab
flags after a critical miss retargets the attack to the attacker.

## Not ready

- Not wired into the session, the turn-based round, AI or the viewer; no
  snapshot schema change.
- Ammunition, approach to melee range, line of fire and cover (the caller
  passes the effective target, cover and blocked flag), projectile flight and
  where a missed throw lands (FR-COMBAT-007).
- Scripts (`SAP_TAKING_DAMAGE` veto, hit/miss events), sounds, floating text
  (the wear messages included), AI reaction to the attack and experience.
- The four critical chance adjustments of the attacker are additive stand-ins
  for its effect adjustments (0x4EAB30, 0x4EAB90 and the two chance ones), not
  the effect engine.
- Of the critical effects (ARC-COMBAT-009): the drop's placement and draws, the
  hero's drops, the ammunition, the injury record of a PC, the NPC's
  look-for-weapon/armour flags, the stun's duration (the stun animation, goal
  57) and the worn armour's critical scripts (points 34 and 35) are not modelled
  ([hero attack](hero-attack-runtime.md#critical-effects-arc-combat-009)).
- Skill levels, AC, resistances and weapon data come from the caller (for the hero, the worn part of AC and resistances,
  its light and the `CombatWeapon` it strikes with come from `QueryHeroEquipmentCombat`, [equipment](equipment-runtime.md),
  ARC-WORLD-028); no
  prototype data is read (research O6).
- Body of Air, traps, explosions and the source's forced-hit path.

## Tests

`tests/Arcanum.Simulation.Tests/Combat`: synthetic, scripted rolls only, no
original data. `OriginalCriticalEffectsTests` (ARC-COMBAT-009) plays every table step at its boundary, the body types, the
NPC-hits-PC rule, the constitution checks' bounds, the miss effects and the resolver's fallback with the table. Formula tables and boundaries (`AttackRulesTests`), resolution
paths and thresholds, dodge, fate, technology failure, magick cancel,
objects, invulnerability, difficulty parts, awareness order, backstab,
damage immunities and body spells, refusals, roll trace and seeded replay
(`AttackResolverTests`), both settings and each switch (`CombatAnomalyProfileTests`), and the wear: every
material for a weapon and bare hands, the complexity and subtype steps, the hand cap, the packet rules and
the resolver's wear of a hit, a fiery hit, a resisted fire, a self-hit and an object (`CombatWearRulesTests`).
