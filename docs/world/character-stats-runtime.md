# Bounded character stats runtime

Status: **Implemented bounded component**, ARC-WORLD-012. An independently written,
Godot-free C# owner in `Arcanum.Simulation` (namespace `Arcanum.Simulation.Character`)
for [FR-CHAR-008..011](../requirements/characters-skills.md#fr-char-008): stored base
versus effective values, set-time limits, race/gender innate effects and read-only
derived stats. Since ARC-WORLD-015 the [game session's hero](starting-hero-runtime.md) is one
of these owners (saved in snapshot schema 6, effective intelligence for dialog gates); the viewer is not wired and
its conversation keeps the PROJECT diagnostic PC profile
(see [dialog conversation](../scripting/dialog-conversation-runtime.md)). Character
creation, skills, levels, backgrounds and effect lifetimes are separate requirements.

## Evidence and provenance

Rules profile `steamwoven-character-stats-v1`. Numbers are **source claims** read in
CE at [`a7ff41b`][ce] (Sustainable Use License 1.0, **not MIT**); they were not
re-observed in the retail executable, so retail parity is **NOT_VERIFIED**. The C# code,
its structure and the synthetic tests are independent MIT work; no CE code is copied,
translated or linked.

| Claim | Source |
| --- | --- |
| Stored stat ranges and defaults (primary 1..20, default 8; magick/tech points 0..210, default 0) and derived clamp ranges | [`stat.c` tables][stat-tables] |
| Racial primary maxima (dwarf/half-orc ST,CN 21; elf/dark elf DX,BE,WP 21; half-elf DX 21; gnome WP 22; halfling DX 22; half-ogre ST 24; ogre ST 26; orc ST,CN 22) | [`stat_level_max`][stat-max] |
| Effective = base, then effects, then clamp to the race range | [`stat_level_get`][stat-get] |
| Derived formulas, derived writes ignored | [`stat_base_get` / `stat_base_set`][stat-base] |
| Race/gender setter removes the old innate effect before applying the new one | [setter side effects][stat-base], [effect causes][effect-cause] |
| Aggregation: sum of percents, product of multipliers, product of divisors, sum of adds, largest minimum, smallest maximum, in that order | [`effect_adjust_func`][effect-adjust] |
| Innate effect contents (e.g. the female ST −1 / CN +1 effect, per-race effects) | Data: `rules\effect.mes` via [`effect_init`][effect-init]. **Not built in**: callers supply an `InnateEffectTable`. |

Derived formulas over effective inputs (C integer division, truncation toward zero):

| Stat | Base formula | Range |
| --- | --- | --- |
| CarryWeight | 500 × ST | 300..10000 |
| DamageBonus | ST − 10, halved if negative; doubled when ST ≥ 20 | −50..50 |
| AcAdjustment | DX − 10 | −9..95 |
| Speed | DX, +5 when DX ≥ 20 | 1..100 |
| HealRate | (CN + 1) / 3 | 0..6 |
| PoisonRecovery | CN | 1..20 |
| ReactionModifier | BE 1..19: source table entry `BE − 1` (−65 … 65); BE ≥ 20: 10 × BE − 100 | −65..200 |
| MaxFollowers | CH / 4 | 1..7 |
| MagickTechAptitude | (50 × MP − 55 × TP) / 10 + context sector adjustment | −100..100 |

"Extraordinary" means a primary stat whose **effective** value is at least 20.

## Accepted departures

- **Strict set-time limits.** `SetBase` rejects a value outside the current race range
  (`OutOfRange`, no state change) instead of silently clamping it; callers query
  `RangeOf` first. Writing a derived stat is a typed `DerivedStatReadOnly` rejection.
- **Race change keeps stored values.** A stored value above the new racial maximum stays
  stored (as in the source) and its effective value is clamped; it can only be lowered.
- **Removal by instance.** `AddEffect` returns an `AppliedEffectId`; `RemoveEffect`
  removes exactly that instance. Race and Gender causes are reserved for the setters,
  and their effects cannot be added or removed directly. Background and Class, which the
  source also treats as innate, are ordinary causes here until backgrounds are modelled.
- **No `-1` sentinel.** A minimum or maximum modifier of −1 is an ordinary bound.
- **Bounded arithmetic.** Explicit `CharacterStatLimits` (default 64 non-innate effects,
  7 modifiers per effect, |value| ≤ 1000; ceilings 4096/64/1,000,000). Divisor 0 is an
  invalid effect. Intermediate results saturate at ±1,000,000,000 (beyond that bound
  accumulation becomes order-dependent); source overflow behaviour is unknown.
- **Context is explicit.** Sector aptitude adjustment is a query argument bounded to
  −100..100, not read from a world location.

## API

| Entry | Contract |
| --- | --- |
| `CharacterStatsOwner.Create(CharacterStatsDefinition)` | Validates race, gender, limits and every innate effect, copies the innate table, applies the race and gender effects at revision 0. Failure returns a typed failure and **no owner**. |
| `Query(CharacterStatContext)` | Detached `CharacterStatsSnapshot`: race, gender, revision, applied effects, `GetBase`/`GetEffective`/`GetRange`/`IsExtraordinary` for every stat. Derived values are recomputed from current state on every query, so they never go stale. |
| `SetBase(stat, value)` | Stored stats only. Changed, NoChange (same value) or Rejected (`InvalidStat`, `DerivedStatReadOnly`, `OutOfRange`). |
| `SetRace` / `SetGender` | Remove the old innate effect, set, apply the new one (missing table entry = no effect). Same value is NoChange. |
| `AddEffect(definition, cause)` / `RemoveEffect(id)` | Non-innate effects with typed rejection (`InvalidEffect`, `InvalidCause`, `EffectCapacityExceeded`, `UnknownEffect`). |

Every accepted mutation increments `Revision` once; rejected and no-op calls leave state
and revision unchanged. The owner is caller-serialized: no locks, events or callbacks.

## Not implemented

Skill/spell/tech prerequisite checks when lowering a stat; background, poison,
Tempus Fugit, crippled-leg, day/night mage and persuasion-expert adjustments; level,
experience, alignment, fate, unspent points, age and poison stats; HP/fatigue and
encumbrance recalculation on effect changes; effect lifetimes and durations (FR-CHAR-022);
skills and resistances; character creation beyond the pre-made start of
[ARC-WORLD-015](starting-hero-runtime.md) (which reads the innate effects from `effect.mes`); the character sheet UI.

[ce]: https://github.com/alexbatalov/arcanum-ce/tree/a7ff41b300ef712f0e7d088183a3d08957110cdd
[stat-tables]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/stat.c#L38-L204
[stat-max]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/stat.c#L891-L960
[stat-get]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/stat.c#L336-L526
[stat-base]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/stat.c#L540-L798
[effect-cause]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/effect.c#L488-L638
[effect-adjust]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/effect.c#L732-L850
[effect-init]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/effect.c#L252-L290
